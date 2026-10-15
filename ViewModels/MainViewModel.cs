using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
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
        private readonly YoutubeService _youtubeService = new();
        private readonly SettingsService _settingsService = new();
        private readonly QueueService _queueService = new();

        // Bytes de la ultima miniatura analizada: cada item de la cola recibe su propia copia.
        private byte[]? _thumbnailBytes;

        // URL de la ultima miniatura analizada, para persistirla por item.
        private string _thumbnailUrl = "";

        // Para saber si el bucle de la cola ya esta corriendo
        private bool _isProcessingQueue = false;

        // Evita persistir la cola mientras se restaura al arrancar.
        private bool _isRestoringQueue = false;

        // Debounce de la persistencia: agrupa rafagas de cambios en un solo guardado.
        private DispatcherTimer? _saveDebounceTimer;
        private bool _saveInProgress;
        private bool _savePending;

        // Limita cuantas miniaturas se descargan a la vez (evita rafagas de N HTTP).
        private readonly SemaphoreSlim _thumbnailSemaphore = new(4);

        // Evita recursion entre "Seleccionar todo" y la seleccion individual.
        private bool _syncingPlaylistSelection;
        #endregion

        #region Interaccion con la vista
        // La vista asigna este delegado para abrir el selector de carpetas.
        public Func<Task<string?>>? FolderPicker { get; set; }

        // La vista se suscribe para mostrar el modal de error.
        public event Action<string>? ErrorRequested;

        // La vista se suscribe para abrir el dialogo de ajustes.
        public event Action? SettingsRequested;

        private void ShowError(string message) => ErrorRequested?.Invoke(message);

        [RelayCommand]
        private void OpenSettings() => SettingsRequested?.Invoke();
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

        // Si esta activo, los items se eliminan solos al completarse.
        [ObservableProperty] private bool autoRemoveCompleted;

        // Carpeta de descarga configurada por el usuario (persistida en disco).
        [ObservableProperty] private string outputFolder = "";

        // Estado de deteccion de playlist (flujo de seleccion y añadido a la cola).
        [ObservableProperty] private bool isPlaylistDetected;
        [ObservableProperty] private string playlistTitle = "";
        [ObservableProperty] private int playlistCount;
        [ObservableProperty] private ObservableCollection<PlaylistEntryViewModel> playlistEntries = new();
        [ObservableProperty] private FormatItem? selectedPlaylistFormat;
        [ObservableProperty] private int selectedPlaylistCount;
        [ObservableProperty] private bool allPlaylistSelected;

        // Presets de formato global para playlists (no dependen de un video concreto).
        public ObservableCollection<FormatItem> PlaylistFormats { get; } = new();

        #endregion

        #region Constructors
        public MainViewModel()
        {
            // Cargamos la configuracion de usuario (carpeta de descarga y preferencias).
            var settings = _settingsService.Load();
            OutputFolder = settings.OutputFolder;
            AutoRemoveCompleted = settings.AutoRemoveCompleted;

            Downloads.CollectionChanged += Downloads_CollectionChanged;

            // Timer de debounce para agrupar los guardados de la cola.
            _saveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _saveDebounceTimer.Tick += (_, _) => FlushSaveQueue();

            BuildPlaylistPresets();

            // Restauramos la cola pendiente de sesiones anteriores (reanudacion).
            RestoreQueue();
        }

        // Presets genericos de calidad que funcionan para cualquier video de la playlist.
        private void BuildPlaylistPresets()
        {
            PlaylistFormats.Add(new FormatItem
            {
                Label = "🎬 Mejor calidad",
                IsAudio = false,
                Extension = "mp4",
                IsGenericSelector = true,
                FormatId = "bestvideo+bestaudio/best"
            });

            foreach (int h in new[] { 1080, 720, 480, 360 })
            {
                PlaylistFormats.Add(new FormatItem
                {
                    Label = $"🎬 {h}p",
                    IsAudio = false,
                    Height = h,
                    Extension = "mp4",
                    IsGenericSelector = true,
                    FormatId = $"bestvideo[height<={h}]+bestaudio/best[height<={h}]"
                });
            }

            PlaylistFormats.Add(new FormatItem
            {
                Label = "🎵 Audio MP3",
                IsAudio = true,
                AudioOutputFormat = "mp3",
                IsGenericSelector = true,
                FormatId = "bestaudio"
            });

            PlaylistFormats.Add(new FormatItem
            {
                Label = "🎵 Audio M4A",
                IsAudio = true,
                AudioOutputFormat = "m4a",
                IsGenericSelector = true,
                FormatId = "bestaudio[ext=m4a]/bestaudio"
            });
        }
        #endregion

        #region Commands
        [RelayCommand]
        private async Task Analyze()
        { 
            if (string.IsNullOrWhiteSpace(Url))
            {
                ShowError("Ingresa una URL de video.");
                return;
            }

            // Normaliamos la URL para que sea más consistente y compatible con yt-dlp
            Url = NormalizeVideoUrl(Url);

            // Almacenamos la Url normalizada por si modifican en la vista
            CurrentNormalizedURL = Url;

            // Limpiar valores anteriores
            ClearInfo();
            IsAnalyzing = true;

            try
            {
                Status = "Analizando video...";

                // Una sola extraccion: si la URL es una playlist devuelve una lista
                // de entradas (ligera); si es un video devuelve toda su info.
                var json = await _youtubeService.GetVideoJson(Url, includePlaylist: true, flatPlaylist: true);

                var playlist = JsonSerializer.Deserialize(json, VideoJsonContext.Default.PlaylistInfo);
                if (playlist != null && playlist.Type == "playlist" && playlist.Entries.Count > 0)
                {
                    // Playlist detectada: mostramos el panel de seleccion en la vista.
                    PlaylistTitle = string.IsNullOrWhiteSpace(playlist.Title) ? "Playlist" : playlist.Title;

                    var entries = new ObservableCollection<PlaylistEntryViewModel>();
                    foreach (var entry in playlist.Entries)
                    {
                        var entryVm = new PlaylistEntryViewModel(entry);
                        entryVm.PropertyChanged += PlaylistEntry_PropertyChanged;
                        entries.Add(entryVm);
                    }

                    PlaylistEntries = entries;
                    PlaylistCount = entries.Count;
                    SelectedPlaylistCount = 0;
                    AllPlaylistSelected = false;
                    SelectedPlaylistFormat = PlaylistFormats.FirstOrDefault();
                    IsPlaylistDetected = true;
                    HasFormats = false;
                    Status = $"Playlist detectada ({PlaylistCount} vídeos)";
                    return;
                }

                var info = JsonSerializer.Deserialize(json, VideoJsonContext.Default.VideoInfo);

                if (info == null)
                {
                    ShowError("No se pudo leer la información del video.");
                    return;
                }

                // Guardar datos básicos
                Title = info.Title ?? "Sin título";
                _thumbnailUrl = info.Thumbnail ?? "";
                _thumbnailBytes = await DownloadHelper.LoadBytesFromUrl(info.Thumbnail!);
                Thumbnail = DownloadHelper.CreateBitmap(_thumbnailBytes);
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
                ShowError(ex.Message);
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
            Thumbnail?.Dispose();
            Thumbnail = null;
            _thumbnailBytes = null;
            SelectedFormat = null;
            Formats.Clear();
            HasFormats = false;
            ResetPlaylistState();
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
                // Cada item recibe su propio Bitmap para poder liberarlo al quitarlo.
                Thumbnail = DownloadHelper.CreateBitmap(_thumbnailBytes),
                Status = DownloadStatus.InQueue,
                SelectedFormat = SelectedFormat,
                // La descarga usa la carpeta configurada por el usuario.
                OutputFolder = this.OutputFolder,
                ThumbnailUrl = _thumbnailUrl,
                // Los errores de descarga se muestran en el modal global.
                OnError = ShowError
            };

            WireItem(newItem);
            Downloads.Add(newItem);

            // Intentamos iniciar la colo (si ya esta corriendo, no hace nada)
            _ = ProccessQueueAsync();           
        }

        // Añade los videos marcados de la playlist, con el formato global elegido.
        [RelayCommand(CanExecute = nameof(CanAddSelectedPlaylistToQueue))]
        private void AddSelectedPlaylistToQueue()
        {
            if (SelectedPlaylistFormat == null) return;

            var format = SelectedPlaylistFormat;
            string label = format.Label;

            foreach (var entry in PlaylistEntries.Where(e => e.IsSelected).ToList())
            {
                var item = new DownloadItemViewModel
                {
                    Url = entry.Url,
                    Title = entry.Title,
                    Progress = 0,
                    Format = label,
                    Status = DownloadStatus.InQueue,
                    SelectedFormat = format,
                    OutputFolder = this.OutputFolder,
                    ThumbnailUrl = entry.ThumbnailUrl,
                    OnError = ShowError
                };

                WireItem(item);
                Downloads.Add(item);

                // Miniatura por entrada (con concurrencia limitada para no saturar).
                if (!string.IsNullOrWhiteSpace(entry.ThumbnailUrl))
                {
                    _ = LoadThumbnailAsync(item, entry.ThumbnailUrl);
                }
            }

            ResetPlaylistState();
            _ = ProccessQueueAsync();
        }

        private bool CanAddSelectedPlaylistToQueue() =>
            SelectedPlaylistCount > 0 && SelectedPlaylistFormat != null;

        [RelayCommand]
        private void CancelAddPlaylist()
        {
            ResetPlaylistState();
        }

        // Recalcula el contador al cambiar la seleccion de una entrada.
        private void PlaylistEntry_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PlaylistEntryViewModel.IsSelected)) return;

            SelectedPlaylistCount = PlaylistEntries.Count(x => x.IsSelected);

            // Refleja en el checkbox "Seleccionar todo" si ya estan todas marcadas,
            // evitando recursion con OnAllPlaylistSelectedChanged.
            if (!_syncingPlaylistSelection)
            {
                _syncingPlaylistSelection = true;
                AllPlaylistSelected = PlaylistEntries.Count > 0
                    && SelectedPlaylistCount == PlaylistEntries.Count;
                _syncingPlaylistSelection = false;
            }
        }

        // Marca/desmarca todas las entradas desde el checkbox "Seleccionar todo".
        partial void OnAllPlaylistSelectedChanged(bool value)
        {
            if (_syncingPlaylistSelection) return;

            _syncingPlaylistSelection = true;
            foreach (var entry in PlaylistEntries)
            {
                entry.IsSelected = value;
            }
            _syncingPlaylistSelection = false;
        }

        partial void OnSelectedPlaylistCountChanged(int value) =>
            AddSelectedPlaylistToQueueCommand.NotifyCanExecuteChanged();

        partial void OnSelectedPlaylistFormatChanged(FormatItem? value) =>
            AddSelectedPlaylistToQueueCommand.NotifyCanExecuteChanged();

        private void ResetPlaylistState()
        {
            foreach (var entry in PlaylistEntries)
            {
                entry.PropertyChanged -= PlaylistEntry_PropertyChanged;
            }

            IsPlaylistDetected = false;
            PlaylistTitle = "";
            PlaylistCount = 0;
            PlaylistEntries = new ObservableCollection<PlaylistEntryViewModel>();
            SelectedPlaylistCount = 0;
            AllPlaylistSelected = false;
            SelectedPlaylistFormat = null;
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

                    // Si el usuario activo el borrado automatico, quitamos los completados.
                    if (AutoRemoveCompleted && nextDownload.Status == DownloadStatus.Completed)
                    {
                        nextDownload.Dispose();
                        Downloads.Remove(nextDownload);
                    }
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

   

        [RelayCommand]
        private async Task ChangeOutputFolder()
        {
            if (FolderPicker == null) return;

            var path = await FolderPicker();
            if (string.IsNullOrWhiteSpace(path)) return;

            OutputFolder = path;
            SaveSettings();
        }

        // Persiste la configuracion de usuario (carpeta de salida + preferencias).
        private void SaveSettings()
        {
            _settingsService.Save(new AppSettings
            {
                OutputFolder = OutputFolder,
                AutoRemoveCompleted = AutoRemoveCompleted
            });
        }

        // Auto-guardado al cambiar "Borrar completados".
        partial void OnAutoRemoveCompletedChanged(bool value) => SaveSettings();

        [RelayCommand]
        private void ExploreOutputFolder()
        {
            if (!ShellHelper.OpenFolder(OutputFolder))
            {
                ShowError("No se pudo abrir la carpeta de descarga.");
            }
        }
        #endregion

        #region Methods

        // Conecta los callbacks comunes de un item de descarga.
        private void WireItem(DownloadItemViewModel item)
        {
            item.OnRemoveRequested = (it) =>
            {
                it.Dispose();
                Downloads.Remove(it);
            };

            // Al reanudar un item pausado/fallido volvemos a disparar el bucle de la cola.
            item.OnResumeRequested = () => _ = ProccessQueueAsync();

            // Persistimos la cola cuando cambia el estado de un item.
            item.OnStatusChangedPersist = _ => RequestSaveQueue();
        }

        // Carga la miniatura de un item de forma asincrona (no bloqueante).
        private async Task LoadThumbnailAsync(DownloadItemViewModel item, string url)
        {
            await _thumbnailSemaphore.WaitAsync();
            try
            {
                var bytes = await DownloadHelper.LoadBytesFromUrl(url);
                item.Thumbnail = DownloadHelper.CreateBitmap(bytes);
            }
            catch
            {
                // Sin miniatura si falla la descarga de la imagen.
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }

        // Solicita guardar la cola con debounce: agrupa rafagas de cambios en un solo guardado.
        private void RequestSaveQueue()
        {
            if (_isRestoringQueue) return;

            _saveDebounceTimer?.Stop();
            _saveDebounceTimer?.Start();
        }

        // Detiene el debounce y guarda de inmediato (usado al cerrar la app).
        private void FlushSaveQueue()
        {
            _saveDebounceTimer?.Stop();
            SaveQueueNow();
        }

        // Construye la lista pendiente y la persiste (en segundo plano).
        private void SaveQueueNow()
        {
            if (_isRestoringQueue) return;

            DownloadJobList list;
            try
            {
                list = BuildJobList();
            }
            catch
            {
                return;
            }

            // Coalescing: si ya hay un guardado en curso, marca pendiente y sale.
            if (_saveInProgress)
            {
                _savePending = true;
                return;
            }

            _saveInProgress = true;
            Task.Run(() =>
            {
                try
                {
                    _queueService.Save(list);
                }
                finally
                {
                    _saveInProgress = false;

                    if (_savePending)
                    {
                        _savePending = false;
                        // Reintenta en el hilo UI para leer las propiedades con seguridad.
                        Dispatcher.UIThread.Post(SaveQueueNow);
                    }
                }
            });
        }

        // Lee el estado actual de la cola (debe ejecutarse en el hilo UI).
        private DownloadJobList BuildJobList()
        {
            var list = new DownloadJobList();
            foreach (var item in Downloads)
            {
                bool persist;
                DownloadStatus status;

                switch (item.Status)
                {
                    case DownloadStatus.InQueue:
                    case DownloadStatus.Paused:
                        persist = true;
                        status = item.Status;
                        break;
                    case DownloadStatus.Downloading:
                        // Al reabrir no hay proceso vivo: se reanuda desde InQueue.
                        persist = true;
                        status = DownloadStatus.InQueue;
                        break;
                    default:
                        persist = false;
                        status = item.Status;
                        break;
                }

                if (!persist) continue;

                var fmt = item.SelectedFormat;
                list.Jobs.Add(new DownloadJob
                {
                    Id = item.Id,
                    Url = item.Url,
                    Title = item.Title,
                    OutputFolder = item.OutputFolder,
                    FormatId = fmt?.FormatId ?? "",
                    Label = fmt?.Label ?? "",
                    IsAudio = fmt?.IsAudio ?? false,
                    Height = fmt?.Height,
                    Width = fmt?.Width,
                    Extension = fmt?.Extension ?? "",
                    AudioOutputFormat = fmt?.AudioOutputFormat ?? "",
                    AudioBitrate = fmt?.AudioBitrate ?? 0,
                    HasAudio = fmt?.HasAudio ?? false,
                    IsGenericSelector = fmt?.IsGenericSelector ?? false,
                    ThumbnailUrl = item.ThumbnailUrl,
                    Status = status
                });
            }

            return list;
        }

        // Restaura la cola persistida de sesiones anteriores y reanuda el procesamiento.
        private void RestoreQueue()
        {
            var saved = _queueService.Load();
            if (saved.Jobs.Count == 0) return;

            _isRestoringQueue = true;
            try
            {
                foreach (var job in saved.Jobs)
                {
                    var item = CreateItemFromJob(job);
                    Downloads.Add(item);
                }
            }
            finally
            {
                _isRestoringQueue = false;
            }

            // La cola vuelve a persistirse conforme avance; limpiamos el archivo obsoleto.
            _queueService.Clear();

            // Reanudamos el procesamiento de los items restaurados.
            _ = ProccessQueueAsync();
        }

        // Reconstruye un item de descarga a partir de un trabajo persistido.
        private DownloadItemViewModel CreateItemFromJob(DownloadJob job)
        {
            FormatItem? fmt = null;
            if (!string.IsNullOrWhiteSpace(job.FormatId)
                || !string.IsNullOrWhiteSpace(job.Label)
                || job.IsAudio)
            {
                fmt = new FormatItem
                {
                    FormatId = job.FormatId,
                    Label = job.Label,
                    IsAudio = job.IsAudio,
                    Height = job.Height,
                    Width = job.Width,
                    Extension = job.Extension,
                    AudioOutputFormat = job.AudioOutputFormat,
                    AudioBitrate = job.AudioBitrate,
                    HasAudio = job.HasAudio,
                    IsGenericSelector = job.IsGenericSelector,
                    IsSelected = true
                };
            }

            var item = new DownloadItemViewModel
            {
                Id = job.Id,
                Url = job.Url,
                Title = string.IsNullOrWhiteSpace(job.Title) ? "Sin título" : job.Title,
                Progress = 0,
                Format = fmt?.Label ?? "Mejor disponible",
                Status = job.Status == DownloadStatus.Paused
                    ? DownloadStatus.Paused
                    : DownloadStatus.InQueue,
                SelectedFormat = fmt,
                OutputFolder = string.IsNullOrWhiteSpace(job.OutputFolder) ? OutputFolder : job.OutputFolder,
                ThumbnailUrl = job.ThumbnailUrl,
                OnError = ShowError
            };

            WireItem(item);

            // Recargamos la miniatura si se guardo su URL.
            if (!string.IsNullOrWhiteSpace(job.ThumbnailUrl))
            {
                _ = LoadThumbnailAsync(item, job.ThumbnailUrl);
            }

            return item;
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
        private string NormalizeVideoUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            url = url.Trim();

            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLower();

                // --- YOUTUBE ---
                if (host.Contains("youtube.com") || host.Contains("youtu.be"))
                {
                    return NormalizeYoutubeUrl(url);
                }

                // --- FACEBOOK ---
                if (host.Contains("facebook.com") || host.Contains("fb.watch") || host.Contains("fb.com"))
                {
                    // Facebook Reels, Videos, Watch
                    if (url.Contains("/reel/") || url.Contains("/watch/") || url.Contains("/videos/"))
                    {
                        // Limpiar parámetros de seguimiento (ej: ?_rdc=1&_rdr)
                        var cleanUrl = url.Split('?')[0];
                        return cleanUrl;
                    }
                    // fb.watch URLs cortas
                    if (host.Contains("fb.watch"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- TIKTOK ---
                if (host.Contains("tiktok.com") || host.Contains("vt.tiktok.com"))
                {
                    // TikTok videos y reels
                    if (url.Contains("/video/") || url.Contains("/v/"))
                    {
                        // Limpiar parámetros de seguimiento
                        return url.Split('?')[0];
                    }
                    // URLs cortas
                    if (host.Contains("vt.tiktok.com"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- INSTAGRAM ---
                if (host.Contains("instagram.com") || host.Contains("instagr.am"))
                {
                    // Reels, Videos, Posts
                    if (url.Contains("/reel/") || url.Contains("/p/") || url.Contains("/tv/"))
                    {
                        // Limpiar parámetros como ?igshid=
                        return url.Split('?')[0];
                    }
                }

                // --- TWITTER / X ---
                if (host.Contains("twitter.com") || host.Contains("x.com"))
                {
                    // Tweets con video
                    if (url.Contains("/status/"))
                    {
                        // Limpiar parámetros de seguimiento
                        return url.Split('?')[0];
                    }
                }

                // --- VIMEO ---
                if (host.Contains("vimeo.com"))
                {
                    // Vimeo videos (formato: vimeo.com/ID)
                    if (url.Contains("/channels/") || url.Contains("/groups/") || url.Contains("/album/"))
                    {
                        // Extraer el ID del video
                        var segments = uri.AbsolutePath.Split('/');
                        var videoId = segments.LastOrDefault(s => !string.IsNullOrEmpty(s) && char.IsDigit(s[0]));
                        if (!string.IsNullOrEmpty(videoId))
                        {
                            return $"https://vimeo.com/{videoId}";
                        }
                    }
                    else
                    {
                        // URL limpia simple
                        return url.Split('?')[0];
                    }
                }

                // --- DAILYMOTION ---
                if (host.Contains("dailymotion.com") || host.Contains("dailymotion.com"))
                {
                    if (url.Contains("/video/"))
                    {
                        // Extraer el ID del video (ej: /video/x12345_)
                        var parts = url.Split('/');
                        var videoPart = parts.FirstOrDefault(p => p.StartsWith("x") && p.Length > 5);
                        if (!string.IsNullOrEmpty(videoPart))
                        {
                            return $"https://www.dailymotion.com/video/{videoPart}";
                        }
                    }
                    return url.Split('?')[0];
                }

                // --- TWITCH ---
                if (host.Contains("twitch.tv"))
                {
                    // Clips y videos
                    if (url.Contains("/clip/"))
                    {
                        return url.Split('?')[0];
                    }
                    if (url.Contains("/videos/"))
                    {
                        var videoId = uri.AbsolutePath.Split('/').Last();
                        return $"https://www.twitch.tv/videos/{videoId}";
                    }
                }

                // --- REDDIT ---
                if (host.Contains("reddit.com") || host.Contains("redd.it"))
                {
                    if (url.Contains("/comments/"))
                    {
                        // Limpiar parámetros (ej: ?utm_source=...)
                        return url.Split('?')[0];
                    }
                    if (host.Contains("redd.it"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- MEDIAFIRE ---
                if (host.Contains("mediafire.com"))
                {
                    if (url.Contains("/file/"))
                    {
                        // Limpiar parámetros
                        return url.Split('?')[0];
                    }
                }

                // --- IMGUR ---
                if (host.Contains("imgur.com"))
                {
                    if (url.Contains("/gallery/") || url.Contains("/a/"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- VK (Vkontakte) ---
                if (host.Contains("vk.com") || host.Contains("vkontakte.ru"))
                {
                    if (url.Contains("/video") || url.Contains("/clip"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- 9GAG ---
                if (host.Contains("9gag.com"))
                {
                    if (url.Contains("/gag/"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- RUMBLE ---
                if (host.Contains("rumble.com"))
                {
                    if (url.Contains("/v/") || url.Contains("/video/"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- BITCHUTE ---
                if (host.Contains("bitchute.com"))
                {
                    if (url.Contains("/video/"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- ODYSEE ---
                if (host.Contains("odysee.com"))
                {
                    if (url.Contains("/@") || url.Contains("/embed/"))
                    {
                        // Extraer solo la parte de la URL sin parámetros
                        return url.Split('?')[0];
                    }
                }

                // --- PEERTUBE ---
                if (host.Contains("peertube") || host.Contains("peertube."))
                {
                    if (url.Contains("/w/") || url.Contains("/videos/watch/"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- TELEGRAM ---
                if (host.Contains("t.me"))
                {
                    if (url.Contains("/s/") || uri.AbsolutePath.Length > 1)
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- BITCHUTE ---
                if (host.Contains("bitchute.com"))
                {
                    if (url.Contains("/video/"))
                    {
                        return url.Split('?')[0];
                    }
                }

                // --- FACEBOOK WATCH (alternativo) ---
                if (host.Contains("watch.facebook.com"))
                {
                    return url.Split('?')[0];
                }

                // Si no coincide con ningún patrón conocido, devolver la URL original
                return url;
            }
            catch
            {
                // Si algo falla parseando la URL, devolvemos la original como respaldo
                return url;
            }
        }

        // Normalizar URL de YouTube para que sea consistente y compatible con yt-dlp
        private string NormalizeYoutubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            url = url.Trim();

            try
            {
                var uri = new Uri(url);

                // Soporte para URLs de formato corto youtu.be
                if (uri.Host.Contains("youtu.be"))
                {
                    // El ID es el primer segmento de la ruta (eliminando el '/' inicial)
                    string videoId = uri.AbsolutePath.TrimStart('/');

                    // Si la URL corta tiene parámetros ocultos (ej: ?si=123), los limpiamos
                    if (videoId.Contains("?"))
                    {
                        videoId = videoId.Split('?')[0];
                    }

                    // Conservamos el parámetro list si forma parte de una playlist.
                    string shortList = HttpUtility.ParseQueryString(uri.Query)["list"] ?? "";
                    if (!string.IsNullOrEmpty(shortList))
                    {
                        return $"https://www.youtube.com/watch?v={videoId}&list={shortList}";
                    }

                    return $"https://www.youtube.com/watch?v={videoId}";
                }

                // Playlist "pura" (youtube.com/playlist?list=...)
                if (url.Contains("youtube.com/playlist"))
                {
                    var playlistParams = HttpUtility.ParseQueryString(uri.Query);
                    string listId = playlistParams["list"] ?? "";
                    if (!string.IsNullOrEmpty(listId))
                    {
                        return $"https://www.youtube.com/playlist?list={listId}";
                    }
                    return url;
                }

                // Soporte para URLs estándar (incluyendo m.youtube.com y www.youtube.com)
                if (url.Contains("youtube.com/watch"))
                {
                    // Extraemos de forma segura los parámetros de la Query String (?v=...&list=...)
                    var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                    string videoId = queryParameters["v"] ?? "";
                    string listId = queryParameters["list"] ?? "";

                    if (!string.IsNullOrEmpty(videoId))
                    {
                        // Conservamos la lista si el link apunta a una playlist.
                        return string.IsNullOrEmpty(listId)
                            ? $"https://www.youtube.com/watch?v={videoId}"
                            : $"https://www.youtube.com/watch?v={videoId}&list={listId}";
                    }
                }

                // Soporte para Shorts (youtube.com/shorts/ID)
                if (url.Contains("youtube.com/shorts/"))
                {
                    var uri2 = new Uri(url);
                    string videoId = uri2.AbsolutePath.Split('/').Last();
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
            RequestSaveQueue();
        }
        #endregion

        public void Dispose()
        {
            Downloads.CollectionChanged -= Downloads_CollectionChanged;

            // Volcamos de inmediato cualquier guardado pendiente del debounce.
            FlushSaveQueue();

            _thumbnailSemaphore.Dispose();

            foreach (var item in Downloads)
            {
                item.Dispose();
            }

            Thumbnail?.Dispose();
            Thumbnail = null;
        }
    }
}
