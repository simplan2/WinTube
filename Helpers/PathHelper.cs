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
    }
}
