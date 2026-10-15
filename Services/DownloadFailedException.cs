using System;

namespace WinTube.Services
{
    /// <summary>
    /// Error esperado de yt-dlp (video no disponible, red, formato, etc.).
    /// No es un fallo critico: el item de descarga ya refleja su propio estado.
    /// </summary>
    public class DownloadFailedException : Exception
    {
        public DownloadFailedException(string message) : base(message)
        {
        }
    }
}
