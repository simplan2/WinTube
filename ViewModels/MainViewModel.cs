using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        private readonly DownloadService _downloadService = new();
        private readonly YoutubeService _youtubeService = new();
        //private DownloadItem? _currentItem;    
        private readonly object lockObj = new();
        private CancellationTokenSource? _cts;
        // Para saber si el bucle de la cola ya esa corriendo
        private bool _isProcessingQueue = false;
        #endregion

        #region Properties
        [ObservableProperty]
        private ObservableCollection<DownloadItemViewModel> downloads = new();

        [ObservableProperty]
        private string url = "";

        public string CurrentNormalizedURL { get; private set; } = string.Empty;

        [ObservableProperty]
        private string title = "";
          
        [ObservableProperty]
        private ObservableCollection<FormatItem> formats = new();

        [ObservableProperty]
        private FormatItem? selectedFormat;

        [ObservableProperty]
        private Bitmap? thumbnail;

        [ObservableProperty]
        private bool isAnalyzing = false;

        [ObservableProperty]
        private bool hasFormats = false;

        public bool IsDownloadsEmpty => Downloads.Count == 0;

        [ObservableProperty] private bool isDownloading;
        [ObservableProperty] private bool isPaused;
        [ObservableProperty] private string status = "Listo"; // Estado inicial

        #endregion

        #region Constructors
        public MainViewModel()
        {
            //_downloadService.ProgressChanged += OnDownloadProgressChanged;
            Downloads.CollectionChanged += Downloads_CollectionChanged;
        }
        #endregion

        #region Commands
        [RelayCommand]
        private async Task Analyze()
        { 
            if (string.IsNullOrWhiteSpace(Url))
            {
                Status = "Ingresa una URL";
                return;
            }

            // Actualizamos en la vista
            Url = NormalizeYoutubeUrl(Url);

            // Almacenamos la Url normalizada por si modifican en la vista
            CurrentNormalizedURL = Url;

            // Limpiar valores anteriores
            ClearInfo();
            IsAnalyzing = true;

            try
            {
                Status = "Analizando video...";

                var json = await _youtubeService.GetVideoJson(Url);

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
                    int width = video.Width ?? 0;
                    int heigth = video.Height ?? 0;

                    // 2. Determinar la etiqueta de calidad base (ej: 720p, 1080p) basada en el alto
                    string qualityLabel = GetQualityLabel(heigth);

                    // 3. Formatear la resolución exacta si ambos valores existen
                    string resolutionExact = (width > 0 && heigth > 0) ? $" {width}x{heigth}" : "";

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
                        Height = heigth,
                        Width = width, // Asegúrate de guardar el Width en tu FormatItem si lo necesitas después
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

                HasFormats = true;
            }
            catch (Exception ex)
            {
                Status = $"{ex.Message}";
                HasFormats = false;
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
            HasFormats = false;
        }

        // Command para agregar un video a la lista e iniciar el procesamiento
        [RelayCommand]
        private void AddToQueue()
        {           
            if (!HasFormats || SelectedFormat == null) return;

            // Construir info según si se selecciono audio o video
            var formatInfo = string.Empty;
            if (SelectedFormat.IsAudio)
            {
                formatInfo = $"Audio {SelectedFormat.Extension.ToUpper()}";
            }
            else
            {
                formatInfo = $"{SelectedFormat.Height}p {SelectedFormat.Extension.ToUpper()}";
            }

            // Add a la lista
            var newItem = new DownloadItemViewModel
            {
                Url = CurrentNormalizedURL,
                Title = Title,
                Progress = 0,
                Format = formatInfo,
                Thumbnail = Thumbnail,
                Status = DownloadStatus.InQueue,
                SelectedFormat = SelectedFormat
            };

            // Le decimos qué hacer cuando ejecute su Removecommand
            newItem.OnRemoveRequested = (item) => Downloads.Remove(item);

            Downloads.Add(newItem);

            // Intentamos iniciar la colo (si ya esta corriendo, no hace nada)
            _ = ProccessQueueAsync();           
        }

        /// <summary>
        /// El buble que procesa los items de la lista uno por uno
        /// </summary>
        /// <returns></returns>
        private async Task ProccessQueueAsync()
        {
            if (_isProcessingQueue == true) return;

            _isProcessingQueue = true;
            try
            {
                // Mienras exista items con estatus "InQueue"
                while (Downloads.Any(d => d.Status == DownloadStatus.InQueue))
                {
                    var nextDownload = Downloads.First(d => d.Status == DownloadStatus.InQueue);

                    // Ejecutamos y es speramos a que complete, falle o se cancele
                    await nextDownload.StartDownloadAsync();                    
                }
            }
            catch (Exception)
            {
                // Manejo global del errores del bucle
                Debug.WriteLine("Error en el bucle de descarga");
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        //[RelayCommand] // Inicia la cola de descarga
        //private async Task StartQueue()
        //{
        //    if (IsDownloading)
        //        return;

        //    IsDownloading = true;
        //    IsPaused = false;
        //    _cts = new CancellationTokenSource();

        //    try
        //    {               

        //        foreach (var item in Downloads)
        //        {
        //            if (item.Progress >= 100 || item.Status == DownloadStatus.Completed)
        //                continue;

        //            _currentItem = item;
        //            _currentItem.Status = DownloadStatus.Downloading;
        //            //item.Progress = 0;

        //            if (item.SelectedFormat == null)
        //            {                 
        //                continue;
        //            }

        //            // Descargar el video usando el servicio
        //            //await _downloadService.RunYtDlpAsync(
        //            //    item.Url,
        //            //    item.SelectedFormat,
        //            //    _outputFolder,
        //            //    _cts.Token);

          
        //            Status = "Descarga terminada...";
        //            _currentItem.Status = DownloadStatus.Completed;
        //        }
        //    }
        //    catch (OperationCanceledException)
        //    {
        //        Status = "Descarga cancelada";
        //        if (_currentItem != null && _currentItem.Progress < 100)
        //        {
        //            _currentItem.Status = DownloadStatus.Canceled;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Status = $"ERROR: {ex.Message}";
        //        if (_currentItem != null) _currentItem.Status = DownloadStatus.Failed;
        //    }
        //    finally
        //    {
        //        _currentItem = null;
        //        IsDownloading = false;
        //        IsPaused = false;
        //        _cts.Dispose();
        //        _cts = null;
        //    }
        //}

        //[RelayCommand]
        //private void PauseQueue()
        //{
        //    if (!IsDownloading || IsPaused)
        //        return;

        //    _downloadService.Pause();
        //    IsPaused = true;
        //    if (_currentItem != null)
        //    {
        //        _currentItem.Status = DownloadStatus.Paused;
        //    }

        //    Status = "Descarga pausada";
        //}

        //[RelayCommand]
        //private void ResumeQueue()
        //{
        //    if (!IsDownloading || !IsPaused)
        //        return;

        //    _downloadService.Resume();
        //    IsPaused = false;
        //    if (_currentItem != null)
        //    {
        //        _currentItem.Status = DownloadStatus.Downloading;
        //    }
        //    Status = "Descarga reanudada";
        //}

        //[RelayCommand]
        //private void CancelQueue()
        //{           
        //    _cts?.Cancel();
        //}

        [RelayCommand]
        private void ExploreOutputFolder()
        {
            var pathFolder = PathHelper.GetDefaultOutputFolder();
            if (string.IsNullOrEmpty(pathFolder)) return;

            try
            {
                // Método simple que funciona en todas las plataformas
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", $"\"{pathFolder}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", $"\"{pathFolder}\"");
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", $"\"{pathFolder}\"");
                }
                //if (!Directory.Exists(pathFolder))
                //{
                //    Directory.CreateDirectory(pathFolder);
                //}
                //Process.Start(new ProcessStartInfo
                //{
                //    FileName = pathFolder,
                //    UseShellExecute = true,
                //    Verb = "open"
                //});
            }
            catch (Exception ex)
            {
                Status = $"Error al abrir la carpeta: {ex.Message}";
            }
        }
        #endregion

        #region Methods
        private async Task StartDownloadAsync()
        {
            throw new NotImplementedException();
        }


        //private void OnDownloadProgressChanged(DownloadProgress progress)
        //{
        //    lock (lockObj)
        //    {
        //        if (_currentItem == null)
        //            return;

        //        _currentItem.Progress = progress.Percentage;
        //        _currentItem.ProgressMessage = IsPaused
        //            ? "Pausado"
        //            : $"{progress.Speed} | ETA {progress.Eta}";
        //    }
        //}

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

        private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {   
            base.OnPropertyChanged(nameof(IsDownloadsEmpty));
        }
        #endregion

        public void Dispose()
        {
            //_downloadService.ProgressChanged -= OnDownloadProgressChanged;
            Downloads.CollectionChanged -= Downloads_CollectionChanged;
        }
    }
}
