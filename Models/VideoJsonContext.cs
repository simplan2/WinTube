using System.Text.Json.Serialization;

namespace WinTube.Models
{
    // Contexto de serializacion por codigo fuente: mantiene el deserializado
    // de VideoInfo funcional incluso con PublishTrimmed=true (Release).
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(VideoInfo))]
    [JsonSerializable(typeof(PlaylistInfo))]
    [JsonSerializable(typeof(PlaylistEntry))]
    [JsonSerializable(typeof(AppSettings))]
    [JsonSerializable(typeof(DownloadJob))]
    [JsonSerializable(typeof(DownloadJobList))]
    public partial class VideoJsonContext : JsonSerializerContext
    {
    }
}
