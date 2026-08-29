using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace WinTube.Models
{
    public class VideoFormat
    {
        //[JsonPropertyName("format_id")]
        //public string? FormatId { get; set; }

        //[JsonPropertyName("ext")]
        //public string? Ext { get; set; }

        //[JsonPropertyName("height")]
        //public int? Height { get; set; }

        //[JsonPropertyName("width")]
        //public int? Width { get; set; }

        //[JsonPropertyName("tbr")]
        //public double? VideoBitrate { get; set; }

        //[JsonPropertyName("filesize")]
        //public long? FileSize { get; set; }

        //[JsonPropertyName("filesize_approx")]
        //public long? FileSizeApprox { get; set; }

        //[JsonPropertyName("abr")]
        //public double? AudioBitrate { get; set; }

        //// Audio formatos
        //[JsonPropertyName("vcodec")]
        //public string? VCodec { get; set; }

        //[JsonPropertyName("acodec")]
        //public string? ACodec { get; set; }



        [JsonPropertyName("format_id")]
        public string FormatId { get; set; }

        [JsonPropertyName("format_note")]
        public string FormatNote { get; set; }

        [JsonPropertyName("ext")]
        public string Ext { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("manifest_url")]
        public string ManifestUrl { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("resolution")]
        public string Resolution { get; set; }

        [JsonPropertyName("fps")]
        public double? Fps { get; set; }

        [JsonPropertyName("vcodec")]
        public string VCodec { get; set; }

        [JsonPropertyName("acodec")]
        public string ACodec { get; set; }

        [JsonPropertyName("tbr")]
        public double? VideoBitrate { get; set; }

        [JsonPropertyName("vbr")]
        public double? Vbr { get; set; }

        [JsonPropertyName("abr")]
        public double? AudioBitrate { get; set; }

        [JsonPropertyName("asr")]
        public double? Asr { get; set; }

        [JsonPropertyName("filesize")]
        public long? FileSize { get; set; }

        [JsonPropertyName("filesize_approx")]
        public long? FileSizeApprox { get; set; }

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; }

        [JsonPropertyName("dynamic_range")]
        public string DynamicRange { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("container")]
        public string Container { get; set; }

        [JsonPropertyName("http_headers")]
        public Dictionary<string, string> HttpHeaders { get; set; }

        [JsonPropertyName("downloader_options")]
        public Dictionary<string, object> DownloaderOptions { get; set; }

        // Propiedades de calidad (para facilitar selección)
        public bool IsVideo => !string.IsNullOrEmpty(VCodec) && VCodec != "none";
        public bool IsAudio => !string.IsNullOrEmpty(ACodec) && ACodec != "none";
        public bool IsDash => FormatNote?.Contains("DASH") ?? false;

        public string GetQualityLabel()
        {
            if (!IsVideo) return "Audio only";
            if (string.IsNullOrEmpty(Resolution)) return "Unknown";
            return $"{Resolution} {(DynamicRange == "HDR" ? "HDR" : "")}";
        }
    }
}
