using System;
using System.IO;
using System.Text.Json;
using WinTube.Models;

namespace WinTube.Services
{
    public class QueueService
    {
        // Serializa las escrituras para evitar corrupcion si coinciden varias.
        private static readonly object WriteLock = new object();

        private static readonly string QueueFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinTube");

        private static readonly string QueuePath = Path.Combine(QueueFolder, "queue.json");

        private static readonly string TempPath = QueuePath + ".tmp";

        public DownloadJobList Load()
        {
            try
            {
                if (File.Exists(QueuePath))
                {
                    string json = File.ReadAllText(QueuePath);
                    var list = JsonSerializer.Deserialize(json, VideoJsonContext.Default.DownloadJobList);

                    if (list != null)
                    {
                        return list;
                    }
                }
            }
            catch
            {
                // Cola corrupta o inaccesible: devolvemos lista vacia.
            }

            return new DownloadJobList();
        }

        public void Save(DownloadJobList list)
        {
            try
            {
                lock (WriteLock)
                {
                    Directory.CreateDirectory(QueueFolder);
                    string json = JsonSerializer.Serialize(list, VideoJsonContext.Default.DownloadJobList);

                    // Escritura atomica: escribir a .tmp y reemplazar, para no dejar
                    // el archivo a medias si la app se cierra durante el guardado.
                    File.WriteAllText(TempPath, json);
                    File.Move(TempPath, QueuePath, overwrite: true);
                }
            }
            catch
            {
                // No interrumpir la app si no se puede persistir la cola.
            }
        }

        public void Clear()
        {
            try
            {
                lock (WriteLock)
                {
                    if (File.Exists(QueuePath))
                    {
                        File.Delete(QueuePath);
                    }
                }
            }
            catch
            {
                // Ignorar errores al limpiar la cola persistida.
            }
        }
    }
}
