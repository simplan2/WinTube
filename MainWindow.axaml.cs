using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WinTube.ViewModels;

namespace WinTube;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Actualizar el texto del botón maximizar según el estado
        UpdateMaximizeButtonText();
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
        if (mainBorder != null)
        {
            if (WindowState == WindowState.Maximized)
            {
                // Sin esquinas redondeadas cuando está maximizada
                mainBorder.CornerRadius = new CornerRadius(0);
                barTitleBorder.CornerRadius = new CornerRadius(0);

            }
            else
            {
                // Restaurar esquinas redondeadas en modo normal
                mainBorder.CornerRadius = new CornerRadius(8);
                barTitleBorder.CornerRadius = new CornerRadius(8,8,0,0);
            }
        }
    }
}