using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

namespace HubFinanceiro;

public sealed record SantanderCommitmentItem(
    string DataPagamento,
    string Favorecido,
    string NumeroPagamento,
    string NumeroCliente,
    string Valor,
    decimal? ValorNumerico,
    string TipoPagamento,
    string Situacao,
    string Canal);

public sealed record SantanderCommitmentSnapshot(
    DateTime CapturadoEm,
    string? DataInicial,
    string? DataFinal,
    string? SituacaoFiltro,
    bool CalendarioAberto,
    int? TotalPagamentos,
    string? ValorTotal,
    decimal? ValorTotalNumerico,
    IReadOnlyList<SantanderCommitmentItem> Pagamentos);

/// <summary>
/// Leitura operacional específica da tela "Consultar compromissos".
///
/// Diferente do monitor genérico de navegação, esta camada pode ler os dados
/// exibidos na tabela de compromissos porque eles serão usados na futura
/// conciliação automática com o HUB. A captura é deliberadamente limitada à
/// área operacional: datas do filtro e colunas da tabela de pagamentos.
/// Cabeçalho bancário, agência, conta, convênio, documentos, credenciais e
/// campos de autenticação não são lidos nem registrados.
/// </summary>
public static class SantanderCommitmentAnalyzerService
{
    private const int TickMilliseconds = 900;
    private const int ProbeIntervalMilliseconds = 1400;
    private const int ProbeTimeoutMilliseconds = 3500;
    private const int HardAbandonMilliseconds = 18000;
    private const int MaxConcurrentProbes = 2;
    private const int MaxAbandonedProbes = 3;
    private const int MaxRowsPerSnapshot = 500;
    private const string EdgeProcessName = "msedge";

    private static readonly object Sync = new();
    private static readonly List<ProbeWork> ActiveProbes = new();
    private static readonly Regex DateRegex = new(@"^\s*(\d{2}/\d{2}/\d{4})\s*$", RegexOptions.Compiled);
    private static readonly Regex MoneyRegex = new(@"^\s*R\$\s*[\d\.]+,\d{2}\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] CommitmentScreenNames =
    {
        "Consultar compromissos",
        "Consultar Compromissos",
        "Consulta de compromissos",
        "Consulta de Compromissos"
    };

    private static readonly string[] CalendarQuickFilterNames =
    {
        "Hoje",
        "Últimos 30 dias",
        "Ultimos 30 dias",
        "Próximos 30 dias",
        "Proximos 30 dias",
        "Mês anterior",
        "Mes anterior",
        "Mês atual",
        "Mes atual"
    };

    private static readonly string[] HeaderLabels =
    {
        "Data de Pagamento",
        "Favorecido",
        "Nº Pagamento",
        "N° Pagamento",
        "Nº Cliente",
        "N° Cliente",
        "Valor (R$)",
        "Tipo de Pagamento",
        "Situação",
        "Situacao",
        "Canal"
    };

    private static readonly HashSet<string> KnownSituations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Todos",
        "Efetivado",
        "Agendado",
        "Pendente",
        "Cancelado",
        "Rejeitado",
        "Processando",
        "Autorizado"
    };

    private static bool _initialized;
    private static bool _running;
    private static Timer? _timer;
    private static DateTime _nextProbeUtc;
    private static long _nextGeneration;
    private static long _lastAppliedGeneration;
    private static int _abandonedProbeCount;
    private static string _lastTimeoutSignature = string.Empty;
    private static string _lastErrorSignature = string.Empty;
    private static bool _wasOnCommitmentScreen;
    private static bool _lastCalendarOpen;
    private static string _lastPeriodSignature = string.Empty;
    private static string _lastTableSignature = string.Empty;
    private static SantanderCommitmentSnapshot? _latestSnapshot;

    public static SantanderCommitmentSnapshot? LatestSnapshot
    {
        get
        {
            lock (Sync)
                return _latestSnapshot;
        }
    }

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            Application.Current.Exit += Application_Exit;
        }

        // A leitura dos compromissos agora é infraestrutura operacional do O.P.E.X.,
        // portanto permanece ativa independentemente do Modo Debug.
        Start();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        Stop();
        Application.Current.Exit -= Application_Exit;
    }

    private static void Start()
    {
        lock (Sync)
        {
            if (_running)
                return;

            _running = true;
            ActiveProbes.Clear();
            _nextProbeUtc = DateTime.MinValue;
            _nextGeneration = 0;
            _lastAppliedGeneration = 0;
            _abandonedProbeCount = 0;
            _lastTimeoutSignature = string.Empty;
            _lastErrorSignature = string.Empty;
            _wasOnCommitmentScreen = false;
            _lastCalendarOpen = false;
            _lastPeriodSignature = string.Empty;
            _lastTableSignature = string.Empty;
            _latestSnapshot = null;
            _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMilliseconds));
        }

        DebugService.Record(
            "SANTANDER",
            "Analisador de compromissos iniciado | captura: período, calendário e composição da tabela operacional.",
            DebugEntryLevel.System);
    }

    private static void Stop()
    {
        Timer? timer;
        lock (Sync)
        {
            if (!_running)
                return;

            _running = false;
            timer = _timer;
            _timer = null;
            ActiveProbes.Clear();
            _latestSnapshot = null;
        }

        timer?.Dispose();
    }

    private static void Tick(object? state)
    {
        try
        {
            lock (Sync)
            {
                if (!_running)
                    return;
            }

            ConsumeCompletedProbes();

            var now = DateTime.UtcNow;
            var window = DetectSantanderWindow();
            MaintainWatchdog(now, window);
        }
        catch (Exception ex)
        {
            PublishDiagnosticError(ex);
        }
    }

    private static void MaintainWatchdog(DateTime now, WindowCandidate? window)
    {
        lock (Sync)
            ActiveProbes.RemoveAll(work => work.Task.IsCompleted);

        if (window == null)
        {
            PublishScreenUnavailable();
            return;
        }

        List<ProbeWork> active;
        lock (Sync)
            active = ActiveProbes.ToList();

        var timedOut = active
            .Where(work => now - work.StartedUtc >= TimeSpan.FromMilliseconds(ProbeTimeoutMilliseconds))
            .OrderBy(work => work.StartedUtc)
            .ToList();

        if (timedOut.Count > 0)
        {
            var oldest = timedOut[0];
            var signature = $"{oldest.Generation}:{oldest.WindowHandle}";
            var shouldReport = false;

            lock (Sync)
            {
                if (!string.Equals(signature, _lastTimeoutSignature, StringComparison.Ordinal))
                {
                    _lastTimeoutSignature = signature;
                    shouldReport = true;
                }
            }

            if (shouldReport)
            {
                DebugService.Record(
                    "SANTANDER",
                    $"Analisador de compromissos: leitura operacional excedeu {ProbeTimeoutMilliseconds}ms; uma sonda de recuperação será utilizada.",
                    DebugEntryLevel.Warning);
            }
        }

        foreach (var stale in active
                     .Where(work => now - work.StartedUtc >= TimeSpan.FromMilliseconds(HardAbandonMilliseconds))
                     .OrderBy(work => work.StartedUtc)
                     .ToList())
        {
            lock (Sync)
            {
                if (_abandonedProbeCount >= MaxAbandonedProbes)
                    break;

                if (ActiveProbes.Remove(stale))
                    _abandonedProbeCount++;
            }
        }

        bool shouldStart;
        lock (Sync)
        {
            var hasTimedOutProbe = ActiveProbes.Any(work =>
                now - work.StartedUtc >= TimeSpan.FromMilliseconds(ProbeTimeoutMilliseconds));

            shouldStart = now >= _nextProbeUtc &&
                          ActiveProbes.Count < MaxConcurrentProbes &&
                          (ActiveProbes.Count == 0 || hasTimedOutProbe);

            if (shouldStart)
                _nextProbeUtc = now.AddMilliseconds(ProbeIntervalMilliseconds);
        }

        if (shouldStart)
            StartProbe(window, now);
    }

    private static void StartProbe(WindowCandidate window, DateTime startedUtc)
    {
        long generation;
        lock (Sync)
            generation = ++_nextGeneration;

        var task = Task.Run(() => Probe(window, generation));
        var work = new ProbeWork(generation, window.Handle, startedUtc, task);

        lock (Sync)
        {
            if (_running)
                ActiveProbes.Add(work);
        }
    }

    private static void ConsumeCompletedProbes()
    {
        List<ProbeWork> completed;
        lock (Sync)
        {
            completed = ActiveProbes.Where(work => work.Task.IsCompleted).ToList();
            foreach (var work in completed)
                ActiveProbes.Remove(work);
        }

        foreach (var work in completed.OrderBy(work => work.Generation))
        {
            try
            {
                var result = work.Task.GetAwaiter().GetResult();
                ApplyProbeResult(result);
            }
            catch (Exception ex)
            {
                PublishDiagnosticError(ex);
            }
        }
    }

    private static ProbeResult Probe(WindowCandidate window, long generation)
    {
        var root = AutomationElement.FromHandle(window.Handle);
        if (root == null)
            return ProbeResult.NotOnScreen(generation, window);

        if (!FindAnyExactName(root, CommitmentScreenNames))
            return ProbeResult.NotOnScreen(generation, window);

        var dates = ReadDateRange(root);
        var situation = ReadSituationFilter(root);
        var calendarOpen = DetectCalendarOpen(root);
        var (totalPayments, totalValueRaw, totalValueNumeric, payments) = ReadPaymentsTable(root);

        var snapshot = new SantanderCommitmentSnapshot(
            DateTime.Now,
            dates.Count > 0 ? dates[0] : null,
            dates.Count > 1 ? dates[1] : dates.FirstOrDefault(),
            situation,
            calendarOpen,
            totalPayments,
            totalValueRaw,
            totalValueNumeric,
            payments);

        return new ProbeResult(generation, window, true, snapshot);
    }

    private static IReadOnlyList<string> ReadDateRange(AutomationElement root)
    {
        var result = new List<string>();

        try
        {
            var edits = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            foreach (AutomationElement edit in edits)
            {
                var value = TryReadValue(edit);
                var match = DateRegex.Match(value);
                if (!match.Success)
                    continue;

                var date = match.Groups[1].Value;
                result.Add(date);
                if (result.Count == 2)
                    break;
            }
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        return result;
    }

    private static string? ReadSituationFilter(AutomationElement root)
    {
        try
        {
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            foreach (AutomationElement button in buttons)
            {
                var name = Normalize(ReadAutomationProperty(() => button.Current.Name));
                if (KnownSituations.Contains(name))
                    return name;
            }
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        return null;
    }

    private static bool DetectCalendarOpen(AutomationElement root)
    {
        return FindAnyExactName(root, CalendarQuickFilterNames);
    }

    private static (int? Total, string? TotalValueRaw, decimal? TotalValueNumeric, IReadOnlyList<SantanderCommitmentItem> Rows)
        ReadPaymentsTable(AutomationElement root)
    {
        var table = FindPaymentsTable(root);
        if (table == null)
            return (null, null, null, Array.Empty<SantanderCommitmentItem>());

        var total = ReadSummaryInteger(root, "Total de pagamentos");
        var totalValueRaw = ReadSummaryMoney(root, "Valor Total");
        var totalValueNumeric = ParseBrazilianMoney(totalValueRaw);

        var rows = TryReadRowsFromGridPattern(table);
        if (rows.Count == 0)
            rows = TryReadRowsFromDataItems(table);
        if (rows.Count == 0)
            rows = TryReadRowsFromOrderedText(table);

        if (!total.HasValue && rows.Count > 0)
            total = rows.Count;

        if (!totalValueNumeric.HasValue && rows.Count > 0 && rows.All(row => row.ValorNumerico.HasValue))
        {
            totalValueNumeric = rows.Sum(row => row.ValorNumerico!.Value);
            totalValueRaw = FormatBrazilianMoney(totalValueNumeric.Value);
        }

        return (total, totalValueRaw, totalValueNumeric, rows);
    }

    private static AutomationElement? FindPaymentsTable(AutomationElement root)
    {
        try
        {
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Table),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid));

            var candidates = root.FindAll(TreeScope.Descendants, condition);
            foreach (AutomationElement candidate in candidates)
            {
                if (FindAnyExactName(candidate, new[] { "Data de Pagamento" }) &&
                    FindAnyExactName(candidate, new[] { "Favorecido" }) &&
                    FindAnyExactName(candidate, new[] { "Valor (R$)" }))
                {
                    return candidate;
                }
            }

            return candidates.Count > 0 ? candidates[0] : null;
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return null;
        }
    }

    private static List<SantanderCommitmentItem> TryReadRowsFromGridPattern(AutomationElement table)
    {
        var rows = new List<SantanderCommitmentItem>();

        try
        {
            if (!table.TryGetCurrentPattern(GridPattern.Pattern, out var pattern) || pattern is not GridPattern grid)
                return rows;

            var rowCount = Math.Min(grid.Current.RowCount, MaxRowsPerSnapshot + 1);
            var columnCount = grid.Current.ColumnCount;
            if (columnCount < 7)
                return rows;

            for (var row = 0; row < rowCount && rows.Count < MaxRowsPerSnapshot; row++)
            {
                var values = new List<string>();
                for (var col = 0; col < Math.Min(columnCount, 8); col++)
                {
                    var cell = grid.GetItem(row, col);
                    values.Add(ReadElementText(cell));
                }

                var item = ParseFixedCells(values);
                if (item != null)
                    rows.Add(item);
            }
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        return DeduplicateRows(rows);
    }

    private static List<SantanderCommitmentItem> TryReadRowsFromDataItems(AutomationElement table)
    {
        var rows = new List<SantanderCommitmentItem>();

        try
        {
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

            var items = table.FindAll(TreeScope.Descendants, condition);
            foreach (AutomationElement itemElement in items)
            {
                if (rows.Count >= MaxRowsPerSnapshot)
                    break;

                var tokens = ReadOrderedTexts(itemElement);
                var item = ParseRowTokens(tokens);
                if (item != null)
                    rows.Add(item);
            }
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        return DeduplicateRows(rows);
    }

    private static List<SantanderCommitmentItem> TryReadRowsFromOrderedText(AutomationElement table)
    {
        var tokens = ReadOrderedTexts(table);
        var rows = new List<SantanderCommitmentItem>();

        for (var index = 0; index < tokens.Count && rows.Count < MaxRowsPerSnapshot; index++)
        {
            if (!DateRegex.IsMatch(tokens[index]))
                continue;

            var (item, consumed) = ParseRowFromTokenStream(tokens, index);
            if (item == null)
                continue;

            rows.Add(item);
            index += Math.Max(0, consumed - 1);
        }

        return DeduplicateRows(rows);
    }

    private static SantanderCommitmentItem? ParseFixedCells(IReadOnlyList<string> cells)
    {
        var normalized = cells.Select(Normalize).ToList();
        if (normalized.Count < 7 || !DateRegex.IsMatch(normalized[0]))
            return null;

        if (normalized.Count >= 8)
        {
            return CreateItem(
                normalized[0], normalized[1], normalized[2], normalized[3],
                normalized[4], normalized[5], normalized[6], normalized[7]);
        }

        return ParseRowTokens(normalized);
    }

    private static SantanderCommitmentItem? ParseRowTokens(IReadOnlyList<string> rawTokens)
    {
        var tokens = rawTokens
            .Select(Normalize)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Where(token => !HeaderLabels.Contains(token, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var dateIndex = tokens.FindIndex(token => DateRegex.IsMatch(token));
        if (dateIndex < 0)
            return null;

        var (item, _) = ParseRowFromTokenStream(tokens, dateIndex);
        return item;
    }

    private static (SantanderCommitmentItem? Item, int Consumed) ParseRowFromTokenStream(
        IReadOnlyList<string> tokens,
        int startIndex)
    {
        if (startIndex < 0 || startIndex >= tokens.Count || !DateRegex.IsMatch(tokens[startIndex]))
            return (null, 0);

        var cursor = startIndex;
        string Next()
        {
            cursor++;
            return cursor < tokens.Count ? Normalize(tokens[cursor]) : string.Empty;
        }

        var date = Normalize(tokens[startIndex]);
        var receiver = Next();
        var paymentNumber = Next();
        var possibleClientOrValue = Next();

        if (string.IsNullOrWhiteSpace(receiver) ||
            string.IsNullOrWhiteSpace(paymentNumber) ||
            string.IsNullOrWhiteSpace(possibleClientOrValue))
        {
            return (null, Math.Max(1, cursor - startIndex + 1));
        }

        string clientNumber;
        string value;
        if (MoneyRegex.IsMatch(possibleClientOrValue))
        {
            clientNumber = string.Empty;
            value = possibleClientOrValue;
        }
        else
        {
            clientNumber = possibleClientOrValue;
            value = Next();
        }

        var paymentType = Next();
        var situation = Next();
        var channel = Next();

        if (!MoneyRegex.IsMatch(value) ||
            string.IsNullOrWhiteSpace(paymentType) ||
            string.IsNullOrWhiteSpace(situation) ||
            string.IsNullOrWhiteSpace(channel))
        {
            return (null, Math.Max(1, cursor - startIndex + 1));
        }

        return (
            CreateItem(date, receiver, paymentNumber, clientNumber, value, paymentType, situation, channel),
            cursor - startIndex + 1);
    }

    private static SantanderCommitmentItem CreateItem(
        string date,
        string receiver,
        string paymentNumber,
        string clientNumber,
        string value,
        string paymentType,
        string situation,
        string channel)
    {
        return new SantanderCommitmentItem(
            Normalize(date),
            Normalize(receiver),
            Normalize(paymentNumber),
            Normalize(clientNumber),
            Normalize(value),
            ParseBrazilianMoney(value),
            Normalize(paymentType),
            Normalize(situation),
            Normalize(channel));
    }

    private static List<SantanderCommitmentItem> DeduplicateRows(IEnumerable<SantanderCommitmentItem> rows)
    {
        return rows
            .GroupBy(row => $"{row.DataPagamento}|{row.NumeroPagamento}|{row.NumeroCliente}|{row.Valor}|{row.Favorecido}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(MaxRowsPerSnapshot)
            .ToList();
    }

    private static int? ReadSummaryInteger(AutomationElement root, string label)
    {
        var labelElement = FindExactName(root, label);
        if (labelElement == null)
            return null;

        foreach (var token in ReadNearbyTexts(labelElement))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
        }

        return null;
    }

    private static string? ReadSummaryMoney(AutomationElement root, string label)
    {
        var labelElement = FindExactName(root, label);
        if (labelElement == null)
            return null;

        return ReadNearbyTexts(labelElement)
            .Select(Normalize)
            .FirstOrDefault(token => MoneyRegex.IsMatch(token));
    }

    private static IReadOnlyList<string> ReadNearbyTexts(AutomationElement element)
    {
        try
        {
            var walker = TreeWalker.RawViewWalker;
            var parent = walker.GetParent(element);
            if (parent == null)
                return Array.Empty<string>();

            return ReadOrderedTexts(parent);
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return Array.Empty<string>();
        }
    }

    private static List<string> ReadOrderedTexts(AutomationElement root)
    {
        var result = new List<string>();

        try
        {
            var texts = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));

            foreach (AutomationElement text in texts)
            {
                var value = Normalize(ReadAutomationProperty(() => text.Current.Name));
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        return result;
    }

    private static string ReadElementText(AutomationElement element)
    {
        var direct = Normalize(ReadAutomationProperty(() => element.Current.Name));
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var texts = ReadOrderedTexts(element);
        return texts.Count == 0 ? string.Empty : Normalize(string.Join(" ", texts));
    }

    private static string TryReadValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
                return Normalize(valuePattern.Current.Value);
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        return string.Empty;
    }

    private static void ApplyProbeResult(ProbeResult result)
    {
        lock (Sync)
        {
            if (!_running || result.Generation <= _lastAppliedGeneration)
                return;

            _lastAppliedGeneration = result.Generation;
        }

        if (!result.OnCommitmentScreen || result.Snapshot == null)
        {
            PublishScreenUnavailable();
            return;
        }

        SantanderCommitmentSnapshot snapshot = result.Snapshot;
        bool enteredScreen;
        bool calendarChanged;
        bool previousCalendarOpen;
        string periodSignature;
        string tableSignature;
        bool periodChanged;
        bool tableChanged;

        lock (Sync)
        {
            enteredScreen = !_wasOnCommitmentScreen;
            _wasOnCommitmentScreen = true;

            previousCalendarOpen = _lastCalendarOpen;
            calendarChanged = previousCalendarOpen != snapshot.CalendarioAberto;
            _lastCalendarOpen = snapshot.CalendarioAberto;

            periodSignature = BuildPeriodSignature(snapshot);
            periodChanged = !string.Equals(periodSignature, _lastPeriodSignature, StringComparison.Ordinal);
            if (periodChanged)
                _lastPeriodSignature = periodSignature;

            tableSignature = BuildTableSignature(snapshot);
            tableChanged = !string.Equals(tableSignature, _lastTableSignature, StringComparison.Ordinal);
            if (tableChanged)
                _lastTableSignature = tableSignature;

            _latestSnapshot = snapshot;
            _lastErrorSignature = string.Empty;
        }

        if (enteredScreen)
        {
            DebugService.Record(
                "SANTANDER",
                "Tela operacional ativa | Consultar compromissos | leitura estruturada de filtros e pagamentos habilitada.",
                DebugEntryLevel.System);
        }

        if (calendarChanged)
        {
            var action = snapshot.CalendarioAberto ? "aberto" : "fechado";
            DebugService.Record(
                "SANTANDER",
                $"Seletor de data {action} | período atual: {FormatPeriod(snapshot)}.",
                DebugEntryLevel.Action);
        }

        if (periodChanged)
        {
            DebugService.Record(
                "SANTANDER",
                $"Filtro de período detectado | {FormatPeriod(snapshot)}{FormatSituation(snapshot.SituacaoFiltro)}.",
                DebugEntryLevel.Action);
        }

        if (tableChanged && (snapshot.Pagamentos.Count > 0 || snapshot.TotalPagamentos.HasValue))
        {
            PublishPaymentComposition(snapshot);
        }
    }

    private static void PublishScreenUnavailable()
    {
        var leftScreen = false;
        lock (Sync)
        {
            if (_wasOnCommitmentScreen)
            {
                _wasOnCommitmentScreen = false;
                _lastCalendarOpen = false;
                leftScreen = true;
            }
        }

        if (leftScreen)
        {
            DebugService.Record(
                "SANTANDER",
                "Leitura operacional de compromissos suspensa | a tela Consultar compromissos não está ativa.",
                DebugEntryLevel.Background);
        }
    }

    private static void PublishPaymentComposition(SantanderCommitmentSnapshot snapshot)
    {
        var totalText = snapshot.TotalPagamentos?.ToString(CultureInfo.InvariantCulture) ?? snapshot.Pagamentos.Count.ToString(CultureInfo.InvariantCulture);
        var valueText = snapshot.ValorTotal ??
                        (snapshot.ValorTotalNumerico.HasValue ? FormatBrazilianMoney(snapshot.ValorTotalNumerico.Value) : "não exposto");

        DebugService.Record(
            "SANTANDER",
            $"Composição de compromissos carregada | Período: {FormatPeriod(snapshot)} | Total: {totalText} | Valor total: {valueText} | Linhas lidas: {snapshot.Pagamentos.Count}.",
            DebugEntryLevel.Background);

        for (var index = 0; index < snapshot.Pagamentos.Count; index++)
        {
            var item = snapshot.Pagamentos[index];
            var clientPart = string.IsNullOrWhiteSpace(item.NumeroCliente)
                ? "Nº Cliente: —"
                : $"Nº Cliente: {item.NumeroCliente}";

            DebugService.Record(
                "SANTANDER",
                $"Compromisso {index + 1}/{snapshot.Pagamentos.Count} | Data: {item.DataPagamento} | Favorecido: {item.Favorecido} | Nº Pagamento: {item.NumeroPagamento} | {clientPart} | Valor: {item.Valor} | Tipo: {item.TipoPagamento} | Situação: {item.Situacao} | Canal: {item.Canal}.",
                DebugEntryLevel.Background);
        }
    }

    private static string BuildPeriodSignature(SantanderCommitmentSnapshot snapshot) =>
        $"{snapshot.DataInicial}|{snapshot.DataFinal}|{snapshot.SituacaoFiltro}";

    private static string BuildTableSignature(SantanderCommitmentSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.TotalPagamentos)
            .Append('|')
            .Append(snapshot.ValorTotal)
            .Append('|');

        foreach (var item in snapshot.Pagamentos)
        {
            builder.Append(item.DataPagamento).Append('|')
                .Append(item.Favorecido).Append('|')
                .Append(item.NumeroPagamento).Append('|')
                .Append(item.NumeroCliente).Append('|')
                .Append(item.Valor).Append('|')
                .Append(item.TipoPagamento).Append('|')
                .Append(item.Situacao).Append('|')
                .Append(item.Canal).Append(';');
        }

        return builder.ToString();
    }

    private static string FormatPeriod(SantanderCommitmentSnapshot snapshot)
    {
        var start = string.IsNullOrWhiteSpace(snapshot.DataInicial) ? "?" : snapshot.DataInicial;
        var end = string.IsNullOrWhiteSpace(snapshot.DataFinal) ? start : snapshot.DataFinal;
        return $"{start} → {end}";
    }

    private static string FormatSituation(string? situation) =>
        string.IsNullOrWhiteSpace(situation) ? string.Empty : $" | Situação: {situation}";

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : WhitespaceRegex.Replace(value, " ").Trim();

    private static decimal? ParseBrazilianMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = Normalize(raw).Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var value)
            ? value
            : null;
    }

    private static string FormatBrazilianMoney(decimal value) =>
        value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));

    private static bool FindAnyExactName(AutomationElement root, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (FindExactName(root, name) != null)
                return true;
        }

        return false;
    }

    private static AutomationElement? FindExactName(AutomationElement root, string name)
    {
        try
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    name,
                    PropertyConditionFlags.IgnoreCase));
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return null;
        }
    }

    private static WindowCandidate? DetectSantanderWindow()
    {
        WindowCandidate? result = null;

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            var title = ReadWindowText(handle);
            if (!LooksLikeSantanderWindow(title))
                return true;

            GetWindowThreadProcessId(handle, out var processId);
            if (!IsEdgeProcess(processId))
                return true;

            result = new WindowCandidate(handle, processId, title);
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static bool LooksLikeSantanderWindow(string title) =>
        title.Contains("Santander", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Internet Banking", StringComparison.OrdinalIgnoreCase);

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

    private static string ReadAutomationProperty(Func<string> getter)
    {
        try
        {
            return getter()?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return string.Empty;
        }
    }

    private static bool IsExpectedUiAutomationException(Exception ex) =>
        ex is ElementNotAvailableException or InvalidOperationException or COMException;

    private static void PublishDiagnosticError(Exception ex)
    {
        var root = ex is AggregateException aggregate ? aggregate.GetBaseException() : ex;
        var signature = $"{root.GetType().Name}:{root.Message}";

        lock (Sync)
        {
            if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
                return;

            _lastErrorSignature = signature;
        }

        DebugService.Record(
            "SANTANDER",
            $"Falha não crítica na leitura estruturada de compromissos ({root.GetType().Name}). O monitor continuará tentando.",
            DebugEntryLevel.Warning);
    }

    private sealed record WindowCandidate(IntPtr Handle, uint ProcessId, string Title);

    private sealed record ProbeResult(
        long Generation,
        WindowCandidate Window,
        bool OnCommitmentScreen,
        SantanderCommitmentSnapshot? Snapshot)
    {
        public static ProbeResult NotOnScreen(long generation, WindowCandidate window) =>
            new(generation, window, false, null);
    }

    private sealed record ProbeWork(
        long Generation,
        IntPtr WindowHandle,
        DateTime StartedUtc,
        Task<ProbeResult> Task);

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
