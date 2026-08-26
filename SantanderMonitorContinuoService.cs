using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace HubFinanceiro;

/// <summary>
/// Monitor contínuo do Internet Banking.
///
/// A camada de UI Automation roda em workers isolados para que uma chamada COM
/// bloqueada pelo Edge nunca interrompa o relógio principal do monitor.
/// O serviço registra somente rota sanitizada e rótulos de navegação/contexto
/// previamente permitidos; valores, contas, documentos, credenciais e campos
/// editáveis não são coletados.
/// </summary>
public static class SantanderMonitorContinuoService
{
    private const int TickMilliseconds = 750;
    private const int ProbeIntervalMilliseconds = 1200;
    private const int ProbeTimeoutMilliseconds = 2800;
    private const int HardAbandonMilliseconds = 15000;
    private const int HeartbeatMilliseconds = 8000;
    private const int MaxConcurrentProbes = 2;
    private const int MaxAbandonedProbesPerSession = 3;
    private const string EdgeProcessName = "msedge";

    private static readonly object Sync = new();
    private static readonly List<ProbeWork> ActiveProbes = new();

    private static readonly string[] ModalDetailMarkers =
    {
        "Detalhe do Pagamento",
        "Detalhe do pagamento"
    };

    private static readonly string[] ReceiverMarkers =
    {
        "Dados do Recebedor",
        "Dados do recebedor"
    };

    private static readonly string[] PayerMarkers =
    {
        "Dados do Pagador",
        "Dados do pagador"
    };

    private static readonly string[] PaymentDataMarkers =
    {
        "Dados dos Pagamentos",
        "Dados dos pagamentos",
        "Dados do Pagamento",
        "Dados do pagamento"
    };

    private static readonly string[] CommitmentMarkers =
    {
        "Consultar Compromissos",
        "Consultar compromissos",
        "Consulta de Compromissos",
        "Consulta de compromissos"
    };

    private static bool _initialized;
    private static bool _running;
    private static Timer? _timer;
    private static DateTime _nextProbeUtc;
    private static DateTime _nextHeartbeatUtc;
    private static long _nextGeneration;
    private static long _lastAppliedGeneration;
    private static int _abandonedProbeCount;
    private static string _lastTimeoutSignature = string.Empty;
    private static string _lastErrorSignature = string.Empty;
    private static WindowCandidate? _lastWindow;
    private static ProbeResult? _lastResult;
    private static string _lastStateSignature = string.Empty;
    private static string _lastContextSignature = string.Empty;
    private static string _lastFocusSignature = string.Empty;
    private static DateTime _lastSuccessfulProbeUtc;

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

    private static void DebugService_EnabledChanged(bool enabled)
    {
        if (enabled)
            Start();
        else
            Stop();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        Stop();
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
            ActiveProbes.Clear();
            _nextProbeUtc = DateTime.MinValue;
            _nextHeartbeatUtc = DateTime.UtcNow.AddMilliseconds(HeartbeatMilliseconds);
            _nextGeneration = 0;
            _lastAppliedGeneration = 0;
            _abandonedProbeCount = 0;
            _lastTimeoutSignature = string.Empty;
            _lastErrorSignature = string.Empty;
            _lastWindow = null;
            _lastResult = null;
            _lastStateSignature = string.Empty;
            _lastContextSignature = string.Empty;
            _lastFocusSignature = string.Empty;
            _lastSuccessfulProbeUtc = DateTime.MinValue;
            _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMilliseconds));
        }

        DebugService.Record(
            "SANTANDER",
            $"Monitor contínuo iniciado | relógio: {TickMilliseconds}ms | sonda UIA isolada: {ProbeIntervalMilliseconds}ms | watchdog: {ProbeTimeoutMilliseconds}ms.",
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
            _lastWindow = null;
            _lastResult = null;
        }

        timer?.Dispose();
    }

    /// <summary>
    /// Este callback nunca executa UI Automation diretamente. Ele só faz
    /// operações Win32 rápidas, observa workers e agenda novas sondas.
    /// </summary>
    private static void Tick(object? state)
    {
        try
        {
            lock (Sync)
            {
                if (!_running)
                    return;
            }

            var now = DateTime.UtcNow;
            var window = DetectSantanderWindow();

            PublishWindowPresence(window);
            ConsumeCompletedProbes();
            MaintainWatchdog(now, window);
            PublishHeartbeat(now, window);
        }
        catch (Exception ex)
        {
            PublishDiagnosticError(ex);
        }
    }

    private static void PublishWindowPresence(WindowCandidate? current)
    {
        WindowCandidate? previous;
        lock (Sync)
        {
            previous = _lastWindow;
            _lastWindow = current;
        }

        if (previous == null && current != null)
        {
            DebugService.Record(
                "SANTANDER",
                $"Internet Banking detectado | janela: {current.Title} | processo Edge: {current.ProcessId}.",
                DebugEntryLevel.Background);
            return;
        }

        if (previous != null && current == null)
        {
            DebugService.Record(
                "SANTANDER",
                "Janela do Internet Banking não está mais disponível.",
                DebugEntryLevel.Background);

            lock (Sync)
            {
                _lastResult = null;
                _lastStateSignature = string.Empty;
                _lastContextSignature = string.Empty;
                _lastFocusSignature = string.Empty;
            }
            return;
        }

        if (previous != null && current != null && previous.Handle != current.Handle)
        {
            DebugService.Record(
                "SANTANDER",
                "Janela do Internet Banking foi recriada pelo Edge; o monitor reassociou automaticamente a nova janela.",
                DebugEntryLevel.System);
        }
    }

    private static void MaintainWatchdog(DateTime now, WindowCandidate? window)
    {
        if (window == null)
            return;

        List<ProbeWork> active;
        lock (Sync)
        {
            ActiveProbes.RemoveAll(work => work.Task.IsCompleted);
            active = ActiveProbes.ToList();
        }

        var timedOut = active
            .Where(work => now - work.StartedUtc >= TimeSpan.FromMilliseconds(ProbeTimeoutMilliseconds))
            .OrderBy(work => work.StartedUtc)
            .ToList();

        if (timedOut.Count > 0)
        {
            var oldest = timedOut[0];
            var timeoutSignature = $"{oldest.Generation}:{oldest.WindowHandle}";
            var shouldReport = false;

            lock (Sync)
            {
                if (!string.Equals(timeoutSignature, _lastTimeoutSignature, StringComparison.Ordinal))
                {
                    _lastTimeoutSignature = timeoutSignature;
                    shouldReport = true;
                }
            }

            if (shouldReport)
            {
                DebugService.Record(
                    "SANTANDER",
                    $"Watchdog: uma sonda de acessibilidade excedeu {ProbeTimeoutMilliseconds}ms. O relógio do monitor continua ativo e uma sonda de recuperação será usada.",
                    DebugEntryLevel.Warning);
            }
        }

        // Uma sonda presa por muito tempo deixa de contar no limite lógico.
        // O Task subjacente não é abortado (Thread.Abort não existe no .NET 8),
        // mas limitamos esse abandono a três ocorrências por sessão.
        foreach (var stale in active
                     .Where(work => now - work.StartedUtc >= TimeSpan.FromMilliseconds(HardAbandonMilliseconds))
                     .OrderBy(work => work.StartedUtc)
                     .ToList())
        {
            var removed = false;
            lock (Sync)
            {
                if (_abandonedProbeCount < MaxAbandonedProbesPerSession && ActiveProbes.Remove(stale))
                {
                    _abandonedProbeCount++;
                    removed = true;
                }
            }

            if (removed)
            {
                DebugService.Record(
                    "SANTANDER",
                    $"Watchdog: sonda UIA antiga isolada após {HardAbandonMilliseconds / 1000}s sem resposta | recuperações isoladas nesta sessão: {_abandonedProbeCount}/{MaxAbandonedProbesPerSession}.",
                    DebugEntryLevel.Warning);
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

    private static ProbeResult Probe(WindowCandidate candidate, long generation)
    {
        var root = AutomationElement.FromHandle(candidate.Handle);
        if (root == null)
            return ProbeResult.Unavailable(generation, candidate);

        var document = TryFindDocument(root);
        string documentName = string.Empty;
        string? safeUrl = null;

        if (document != null)
        {
            documentName = ReadAutomationProperty(() => document.Current.Name);
            safeUrl = SantanderMonitorService.SanitizeSantanderUrl(TryReadDocumentUrl(document));
        }

        var markers = TryReadContextMarkers(root);
        var labels = TryReadInteractiveNavigationLabels(root);
        var focus = TryReadFocusedSafeControl(candidate.Handle);

        var page = SantanderMonitorService.InferPage(safeUrl, documentName);
        if (markers.Contains("Detalhe do pagamento", StringComparer.OrdinalIgnoreCase))
            page = "Detalhe do pagamento";
        else if (markers.Contains("Consultar compromissos", StringComparer.OrdinalIgnoreCase))
            page = "Consultar compromissos";

        return new ProbeResult(
            generation,
            candidate,
            page,
            safeUrl,
            safeUrl != null,
            markers,
            labels,
            focus,
            DateTime.UtcNow);
    }

    private static AutomationElement? TryFindDocument(AutomationElement root)
    {
        try
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return null;
        }
    }

    private static string? TryReadDocumentUrl(AutomationElement document)
    {
        try
        {
            if (document.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
                return valuePattern.Current.Value;
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
        }

        return null;
    }

    private static IReadOnlyList<string> TryReadContextMarkers(AutomationElement root)
    {
        var markers = new List<string>();

        if (FindAnyExactName(root, ModalDetailMarkers))
        {
            markers.Add("Detalhe do pagamento");
            if (FindAnyExactName(root, ReceiverMarkers)) markers.Add("Dados do recebedor");
            if (FindAnyExactName(root, PayerMarkers)) markers.Add("Dados do pagador");
            if (FindAnyExactName(root, PaymentDataMarkers)) markers.Add("Dados do pagamento");
            return markers;
        }

        if (FindAnyExactName(root, CommitmentMarkers))
            markers.Add("Consultar compromissos");

        if (FindAnyExactName(root, new[] { "Filtros avançados", "Filtros Avançados", "Filtros avancados" }))
            markers.Add("Filtros avançados");

        return markers;
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

    private static IReadOnlyList<string> TryReadInteractiveNavigationLabels(AutomationElement root)
    {
        try
        {
            // Não incluímos Text/Group aqui. A versão anterior varria milhares de
            // nós do DOM e podia ficar presa durante modais. Textos de contexto são
            // lidos separadamente por marcadores exatos e seguros.
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));

            var elements = root.FindAll(TreeScope.Descendants, condition);
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AutomationElement element in elements)
            {
                var label = SantanderMonitorService.SanitizeNavigationLabel(
                    ReadAutomationProperty(() => element.Current.Name));

                if (label != null)
                    labels.Add(label);
            }

            return OrderSafeLabels(labels).Take(20).ToList();
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> OrderSafeLabels(IEnumerable<string> labels)
    {
        static int Priority(string label)
        {
            if (label.Equals("Detalhe do pagamento", StringComparison.OrdinalIgnoreCase)) return 0;
            if (label.Equals("Consultar compromissos", StringComparison.OrdinalIgnoreCase)) return 1;
            if (label.Equals("Compromissos", StringComparison.OrdinalIgnoreCase)) return 2;
            if (label.Equals("Pagamentos", StringComparison.OrdinalIgnoreCase)) return 3;
            if (label.Equals("Voltar", StringComparison.OrdinalIgnoreCase)) return 4;
            if (label.Equals("Fechar", StringComparison.OrdinalIgnoreCase)) return 5;
            if (label.Equals("Salvar em PDF", StringComparison.OrdinalIgnoreCase)) return 6;
            if (label.Equals("Imprimir", StringComparison.OrdinalIgnoreCase)) return 7;
            return 50;
        }

        return labels
            .OrderBy(Priority)
            .ThenBy(label => label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static SafeFocus? TryReadFocusedSafeControl(IntPtr expectedWindow)
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground != expectedWindow)
                return null;

            var element = AutomationElement.FocusedElement;
            if (element == null)
                return null;

            var controlType = element.Current.ControlType;
            if (!IsSafeInteractiveType(controlType))
                return null;

            var label = SantanderMonitorService.SanitizeNavigationLabel(
                ReadAutomationProperty(() => element.Current.Name));
            if (label == null)
                return null;

            return new SafeFocus(DescribeControlType(controlType), label);
        }
        catch (Exception ex) when (IsExpectedUiAutomationException(ex))
        {
            return null;
        }
    }

    private static bool IsSafeInteractiveType(ControlType type) =>
        type == ControlType.Button ||
        type == ControlType.Hyperlink ||
        type == ControlType.MenuItem ||
        type == ControlType.TabItem ||
        type == ControlType.TreeItem;

    private static string DescribeControlType(ControlType type)
    {
        if (type == ControlType.Button) return "botão";
        if (type == ControlType.Hyperlink) return "link";
        if (type == ControlType.MenuItem) return "item de menu";
        if (type == ControlType.TabItem) return "aba";
        if (type == ControlType.TreeItem) return "item de navegação";
        return "controle";
    }

    private static void ApplyProbeResult(ProbeResult result)
    {
        lock (Sync)
        {
            if (!_running || result.Generation <= _lastAppliedGeneration)
                return;

            // Não aplicamos resultado de uma janela antiga se o Edge já recriou o HWND.
            if (_lastWindow == null || _lastWindow.Handle != result.Window.Handle)
                return;

            _lastAppliedGeneration = result.Generation;
            _lastResult = result;
            _lastSuccessfulProbeUtc = result.CompletedUtc;
            _lastErrorSignature = string.Empty;
        }

        var stateSignature = $"{result.Window.Handle}:{result.Page}:{result.SafeUrl}";
        var publishState = false;
        lock (Sync)
        {
            if (!string.Equals(stateSignature, _lastStateSignature, StringComparison.Ordinal))
            {
                _lastStateSignature = stateSignature;
                publishState = true;
            }
        }

        if (publishState)
        {
            DebugService.Record(
                "SANTANDER",
                $"Navegação detectada | página: {result.Page}{FormatUrl(result.SafeUrl)}.",
                DebugEntryLevel.Action);
        }

        var contextParts = result.Markers
            .Concat(result.NavigationLabels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var contextSignature = string.Join("|", contextParts);
        var publishContext = false;

        lock (Sync)
        {
            if (!string.Equals(contextSignature, _lastContextSignature, StringComparison.Ordinal))
            {
                _lastContextSignature = contextSignature;
                publishContext = true;
            }
        }

        if (publishContext)
        {
            if (result.Markers.Count > 0)
            {
                DebugService.Record(
                    "SANTANDER",
                    $"Contexto interno detectado | {string.Join(" • ", result.Markers)}.",
                    DebugEntryLevel.Background);
            }

            if (result.NavigationLabels.Count > 0)
            {
                DebugService.Record(
                    "SANTANDER",
                    $"Navegação disponível | {string.Join(" • ", result.NavigationLabels)}.",
                    DebugEntryLevel.Background);
            }
            else if (result.Markers.Count == 0)
            {
                DebugService.Record(
                    "SANTANDER",
                    "Diagnóstico de acessibilidade | nenhum controle seguro foi exposto nesta varredura.",
                    DebugEntryLevel.Background);
            }
        }

        if (result.Focus != null)
        {
            var focusSignature = $"{result.Focus.ControlType}:{result.Focus.Label}";
            var publishFocus = false;
            lock (Sync)
            {
                if (!string.Equals(focusSignature, _lastFocusSignature, StringComparison.Ordinal))
                {
                    _lastFocusSignature = focusSignature;
                    publishFocus = true;
                }
            }

            if (publishFocus)
            {
                DebugService.Record(
                    "SANTANDER",
                    $"Interação detectada | {result.Focus.ControlType}: {result.Focus.Label}.",
                    DebugEntryLevel.Action);
            }
        }
    }

    private static void PublishHeartbeat(DateTime now, WindowCandidate? window)
    {
        if (window == null)
            return;

        bool shouldPublish;
        int activeCount;
        TimeSpan? oldestAge = null;
        DateTime lastSuccess;
        ProbeResult? lastResult;

        lock (Sync)
        {
            shouldPublish = now >= _nextHeartbeatUtc;
            if (!shouldPublish)
                return;

            _nextHeartbeatUtc = now.AddMilliseconds(HeartbeatMilliseconds);
            activeCount = ActiveProbes.Count;
            if (ActiveProbes.Count > 0)
                oldestAge = now - ActiveProbes.Min(work => work.StartedUtc);
            lastSuccess = _lastSuccessfulProbeUtc;
            lastResult = _lastResult;
        }

        var page = lastResult?.Page ?? "aguardando leitura";
        var labels = lastResult?.NavigationLabels.Count ?? 0;
        var ageText = lastSuccess == DateTime.MinValue
            ? "sem leitura UIA concluída ainda"
            : $"última leitura válida há {Math.Max(0, (int)(now - lastSuccess).TotalSeconds)}s";

        var recovery = oldestAge.HasValue && oldestAge.Value >= TimeSpan.FromMilliseconds(ProbeTimeoutMilliseconds)
            ? $" | UIA em recuperação ({activeCount} sonda(s) ativa(s))"
            : string.Empty;

        DebugService.Record(
            "SANTANDER",
            $"Monitor ativo | página: {page} | controles seguros: {labels} | {ageText}{recovery}.",
            DebugEntryLevel.System);
    }

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
            $"Falha não crítica na sonda de acessibilidade ({root.GetType().Name}). O watchdog continua ativo.",
            DebugEntryLevel.Warning);
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

            result = new WindowCandidate(handle, processId, ClassifyWindowTitle(title));
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

    private static string ClassifyWindowTitle(string title)
    {
        if (title.Contains("Internet Banking", StringComparison.OrdinalIgnoreCase))
            return "Internet Banking";
        if (title.Contains("Santander", StringComparison.OrdinalIgnoreCase))
            return "Santander";
        return "Janela Santander";
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

    private static string FormatUrl(string? safeUrl) =>
        string.IsNullOrWhiteSpace(safeUrl) ? string.Empty : $" | rota segura: {safeUrl}";

    private sealed record WindowCandidate(IntPtr Handle, uint ProcessId, string Title);

    private sealed record SafeFocus(string ControlType, string Label);

    private sealed record ProbeResult(
        long Generation,
        WindowCandidate Window,
        string Page,
        string? SafeUrl,
        bool UrlAvailable,
        IReadOnlyList<string> Markers,
        IReadOnlyList<string> NavigationLabels,
        SafeFocus? Focus,
        DateTime CompletedUtc)
    {
        public static ProbeResult Unavailable(long generation, WindowCandidate window) =>
            new(generation, window, "Página não exposta pelo Edge", null, false,
                Array.Empty<string>(), Array.Empty<string>(), null, DateTime.UtcNow);
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
