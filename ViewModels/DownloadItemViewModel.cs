using Avalonia.Media.Imaging;
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
        private readonly object _lockObj = new object();
        #endregion


        #region Properties
        // Acción que se invoca cuando se solicita eliminar este elemento de descarga
        public Action<DownloadItemViewModel>? OnRemoveRequested { get; set; }

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

        public string OutputFolder { get; set; } = string.Empty;
        #endregion

        #region Constructors
        public DownloadItemViewModel()
        {
            _downloadService.ProgressChanged += _downloadService_ProgressChanged;
        }

        private void _downloadService_ProgressChanged(DownloadProgress progress)
        {
            lock (_lockObj)
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
            }
        }
        #endregion

        #region Commands
        // El comando solo se puede presionar si el método retorna true
        [RelayCommand(CanExecute = nameof(CanCancelDownload))]
        private void Cancel()
        {
            _cts?.Cancel();
            Status = DownloadStatus.Canceled;         
            CancelCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanRemoveDownload))]
        private void Remove()
        {
            // Notificamos al MainViewModel que nos elimine de la ObservableCollection
            OnRemoveRequested?.Invoke(this);
        }

        #region Methods        
        private bool CanRemoveDownload()
        {
            // Solo se permite remover si ya se completó, canceló o falló
            return Status == DownloadStatus.Completed ||
                Status == DownloadStatus.Failed ||
                Status == DownloadStatus.Canceled;
        }
        #endregion

        private bool CanCancelDownload()
        {
            // Solo permitimos hacer clic si está descargando o en cola
            return Status == DownloadStatus.Downloading || Status == DownloadStatus.InQueue;
        }
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

            // Esto hace que el botón de cancelar se active/desactive en vivo según el estado
            CancelCommand.NotifyCanExecuteChanged();
            RemoveCommand.NotifyCanExecuteChanged();
        }


        /// <summary>
        /// Este es el método que llama la MainViewModel
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task StartDownloadAsync()
        {
            if (Status == DownloadStatus.Canceled) return;  
            
            var outputFolder = PathHelper.GetDefaultOutputFolder();
            if (string.IsNullOrWhiteSpace(outputFolder) || outputFolder == string.Empty) return;
            
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            OutputFolder = outputFolder;
            Status = DownloadStatus.Downloading;

            try
            {
                await _downloadService.RunYtDlpAsync(Url, SelectedFormat!, OutputFolder, _cts.Token);

                // Solo llegamos aquí si el proceso no termino con errores ni cancelaciones
                if (Status != DownloadStatus.Canceled)
                {
                    Status = DownloadStatus.Completed;
                    Progress = 100;
                }
            }
            catch (OperationCanceledException)
            {
                Status = DownloadStatus.Canceled;
            }

            catch (Exception)
            {

                Status = DownloadStatus.Failed;
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }
        }

        public void Dispose()
        {
            _downloadService.ProgressChanged -= _downloadService_ProgressChanged;
        }
    }
}
