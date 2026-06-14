using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace WinTube.Models
{
    public class VideoFormat
    {
        [JsonPropertyName("format_id")]
        public string? FormatId { get; set; }

        [JsonPropertyName("ext")]
        public string? Ext { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("tbr")]
        public double? VideoBitrate { get; set; }

        [JsonPropertyName("filesize")]
        public long? FileSize { get; set; }

        [JsonPropertyName("filesize_approx")]
        public long? FileSizeApprox { get; set; }

        [JsonPropertyName("abr")]
        public double? AudioBitrate { get; set; }

        // Audio formatos
        [JsonPropertyName("vcodec")]
        public string? VCodec { get; set; }

        [JsonPropertyName("acodec")]
        public string? ACodec { get; set; }
    }
}
