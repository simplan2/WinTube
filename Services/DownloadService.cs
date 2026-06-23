using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinTube.Models;

namespace WinTube.Services
{
    public class DownloadService
    {
        public event Action<DownloadProgress>? ProgressChanged;
        private Process? _currentProcess;

        // Importaciones nativas de Windows para pausar/reanudar procesos
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        public void Pause()
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                NtSuspendProcess(_currentProcess.Handle);
            }
        }

        public void Resume()
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                NtResumeProcess(_currentProcess.Handle);
            }
        }

        public async Task DownloadVideo(
            string url,
            FormatItem format,
            string outputFolder, CancellationToken cancellationToken)
        {
            string ytDlpPath = Path.Combine(
                AppContext.BaseDirectory,
                "Tools",
                "yt-dlp.exe");

            string ffmpegPath = Path.Combine(
                AppContext.BaseDirectory,
                "Tools");

            string arguments = "";
            if (format.IsAudio)
            {
                outputFolder = Path.Combine(outputFolder, "Audio");
                if (format.AudioOutputFormat == "mp3")
                {
                    arguments =
                        $"-f bestaudio " +
                        $"--extract-audio " +
                        $"--audio-format mp3 " +
                        $"--ffmpeg-location \"{ffmpegPath}\" " +
                        $"-o \"{outputFolder}\\%(title)s.%(ext)s\" " +
                        $"\"{url}\"";
                }
                else if (format.AudioOutputFormat == "m4a" || format.AudioOutputFormat == "opus")
                {
                    arguments =
                        $"-f {format.FormatId} " +
                        $"-o \"{outputFolder}\\%(title)s.%(ext)s\" " +
                        $"\"{url}\"";
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Formato de audio no soportado: {format.AudioOutputFormat}");
                }
            }
            else
            {
                outputFolder= Path.Combine(outputFolder, "Video");
                // Si en tu FormatItem guardas si ya tiene audio o no (por ejemplo, si ACodec != "none")
                if (format.HasAudio)
                {
                    // El video ya está completo. No necesitamos descargar otro audio ni usar FFmpeg.
                    arguments =
                        $"-f \"{format.FormatId}\" " +
                        $"-o \"{outputFolder}\\%(title)s.%(ext)s\" " +
                        $"\"{url}\"";
                }
                else
                {
                    // Forzamos a que el audio se convierta a AAC y el contenedor final sea MP4
                    arguments =
                        $"-f \"{format.FormatId}+bestaudio/best\" " +
                        $"--merge-output-format mp4 " +            // Contenedor final estricto MP4
                        $"--recode-video mp4 " +                   // Asegura compatibilidad de video si es necesario
                        $"--convert-subs srt " +                   // (Opcional) Por si descarga subtítulos
                        $"--postprocessor-args \"ffmpeg:-c:a aac -b:a 192k\" " +
                        $"--ffmpeg-location \"{ffmpegPath}\" " +
                        $"-o \"{outputFolder}\\%(title)s.%(ext)s\" " +
                        $"\"{url}\"";
                }
            }

            _currentProcess = new Process();

            _currentProcess.StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _currentProcess.OutputDataReceived += OnData;
            _currentProcess.ErrorDataReceived += OnData;

            _currentProcess.Start();
            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();

            // Registrar la cancelación para matar el proceso si se solicita
            using (cancellationToken.Register(() => KillProcess(_currentProcess)))
            {
                try
                {
                    // Esperar a que el proceso termine o sea cancelado
                    await _currentProcess.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {

                    ProgressChanged?.Invoke(new DownloadProgress { Status = "Cancelado", Percentage = 0 });
                    throw; // relanzar para que el llamador sepa que fue cancelado
                }
                finally
                {
                    _currentProcess = null;
                }
            }

            // Verificar si la cancelación fue solicitada después de que el proceso terminó
            cancellationToken.ThrowIfCancellationRequested();

            // Si terminó normalmente, enviar progreso final
            ProgressChanged?.Invoke(new DownloadProgress
            {
                Percentage = 100,
                Status = "Completado"
            });
        }

        private void KillProcess(Process process)
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    NtResumeProcess(process.Handle); // Asegurarse de que el proceso no esté suspendido
                    process.Kill(entireProcessTree: true); // Matar proceso y sus hijos
                }
                catch
                {
                    // Ignorar errores al matar el proceso
                }
            }
        }

        private void OnData(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            var line = e.Data;

            // yt-dlp progreso típico
            if (line.Contains("[download]"))
            {
                var progress = ParseProgress(line);

                ProgressChanged?.Invoke(progress);
            }
        }

        //private DownloadProgress ParseProgress(string line)
        //{
        //    var progress = new DownloadProgress();

        //    try
        //    {
        //        // porcentaje
        //        var percentIndex = line.IndexOf('%');
        //        if (percentIndex > 0)
        //        {
        //            var start = line.LastIndexOf(' ', percentIndex) + 1;
        //            var percentStr = line[start..percentIndex];
        //            if (double.TryParse(percentStr, out double p))
        //                progress.Percentage = p;
        //        }

        //        // tamaño total (ejemplo: "of 1.4GiB")
        //        var ofTag = "of ";
        //        var ofIndex = line.IndexOf(ofTag);
        //        if (ofIndex > 0)
        //        {
        //            var end = line.IndexOf(' ', ofIndex + ofTag.Length);
        //            if (end < 0) end = line.Length;
        //            progress.TotalSize = line.Substring(ofIndex + ofTag.Length, end - (ofIndex + ofTag.Length));
        //        }

        //        // velocidad (ejemplo: "at 1.2MiB/s")
        //        var atTag = "at ";
        //        var atIndex = line.IndexOf(atTag);
        //        if (atIndex > 0)
        //        {
        //            var end = line.IndexOf(' ', atIndex + atTag.Length);
        //            if (end < 0) end = line.Length;
        //            progress.Speed = line.Substring(atIndex + atTag.Length, end - (atIndex + atTag.Length));
        //        }

        //        // ETA (ejemplo: "ETA 00:15")
        //        var etaTag = "ETA ";
        //        var etaIndex = line.IndexOf(etaTag);
        //        if (etaIndex > 0)
        //        {
        //            var end = line.IndexOf(' ', etaIndex + etaTag.Length);
        //            if (end < 0) end = line.Length;
        //            progress.Eta = line.Substring(etaIndex + etaTag.Length, end - (etaIndex + etaTag.Length));
        //        }

        //        progress.Status = "Descargando...";
        //    }
        //    catch
        //    {
        //        progress.Status = "Procesando...";
        //    }

        //    return progress;
        //}


        private DownloadProgress ParseProgress(string line)
        {
            var progress = new DownloadProgress();

            try
            {
                // [download]  45.3% of ...

                var percentIndex = line.IndexOf('%');

                if (percentIndex > 0)
                {
                    var start = line.LastIndexOf(' ', percentIndex) + 1;

                    var percentStr =
                        line[start..percentIndex];

                    if (double.TryParse(percentStr, out double p))
                        progress.Percentage = p;
                }

                if (line.Contains("at"))
                {
                    progress.Speed = line;
                }

                if (line.Contains("ETA"))
                {
                    progress.Eta = line;
                }

                progress.Status = "Descargando...";
            }
            catch
            {
                progress.Status = "Procesando...";
            }

            return progress;
        }

    }
}