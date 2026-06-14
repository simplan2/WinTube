using System;
using System.Collections.Generic;
using System.Text;

namespace WinTube.Models
{
    public class DownloadProgress
    {
        public double Percentage { get; set; }

        public string Speed { get; set; } = "";

        public string Eta { get; set; } = "";

        public string Status { get; set; } = "";
    }
}
