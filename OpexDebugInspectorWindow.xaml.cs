using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace HubFinanceiro;

public partial class OpexDebugInspectorWindow : Window
{
    private sealed class ContextViewModel
    {
        public string Key { get; init; } = string.Empty;
        public string Titulo { get; init; } = string.Empty;
        public string Subtitulo { get; init; } = string.Empty;
        public IReadOnlyList<PeriodViewModel> Periodos { get; init; } = Array.Empty<PeriodViewModel>();
    }

    private sealed class PeriodViewModel
    {
        public SantanderCommitmentMemoryEntry Entry { get; init; } = null!;
        public string PeriodoDisplay => $"{Entry.Periodo} • {Entry.AtualizadoEm:HH:mm:ss}";
    }

    private readonly ObservableCollection<ContextViewModel> _contexts = new();
    private readonly ObservableCollection<PeriodViewModel> _periods = new();

    public OpexDebugInspectorWindow()
    {
        InitializeComponent();
        ContextList.ItemsSource = _contexts;
        PeriodCombo.ItemsSource = _periods;

        Loaded += OpexDebugInspectorWindow_Loaded;
        Closed += OpexDebugInspectorWindow_Closed;
        SantanderCommitmentMemoryService.MemoryChanged += MemoryService_MemoryChanged;
    }

    private void OpexDebugInspectorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadMemory(preserveSelection: false);
        DebugService.Record(
            "OPEX",
            "Inspetor de memória temporária aberto.",
            DebugEntryLevel.Action);
    }

    private void OpexDebugInspectorWindow_Closed(object? sender, EventArgs e)
    {
        SantanderCommitmentMemoryService.MemoryChanged -= MemoryService_MemoryChanged;
    }

    private void MemoryService_MemoryChanged()
    {
        Dispatcher.BeginInvoke(new Action(() => ReloadMemory(preserveSelection: true)));
    }

    private void ReloadMemory(bool preserveSelection)
    {
        var selectedContextKey = preserveSelection
            ? (ContextList.SelectedItem as ContextViewModel)?.Key
            : null;

        var selectedPeriodKey = preserveSelection
            ? (PeriodCombo.SelectedItem as PeriodViewModel)?.Entry.StorageKey
            : null;

        var entries = SantanderCommitmentMemoryService.Snapshot();
        var groups = entries
            .GroupBy(entry => entry.ContextKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(entry => entry.AtualizadoEm).ToList();
                var latest = ordered[0];
                return new ContextViewModel
                {
                    Key = group.Key,
                    Titulo = $"{latest.Banco} — {latest.Contexto}",
                    Subtitulo = $"{ordered.Count} período(s) • último {latest.AtualizadoEm:dd/MM HH:mm}",
                    Periodos = ordered.Select(entry => new PeriodViewModel { Entry = entry }).ToList()
                };
            })
            .OrderBy(group => group.Titulo, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _contexts.Clear();
        foreach (var group in groups)
            _contexts.Add(group);

        MemoryCountText.Text = entries.Count == 1
            ? "1 snapshot em memória"
            : $"{entries.Count} snapshots em memória";

        if (_contexts.Count == 0)
        {
            _periods.Clear();
            ContextList.SelectedItem = null;
            ShowEmptyState();
            return;
        }

        var contextToSelect = !string.IsNullOrWhiteSpace(selectedContextKey)
            ? _contexts.FirstOrDefault(item => string.Equals(item.Key, selectedContextKey, StringComparison.OrdinalIgnoreCase))
            : null;
        contextToSelect ??= _contexts[0];
        ContextList.SelectedItem = contextToSelect;

        LoadPeriods(contextToSelect, selectedPeriodKey);
    }

    private void ContextList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContextList.SelectedItem is ContextViewModel context)
            LoadPeriods(context, selectedPeriodKey: null);
        else
            ShowEmptyState();
    }

    private void LoadPeriods(ContextViewModel context, string? selectedPeriodKey)
    {
        _periods.Clear();
        foreach (var period in context.Periodos)
            _periods.Add(period);

        ContextTitleText.Text = context.Titulo;
        CompanyText.Text = context.Periodos.FirstOrDefault()?.Entry.Empresa ?? string.Empty;

        if (_periods.Count == 0)
        {
            ShowEmptyState();
            return;
        }

        var periodToSelect = !string.IsNullOrWhiteSpace(selectedPeriodKey)
            ? _periods.FirstOrDefault(item => string.Equals(item.Entry.StorageKey, selectedPeriodKey, StringComparison.OrdinalIgnoreCase))
            : null;
        periodToSelect ??= _periods[0];
        PeriodCombo.SelectedItem = periodToSelect;
        ShowEntry(periodToSelect.Entry);
    }

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodCombo.SelectedItem is PeriodViewModel period)
            ShowEntry(period.Entry);
    }

    private void ShowEntry(SantanderCommitmentMemoryEntry entry)
    {
        ContextTitleText.Text = $"{entry.Banco} — {entry.Contexto}";
        CompanyText.Text = entry.Empresa;
        PeriodText.Text = entry.Periodo;
        CountText.Text = entry.TotalPagamentos.ToString("N0");
        TotalValueText.Text = string.IsNullOrWhiteSpace(entry.ValorTotal) ? "—" : entry.ValorTotal;
        CapturedText.Text = entry.AtualizadoEm.ToString("dd/MM HH:mm:ss");
        PaymentsGrid.ItemsSource = entry.Pagamentos;
    }

    private void ShowEmptyState()
    {
        ContextTitleText.Text = "Nenhum contexto em memória";
        CompanyText.Text = "Abra Santander → Consultar compromissos e visualize um resultado para alimentar esta tela.";
        PeriodText.Text = "—";
        CountText.Text = "—";
        TotalValueText.Text = "—";
        CapturedText.Text = "—";
        PaymentsGrid.ItemsSource = null;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadMemory(preserveSelection: true);
    }

    private void ClearMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Limpar todos os snapshots temporários desta sessão?",
            "Memória O.P.E.X.",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            SantanderCommitmentMemoryService.Clear();
    }
}
