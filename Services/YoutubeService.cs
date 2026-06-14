using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WinTube.Services
{
    public class YoutubeService
    {
        public async Task<string> GetVideoJson(string url)
        {
            string ytDlpPath = Path.Combine(
                AppContext.BaseDirectory,
                "Tools",
                "yt-dlp.exe");

            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = $"-J \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new Exception(error);
            }

            return output;
        }

        //public async Task<string> GetVideoJson(string url)
        //{
        //    string ytDlpPath =
        //        Path.Combine(
        //            AppContext.BaseDirectory,
        //            "Tools",
        //            "yt-dlp.exe");

        //    Process process = new();

        //    process.StartInfo = new ProcessStartInfo
        //    {
        //        FileName = ytDlpPath,
        //        Arguments = $"-J {url}",
        //        RedirectStandardOutput = true,
        //        RedirectStandardError = true,
        //        UseShellExecute = false,
        //        CreateNoWindow = true
        //    };

        //    process.Start();           
        //    string output = await process.StandardOutput.ReadToEndAsync();
        //    string error = await process.StandardError.ReadToEndAsync();

        //    await process.WaitForExitAsync();

        //     if(process.ExitCode != 0)
        //    {
        //        throw new Exception($"yt-dlp failed with exit code {process.ExitCode}: {error}");
        //    }

        //     return output;        
        //}
    }
}
