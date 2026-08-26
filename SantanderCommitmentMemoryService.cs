using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace HubFinanceiro;

public sealed record SantanderCommitmentMemoryEntry(
    string Banco,
    string Contexto,
    string Empresa,
    DateTime AtualizadoEm,
    string DataInicial,
    string DataFinal,
    string SituacaoFiltro,
    int TotalPagamentos,
    string ValorTotal,
    decimal? ValorTotalNumerico,
    IReadOnlyList<SantanderCommitmentItem> Pagamentos)
{
    public string Periodo => string.Equals(DataInicial, DataFinal, StringComparison.Ordinal)
        ? DataInicial
        : $"{DataInicial} → {DataFinal}";

    public string ContextKey => $"{Banco}|{Contexto}";
    public string StorageKey => $"{Banco}|{Contexto}|{DataInicial}|{DataFinal}";
}

/// <summary>
/// Memória temporária dos últimos resultados efetivamente observados na tela
/// Consultar compromissos. Nada é persistido em disco.
///
/// A memória NÃO atualiza a cada sonda do Santander. Ela só grava quando a
/// composição da tabela realmente muda. Assim, mexer no calendário sem carregar
/// um novo resultado não associa pagamentos antigos a um período novo.
/// </summary>
public static class SantanderCommitmentMemoryService
{
    private const int TickMilliseconds = 850;
    private const int MaxPeriodsPerContext = 4;
    private const int MaxEntriesTotal = 12;
    private const string BankName = "Santander";
    private const string EdgeProcessName = "msedge";

    private static readonly object Sync = new();
    private static readonly Dictionary<string, SantanderCommitmentMemoryEntry> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _initialized;
    private static bool _running;
    private static System.Threading.Timer? _timer;
    private static string _lastObservedTableSignature = string.Empty;
    private static bool _captureInProgress;

    public static event Action? MemoryChanged;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            DebugService.EnabledChanged += DebugService_EnabledChanged;
            Application.Current.Exit += Application_Exit;
        }

        if (DebugService.IsEnabled)
            Start();
    }

    public static IReadOnlyList<SantanderCommitmentMemoryEntry> Snapshot()
    {
        lock (Sync)
        {
            return Entries.Values
                .OrderBy(entry => entry.Banco, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Contexto, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(entry => entry.AtualizadoEm)
                .ToList();
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
            _lastObservedTableSignature = string.Empty;
        }

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => MemoryChanged?.Invoke()));
        DebugService.Record(
            "OPEX",
            "Memória temporária de compromissos foi limpa pelo usuário.",
            DebugEntryLevel.System);
    }

    private static void DebugService_EnabledChanged(bool enabled)
    {
        if (enabled)
            Start();
        else
            StopMonitoring();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        StopMonitoring();
        lock (Sync)
            Entries.Clear();

        DebugService.EnabledChanged -= DebugService_EnabledChanged;
        Application.Current.Exit -= Application_Exit;
    }

    private static void Start()
    {
        lock (Sync)
        {
            if (_running)
                return;

            _running = true;
            _lastObservedTableSignature = string.Empty;
            _captureInProgress = false;
            _timer = new System.Threading.Timer(
                Tick,
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(TickMilliseconds));
        }

        DebugService.Record(
            "SANTANDER",
            $"Memória contextual ativa | temporária em RAM | retenção: {MaxPeriodsPerContext} períodos por contexto / {MaxEntriesTotal} snapshots no total.",
            DebugEntryLevel.System);
    }

    private static void StopMonitoring()
    {
        System.Threading.Timer? timer;
        lock (Sync)
        {
            if (!_running)
                return;

            _running = false;
            timer = _timer;
            _timer = null;
            _captureInProgress = false;
            _lastObservedTableSignature = string.Empty;
        }

        timer?.Dispose();
    }

    private static void Tick(object? state)
    {
        try
        {
            lock (Sync)
            {
                if (!_running || _captureInProgress)
                    return;
            }

            var snapshot = SantanderCommitmentAnalyzerService.LatestSnapshot;
            if (!IsEffectiveResult(snapshot))
                return;

            var tableSignature = BuildTableSignature(snapshot!);
            lock (Sync)
            {
                if (string.Equals(tableSignature, _lastObservedTableSignature, StringComparison.Ordinal))
                    return;

                // A assinatura usada aqui NÃO inclui as datas dos inputs. Portanto,
                // abrir o calendário ou simplesmente trocar o período não sobrescreve
                // a memória enquanto a tabela antiga continuar sendo exibida.
                _lastObservedTableSignature = tableSignature;
                _captureInProgress = true;
            }

            var frozenSnapshot = CloneSnapshot(snapshot!);
            _ = Task.Run(() => CaptureContextAndStore(frozenSnapshot));
        }
        catch (Exception ex)
        {
            lock (Sync)
                _captureInProgress = false;

            DebugService.Record(
                "SANTANDER",
                $"Memória contextual: falha não crítica ({ex.GetType().Name}); a observação continuará.",
                DebugEntryLevel.Warning);
        }
    }

    private static bool IsEffectiveResult(SantanderCommitmentSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.CalendarioAberto)
            return false;

        if (string.IsNullOrWhiteSpace(snapshot.DataInicial) || string.IsNullOrWhiteSpace(snapshot.DataFinal))
            return false;

        // Total 0 é um resultado válido. Null + nenhuma linha significa que a tabela
        // ainda não foi exposta/carregada e não deve virar memória.
        return snapshot.TotalPagamentos.HasValue || snapshot.Pagamentos.Count > 0;
    }

    private static SantanderCommitmentSnapshot CloneSnapshot(SantanderCommitmentSnapshot snapshot)
    {
        return snapshot with
        {
            Pagamentos = snapshot.Pagamentos.ToList()
        };
    }

    private static string BuildTableSignature(SantanderCommitmentSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.TotalPagamentos?.ToString() ?? "null")
            .Append('|')
            .Append(snapshot.ValorTotal ?? string.Empty)
            .Append('|');

        foreach (var payment in snapshot.Pagamentos)
        {
            builder.Append(payment.DataPagamento).Append('|')
                .Append(payment.Favorecido).Append('|')
                .Append(payment.NumeroPagamento).Append('|')
                .Append(payment.NumeroCliente).Append('|')
                .Append(payment.Valor).Append('|')
                .Append(payment.TipoPagamento).Append('|')
                .Append(payment.Situacao).Append('|')
                .Append(payment.Canal).Append(';');
        }

        return builder.ToString();
    }

    private static void CaptureContextAndStore(SantanderCommitmentSnapshot snapshot)
    {
        try
        {
            var context = DetectCompanyContext();
            Store(snapshot, context.Contexto, context.Empresa);
        }
        catch (Exception ex)
        {
            // Mesmo que o Edge não exponha o nome da empresa nessa leitura, não
            // perdemos o resultado operacional: ele entra num contexto explícito.
            Store(snapshot, "Não identificado", "Empresa não identificada");
            DebugService.Record(
                "SANTANDER",
                $"Contexto da empresa não pôde ser identificado ({ex.GetType().Name}); snapshot mantido como 'Não identificado'.",
                DebugEntryLevel.Warning);
        }
        finally
        {
            lock (Sync)
                _captureInProgress = false;
        }
    }

    private static void Store(SantanderCommitmentSnapshot snapshot, string context, string company)
    {
        var dataInicial = snapshot.DataInicial?.Trim() ?? string.Empty;
        var dataFinal = snapshot.DataFinal?.Trim() ?? dataInicial;
        var total = snapshot.TotalPagamentos ?? snapshot.Pagamentos.Count;
        var totalValue = snapshot.ValorTotal?.Trim();
        if (string.IsNullOrWhiteSpace(totalValue) && snapshot.ValorTotalNumerico.HasValue)
            totalValue = snapshot.ValorTotalNumerico.Value.ToString("C2", new System.Globalization.CultureInfo("pt-BR"));
        totalValue ??= string.Empty;

        var entry = new SantanderCommitmentMemoryEntry(
            BankName,
            context,
            company,
            DateTime.Now,
            dataInicial,
            dataFinal,
            snapshot.SituacaoFiltro?.Trim() ?? string.Empty,
            total,
            totalValue,
            snapshot.ValorTotalNumerico,
            snapshot.Pagamentos.ToList());

        lock (Sync)
        {
            Entries[entry.StorageKey] = entry;
            TrimUnsafe(entry.ContextKey);
        }

        DebugService.Record(
            "SANTANDER",
            $"Memória contextual atualizada | Banco: {entry.Banco} | Contexto: {entry.Contexto} | Empresa: {entry.Empresa} | Período: {entry.Periodo} | Pagamentos: {entry.TotalPagamentos} | Valor total: {entry.ValorTotal}.",
            DebugEntryLevel.Background);

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => MemoryChanged?.Invoke()));
    }

    private static void TrimUnsafe(string updatedContextKey)
    {
        var contextEntries = Entries.Values
            .Where(entry => string.Equals(entry.ContextKey, updatedContextKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.AtualizadoEm)
            .ToList();

        foreach (var obsolete in contextEntries.Skip(MaxPeriodsPerContext))
            Entries.Remove(obsolete.StorageKey);

        var allEntries = Entries.Values
            .OrderByDescending(entry => entry.AtualizadoEm)
            .ToList();

        foreach (var obsolete in allEntries.Skip(MaxEntriesTotal))
            Entries.Remove(obsolete.StorageKey);
    }

    private static (string Contexto, string Empresa) DetectCompanyContext()
    {
        var window = DetectSantanderWindow();
        if (window == null)
            return ("Não identificado", "Empresa não identificada");

        var root = AutomationElement.FromHandle(window.Value.Handle);
        if (root == null)
            return ("Não identificado", "Empresa não identificada");

        var candidates = new List<string>();
        try
        {
            var textNodes = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));

            foreach (AutomationElement node in textNodes)
            {
                var name = SafeName(node);
                if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                    continue;

                if (name.Contains("POSITIVA", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("ADMINISTRADORA", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CORRETORA", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(NormalizeWhitespace(name));
                }
            }
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        var company = candidates
            .Where(candidate => candidate.Contains("POSITIVA", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ContextPriority)
            .ThenByDescending(candidate => candidate.Length)
            .FirstOrDefault();

        company ??= candidates.OrderByDescending(ContextPriority).FirstOrDefault();
        company ??= "Empresa não identificada";

        return (ClassifyContext(company), company);
    }

    private static int ContextPriority(string text)
    {
        var score = 0;
        if (text.Contains("POSITIVA", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (text.Contains("ADMINISTRADORA", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (text.Contains("CORRETORA", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (text.Contains("SEGUROS", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (text.Contains("BENEF", StringComparison.OrdinalIgnoreCase)) score += 4;
        return score;
    }

    private static string ClassifyContext(string company)
    {
        if (company.Contains("ADMINISTRADORA", StringComparison.OrdinalIgnoreCase) ||
            company.Contains("BENEF", StringComparison.OrdinalIgnoreCase))
        {
            return "Administradora";
        }

        if (company.Contains("CORRETORA", StringComparison.OrdinalIgnoreCase) ||
            company.Contains("SEGUROS", StringComparison.OrdinalIgnoreCase))
        {
            return "Corretora";
        }

        return "Não identificado";
    }

    private static WindowCandidate? DetectSantanderWindow()
    {
        WindowCandidate? result = null;

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            var title = ReadWindowText(handle);
            if (!title.Contains("Santander", StringComparison.OrdinalIgnoreCase) &&
                !title.Contains("Internet Banking", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (!IsEdgeProcess(processId))
                return true;

            result = new WindowCandidate(handle, processId);
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsEdgeProcess(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return string.Equals(process.ProcessName, EdgeProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadWindowText(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.Name?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return string.Empty;
        }
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsExpectedUiAutomationException(Exception ex) =>
        ex is ElementNotAvailableException or InvalidOperationException or COMException;

    private readonly record struct WindowCandidate(IntPtr Handle, uint ProcessId);

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
