using System;
using System.IO;
using System.Text.Json;
using WinTube.Helpers;
using WinTube.Models;

namespace WinTube.Services
{
    public class SettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinTube");

        private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize(json, VideoJsonContext.Default.AppSettings);

                    if (settings != null)
                    {
                        // Si falta la carpeta (config antigua/incompleta), aplicamos la
                        // por defecto sin descartar el resto de ajustes (p. ej. borrado auto).
                        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
                        {
                            settings.OutputFolder = PathHelper.GetDefaultOutputFolder();
                            Save(settings);
                        }

                        return settings;
                    }
                }
            }
            catch
            {
                // Config corrupta o inaccesible: caemos a los valores por defecto.
            }

            // Primer arranque (o config invalida): persistimos el directorio por defecto.
            // A partir de aqui, solo el usuario lo cambia.
            var defaults = new AppSettings
            {
                OutputFolder = PathHelper.GetDefaultOutputFolder()
            };

            Save(defaults);
            return defaults;
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                string json = JsonSerializer.Serialize(settings, VideoJsonContext.Default.AppSettings);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // No interrumpir la app si no se puede persistir la configuracion.
            }
        }
    }
}
