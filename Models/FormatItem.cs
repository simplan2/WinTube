using System;
using System.Collections.Generic;
using System.Text;

namespace WinTube.Models
{
    public class FormatItem
    {
        public string FormatId { get; set; } = "";

        public string Label { get; set; } = "";

        public bool IsAudio { get; set; }

        public int? Height { get; set; }
        public int? Width { get; set; }

        public string Extension { get; set; } = "";

        public string AudioOutputFormat { get; set; } = "";

        public double AudioBitrate { get; set; }

        public bool IsSelected { get; set; }

        public bool HasAudio { get; set; }
    }
}
