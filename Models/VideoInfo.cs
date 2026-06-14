using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace WinTube.Models
{
    public class VideoInfo
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }

        [JsonPropertyName("duration")]
        public int Duration { get; set; }

        [JsonPropertyName("formats")]
        public List<VideoFormat>? Formats { get; set; }
    }
}
