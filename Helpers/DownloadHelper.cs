using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace WinTube.Helpers
{
    public static class DownloadHelper
    {
        // HttpClient estatico: crear uno por llamada agota los sockets.
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

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

        public static async Task<byte[]?> LoadBytesFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                return await Http.GetByteArrayAsync(url);
            }
            catch
            {
                // Una miniatura fallida no debe romper el analisis del video.
                return null;
            }
        }

        public static Bitmap? CreateBitmap(byte[]? data)
        {
            if (data == null || data.Length == 0)
                return null;

            using var ms = new MemoryStream(data);
            return new Bitmap(ms);
        }

        public static async Task<Bitmap?> LoadFromUrl(string url)
        {
            return CreateBitmap(await LoadBytesFromUrl(url));
        }
    }
}
