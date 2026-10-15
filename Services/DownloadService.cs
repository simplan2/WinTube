using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinTube.Helpers;
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
            FormatItem? format,
            string outputFolder,
            Guid id,
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

            // Construimos los argumentos como elementos separados para evitar
            // problemas de quoting e inyeccion de argumentos.
            var args = new List<string>();

            // Los archivos .part van a una carpeta temporal por descarga; el archivo
            // final se mueve a la carpeta de salida al terminar.
            string tempFolder = PathHelper.GetPartialFolder(id);
            Directory.CreateDirectory(tempFolder);

            // Plantilla relativa: yt-dlp la resuelve contra la ruta "home".
            string outputTemplate = "%(title)s.%(ext)s";

            // Presets de playlist: el FormatId ya es un selector -f completo y generico.
            if (format != null && format.IsGenericSelector)
            {
                if (format.IsAudio)
                {
                    outputFolder = Path.Combine(outputFolder, "Audio");
                    if (format.AudioOutputFormat == "mp3")
                    {
                        args.Add("-f"); args.Add("bestaudio");
                        args.Add("--extract-audio");
                        args.Add("--audio-format"); args.Add("mp3");
                        args.Add("--ffmpeg-location"); args.Add(ffmpegPath);
                    }
                    else
                    {
                        args.Add("-f"); args.Add(string.IsNullOrWhiteSpace(format.FormatId)
                            ? "bestaudio"
                            : format.FormatId);
                        args.Add("--ffmpeg-location"); args.Add(ffmpegPath);
                    }
                }
                else
                {
                    outputFolder = Path.Combine(outputFolder, "Video");
                    args.Add("-f"); args.Add(string.IsNullOrWhiteSpace(format.FormatId)
                        ? "bestvideo+bestaudio/best"
                        : format.FormatId);
                    args.Add("--merge-output-format"); args.Add("mp4");
                    args.Add("--ffmpeg-location"); args.Add(ffmpegPath);
                }
                args.Add("--no-playlist");
            }
            else if (format != null && format.IsAudio)
            {
                outputFolder = Path.Combine(outputFolder, "Audio");

                if (format.AudioOutputFormat == "mp3")
                {
                    args.Add("-f"); args.Add("bestaudio");
                    args.Add("--extract-audio");
                    args.Add("--audio-format"); args.Add("mp3");
                    args.Add("--ffmpeg-location"); args.Add(ffmpegPath);
                    args.Add("--no-playlist");
                }
                else if (format.AudioOutputFormat == "m4a" || format.AudioOutputFormat == "opus")
                {
                    args.Add("-f"); args.Add(format.FormatId);
                    args.Add("--no-playlist");
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

                if (format == null || string.IsNullOrWhiteSpace(format.FormatId))
                {
                    // Sin formato elegido (p. ej. items de playlist): mejor calidad disponible.
                    args.Add("-f"); args.Add("bestvideo+bestaudio/best");
                    args.Add("--merge-output-format"); args.Add("mp4");
                    args.Add("--ffmpeg-location"); args.Add(ffmpegPath);
                    args.Add("--no-playlist");
                }
                // Si el formato ya trae audio integrado no hace falta descargar pista aparte.
                else if (format.HasAudio)
                {
                    args.Add("-f"); args.Add(format.FormatId);
                    args.Add("--no-playlist");
                }
                else
                {
                    // Forzamos a que el audio se convierta a AAC y el contenedor final sea MP4
                    args.Add("-f"); args.Add($"{format.FormatId}+bestaudio/best");
                    args.Add("--merge-output-format"); args.Add("mp4");
                    args.Add("--recode-video"); args.Add("mp4");
                    args.Add("--convert-subs"); args.Add("srt");
                    args.Add("--postprocessor-args"); args.Add("ffmpeg:-c:a aac -b:a 192k");
                    args.Add("--ffmpeg-location"); args.Add(ffmpegPath);
                    args.Add("--no-playlist");
                }
            }

            // El archivo final va a la carpeta de salida; los .part, a la temporal del item.
            args.Add("--paths"); args.Add($"home:{outputFolder}");
            args.Add("--paths"); args.Add($"temp:{tempFolder}");
            args.Add("-o"); args.Add(outputTemplate);

            // Reanuda desde los archivos .part si la descarga se pauso antes.
            args.Add("--continue");
            // Progreso linea a linea (evita el uso de '\r' que complica la lectura).
            args.Add("--newline");
            args.Add(url);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            // REGISTRO DE CANCELACIÓN: si el token se activa, matamos el proceso.
            // Ojo: Cancel() invoca este callback de forma sincrona en el hilo que
            // cancela (el de UI), asi que el Kill se hace en un hilo de fondo para
            // no congelar la interfaz (Kill con todo el arbol puede tardar).
            using var registration = cancellationToken.Register(() =>
                Task.Run(() => KillProcess(process)));

            // Leemos la salida en vivo para actualizar la barra de progreso.
            process.Start();

            // IMPORTANTE: drenar stdout y stderr en paralelo. Si solo se lee stdout,
            // el buffer de stderr puede llenarse (avisos de yt-dlp/ffmpeg) y el proceso
            // se bloquea escribiendo, colgando la descarga en el post-procesado.
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            string? line;
            try
            {
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Descartar lineas vacias que envia la consola de yt-dlp.exe
                    if (!string.IsNullOrWhiteSpace(line))
                    {                    
                        ParseAndUpdateProgress(line);
                    }
                }

                // Esperamos asíncronicamente a que el proceso de windows se cierre del todo
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Aseguramos que yt-dlp/ffmpeg terminaron del todo antes de propagar:
                // asi el archivo .part queda liberado y se puede borrar/reanudar.
                try { process.WaitForExit(5000); } catch { }
                throw;
            }

            // Si yt-dlp devuelve un código distinto de 0 (y no fue por una cancelación), algo salió mal.
            // Es un fallo esperado de yt-dlp, no un error critico.
            if (process.ExitCode != 0)
            {
                string errorDetails = await stderrTask.ConfigureAwait(false);
                throw new DownloadFailedException(errorDetails);
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