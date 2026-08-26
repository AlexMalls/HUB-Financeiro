using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

namespace HubFinanceiro;

/// <summary>
/// Fase 1 do monitoramento Santander.
/// Detecta passivamente a janela do Internet Banking no Edge e registra apenas
/// mudanças de estado/página no Modo Debug. Não lê campos editáveis, conteúdo
/// bancário, cookies, requisições de rede ou credenciais.
/// </summary>
public static class SantanderMonitorService
{
    private const int PollIntervalMilliseconds = 2000;
    private const string EdgeProcessName = "msedge";
    private const string SantanderPwaAppId = "mmbkodmnnmlokekegikhdcpakjfahekf";

    private static readonly object Sync = new();
    private static readonly Regex SeparadorCamelCase = new("(?<=[a-zá-ú])(?=[A-ZÁ-Ú])", RegexOptions.Compiled);

    private static Timer? _timer;
    private static bool _initialized;
    private static bool _scanInProgress;
    private static MonitorState _lastState = MonitorState.NotDetected;
    private static string? _lastDiagnosticError;

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

    private static void Start()
    {
        lock (Sync)
        {
            if (_timer != null)
                return;

            _lastState = MonitorState.NotDetected;
            _lastDiagnosticError = null;
            _timer = new Timer(Poll, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        DebugService.Record(
            "SANTANDER",
            $"Monitor passivo iniciado | Edge PWA: {SantanderPwaAppId} | intervalo: {PollIntervalMilliseconds / 1000}s.",
            DebugEntryLevel.System);
    }

    private static void Stop()
    {
        Timer? timer;
        lock (Sync)
        {
            timer = _timer;
            _timer = null;
            _scanInProgress = false;
            _lastState = MonitorState.NotDetected;
            _lastDiagnosticError = null;
        }

        timer?.Dispose();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        Stop();
        DebugService.EnabledChanged -= DebugService_EnabledChanged;
        Application.Current.Exit -= Application_Exit;
    }

    private static void Poll(object? state)
    {
        lock (Sync)
        {
            if (_timer == null || _scanInProgress)
                return;

            _scanInProgress = true;
        }

        try
        {
            var currentState = DetectCurrentState();
            PublishStateChange(currentState);
            _lastDiagnosticError = null;
        }
        catch (Exception ex)
        {
            var signature = $"{ex.GetType().Name}:{ex.Message}";
            if (!string.Equals(signature, _lastDiagnosticError, StringComparison.Ordinal))
            {
                _lastDiagnosticError = signature;
                DebugService.Record(
                    "SANTANDER",
                    $"Falha não crítica ao consultar a janela do Internet Banking ({ex.GetType().Name}). O HUB continuará tentando.",
                    DebugEntryLevel.Warning);
            }
        }
        finally
        {
            lock (Sync)
            {
                _scanInProgress = false;
                _timer?.Change(PollIntervalMilliseconds, Timeout.Infinite);
            }
        }
    }

    private static MonitorState DetectCurrentState()
    {
        var candidates = new List<WindowCandidate>();

        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
                return true;

            var title = ReadWindowText(windowHandle);
            if (!LooksLikeSantanderWindow(title))
                return true;

            GetWindowThreadProcessId(windowHandle, out var processId);
            if (!IsEdgeProcess(processId))
                return true;

            candidates.Add(new WindowCandidate(windowHandle, processId, ClassifyWindowTitle(title)));
            return true;
        }, IntPtr.Zero);

        if (candidates.Count == 0)
            return MonitorState.NotDetected;

        foreach (var candidate in candidates)
        {
            var document = TryReadBrowserDocument(candidate.Handle);
            if (document.IsSantanderDocument)
            {
                return new MonitorState(
                    true,
                    candidate.ProcessId,
                    candidate.Title,
                    document.Page,
                    document.SafeUrl,
                    document.UrlAvailable);
            }
        }

        var first = candidates[0];
        return new MonitorState(
            true,
            first.ProcessId,
            first.Title,
            "Página não exposta pelo Edge",
            null,
            false);
    }

    private static BrowserDocument TryReadBrowserDocument(IntPtr windowHandle)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var documents = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));

            BrowserDocument? fallback = null;

            foreach (AutomationElement document in documents)
            {
                var documentName = ReadAutomationProperty(() => document.Current.Name);
                var rawUrl = TryReadDocumentUrl(document);
                var safeUrl = SanitizeSantanderUrl(rawUrl);
                var isSantander = safeUrl != null || LooksLikeSantanderDocument(documentName);
                var page = InferPage(safeUrl, documentName);
                var snapshot = new BrowserDocument(isSantander, page, safeUrl, safeUrl != null);

                if (isSantander)
                    return snapshot;

                fallback ??= snapshot;
            }

            return fallback ?? BrowserDocument.Unavailable;
        }
        catch (ElementNotAvailableException)
        {
            return BrowserDocument.Unavailable;
        }
        catch (InvalidOperationException)
        {
            return BrowserDocument.Unavailable;
        }
        catch (COMException)
        {
            return BrowserDocument.Unavailable;
        }
    }

    private static string? TryReadDocumentUrl(AutomationElement document)
    {
        try
        {
            if (document.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
                return valuePattern.Current.Value;
        }
        catch (ElementNotAvailableException)
        {
            // A página pode ter navegado durante a leitura.
        }
        catch (InvalidOperationException)
        {
            // Nem toda versão do Edge expõe ValuePattern no documento.
        }
        catch (COMException)
        {
            // A árvore de acessibilidade pode ser recriada durante a navegação.
        }

        return null;
    }

    private static string ReadAutomationProperty(Func<string> getter)
    {
        try
        {
            return getter()?.Trim() ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    private static void PublishStateChange(MonitorState current)
    {
        var previous = _lastState;
        if (current == previous)
            return;

        _lastState = current;

        if (!previous.Detected && current.Detected)
        {
            DebugService.Record(
                "SANTANDER",
                $"Internet Banking detectado | janela: {current.WindowTitle} | página: {current.Page}{FormatUrl(current.SafeUrl)}.",
                DebugEntryLevel.Background);

            if (!current.UrlAvailable)
            {
                DebugService.Record(
                    "SANTANDER",
                    "O Edge não expôs a URL desta tela pelo modo aplicativo; o monitor continuará acompanhando título e documento.",
                    DebugEntryLevel.Warning);
            }

            return;
        }

        if (previous.Detected && !current.Detected)
        {
            DebugService.Record(
                "SANTANDER",
                "Janela do Internet Banking não está mais disponível.",
                DebugEntryLevel.Background);
            return;
        }

        DebugService.Record(
            "SANTANDER",
            $"Navegação detectada | página: {current.Page}{FormatUrl(current.SafeUrl)}.",
            DebugEntryLevel.Action);
    }

    private static string FormatUrl(string? safeUrl) =>
        string.IsNullOrWhiteSpace(safeUrl) ? string.Empty : $" | rota segura: {safeUrl}";

    private static bool LooksLikeSantanderWindow(string title) =>
        title.Contains("Santander", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Internet Banking", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSantanderDocument(string documentName) =>
        documentName.Contains("Santander", StringComparison.OrdinalIgnoreCase) ||
        documentName.Contains("Internet Banking", StringComparison.OrdinalIgnoreCase);

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

    internal static string? SanitizeSantanderUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !IsSantanderHost(uri.Host))
        {
            return null;
        }

        var path = SanitizePath(uri.AbsolutePath);
        return $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}{path}";
    }

    private static string SanitizePath(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || absolutePath == "/")
            return "/";

        var safeSegments = absolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .Select(segment => segment.Split(';', 2)[0])
            .Select(segment =>
                LooksSensitivePathSegment(segment)
                    ? "{oculto}"
                    : Uri.EscapeDataString(segment));

        return "/" + string.Join("/", safeSegments);
    }

    private static bool LooksSensitivePathSegment(string segment) =>
        segment.Length > 80 ||
        Regex.IsMatch(segment, @"\d{7,}") ||
        Guid.TryParse(segment, out _) ||
        (segment.Length >= 24 && Regex.IsMatch(segment, @"^[A-Za-z0-9_-]+$"));

    internal static bool IsSantanderHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized == "santander.com.br" ||
               normalized.EndsWith(".santander.com.br", StringComparison.Ordinal) ||
               normalized == "santandernetibe.com.br" ||
               normalized.EndsWith(".santandernetibe.com.br", StringComparison.Ordinal);
    }

    internal static string InferPage(string? safeUrl, string? documentName)
    {
        var recognizedDocumentPage = RecognizeDocumentPage(documentName);
        if (recognizedDocumentPage != null)
            return recognizedDocumentPage;

        if (safeUrl == null || !Uri.TryCreate(safeUrl, UriKind.Absolute, out var uri))
            return "Internet Banking";

        if (uri.AbsolutePath.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.EndsWith("/home.xhtml", StringComparison.OrdinalIgnoreCase))
        {
            return "Início";
        }

        var segment = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(segment))
            return "Internet Banking";

        var withoutExtension = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(segment));
        var readable = SeparadorCamelCase.Replace(withoutExtension, " ")
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();

        return string.IsNullOrWhiteSpace(readable) ? "Internet Banking" : readable;
    }

    private static string? RecognizeDocumentPage(string? documentName)
    {
        if (string.IsNullOrWhiteSpace(documentName))
            return null;

        var knownPages = new[]
        {
            "Consultar compromissos",
            "Consulta de compromissos",
            "Compromissos",
            "Pagamentos",
            "Transferências"
        };

        return knownPages.FirstOrDefault(page =>
            documentName.Contains(page, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadWindowText(IntPtr windowHandle)
    {
        var length = GetWindowTextLength(windowHandle);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private sealed record WindowCandidate(IntPtr Handle, uint ProcessId, string Title);

    private sealed record BrowserDocument(bool IsSantanderDocument, string Page, string? SafeUrl, bool UrlAvailable)
    {
        public static readonly BrowserDocument Unavailable = new(false, "Página não exposta pelo Edge", null, false);
    }

    private sealed record MonitorState(
        bool Detected,
        uint ProcessId,
        string WindowTitle,
        string Page,
        string? SafeUrl,
        bool UrlAvailable)
    {
        public static readonly MonitorState NotDetected = new(false, 0, string.Empty, string.Empty, null, false);
    }

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
