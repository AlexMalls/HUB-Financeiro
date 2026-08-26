using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace HubFinanceiro;

public sealed record SantanderCommitmentMemoryEntry(
    string Banco,
    string Contexto,
    string Convenio,
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

    public string ContextKey => $"{Banco}|{Contexto}|{Convenio}";
    public string StorageKey => $"{Banco}|{Contexto}|{Convenio}|{DataInicial}|{DataFinal}";
}

/// <summary>
/// Histórico persistente dos últimos resultados efetivamente observados na tela
/// Consultar compromissos. Os snapshots são persistidos em JSON na pasta data do HUB.
///
/// O número do convênio visível no cabeçalho é a fonte de verdade para definir
/// Administradora x Corretora. O nome da empresa é usado apenas como metadado.
/// </summary>
public static class SantanderCommitmentMemoryService
{
    private const int TickMilliseconds = 850;
    private const int ContextProbeIntervalMilliseconds = 1600;
    private const int MaxPeriodsPerContext = 4;
    private const int MaxEntriesTotal = 12;
    private const string BankName = "Santander";
    private const string EdgeProcessName = "msedge";
    private const string AdministradoraConvenio = "0033-3409-004902845301";
    private const string CorretoraConvenio = "0033-4268-004905078983";
    private const string AdministradoraCompanyName = "POSITIVA ADMINISTRADORA DE BENEFÍCIOS LTDA";
    private const string CorretoraCompanyName = "POSITIVA CORRETORA DE SEGUROS LTDA";

    private static readonly object Sync = new();
    private static readonly object PersistenceSync = new();
    private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly string PersistenceDirectory = ResolvePersistenceDirectory();
    private static readonly string PersistencePath = Path.Combine(PersistenceDirectory, "santander_contextos.json");

    public static string PersistenceFilePath => PersistencePath;
    private static readonly Dictionary<string, SantanderCommitmentMemoryEntry> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] CommitmentScreenNames =
    {
        "Consultar compromissos",
        "Consultar Compromissos",
        "Consulta de compromissos",
        "Consulta de Compromissos"
    };

    private static bool _initialized;
    private static bool _running;
    private static System.Threading.Timer? _timer;
    private static string _lastSeenTableSignature = string.Empty;
    private static string _lastStoredCompositeSignature = string.Empty;
    private static string _lastDetectedContextKey = string.Empty;
    private static DateTime _nextContextProbeUtc;
    private static bool _captureInProgress;
    private static string _lastContextDiagnostic = string.Empty;
    private static string _pendingContextKey = string.Empty;
    private static string _pendingContextBaselineTableSignature = string.Empty;
    private static DateTime _pendingContextDetectedAt = DateTime.MinValue;
    private static bool _pendingContextSawEmptyResult;

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

        LoadPersistedEntries();

        if (DebugService.IsEnabled)
            Start();
    }

    public static IReadOnlyList<SantanderCommitmentMemoryEntry> Snapshot()
    {
        lock (Sync)
        {
            return Entries.Values
                .OrderByDescending(entry => ParseDate(entry.DataInicial) ?? DateTime.MinValue)
                .ThenBy(entry => ContextSortOrder(entry.Contexto))
                .ThenByDescending(entry => entry.AtualizadoEm)
                .ToList();
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
            _lastSeenTableSignature = string.Empty;
            _lastStoredCompositeSignature = string.Empty;
            _lastDetectedContextKey = string.Empty;
            _pendingContextKey = string.Empty;
            _pendingContextBaselineTableSignature = string.Empty;
            _pendingContextDetectedAt = DateTime.MinValue;
            _pendingContextSawEmptyResult = false;
            _nextContextProbeUtc = DateTime.MinValue;
        }

        PersistEntriesToDisk();

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => MemoryChanged?.Invoke()));
        DebugService.Record(
            "OPEX",
            "Histórico persistente de compromissos foi limpo pelo usuário.",
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
            _lastSeenTableSignature = string.Empty;
            _lastStoredCompositeSignature = string.Empty;
            _lastDetectedContextKey = string.Empty;
            _lastContextDiagnostic = string.Empty;
            _nextContextProbeUtc = DateTime.MinValue;
            _captureInProgress = false;
            _timer = new System.Threading.Timer(
                Tick,
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(TickMilliseconds));
        }

        DebugService.Record(
            "SANTANDER",
            $"Histórico contextual ativo | persistência: {PersistencePath} | retenção: {MaxPeriodsPerContext} períodos por contexto / {MaxEntriesTotal} snapshots no total.",
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
            _lastSeenTableSignature = string.Empty;
            _lastStoredCompositeSignature = string.Empty;
            _lastDetectedContextKey = string.Empty;
            _pendingContextKey = string.Empty;
            _pendingContextBaselineTableSignature = string.Empty;
            _pendingContextDetectedAt = DateTime.MinValue;
            _pendingContextSawEmptyResult = false;
            _nextContextProbeUtc = DateTime.MinValue;
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
            var effectiveResult = IsEffectiveResult(snapshot);
            var now = DateTime.UtcNow;
            var tableSignature = effectiveResult ? BuildTableSignature(snapshot!) : string.Empty;
            bool tableChanged = false;
            string previousEffectiveTableSignature;

            lock (Sync)
            {
                previousEffectiveTableSignature = _lastSeenTableSignature;

                if (effectiveResult)
                {
                    tableChanged = !string.Equals(tableSignature, _lastSeenTableSignature, StringComparison.Ordinal);
                    if (tableChanged)
                        _lastSeenTableSignature = tableSignature;
                }

                // Mesmo sem tabela, a sonda contextual continua ativa. Assim a troca
                // de convênio enxerga a tela vazia e não reaproveita o snapshot antigo.
                if (!tableChanged && now < _nextContextProbeUtc)
                    return;

                _nextContextProbeUtc = now.AddMilliseconds(ContextProbeIntervalMilliseconds);
                _captureInProgress = true;
            }

            var frozenSnapshot = snapshot == null ? null : CloneSnapshot(snapshot);
            _ = Task.Run(() => CaptureContextAndStore(
                frozenSnapshot,
                tableSignature,
                previousEffectiveTableSignature,
                effectiveResult));
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

    private static void CaptureContextAndStore(
        SantanderCommitmentSnapshot? snapshot,
        string tableSignature,
        string previousEffectiveTableSignature,
        bool effectiveResult)
    {
        try
        {
            var detection = DetectCompanyContext();
            if (!detection.OnCommitmentScreen)
                return;

            if (string.IsNullOrWhiteSpace(detection.Convenio) ||
                string.Equals(detection.Contexto, "Não identificado", StringComparison.OrdinalIgnoreCase))
            {
                PublishContextDiagnosticOnce(
                    "Número do convênio conhecido ainda não foi exposto de forma visível; snapshot aguardará a próxima sonda.");
                return;
            }

            var company = string.IsNullOrWhiteSpace(detection.Empresa)
                ? "Empresa não identificada"
                : detection.Empresa;

            var contextKey = $"{BankName}|{detection.Contexto}|{detection.Convenio}";
            var firstContextDetection = false;
            var contextSwitched = false;
            string previousContext;

            lock (Sync)
            {
                previousContext = _lastDetectedContextKey;
                firstContextDetection = string.IsNullOrWhiteSpace(previousContext);

                if (!string.Equals(previousContext, contextKey, StringComparison.OrdinalIgnoreCase))
                {
                    _lastDetectedContextKey = contextKey;

                    if (!firstContextDetection)
                    {
                        contextSwitched = true;
                        _pendingContextKey = contextKey;
                        _pendingContextBaselineTableSignature = previousEffectiveTableSignature;
                        _pendingContextDetectedAt = DateTime.Now;
                        _pendingContextSawEmptyResult = !effectiveResult;
                    }
                }
                else if (string.Equals(_pendingContextKey, contextKey, StringComparison.OrdinalIgnoreCase) && !effectiveResult)
                {
                    _pendingContextSawEmptyResult = true;
                }

                _lastContextDiagnostic = string.Empty;
            }

            if (firstContextDetection || contextSwitched)
            {
                var previousDisplay = string.IsNullOrWhiteSpace(previousContext)
                    ? "nenhum"
                    : previousContext.Replace("|", " — ");

                DebugService.Record(
                    "SANTANDER",
                    $"Contexto bancário detectado/alterado | {previousDisplay} → {BankName} — {detection.Contexto} | Convênio: {detection.Convenio} | Empresa: {company}.",
                    DebugEntryLevel.Action);
            }

            if (contextSwitched)
            {
                DebugService.Record(
                    "SANTANDER",
                    "Troca de convênio detectada | snapshot anterior invalidado; aguardando resultado novo antes de salvar o novo contexto.",
                    DebugEntryLevel.Background);
                return;
            }

            if (!effectiveResult || snapshot == null)
                return;

            bool waitingForFreshContextResult;
            bool sawEmptyResult;
            string baselineTableSignature;
            DateTime detectedAt;

            lock (Sync)
            {
                waitingForFreshContextResult = string.Equals(_pendingContextKey, contextKey, StringComparison.OrdinalIgnoreCase);
                sawEmptyResult = _pendingContextSawEmptyResult;
                baselineTableSignature = _pendingContextBaselineTableSignature;
                detectedAt = _pendingContextDetectedAt;
            }

            if (waitingForFreshContextResult)
            {
                if (!IsSafeSnapshotAfterContextSwitch(
                        snapshot.CapturadoEm,
                        detectedAt,
                        tableSignature,
                        baselineTableSignature,
                        sawEmptyResult))
                {
                    return;
                }

                lock (Sync)
                {
                    if (string.Equals(_pendingContextKey, contextKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _pendingContextKey = string.Empty;
                        _pendingContextBaselineTableSignature = string.Empty;
                        _pendingContextDetectedAt = DateTime.MinValue;
                        _pendingContextSawEmptyResult = false;
                    }
                }

                DebugService.Record(
                    "SANTANDER",
                    "Resultado novo confirmado após troca de convênio | contexto liberado para persistência.",
                    DebugEntryLevel.Background);
            }

            var compositeSignature = $"{contextKey}|{tableSignature}";

            lock (Sync)
            {
                if (string.Equals(compositeSignature, _lastStoredCompositeSignature, StringComparison.Ordinal))
                    return;

                _lastStoredCompositeSignature = compositeSignature;
            }

            Store(snapshot, detection.Contexto, detection.Convenio, company);
        }
        catch (Exception ex)
        {
            PublishContextDiagnosticOnce(
                $"Falha temporária ao identificar convênio/contexto ({ex.GetType().Name}); nenhum snapshot será classificado incorretamente.");
        }
        finally
        {
            lock (Sync)
                _captureInProgress = false;
        }
    }

    internal static bool IsSafeSnapshotAfterContextSwitch(
        DateTime snapshotCapturedAt,
        DateTime contextDetectedAt,
        string tableSignature,
        string previousContextTableSignature,
        bool sawEmptyResult)
    {
        if (snapshotCapturedAt <= contextDetectedAt)
            return false;

        if (sawEmptyResult)
            return true;

        return !string.Equals(tableSignature, previousContextTableSignature, StringComparison.Ordinal);
    }

    private static void PublishContextDiagnosticOnce(string message)
    {
        var shouldPublish = false;
        lock (Sync)
        {
            if (!string.Equals(_lastContextDiagnostic, message, StringComparison.Ordinal))
            {
                _lastContextDiagnostic = message;
                shouldPublish = true;
            }
        }

        if (shouldPublish)
        {
            DebugService.Record(
                "SANTANDER",
                $"Memória contextual | {message}",
                DebugEntryLevel.Background);
        }
    }

    private static void Store(SantanderCommitmentSnapshot snapshot, string context, string convenio, string company)
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
            convenio,
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

        PersistEntriesToDisk();

        DebugService.Record(
            "SANTANDER",
            $"Memória contextual atualizada | Banco: {entry.Banco} | Contexto: {entry.Contexto} | Convênio: {entry.Convenio} | Empresa: {entry.Empresa} | Período: {entry.Periodo} | Pagamentos: {entry.TotalPagamentos} | Valor total: {entry.ValorTotal}.",
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

    private static int RemoveCrossContextRaceDuplicatesUnsafe()
    {
        const double duplicateWindowSeconds = 5d;
        var ordered = Entries.Values.OrderBy(entry => entry.AtualizadoEm).ToList();
        var keysToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ordered.Count; i++)
        {
            var older = ordered[i];
            if (keysToRemove.Contains(older.StorageKey))
                continue;

            for (var j = i + 1; j < ordered.Count; j++)
            {
                var newer = ordered[j];
                if (keysToRemove.Contains(newer.StorageKey))
                    continue;

                if (!string.Equals(older.Banco, newer.Banco, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(older.Contexto, newer.Contexto, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(older.DataInicial, newer.DataInicial, StringComparison.Ordinal) ||
                    !string.Equals(older.DataFinal, newer.DataFinal, StringComparison.Ordinal))
                    continue;

                if (Math.Abs((newer.AtualizadoEm - older.AtualizadoEm).TotalSeconds) > duplicateWindowSeconds)
                    continue;

                if (string.Equals(BuildStoredEntryPayloadSignature(older), BuildStoredEntryPayloadSignature(newer), StringComparison.Ordinal))
                    keysToRemove.Add(newer.StorageKey);
            }
        }

        foreach (var key in keysToRemove)
            Entries.Remove(key);

        return keysToRemove.Count;
    }

    private static string BuildStoredEntryPayloadSignature(SantanderCommitmentMemoryEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append(entry.TotalPagamentos)
            .Append('|')
            .Append(entry.ValorTotal)
            .Append('|')
            .Append(entry.ValorTotalNumerico?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
            .Append('|');

        foreach (var payment in entry.Pagamentos)
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

    private static string ResolvePersistenceDirectory()
    {
        const string AlexandreUser = @"C:\Users\Alexandre Mallorca";
        const string AlexandreData = @"C:\Users\Alexandre Mallorca\OneDrive - Positiva Administradora de Benefícios Ltda\Documentos\Financeiro\HUB Financeiro\data";
        const string ViniciusUser = @"C:\Users\Vinícius Oliveira";
        const string ViniciusData = @"C:\Users\Vinícius Oliveira\Positiva Administradora de Benefícios Ltda\Alexandre Mallorca Silveira - data";

        if (Directory.Exists(AlexandreUser))
            return AlexandreData;

        if (Directory.Exists(ViniciusUser))
            return ViniciusData;

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
    }

    private static void LoadPersistedEntries()
    {
        try
        {
            Directory.CreateDirectory(PersistenceDirectory);
            if (!File.Exists(PersistencePath))
                return;

            var json = File.ReadAllText(PersistencePath, Encoding.UTF8);
            var savedEntries = JsonSerializer.Deserialize<List<SantanderCommitmentMemoryEntry>>(
                json,
                PersistenceJsonOptions) ?? new List<SantanderCommitmentMemoryEntry>();

            int removedCrossContextDuplicates;
            lock (Sync)
            {
                Entries.Clear();
                foreach (var entry in savedEntries.OrderBy(entry => entry.AtualizadoEm))
                {
                    if (!string.IsNullOrWhiteSpace(entry.StorageKey))
                        Entries[entry.StorageKey] = entry;
                }

                removedCrossContextDuplicates = RemoveCrossContextRaceDuplicatesUnsafe();

                var contextKeys = Entries.Values
                    .Select(entry => entry.ContextKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var contextKey in contextKeys)
                    TrimUnsafe(contextKey);
            }

            if (removedCrossContextDuplicates > 0)
            {
                PersistEntriesToDisk();
                DebugService.Record(
                    "OPEX",
                    $"Migração de segurança: {removedCrossContextDuplicates} snapshot(s) duplicado(s) entre convênios foram removidos do histórico.",
                    DebugEntryLevel.System);
            }

            DebugService.Record(
                "OPEX",
                $"Histórico persistente carregado | {Entries.Count} snapshot(s) | arquivo: {PersistencePath}.",
                DebugEntryLevel.System);
        }
        catch (Exception ex)
        {
            DebugService.Record(
                "OPEX",
                $"Não foi possível carregar o histórico persistente ({ex.GetType().Name}). O HUB continuará operando e tentará salvar novamente nas próximas capturas.",
                DebugEntryLevel.Warning);
        }
    }

    private static void PersistEntriesToDisk()
    {
        List<SantanderCommitmentMemoryEntry> snapshot;
        lock (Sync)
        {
            snapshot = Entries.Values
                .OrderByDescending(entry => entry.AtualizadoEm)
                .ToList();
        }

        lock (PersistenceSync)
        {
            string? temporaryPath = null;
            try
            {
                Directory.CreateDirectory(PersistenceDirectory);
                temporaryPath = PersistencePath + ".tmp";

                var json = JsonSerializer.Serialize(snapshot, PersistenceJsonOptions);
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                File.Move(temporaryPath, PersistencePath, true);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }

                DebugService.Record(
                    "OPEX",
                    $"Falha não crítica ao salvar histórico persistente ({ex.GetType().Name}).",
                    DebugEntryLevel.Warning);
            }
        }
    }
    private static ContextDetection DetectCompanyContext()
    {
        var window = DetectSantanderWindow();
        if (window == null)
            return ContextDetection.NotAvailable;

        var root = AutomationElement.FromHandle(window.Value.Handle);
        if (root == null)
            return ContextDetection.NotAvailable;

        if (!FindAnyExactName(root, CommitmentScreenNames))
            return ContextDetection.NotAvailable;

        // Fonte de verdade: Número do convênio visível no cabeçalho da própria
        // tela Consultar compromissos. O nome da empresa é apenas metadado visual.
        var convenio = FindVisibleKnownConvenio(root);
        var context = ClassifyContextFromConvenio(convenio);

        var candidates = new List<CompanyCandidate>();
        try
        {
            var textNodes = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));

            foreach (AutomationElement node in textNodes)
            {
                var name = NormalizeWhitespace(SafeName(node));
                if (string.IsNullOrWhiteSpace(name) || name.Length > 180)
                    continue;

                if (!name.Contains("POSITIVA", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("ADMINISTRADORA", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CORRETORA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetVisibleRectangle(node, out var top, out var left))
                    continue;

                candidates.Add(new CompanyCandidate(name, top, left));
            }
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        var company = candidates
            .Where(candidate => candidate.Text.Contains("POSITIVA", StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Top)
            .ThenBy(candidate => candidate.Left)
            .ThenByDescending(candidate => ContextPriority(candidate.Text))
            .Select(candidate => candidate.Text)
            .FirstOrDefault();

        company ??= candidates
            .OrderBy(candidate => candidate.Top)
            .ThenBy(candidate => candidate.Left)
            .ThenByDescending(candidate => ContextPriority(candidate.Text))
            .Select(candidate => candidate.Text)
            .FirstOrDefault();

        return new ContextDetection(
            true,
            context,
            CanonicalCompanyNameFromConvenio(convenio),
            convenio);
    }

    private static string FindVisibleKnownConvenio(AutomationElement root)
    {
        var matches = new List<CompanyCandidate>();

        foreach (var known in new[] { AdministradoraConvenio, CorretoraConvenio })
        {
            try
            {
                var elements = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.NameProperty,
                        known,
                        PropertyConditionFlags.IgnoreCase));

                foreach (AutomationElement element in elements)
                {
                    if (TryGetVisibleRectangle(element, out var top, out var left))
                        matches.Add(new CompanyCandidate(known, top, left));
                }
            }
            catch (Exception ex) when (IsExpectedUiAutomationException(ex))
            {
            }
        }

        return matches
            .OrderBy(candidate => candidate.Top)
            .ThenBy(candidate => candidate.Left)
            .Select(candidate => candidate.Text)
            .FirstOrDefault() ?? string.Empty;
    }

    private static bool FindAnyExactName(AutomationElement root, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                var found = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.NameProperty,
                        name,
                        PropertyConditionFlags.IgnoreCase));

                if (found != null)
                    return true;
            }
            catch (Exception ex) when (IsExpectedUiAutomationException(ex))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryGetVisibleRectangle(AutomationElement element, out double top, out double left)
    {
        top = double.MaxValue;
        left = double.MaxValue;

        try
        {
            if (element.Current.IsOffscreen)
                return false;

            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                return false;

            top = rect.Top;
            left = rect.Left;
            return true;
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return false;
        }
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

    private static string CanonicalCompanyNameFromConvenio(string convenio)
    {
        if (string.Equals(convenio, CorretoraConvenio, StringComparison.OrdinalIgnoreCase))
            return CorretoraCompanyName;

        if (string.Equals(convenio, AdministradoraConvenio, StringComparison.OrdinalIgnoreCase))
            return AdministradoraCompanyName;

        return "Empresa não identificada";
    }
    private static string ClassifyContextFromConvenio(string convenio)
    {
        if (string.Equals(convenio, CorretoraConvenio, StringComparison.OrdinalIgnoreCase))
            return "Corretora";

        if (string.Equals(convenio, AdministradoraConvenio, StringComparison.OrdinalIgnoreCase))
            return "Administradora";

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

    private static DateTime? ParseDate(string value)
    {
        return DateTime.TryParseExact(
            value,
            "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static int ContextSortOrder(string context)
    {
        if (context.Equals("Administradora", StringComparison.OrdinalIgnoreCase)) return 0;
        if (context.Equals("Corretora", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    private static bool IsExpectedUiAutomationException(Exception ex) =>
        ex is ElementNotAvailableException or InvalidOperationException or COMException;

    private sealed record CompanyCandidate(string Text, double Top, double Left);
    private sealed record ContextDetection(bool OnCommitmentScreen, string Contexto, string Empresa, string Convenio)
    {
        public static ContextDetection NotAvailable { get; } =
            new(false, "Não identificado", string.Empty, string.Empty);
    }

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

