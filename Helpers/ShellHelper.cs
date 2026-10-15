using System;
using System.Diagnostics;
using System.IO;

namespace WinTube.Helpers
{
    public static class ShellHelper
    {
        /// <summary>
        /// Abre una carpeta en el explorador de archivos nativo del sistema.
        /// Devuelve false si la ruta está vacía o si ocurre un error.
        /// </summary>
        public static bool OpenFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", $"\"{path}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", $"\"{path}\"");
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", $"\"{path}\"");
                }
                else
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
