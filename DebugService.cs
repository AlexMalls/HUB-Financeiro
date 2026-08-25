using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace HubFinanceiro;

public enum DebugEntryLevel
{
    Info,
    Action,
    Background,
    Warning,
    Error,
    System
}

public sealed class DebugEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public DebugEntryLevel Level { get; init; }
    public string Category { get; init; } = "SYSTEM";
    public string Message { get; init; } = string.Empty;

    public string DisplayText => $"[{Timestamp:HH:mm:ss.fff}] [{Category}] {Message}";
}

public static class DebugService
{
    private sealed class DebugSettings
    {
        public bool Enabled { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly List<DebugEntry> Entries = new();
    private static readonly HashSet<TextBox> DirtyTextBoxes = new();
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HubFinanceiro");
    private static readonly string SettingsPath = Path.Combine(AppDataDirectory, "debug_settings.json");
    private static readonly string LogsDirectory = Path.Combine(AppDataDirectory, "Logs");
    private static readonly string DebugLogPath = Path.Combine(LogsDirectory, $"Debug_{DateTime.Now:yyyy-MM-dd}.log");
    private static readonly string LegacyLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HubFinanceiro_log.txt");

    private static bool _initialized;
    private static bool _isEnabled;
    private static long _legacyLogPosition;
    private static DispatcherTimer? _legacyLogTimer;
    private static DebugWindow? _window;

    public static event Action<DebugEntry>? EntryAdded;
    public static event Action<bool>? EnabledChanged;

    public static bool IsEnabled => _isEnabled;
    public static string CurrentLogPath => DebugLogPath;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        _isEnabled = LoadSettings();

        RegisterUiCaptureHandlers();

        _legacyLogTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _legacyLogTimer.Tick += (_, _) => ReadLegacyLogTail();
        _legacyLogTimer.Start();

        if (File.Exists(LegacyLogPath))
        {
            try { _legacyLogPosition = new FileInfo(LegacyLogPath).Length; }
            catch { _legacyLogPosition = 0; }
        }

        if (_isEnabled)
        {
            Record("SYSTEM", "Modo Debug restaurado como ATIVO.", DebugEntryLevel.System);
            Record("SYSTEM", "Captura UI global pronta: cliques e edições de campos serão monitorados.", DebugEntryLevel.System);
            Application.Current.Dispatcher.BeginInvoke(new Action(ShowConsole), DispatcherPriority.ApplicationIdle);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (_isEnabled == enabled)
            return;

        _isEnabled = enabled;
        SaveSettings();
        EnabledChanged?.Invoke(enabled);

        if (enabled)
        {
            Record("SYSTEM", "Modo Debug ativado.", DebugEntryLevel.System, force: true);
            Record("SYSTEM", "Captura UI global pronta: cliques e edições de campos serão monitorados.", DebugEntryLevel.System, force: true);
            ShowConsole();
        }
        else
        {
            DirtyTextBoxes.Clear();
            Record("SYSTEM", "Modo Debug desativado.", DebugEntryLevel.System, force: true);
            _window?.CloseFromService();
            _window = null;
        }
    }

    public static void ShowConsole()
    {
        if (!_isEnabled)
            return;

        if (_window is { IsLoaded: true })
        {
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
            return;
        }

        _window = new DebugWindow();
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    public static IReadOnlyList<DebugEntry> Snapshot()
    {
        lock (Sync)
            return Entries.ToList();
    }

    public static void Clear()
    {
        lock (Sync)
            Entries.Clear();
    }

    public static void Record(
        string category,
        string message,
        DebugEntryLevel level = DebugEntryLevel.Info,
        Exception? exception = null,
        bool force = false)
    {
        if (!_isEnabled && !force)
            return;

        var finalMessage = exception == null
            ? message
            : $"{message} | {exception.GetType().Name}: {exception.Message}";

        var entry = new DebugEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Category = string.IsNullOrWhiteSpace(category) ? "SYSTEM" : category.Trim().ToUpperInvariant(),
            Message = finalMessage
        };

        lock (Sync)
        {
            Entries.Add(entry);
            if (Entries.Count > 5000)
                Entries.RemoveRange(0, Entries.Count - 5000);
        }

        try
        {
            Directory.CreateDirectory(LogsDirectory);
            File.AppendAllText(DebugLogPath, entry.DisplayText + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // O debug nunca pode interromper o HUB.
        }

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => EntryAdded?.Invoke(entry)));
    }

    public static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = LogsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Record("DEBUG", "Não foi possível abrir a pasta de logs.", DebugEntryLevel.Error, ex, force: true);
        }
    }

    private static void RegisterUiCaptureHandlers()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler(Window_PreviewMouseUp),
            true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(TextBox_TextChanged),
            true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            Keyboard.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(TextBox_LostKeyboardFocus),
            true);

        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(ComboBox_SelectionChanged),
            true);
    }

    private static void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isEnabled || e.ChangedButton != MouseButton.Left)
            return;

        try
        {
            if (sender is DebugWindow)
                return;

            if (e.OriginalSource is not DependencyObject source)
                return;

            var element = FindRelevantElement(source);
            if (element == null)
                return;

            var window = Window.GetWindow(element) ?? sender as Window;
            if (window is DebugWindow)
                return;

            var description = DescribeElement(element);
            var windowTitle = GetWindowTitle(window);

            Record("UI", $"Clique em {description} | Janela: {windowTitle}", DebugEntryLevel.Action);
        }
        catch (Exception ex)
        {
            Record("DEBUG", "Falha ao identificar clique da interface.", DebugEntryLevel.Warning, ex);
        }
    }

    private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isEnabled || sender is not TextBox textBox)
            return;

        var window = Window.GetWindow(textBox);
        if (window is DebugWindow || !textBox.IsKeyboardFocusWithin)
            return;

        DirtyTextBoxes.Add(textBox);
    }

    private static void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isEnabled || sender is not TextBox textBox || !DirtyTextBoxes.Remove(textBox))
            return;

        try
        {
            var window = Window.GetWindow(textBox);
            if (window is DebugWindow)
                return;

            Record(
                "UI",
                $"Campo editado: {DescribeElement(textBox)} | Janela: {GetWindowTitle(window)} | conteúdo omitido",
                DebugEntryLevel.Action);
        }
        catch (Exception ex)
        {
            Record("DEBUG", "Falha ao registrar edição de campo.", DebugEntryLevel.Warning, ex);
        }
    }

    private static void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isEnabled || sender is not ComboBox comboBox)
            return;

        var window = Window.GetWindow(comboBox);
        if (window is DebugWindow)
            return;

        if (!comboBox.IsKeyboardFocusWithin && !comboBox.IsDropDownOpen)
            return;

        try
        {
            Record(
                "UI",
                $"Seleção alterada: {DescribeElement(comboBox)} | Janela: {GetWindowTitle(window)} | valor omitido",
                DebugEntryLevel.Action);
        }
        catch (Exception ex)
        {
            Record("DEBUG", "Falha ao registrar alteração de seleção.", DebugEntryLevel.Warning, ex);
        }
    }

    private static FrameworkElement? FindRelevantElement(DependencyObject source)
    {
        DependencyObject? current = source;
        FrameworkElement? namedFallback = null;
        FrameworkElement? fallback = null;

        while (current != null)
        {
            if (current is ButtonBase or TextBox or ComboBox or ListBoxItem or MenuItem or DataGridRow or TreeViewItem or TabItem or DatePicker)
                return current as FrameworkElement;

            if (current is FrameworkElement fe)
            {
                fallback ??= fe;
                if (namedFallback == null && !string.IsNullOrWhiteSpace(fe.Name))
                    namedFallback = fe;
            }

            current = GetParent(current);
        }

        return namedFallback ?? fallback;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        try
        {
            if (element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
                return System.Windows.Media.VisualTreeHelper.GetParent(element);

            if (element is FrameworkContentElement contentElement)
                return contentElement.Parent;

            return LogicalTreeHelper.GetParent(element);
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeElement(FrameworkElement element)
    {
        var type = element.GetType().Name;
        var name = string.IsNullOrWhiteSpace(element.Name) ? null : element.Name;
        var label = GetSafeLabel(element);

        if (!string.IsNullOrWhiteSpace(label))
            return $"{type} '{label}'" + (name == null ? string.Empty : $" ({name})");

        return name == null ? type : $"{type} ({name})";
    }

    private static string? GetSafeLabel(FrameworkElement element)
    {
        object? content = element switch
        {
            ContentControl cc => cc.Content,
            _ => null
        };

        if (content is string text && !string.IsNullOrWhiteSpace(text))
        {
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length > 60 ? text[..60] + "…" : text;
        }

        return null;
    }

    private static string GetWindowTitle(Window? window)
    {
        return string.IsNullOrWhiteSpace(window?.Title) ? "HUB" : window!.Title;
    }

    private static void ReadLegacyLogTail()
    {
        if (!_isEnabled || !File.Exists(LegacyLogPath))
            return;

        try
        {
            using var stream = new FileStream(LegacyLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < _legacyLogPosition)
                _legacyLogPosition = 0;

            if (stream.Length == _legacyLogPosition)
                return;

            stream.Seek(_legacyLogPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            var newText = reader.ReadToEnd();
            _legacyLogPosition = stream.Position;

            foreach (var line in newText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("    "))
                    continue;

                Record("CORE", line.Trim(), DebugEntryLevel.Background);
            }
        }
        catch
        {
            // Arquivo pode estar momentaneamente em escrita. Tentamos novamente no próximo ciclo.
        }
    }

    private static bool LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return false;

            var settings = JsonSerializer.Deserialize<DebugSettings>(File.ReadAllText(SettingsPath));
            return settings?.Enabled == true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(new DebugSettings { Enabled = _isEnabled }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Configuração de debug não pode impedir o funcionamento do HUB.
        }
    }
}
