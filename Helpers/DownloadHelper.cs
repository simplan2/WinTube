using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WinTube.Helpers
{
    public class DownloadHelper
    {
        public static double EstimateAudioSizeMb(double durationSeconds, double bitrateKbps)
        {
            return durationSeconds * bitrateKbps / 8 / 1024;
        }

        public static string FormatSize(long bytes)
        {
            double mb = bytes / 1024d / 1024d;

            if (mb < 1024)
                return $"{mb:0.#} MB";

            return $"{mb / 1024:0.#} GB";
        }


        public static async Task<Bitmap?> LoadFromUrl(string url)
        {
            using var http = new HttpClient();

            var data = await http.GetByteArrayAsync(url);

            using var ms = new MemoryStream(data);

            return new Bitmap(ms);
        }
    }
}
