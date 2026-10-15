using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinTube.Helpers;
using WinTube.Models;
using WinTube.Services;

namespace WinTube.ViewModels
{
    public partial class DownloadItemViewModel : ObservableObject
    {
        #region Fields
        private CancellationTokenSource? _cts;
        private readonly DownloadService _downloadService = new();

        // Distingue una cancelacion por "pausar" de una cancelacion real.
        private bool _isPaused;

        // Ultimo porcentaje notificado a la UI, para suavizar la frecuencia de updates.
        private double _lastReportedProgress = -1;

        // Marca si al detener la descarga hay que borrar los archivos parciales (.part).
        private bool _deletePartialOnStop;
        #endregion


        #region Properties
        // Identificador estable del item: nombre de su carpeta temporal de descarga.
        public Guid Id { get; set; } = Guid.NewGuid();

        // Acción que se invoca cuando se solicita eliminar este elemento de descarga
        public Action<DownloadItemViewModel>? OnRemoveRequested { get; set; }

        // Acción que se invoca cuando la descarga falla, para notificar a la vista.
        public Action<string>? OnError { get; set; }

        // Acción que se invoca al reanudar, para volver a disparar el bucle de la cola.
        public Action? OnResumeRequested { get; set; }

        // Acción que se invoca cuando el estado cambia, para persistir la cola.
        public Action<DownloadItemViewModel>? OnStatusChangedPersist { get; set; }

        [ObservableProperty]
        private string url = string.Empty;

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private double progress;

        [ObservableProperty]
        private DownloadStatus status = default;

        [ObservableProperty]
        private FormatItem? selectedFormat;


        [ObservableProperty]
        private string statusMessage = string.Empty;

        [ObservableProperty]
        private string progressMessage = string.Empty;

        [ObservableProperty]
        private Bitmap? thumbnail;

        [ObservableProperty]
        private string format = string.Empty;

        // URL de origen de la miniatura, para recargarla al restaurar la cola.
        public string ThumbnailUrl { get; set; } = string.Empty;

        public string OutputFolder { get; set; } = string.Empty;
        #endregion

        #region Constructors
        public DownloadItemViewModel()
        {
            _downloadService.ProgressChanged += _downloadService_ProgressChanged;
        }

        private void _downloadService_ProgressChanged(DownloadProgress progress)
        {
            // Suavizado: ignorar cambios subporcentuales para no inundar el hilo UI.
            if (progress.Percentage < 100 &&
                Math.Abs(progress.Percentage - _lastReportedProgress) < 0.5)
            {
                return;
            }
            _lastReportedProgress = progress.Percentage;

            // El progreso llega desde el hilo que lee la salida de yt-dlp.
            // Marshalar al hilo de UI evita tocar propiedades observables en otro hilo.
            Dispatcher.UIThread.Post(() =>
            {
                Progress = progress.Percentage;
                if (!string.IsNullOrEmpty(progress.Speed) && !string.IsNullOrEmpty(progress.Eta))
                {
                    ProgressMessage = $"{progress.Percentage:F1}% de {progress.TotalSize} • {progress.Speed} • ETA:{progress.Eta}";
                }
                else
                {
                    ProgressMessage = $"{progress.Percentage:F1}%";
                }
            });
        }
        #endregion

        #region Commands
        [RelayCommand(CanExecute = nameof(CanPauseDownload))]
        private void Pause()
        {
            _isPaused = true;
            _cts?.Cancel();
            Status = DownloadStatus.Paused;
        }

        [RelayCommand(CanExecute = nameof(CanResumeDownload))]
        private void Resume()
        {
            // Cancelamos la intencion de pausa antes de volver a encolar; asi el
            // catch de OperationCanceledException no vuelve a marcar el item como Pausado.
            _isPaused = false;
            Status = DownloadStatus.InQueue;
            OnResumeRequested?.Invoke();
        }

        [RelayCommand]
        private void ExploreFolder()
        {
            // Abre la subcarpeta real (Video/Audio) donde quedó el archivo; si no existe, la base.
            string baseFolder = string.IsNullOrWhiteSpace(OutputFolder)
                ? PathHelper.GetDefaultOutputFolder()
                : OutputFolder;

            string subFolder = SelectedFormat?.IsAudio == true
                ? Path.Combine(baseFolder, "Audio")
                : Path.Combine(baseFolder, "Video");

            if (!ShellHelper.OpenFolder(subFolder))
            {
                ShellHelper.OpenFolder(baseFolder);
            }
        }

        [RelayCommand]
        private void Remove()
        {
            // Si hay una descarga en curso, la detenemos antes de quitar el item
            // y marcamos que se borren sus archivos parciales.
            _deletePartialOnStop = true;
            _cts?.Cancel();
            CleanupPartialFiles();
            // Notificamos al MainViewModel que nos elimine de la ObservableCollection
            OnRemoveRequested?.Invoke(this);
        }

        // Solo se puede pausar mientras se esta descargando.
        private bool CanPauseDownload() => Status == DownloadStatus.Downloading;

        // Se puede reanudar una descarga pausada o reintentar una fallida.
        private bool CanResumeDownload() =>
            Status == DownloadStatus.Paused || Status == DownloadStatus.Failed;
        #endregion

        partial void OnStatusChanged(DownloadStatus oldValue, DownloadStatus newValue)
        {
            statusMessage = newValue switch
            {
                DownloadStatus.NotStarted => "No iniciado",
                DownloadStatus.Downloading => "Descargando...",
                DownloadStatus.Paused => "Pausado",
                DownloadStatus.Completed => "Completado",
                DownloadStatus.InQueue => "En cola",
                DownloadStatus.Failed => "Error",
                DownloadStatus.Canceled => "Cancelado",
                _ => "Estado desconocido"
            };

            OnPropertyChanged(nameof(StatusMessage));

            // Refresca los botones de pausar/reanudar según el estado.
            PauseCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();

            // Persiste el estado de la cola (InQueue/Paused) para reanudar tras cerrar la app.
            OnStatusChangedPersist?.Invoke(this);
        }


        /// <summary>
        /// Este es el método que llama la MainViewModel
        /// </summary>
        public async Task StartDownloadAsync()
        {
            if (Status == DownloadStatus.Canceled) return;  
            
            var outputFolder = string.IsNullOrWhiteSpace(OutputFolder)
                ? PathHelper.GetDefaultOutputFolder()
                : OutputFolder;
            if (string.IsNullOrWhiteSpace(outputFolder)) return;

            // Garantizamos que la carpeta de destino exista antes de descargar.
            try
            {
                Directory.CreateDirectory(outputFolder);
            }
            catch
            {
                // Si no se puede crear, yt-dlp reportara el error correspondiente.
            }
            
            _cts?.Cancel();
            _cts?.Dispose();

            // CTS local para evitar disponer un token mas nuevo si el item se reanuda.
            var cts = new CancellationTokenSource();
            _cts = cts;

            _isPaused = false;
            _lastReportedProgress = -1;
            OutputFolder = outputFolder;
            Status = DownloadStatus.Downloading;

            try
            {
                await _downloadService.RunYtDlpAsync(Url, SelectedFormat, OutputFolder, Id, cts.Token);

                // Solo llegamos aquí si el proceso no termino con errores ni cancelaciones
                if (Status == DownloadStatus.Downloading)
                {
                    Status = DownloadStatus.Completed;
                    Progress = 100;
                }
            }
            catch (OperationCanceledException)
            {
                // La cancelacion pudo venir de "pausar", de "eliminar" o de "reanudar".
                if (_isPaused)
                {
                    Status = DownloadStatus.Paused;
                }
                else if (Status == DownloadStatus.Downloading)
                {
                    // Eliminado o app cerrándose durante la descarga.
                    Status = DownloadStatus.Canceled;
                }
                // Si Resume ya dejo el item en InQueue, se respeta ese estado.
            }
            catch (DownloadFailedException)
            {
                // Fallo esperado de yt-dlp: el item ya muestra su propio estado ("Error").
                // No abrimos el modal global.
                Status = DownloadStatus.Failed;
            }
            catch (Exception ex)
            {
                // Fallo critico o inesperado (entorno/config): notificamos a la vista.
                Status = DownloadStatus.Failed;
                OnError?.Invoke(ex.Message);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }

                // Limpia los .part si el item se elimino o si la descarga termino bien.
                if (_deletePartialOnStop || Status == DownloadStatus.Completed)
                {
                    CleanupPartialFiles();
                }
            }
        }

        // Borra la carpeta temporal con los archivos .part de este item.
        // Se hace en segundo plano porque Directory.Delete puede bloquear mientras
        // el proceso de yt-dlp/ffmpeg aun tiene los archivos abiertos.
        private void CleanupPartialFiles()
        {
            var id = Id;
            Task.Run(() =>
            {
                string folder = PathHelper.GetPartialFolder(id);
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(folder))
                        {
                            Directory.Delete(folder, recursive: true);
                        }
                        return;
                    }
                    catch
                    {
                        // El proceso aun puede tener el archivo bloqueado; reintentamos.
                        Thread.Sleep(200);
                    }
                }
            });
        }

        public void Dispose()
        {
            // Detenemos cualquier descarga en curso antes de liberar recursos.
            // El CTS lo libera StartDownloadAsync en su bloque finally.
            _isPaused = false;
            _cts?.Cancel();

            _downloadService.ProgressChanged -= _downloadService_ProgressChanged;
            Thumbnail?.Dispose();
            Thumbnail = null;
        }
    }
}
