using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HubFinanceiro;

/// <summary>
/// Injeta o olho verde de Debug na O.P.E.X. sem alterar o XAML principal.
/// O botão só aparece enquanto o Modo Debug está ativo.
/// </summary>
public static class OpexDebugInspectorService
{
    private static bool _initialized;
    private static DispatcherTimer? _attachTimer;
    private static Grid? _opexGrid;
    private static Button? _eyeButton;
    private static OpexDebugInspectorWindow? _window;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        DebugService.EnabledChanged += DebugService_EnabledChanged;
        Application.Current.Exit += Application_Exit;

        _attachTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _attachTimer.Tick += AttachTimer_Tick;
        _attachTimer.Start();

        Application.Current.Dispatcher.BeginInvoke(new Action(TryAttach), DispatcherPriority.ApplicationIdle);
    }

    private static void AttachTimer_Tick(object? sender, EventArgs e)
    {
        TryAttach();
        UpdateEyeVisibility();
    }

    private static void TryAttach()
    {
        if (_opexGrid != null && _eyeButton != null)
            return;

        var mainWindow = Application.Current.MainWindow;
        if (mainWindow == null || !mainWindow.IsLoaded)
            return;

        if (mainWindow.FindName("OpexLayoutGrid") is not Grid grid)
            return;

        _opexGrid = grid;
        _opexGrid.IsVisibleChanged += OpexGrid_IsVisibleChanged;

        var green = new SolidColorBrush(Color.FromRgb(85, 217, 138));
        var hoverBackground = new SolidColorBrush(Color.FromArgb(65, 85, 217, 138));

        var glyph = new TextBlock
        {
            Text = "\uE890",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 19,
            Foreground = green,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var button = new Button
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-13, -13, 0, 0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = green,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Content = glyph,
            ToolTip = "Debug O.P.E.X. — ver memória temporária por trás da rotina",
            Visibility = Visibility.Collapsed
        };

        button.MouseEnter += (_, _) => button.Background = hoverBackground;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.Click += EyeButton_Click;

        Panel.SetZIndex(button, 10000);
        Grid.SetRow(button, 0);
        Grid.SetColumn(button, 0);

        _opexGrid.Children.Add(button);
        _eyeButton = button;
        UpdateEyeVisibility();
    }

    private static void OpexGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateEyeVisibility();
    }

    private static void DebugService_EnabledChanged(bool enabled)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            TryAttach();
            UpdateEyeVisibility();

            if (!enabled && _window is { IsLoaded: true })
                _window.Close();
        }));
    }

    private static void UpdateEyeVisibility()
    {
        if (_eyeButton == null || _opexGrid == null)
            return;

        _eyeButton.Visibility = DebugService.IsEnabled && _opexGrid.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void EyeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DebugService.IsEnabled)
            return;

        if (_window is { IsLoaded: true })
        {
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
            return;
        }

        _window = new OpexDebugInspectorWindow
        {
            Owner = Application.Current.MainWindow
        };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        if (_opexGrid != null)
            _opexGrid.IsVisibleChanged -= OpexGrid_IsVisibleChanged;

        if (_attachTimer != null)
        {
            _attachTimer.Stop();
            _attachTimer.Tick -= AttachTimer_Tick;
            _attachTimer = null;
        }

        DebugService.EnabledChanged -= DebugService_EnabledChanged;
        Application.Current.Exit -= Application_Exit;
    }
}
