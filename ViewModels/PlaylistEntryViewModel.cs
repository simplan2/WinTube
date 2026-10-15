using CommunityToolkit.Mvvm.ComponentModel;
using WinTube.Models;

namespace WinTube.ViewModels
{
    // Representa una entrada de playlist en el panel de seleccion.
    public partial class PlaylistEntryViewModel : ObservableObject
    {
        public string Title { get; }

        public string Url { get; }

        public string ThumbnailUrl { get; }

        // Por defecto sin marcar: el usuario elige que videos descargar.
        [ObservableProperty]
        private bool isSelected;

        public PlaylistEntryViewModel(PlaylistEntry entry)
        {
            Title = string.IsNullOrWhiteSpace(entry.Title) ? "Sin título" : entry.Title;

            Url = !string.IsNullOrWhiteSpace(entry.WebpageUrl)
                ? entry.WebpageUrl
                : (!string.IsNullOrWhiteSpace(entry.Url) ? entry.Url : entry.Id);

            ThumbnailUrl = !string.IsNullOrWhiteSpace(entry.Thumbnail)
                ? entry.Thumbnail
                : (entry.Thumbnails != null && entry.Thumbnails.Count > 0
                    ? entry.Thumbnails[^1].Url
                    : string.Empty);
        }
    }
}
