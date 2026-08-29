using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace WinTube.Models
{
    public class VideoInfo
    {
        //[JsonPropertyName("title")]
        //public string? Title { get; set; }

        //[JsonPropertyName("thumbnail")]
        //public string? Thumbnail { get; set; }

        //[JsonPropertyName("duration")]
        //public int Duration { get; set; }

        //[JsonPropertyName("formats")]
        //public List<VideoFormat>? Formats { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("uploader")]
        public string Uploader { get; set; }

        [JsonPropertyName("uploader_id")]
        public string UploaderId { get; set; }

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; }

        [JsonPropertyName("view_count")]
        public long? ViewCount { get; set; }

        [JsonPropertyName("like_count")]
        public long? LikeCount { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("duration_string")]
        public string DurationString { get; set; }

        [JsonPropertyName("upload_date")]
        public string UploadDate { get; set; }

        [JsonPropertyName("extractor")]
        public string Extractor { get; set; } // "youtube", "facebook", "twitter", etc.

        [JsonPropertyName("extractor_key")]
        public string ExtractorKey { get; set; }

        [JsonPropertyName("webpage_url")]
        public string WebpageUrl { get; set; }

        [JsonPropertyName("original_url")]
        public string OriginalUrl { get; set; }

        [JsonPropertyName("formats")]
        public List<VideoFormat> Formats { get; set; }

        [JsonPropertyName("thumbnails")]
        public List<Thumbnail> Thumbnails { get; set; }

        [JsonPropertyName("automatic_captions")]
        public Dictionary<string, List<Subtitle>> AutomaticCaptions { get; set; }

        [JsonPropertyName("subtitles")]
        public Dictionary<string, List<Subtitle>> Subtitles { get; set; }

        [JsonPropertyName("requested_formats")]
        public List<VideoFormat> RequestedFormats { get; set; }

        [JsonPropertyName("_filename")]
        public string Filename { get; set; }

        // Propiedades específicas para plataformas
        [JsonPropertyName("repost_count")]
        public int? RepostCount { get; set; } // Para TikTok

        [JsonPropertyName("comment_count")]
        public int? CommentCount { get; set; } // Para Facebook/Instagram

        [JsonPropertyName("live_status")]
        public string LiveStatus { get; set; } // Para Twitch/YouTube Live

        // Propiedades de calidad
        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("resolution")]
        public string Resolution { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("format_id")]
        public string FormatId { get; set; }

        [JsonPropertyName("tbr")]
        public double? Tbr { get; set; }

        [JsonPropertyName("vbr")]
        public double? Vbr { get; set; }

        [JsonPropertyName("abr")]
        public double? Abr { get; set; }

        [JsonPropertyName("acodec")]
        public string Acodec { get; set; }

        [JsonPropertyName("vcodec")]
        public string Vcodec { get; set; }
    }


    public class Thumbnail
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }
    }

    public class Subtitle
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("ext")]
        public string Extension { get; set; }
    }
}
