using System;
using System.Collections.Generic;
using System.Text;

namespace WinTube
{
    internal class Enumerators
    {
    }

    public enum DownloadStatus
    {
        NotStarted,
        Downloading,
        InQueue,
        Paused,
        Completed,
        Canceled,
        Failed
    }
}
