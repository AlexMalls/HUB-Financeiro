using System.Collections.ObjectModel;
using System.Windows;

namespace HubFinanceiro;

public partial class DebugWindow : Window
{
    private readonly ObservableCollection<DebugEntry> _visibleEntries = new();
    private bool _paused;
    private bool _closingFromService;

    public DebugWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = _visibleEntries;

        foreach (var entry in DebugService.Snapshot())
            _visibleEntries.Add(entry);

        UpdateCounter();
        DebugService.EntryAdded += DebugService_EntryAdded;
        Loaded += DebugWindow_Loaded;
        Closed += DebugWindow_Closed;
    }

    private void DebugWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DebugService.Record("SYSTEM", "Console de Debug aberto.", DebugEntryLevel.System);
        ScrollToEnd();
    }

    private void DebugWindow_Closed(object? sender, EventArgs e)
    {
        DebugService.EntryAdded -= DebugService_EntryAdded;

        if (!_closingFromService && DebugService.IsEnabled)
            DebugService.Record("SYSTEM", "Console fechado; monitoramento continua em background.", DebugEntryLevel.System);
    }

    private void DebugService_EntryAdded(DebugEntry entry)
    {
        if (_paused)
        {
            StatusText.Text = "Exibição pausada — gravação continua em arquivo e memória.";
            return;
        }

        _visibleEntries.Add(entry);
        if (_visibleEntries.Count > 5000)
            _visibleEntries.RemoveAt(0);

        StatusText.Text = $"Último evento: {entry.Timestamp:HH:mm:ss} • {entry.Category}";
        UpdateCounter();

        if (AutoScrollCheckBox.IsChecked == true)
            ScrollToEnd();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "Retomar exibição" : "Pausar exibição";

        if (_paused)
        {
            StatusText.Text = "Exibição pausada — gravação continua em arquivo e memória.";
            return;
        }

        _visibleEntries.Clear();
        foreach (var entry in DebugService.Snapshot())
            _visibleEntries.Add(entry);

        StatusText.Text = "Exibição retomada.";
        UpdateCounter();
        ScrollToEnd();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        DebugService.Clear();
        _visibleEntries.Clear();
        StatusText.Text = "Tela limpa. Novos eventos continuarão sendo exibidos.";
        UpdateCounter();
        DebugService.Record("SYSTEM", "Histórico em memória do console foi limpo.", DebugEntryLevel.System);
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        DebugService.OpenLogFolder();
    }

    internal void CloseFromService()
    {
        _closingFromService = true;
        Close();
    }

    private void ScrollToEnd()
    {
        if (_visibleEntries.Count > 0)
            LogList.ScrollIntoView(_visibleEntries[^1]);
    }

    private void UpdateCounter()
    {
        CounterText.Text = _visibleEntries.Count == 1
            ? "1 evento"
            : $"{_visibleEntries.Count:N0} eventos";
    }
}
