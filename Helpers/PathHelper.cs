using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinTube.Helpers
{
    public static class PathHelper
    {
        private static readonly string defaultOutputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "WinTube Downloads");

        public static string GetDefaultOutputFolder()
        {
            return defaultOutputFolder;
        }

        // Carpeta temporal por descarga donde yt-dlp deja los archivos .part
        // (aqui se puede borrar el parcial al eliminar un item).
        public static string GetPartialFolder(Guid id)
        {
            return Path.Combine(Path.GetTempPath(), "WinTube", id.ToString("N"));
        }
    }
}
