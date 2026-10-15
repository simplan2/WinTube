namespace WinTube.Models
{
    public class AppSettings
    {
        public string OutputFolder { get; set; } = "";

        // Elimina automaticamente los items completados de la lista.
        public bool AutoRemoveCompleted { get; set; }
    }
}
