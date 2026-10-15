using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using WinTube.ViewModels;
using WinTube.Views;

namespace WinTube;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.FolderPicker = PickFolderAsync;
            viewModel.ErrorRequested += OnErrorRequested;
            viewModel.SettingsRequested += OnSettingsRequested;
        }

        // Actualizar el texto del botón maximizar según el estado
        UpdateMaximizeButtonText();

        this.Activated += OnWindowActivated;
    }

    // Abre el selector nativo de carpetas y devuelve la ruta elegida.
    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecciona la carpeta de descarga",
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    // Muestra el modal de error cuando el ViewModel lo solicita.
    private void OnErrorRequested(string message)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var dialog = new ErrorDialog(message);
            await dialog.ShowDialog(this);
        });
    }

    // Abre el dialogo de ajustes cuando el ViewModel lo solicita.
    private void OnSettingsRequested()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var dialog = new SettingsDialog { DataContext = DataContext };
            await dialog.ShowDialog(this);
        });
    }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        await CaptureUrlAsync();
    }

    private async Task CaptureUrlAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;

        // Leer el texto actual
        string? text = await clipboard.TryGetTextAsync();

        if (!string.IsNullOrEmpty(text))
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uriResult) &&
               (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                // Asignamos el texto capturado a la propiedad del ViewModel
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.Url = text;
                }
            }
        }
    }

    // Método para arrastrar la ventana
    private void TitleBar_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    // Minimizar ventana
    private void MinimizeWindow_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    // Maximizar/Restaurar ventana
    private void MaximizeWindow_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;

        UpdateMaximizeButtonText();
        UpdateWindowCornerRadius();
    }

    // Cerrar ventana
    private void CloseWindow_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Guardado final de la cola y liberacion de recursos.
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }

    // Actualizar el texto del botón maximizar
    private void UpdateMaximizeButtonText()
    {
        var maximizeButton = this.FindControl<Button>("MaximizeButton");
        if (maximizeButton != null)
        {
            maximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }
    }

    // Opcional: Actualizar cuando el estado de la ventana cambie por otros medios
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeButtonText();
            UpdateWindowCornerRadius();
        }
    }

    // Maneja el doble clic en la barra de título
    private void TitleBar_DoubleTapped(object sender, Avalonia.Input.TappedEventArgs e)
    {
        // Alterna entre maximizado y normal
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;

        // Actualiza el ícono del botón maximizar
        UpdateMaximizeButtonText();
        UpdateWindowCornerRadius();
        
    }

    // Actualiza el redondeo de las esquinas según el estado
    private void UpdateWindowCornerRadius()
    {
        var mainBorder = this.FindControl<Border>("MainBorder");
        var barTitleBorder = this.FindControl<Border>("BarTitleBorder");

        bool maximized = WindowState == WindowState.Maximized;

        if (mainBorder != null)
        {
            // Sin esquinas redondeadas cuando está maximizada
            mainBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(8);
        }

        if (barTitleBorder != null)
        {
            barTitleBorder.CornerRadius = maximized
                ? new CornerRadius(0)
                : new CornerRadius(8, 8, 0, 0);
        }
    }
}