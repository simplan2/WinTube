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
        private string status = "";

        [ObservableProperty]
        private FormatItem? selectedFormat;
    }
}
