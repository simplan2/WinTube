using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WinTube.Models
{
    public class DownloadJobList
    {
        [JsonPropertyName("jobs")]
        public List<DownloadJob> Jobs { get; set; } = new();
    }

    public class DownloadJob
    {
        // Identificador estable para reconstrucción
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("outputFolder")]
        public string OutputFolder { get; set; } = string.Empty;

        // Formato seleccionado (serializable). No guardamos Bitmap.
        [JsonPropertyName("formatId")]
        public string FormatId { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("isAudio")]
        public bool IsAudio { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("extension")]
        public string Extension { get; set; } = string.Empty;

        [JsonPropertyName("audioOutputFormat")]
        public string AudioOutputFormat { get; set; } = string.Empty;

        [JsonPropertyName("audioBitrate")]
        public double AudioBitrate { get; set; }

        [JsonPropertyName("hasAudio")]
        public bool HasAudio { get; set; }

        // Indica si el formato es un preset generico (selector -f de playlist).
        [JsonPropertyName("isGenericSelector")]
        public bool IsGenericSelector { get; set; }

        // Miniatura
        [JsonPropertyName("thumbnailUrl")]
        public string ThumbnailUrl { get; set; } = string.Empty;

        // Estado persistible: solo InQueue/Paused son relevantes para reanudar
        [JsonPropertyName("status")]
        public DownloadStatus Status { get; set; } = DownloadStatus.InQueue;

        [JsonPropertyName("addedAt")]
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
