using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace WinTube.Models
{
    public partial class DownloadItem : ObservableObject
    {
        [ObservableProperty]
        private string url = "";

        [ObservableProperty]
        private string title = "";

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
        }
    }
}
