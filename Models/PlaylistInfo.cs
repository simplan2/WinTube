using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WinTube.Models
{
    public class PlaylistInfo
    {
        [JsonPropertyName("_type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("uploader")]
        public string Uploader { get; set; } = string.Empty;

        [JsonPropertyName("playlist_count")]
        public int? PlaylistCount { get; set; }

        [JsonPropertyName("webpage_url")]
        public string WebpageUrl { get; set; } = string.Empty;

        [JsonPropertyName("entries")]
        public List<PlaylistEntry> Entries { get; set; } = new();
    }

    public class PlaylistEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; } = string.Empty;

        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        [JsonPropertyName("webpage_url")]
        public string WebpageUrl { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("extractor")]
        public string Extractor { get; set; } = string.Empty;

        [JsonPropertyName("formats")]
        public List<VideoFormat>? Formats { get; set; }

        [JsonPropertyName("thumbnails")]
        public List<Thumbnail>? Thumbnails { get; set; }
    }
}
