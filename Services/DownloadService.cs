using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinTube.Models;

namespace WinTube.Services
{
    public class DownloadService
    {
        public event Action<DownloadProgress>? ProgressChanged;
        private static readonly Regex YtDlpProgressRegex = new Regex(
            @"\[download\]\s+(?<percent>[\d\.]+)%\s+of\s+~?\s*(?<total>\S+)\s+at\s+(?<speed>\S+)\s+ETA\s+(?<eta>\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
        public async Task RunYtDlpAsync(
            string url,
            FormatItem format,
            string outputFolder,
            CancellationToken cancellationToken)
        {
            // forzamos el lanzamiento de la excepción si ya se pidió cancelar antes de empezar
            cancellationToken.ThrowIfCancellationRequested();

            // Ruta completa a yt-dlp.exe y ffmpeg.exe, asumiendo que están en la carpeta "Tools" dentro del directorio base de la aplicación
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
                outputFolder = Path.Combine(outputFolder, "Video");
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

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // REGISTRO DE CANCELACIÓN: Si el token se activa, matamos el proceso inmediatamente
            using var registration = cancellationToken.Register(() => KillProcess(process));

            // Leemos la salida en vivo para actualizar la barra de progreso
            process.Start();
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Descartar lineas vacias que envia la consola de yt-dlp.exe
                if (!string.IsNullOrWhiteSpace(line))
                {                    
                    ParseAndUpdateProgress(line);
                }
            }

            // Esperamos asíncronicamente a que el proceso de windows se cierre del todo
            await process.WaitForExitAsync(cancellationToken);

            // Si yt-dlp devuelve un código distinto de 0 (y no fue por una cancelación), algo salió mal
            if (process.ExitCode != 0)
            {
                string errorDetails = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new Exception(errorDetails);
            }        
        }

             private void KillProcess(Process process)
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    // NtResumeProcess(process.Handle); // Asegurarse de que el proceso no esté suspendido

                    // "entireProcessTree: true" destruye yt-dlp y a ffmpeg si estaba uniendo audio/video
                    process.Kill(entireProcessTree: true); // Matar proceso y sus hijos
                }
                catch
                {
                    // Ignorar errores al matar el proceso
                }
            }
        }


        private void ParseAndUpdateProgress(string line)
        {
            if(line.Contains("[download]"))
            {
                var progress = ParseProgress(line);
                ProgressChanged?.Invoke(progress);
            }
        }
        private DownloadProgress ParseProgress(string line)
        {
            
            var progress = new DownloadProgress { Status = "Descargando..." };
        
            try
            {
                var match = YtDlpProgressRegex.Match(line);
                if (match.Success)
                {
                    // 1. Extracto de porcentaje (ej. 45.3)
                    if (double.TryParse(match.Groups["percent"].Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double percent))
                    {
                        progress.Percentage = percent;
                    }

                    // 2. Tamaño total (ej. 123.45MiB)
                    progress.TotalSize = match.Groups["total"].Value;

                    // Extracto de velocidad (ej. 1.23MiB/s)
                    progress.Speed = match.Groups["speed"].Value;

                    // Extracto de ETA (ej. 00:02:34)
                    progress.Eta= match.Groups["eta"].Value;
                }
                else
                {
                    // Rescate por si la línea tiene un formato ligeramente distino
                    ExtractFallbackPercentage(line, progress);
                }
                
            }
            catch
            {
                progress.Status = "Procesando...";
            }

            return progress;
        }

        private void ExtractFallbackPercentage(string line, DownloadProgress progress)
        {
            var percentIndex = line.IndexOf("%");
            if (percentIndex > 0)
            {
                var start = line.LastIndexOf(' ', percentIndex) + 1;
                var percentString = line[start..percentIndex];
                if (double.TryParse(percentString,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double p))
                {
                    progress.Percentage = p;
                }
            }           
        }
    }
}