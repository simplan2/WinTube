using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using WinTube.Helpers;
using WinTube.Models;
using WinTube.Services;

namespace WinTube.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        #region Fields 
        private readonly DownloadService downloadService = new();
        private readonly YoutubeService youtubeService = new();
        private DownloadItem? currentItem;

        private bool isDownloading = false;
        private readonly object lockObj = new();
        #endregion

        #region Properties
        [ObservableProperty]
        private ObservableCollection<DownloadItem> downloads = new();

        [ObservableProperty]
        private string url = "";

        [ObservableProperty]
        private string title = "";

        [ObservableProperty]
        private string status = "";

        public string MyStatus => "FUNCIONA MVVM";

        [ObservableProperty]
        private ObservableCollection<FormatItem> formats = new();

        [ObservableProperty]
        private FormatItem? selectedFormat;

        [ObservableProperty]
        private Bitmap? thumbnail;

        [ObservableProperty]
        private bool isAnalyzing = false;
        #endregion

        #region Constructors
        public MainViewModel()
        {
            downloadService.ProgressChanged += OnProgress;
        }
        #endregion

        #region Commands
        [RelayCommand]
        private async Task Analyze()
        {
            Url = NormalizeYoutubeUrl(Url);

            if (string.IsNullOrWhiteSpace(Url))
            {
                Status = "Ingresa una URL";
                return;
            }

            // Limpiar valores anteriores
            ClearInfo();
            IsAnalyzing = true;

            try
            {
                Status = "Analizando video...";

                var json = await youtubeService.GetVideoJson(Url);

                var info = JsonSerializer.Deserialize<VideoInfo>(json);

                if (info == null)
                {
                    Status = "No se pudo leer el video";
                    return;
                }

                // Guardar datos básicos
                Title = info.Title ?? "Sin título";
                Thumbnail = await DownloadHelper.LoadFromUrl(info.Thumbnail!);
                Status = "Video cargado";

                // ==========================================
                // Formato video (Optimizado para yt-dlp)
                // ==========================================

                var videoFormats = info.Formats!
                    .Where(f => f.Height.HasValue && f.Height.Value > 0 && f.VCodec != "none")
                    .GroupBy(f => f.Height!.Value)
                    .Select(grupo => grupo
                        // Prioridad 1: Los que sí tengan audio integrado (formatos menores o muxed)
                        .OrderByDescending(f => f.ACodec != "none" && !string.IsNullOrEmpty(f.ACodec))
                        // Prioridad 2: Preferir contenedores mp4 si están disponibles
                        .ThenByDescending(f => f.Ext == "mp4")
                        // Prioridad 3: El que tenga mayor bitrate (mejor calidad de imagen)
                        .ThenByDescending(f => f.VideoBitrate ?? 0)
                        .First()
                    )
                    .OrderByDescending(f => f.Height);

                foreach (var video in videoFormats)
                {
                    long size = video.FileSize ?? video.FileSizeApprox ?? 0;

                    // 1. Obtener Ancho y Alto de forma segura
                    int ancho = video.Width ?? 0;
                    int alto = video.Height ?? 0;

                    // 2. Determinar la etiqueta de calidad base (ej: 720p, 1080p) basada en el alto
                    string qualityLabel = GetQualityLabel(alto);

                    // 3. Formatear la resolución exacta si ambos valores existen
                    string resolutionExact = (ancho > 0 && alto > 0) ? $" {ancho}x{alto}" : "";

                    // 4. Formatear el tamaño de descarga
                    string sizeLabel = size > 0 ? $" - [{DownloadHelper.FormatSize(size)}]" : " - Tamaño desconocido";

                    // 5. Determinar el estado del audio/video de forma más amigable para el usuario
                    //string audioStatus = video.ACodec == "none"
                    //    ? " 🎬 (Alta Calidad - Se unirá con Audio HD)"
                    //    : " 🎥 (Video + Audio Integrado)";

                    // Resultado final de la etiqueta: 
                    // Ejemplo HD parcial: "720p [1280x692] - 45.2 MB 🎬 (Alta Calidad - Se unirá con Audio HD)"
                    // Ejemplo Estándar:  "480p [854x480] - 12.4 MB 🎥 (Video + Audio Integrado)"
                    //string label = $"{qualityLabel}{resolutionExact}{sizeLabel}{audioStatus}";
                    string label = $"🎬 Video {resolutionExact}{sizeLabel}";

                    Formats.Add(new FormatItem
                    {
                        FormatId = video.FormatId ?? "",
                        Height = alto,
                        Width = ancho, // Asegúrate de guardar el Width en tu FormatItem si lo necesitas después
                        IsAudio = false,
                        Extension = video.Ext ?? "",
                        Label = label,
                        HasAudio = video.ACodec != "none" && video.ACodec != null // ¡Crucial para tus argumentos de descarga!
                    });
                }

                // Formatos de audio
                var audioFormats = info.Formats!
                    .Where(f =>
                    f.VCodec == "none" && f.ACodec != "none" && f.AudioBitrate.HasValue);

                var opus = audioFormats.Where(f => f.Ext == "webm")
                    .OrderByDescending(f => f.AudioBitrate)
                    .FirstOrDefault();

                var m4a = audioFormats
                    .Where(f => f.Ext == "m4a")
                    .OrderByDescending(f => f.AudioBitrate)
                    .FirstOrDefault();

                if (opus != null)
                {
                    double bitrate = opus.AudioBitrate ?? 160;

                    double size = DownloadHelper.EstimateAudioSizeMb(info.Duration, bitrate);

                    Formats.Add(new FormatItem
                    {
                        FormatId = opus.FormatId!,
                        IsAudio = true,
                        AudioBitrate = bitrate,
                        AudioOutputFormat = "opus",
                        Label = $"🎵 Audio Opus (~{size:0.0} MB)"
                    });
                }

                if (m4a != null)
                {
                    double bitrate = m4a.AudioBitrate ?? 128;

                    double size = DownloadHelper.EstimateAudioSizeMb(info.Duration, bitrate);

                    Formats.Add(new FormatItem
                    {
                        FormatId = m4a.FormatId!,
                        IsAudio = true,
                        AudioBitrate = bitrate,
                        AudioOutputFormat = "m4a",
                        Label = $"🎵 Audio M4A (~{size:0.0} MB)"
                    });
                }

                Formats.Add(new FormatItem
                {
                    IsAudio = true,
                    AudioOutputFormat = "mp3",
                    AudioBitrate = 256, // valor típico
                    Label = "🎵 Audio MP3 (convertido)"
                });

                if (Formats.Any())
                {
                    SelectedFormat = Formats.First();
                    SelectedFormat.IsSelected = true;
                }
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private void ClearInfo()
        {
            Status = "";
            Title = "";
            Thumbnail = null;
            SelectedFormat = null;
            Formats.Clear();
        }

        [RelayCommand]
        private async Task Download()
        {
            if (string.IsNullOrWhiteSpace(Url))
                return;

            Downloads.Add(new DownloadItem
            {
                Url = Url,
                Title = Title,
                Progress = 0,
                Status = "En cola",
                SelectedFormat = SelectedFormat
            });

            Url = "";
        }

        [RelayCommand]
        private void AddToQueue()
        {
            if (string.IsNullOrWhiteSpace(Url))
                return;

            Downloads.Add(new DownloadItem
            {
                Url = Url,
                Title = Title,
                Progress = 0,
                Status = "En cola",
                SelectedFormat = SelectedFormat
            });

            Url = "";
        }

        [RelayCommand]
        private async Task StartQueue()
        {
            if (isDownloading)
                return;

            isDownloading = true;

            string output = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "YoutubeDownloads");

            try
            {
                Directory.CreateDirectory(output);

                foreach (var item in Downloads)
                {
                    if (item.Progress >= 100)
                        continue;

                    currentItem = item;

                    item.Status = "Descargando...";
                    item.Progress = 0;

                    if (item.SelectedFormat == null)
                    {
                        item.Status = "Formato no seleccionado";
                        continue;
                    }

                    await downloadService.DownloadVideo(
                        item.Url,
                        item.SelectedFormat,
                        output);
                    Status = "Completado...";
                }
            }
            catch (Exception ex)
            {
                Status = $"ERROR: {ex.Message}";
            }
            finally
            {
                currentItem = null;
                isDownloading = false;
            }

        }
        #endregion

        #region Methods
        private void OnProgress(DownloadProgress p)
        {
            lock (lockObj)
            {
                if (currentItem == null)
                    return;

                currentItem.Progress = p.Percentage;
                currentItem.Status = $"Descargando {p.Speed} | ETA {p.Eta}";
            }
        }

        private string GetQualityLabel(int height)
        {
            return height switch
            {
                480 => "📹 480p",
                720 => "📹 720p HD",
                1080 => "📹 1080p Full HD",
                1440 => "📹 1440p 2K",
                2160 => "📹 2160p 4K",
                _ => $"📹 {height}p"
            };
        }

        // Normalizar URL
        private string NormalizeYoutubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            url = url.Trim();

            try
            {
                // Soporte para URLs de formato corto youtu.be
                if (url.Contains("youtu.be/"))
                {
                    var uri = new Uri(url);
                    // El ID es el primer segmento de la ruta (eliminando el '/' inicial)
                    string videoId = uri.AbsolutePath.TrimStart('/');

                    // Si la URL corta tiene parámetros ocultos (ej: ?si=123), los limpiamos
                    if (videoId.Contains("?"))
                    {
                        videoId = videoId.Split('?')[0];
                    }

                    return $"https://www.youtube.com/watch?v={videoId}";
                }

                // Soporte para URLs estándar (incluyendo m.youtube.com y www.youtube.com)
                if (url.Contains("youtube.com/watch"))
                {
                    var uri = new Uri(url);
                    // Extraemos de forma segura los parámetros de la Query String (?v=...&list=...)
                    var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                    string videoId = queryParameters["v"] ?? "";

                    if (!string.IsNullOrEmpty(videoId))
                    {
                        // Devolvemos la URL limpia ÚNICAMENTE con el ID del video
                        return $"https://www.youtube.com/watch?v={videoId}";
                    }
                }

                // Soporte para Shorts (youtube.com/shorts/ID)
                if (url.Contains("youtube.com/shorts/"))
                {
                    var uri = new Uri(url);
                    string videoId = uri.AbsolutePath.Split('/').Last();
                    return $"https://www.youtube.com/watch?v={videoId}";
                }
            }
            catch
            {
                // Si algo falla parseando la URL, devolvemos la original como respaldo
                return url;
            }

            return url;
        }
        #endregion
    }
}
