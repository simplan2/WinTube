using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WinTube.Converters
{
    public class StatusToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is DownloadStatus status)
            {
                // Sin parametro mantenemos el comportamiento clasico: solo "Downloading".
                if (parameter is not string raw || string.IsNullOrWhiteSpace(raw))
                {
                    return status == DownloadStatus.Downloading;
                }

                // El parametro es una lista separada por comas de estados permitidos.
                foreach (var part in raw.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Enum.TryParse<DownloadStatus>(part, ignoreCase: true, out var allowed)
                        && allowed == status)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
