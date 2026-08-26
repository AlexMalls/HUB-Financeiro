using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HubFinanceiro;

public partial class OpexDebugInspectorWindow : Window
{
    private sealed class ContextViewModel
    {
        public SantanderCommitmentMemoryEntry Entry { get; init; } = null!;
        public string Key => Entry.StorageKey;
        public string Titulo => $"{Entry.Banco} — {Entry.Contexto}";
        public string Subtitulo => $"{Entry.Periodo} ({Entry.AtualizadoEm:dd/MM/yy HH:mm})";
    }

    private readonly ObservableCollection<ContextViewModel> _contexts = new();

    public OpexDebugInspectorWindow()
    {
        InitializeComponent();
        ContextList.ItemsSource = _contexts;

        Loaded += OpexDebugInspectorWindow_Loaded;
        Closed += OpexDebugInspectorWindow_Closed;
        SantanderCommitmentMemoryService.MemoryChanged += MemoryService_MemoryChanged;
    }

    private void OpexDebugInspectorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadMemory(preserveSelection: false);
        DebugService.Record(
            "OPEX",
            "Inspetor de histórico persistente aberto.",
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
        var selectedKey = preserveSelection
            ? (ContextList.SelectedItem as ContextViewModel)?.Key
            : null;

        var entries = SantanderCommitmentMemoryService.Snapshot()
            .OrderByDescending(entry => ParsePeriodStart(entry.DataInicial) ?? DateTime.MinValue)
            .ThenBy(entry => ContextOrder(entry.Contexto))
            .ThenByDescending(entry => entry.AtualizadoEm)
            .ToList();

        _contexts.Clear();
        foreach (var entry in entries)
            _contexts.Add(new ContextViewModel { Entry = entry });

        MemoryCountText.Text = entries.Count == 1
            ? "1 snapshot salvo"
            : $"{entries.Count} snapshots salvos";

        if (_contexts.Count == 0)
        {
            ContextList.SelectedItem = null;
            ShowEmptyState();
            return;
        }

        var itemToSelect = !string.IsNullOrWhiteSpace(selectedKey)
            ? _contexts.FirstOrDefault(item => string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
            : null;

        itemToSelect ??= _contexts[0];
        ContextList.SelectedItem = itemToSelect;
        ShowEntry(itemToSelect.Entry);
    }

    private void ContextList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContextList.SelectedItem is ContextViewModel context)
            ShowEntry(context.Entry);
        else
            ShowEmptyState();
    }

    private void ShowEntry(SantanderCommitmentMemoryEntry entry)
    {
        ContextTitleText.Text = $"{entry.Banco} — {entry.Contexto}";
        CompanyText.Text = string.IsNullOrWhiteSpace(entry.Convenio)
            ? entry.Empresa
            : $"{entry.Empresa} • Convênio {entry.Convenio}";
        PeriodText.Text = entry.Periodo;
        CountText.Text = entry.TotalPagamentos.ToString("N0");
        TotalValueText.Text = string.IsNullOrWhiteSpace(entry.ValorTotal) ? "—" : entry.ValorTotal;
        CapturedText.Text = entry.AtualizadoEm.ToString("dd/MM HH:mm:ss");
        PaymentsGrid.ItemsSource = entry.Pagamentos;
    }

    private void ShowEmptyState()
    {
        ContextTitleText.Text = "Nenhum contexto salvo";
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
        var result = System.Windows.MessageBox.Show(
            this,
            "Limpar todos os snapshots salvos no histórico? Esta ação também atualizará o arquivo JSON.",
            "Histórico O.P.E.X.",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
            SantanderCommitmentMemoryService.Clear();
    }

    private static DateTime? ParsePeriodStart(string value)
    {
        return DateTime.TryParseExact(
            value,
            "dd/MM/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static int ContextOrder(string context)
    {
        if (context.Equals("Administradora", StringComparison.OrdinalIgnoreCase)) return 0;
        if (context.Equals("Corretora", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }
}
