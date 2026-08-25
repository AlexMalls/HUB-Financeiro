using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HubFinanceiro;

/// <summary>
/// Janela de Opções do sistema
/// </summary>
public partial class OpcoesWindow : Window
{
    private bool _carregandoEstadoDebug;

    public OpcoesWindow()
    {
        InitializeComponent();
        CarregarEstadoDebug();
        DebugService.EnabledChanged += DebugService_EnabledChanged;
        Closed += OpcoesWindow_Closed;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void GeralButton_Click(object sender, RoutedEventArgs e)
    {
        GeralButton.IsChecked = true;
        DebugButton.IsChecked = false;
        GeralContent.Visibility = Visibility.Visible;
        DebugContent.Visibility = Visibility.Collapsed;
    }

    private void DebugButton_Click(object sender, RoutedEventArgs e)
    {
        DebugButton.IsChecked = true;
        GeralButton.IsChecked = false;
        GeralContent.Visibility = Visibility.Collapsed;
        DebugContent.Visibility = Visibility.Visible;
    }

    private void DebugModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_carregandoEstadoDebug)
            return;

        DebugService.SetEnabled(DebugModeCheckBox.IsChecked == true);
        AtualizarStatusDebug();
    }

    private void OpenDebugConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DebugService.IsEnabled)
        {
            DebugService.SetEnabled(true);
            CarregarEstadoDebug();
        }

        DebugService.ShowConsole();
    }

    private void OpenDebugLogsButton_Click(object sender, RoutedEventArgs e)
    {
        DebugService.OpenLogFolder();
    }

    private void DebugService_EnabledChanged(bool enabled)
    {
        Dispatcher.Invoke(() =>
        {
            _carregandoEstadoDebug = true;
            DebugModeCheckBox.IsChecked = enabled;
            _carregandoEstadoDebug = false;
            AtualizarStatusDebug();
        });
    }

    private void CarregarEstadoDebug()
    {
        _carregandoEstadoDebug = true;
        DebugModeCheckBox.IsChecked = DebugService.IsEnabled;
        _carregandoEstadoDebug = false;
        AtualizarStatusDebug();
    }

    private void AtualizarStatusDebug()
    {
        var ativo = DebugService.IsEnabled;
        DebugStatusText.Text = ativo
            ? "Ativo — monitorando o HUB em background"
            : "Desativado";
        DebugStatusDot.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(ativo ? "#61D095" : "#77777E"));
        OpenDebugConsoleButton.IsEnabled = true;
    }

    private void OpcoesWindow_Closed(object? sender, EventArgs e)
    {
        DebugService.EnabledChanged -= DebugService_EnabledChanged;
    }
}
