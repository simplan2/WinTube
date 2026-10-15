using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WinTube.Services
{
    public class YoutubeService
    {
        public async Task<string> GetVideoJson(string url, bool includePlaylist = false, bool flatPlaylist = true, CancellationToken cancellationToken = default)
        {
            string ytDlpPath = Path.Combine(
                AppContext.BaseDirectory,
                "Tools",
                "yt-dlp.exe");

            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // ArgumentList evita problemas de quoting e inyeccion de argumentos.
            process.StartInfo.ArgumentList.Add("-J");
            if (includePlaylist)
            {
                process.StartInfo.ArgumentList.Add("--yes-playlist");
                if (flatPlaylist)
                {
                    process.StartInfo.ArgumentList.Add("--flat-playlist");
                }
            }
            else
            {
                process.StartInfo.ArgumentList.Add("--no-playlist");
            }
            process.StartInfo.ArgumentList.Add(url);

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignorar errores al matar el proceso
                }
            });

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new Exception(error);
            }

            return output;
        }
    }
}
