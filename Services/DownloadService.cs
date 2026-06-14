using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using WinTube.Models;

namespace WinTube.Services
{
    public class DownloadService
    {
        public event Action<DownloadProgress>? ProgressChanged;

        //private void RaiseProgress(DownloadProgress p)
        //{
        //    ProgressChanged?.Invoke(p);
        //}

        public async Task DownloadVideo(
            string url,
            FormatItem format,
            string outputFolder)
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
                        $"--postprocessor-args \"ffmpeg:-c:a aac -b:a 192k\" " + // ¡AQUÍ ESTÁ LA MAGIA!
                        $"--ffmpeg-location \"{ffmpegPath}\" " +
                        $"-o \"{outputFolder}\\%(title)s.%(ext)s\" " +
                        $"\"{url}\"";
                }
            }

            var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,

                Arguments = arguments,

                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.OutputDataReceived += OnData;
            process.ErrorDataReceived += OnData;

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            ProgressChanged?.Invoke(new DownloadProgress
            {
                Percentage = 100,
                Status = "Completado"
            });
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

        private DownloadProgress ParseProgress(string line)
        {
            var progress = new DownloadProgress();

            try
            {
                // Ejemplo:
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
