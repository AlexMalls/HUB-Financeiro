using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HubFinanceiro;

public static class DebugSemanticService
{
    private sealed class Marker { }
    private sealed class TextState { public string Original { get; set; } = string.Empty; public bool Dirty { get; set; } }
    private sealed class ComboState { public string Original { get; set; } = string.Empty; }
    private sealed record JsonMatch(int OldIndex, int NewIndex);
    private sealed record Context(string Routine, string? Entity, string? Action, DateTime Timestamp);

    private const int MaxLogLines = 10_000;
    private const int TrimToLines = 9_500;
    private const int MaxSnapshotBytes = 4 * 1024 * 1024;
    private const int MaxExternalWatchers = 12;

    private static readonly object Sync = new();
    private static readonly ConditionalWeakTable<Window, Marker> AttachedWindows = new();
    private static readonly ConditionalWeakTable<TextBox, TextState> TextStates = new();
    private static readonly ConditionalWeakTable<ComboBox, ComboState> ComboStates = new();
    private static readonly Dictionary<string, string> JsonSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> LastFileEvents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WatchedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AnnouncedResources = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<FileSystemWatcher> Watchers = new();

    private static bool _initialized;
    private static string? _businessBasePath;
    private static Context? _recent;
    private static DispatcherTimer? _trimTimer;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        RegisterHandlers();
        DebugService.EnabledChanged += DebugService_EnabledChanged;

        _trimTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _trimTimer.Tick += (_, _) => TrimLogFile();
        _trimTimer.Start();
        TrimLogFile();

        if (DebugService.IsEnabled)
            AttachOpenWindows();
    }

    private static void DebugService_EnabledChanged(bool enabled)
    {
        if (enabled)
        {
            AttachOpenWindows();
            TrimLogFile();
            DebugService.Record("SYSTEM", "Debug semântico ativo: rotinas, entidades, arquivos e alterações serão correlacionados.", DebugEntryLevel.System);
        }
    }

    private static void RegisterHandlers()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(Window_Loaded), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(Button_Click), true);
        EventManager.RegisterClassHandler(typeof(TextBox), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(TextBox_GotFocus), true);
        EventManager.RegisterClassHandler(typeof(TextBox), TextBoxBase.TextChangedEvent, new TextChangedEventHandler(TextBox_TextChanged), true);
        EventManager.RegisterClassHandler(typeof(TextBox), Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(TextBox_LostFocus), true);
        EventManager.RegisterClassHandler(typeof(ComboBox), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(ComboBox_GotFocus), true);
        EventManager.RegisterClassHandler(typeof(ComboBox), Selector.SelectionChangedEvent, new SelectionChangedEventHandler(ComboBox_Changed), true);
    }

    private static void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DebugService.IsEnabled && sender is Window window && window is not DebugWindow)
            AttachWindow(window);
    }

    private static void AttachOpenWindows()
    {
        try
        {
            if (Application.Current == null)
                return;

            foreach (Window window in Application.Current.Windows)
                if (window is not DebugWindow)
                    AttachWindow(window);
        }
        catch { }
    }

    private static void AttachWindow(Window window)
    {
        try
        {
            if (AttachedWindows.TryGetValue(window, out _))
                return;

            AttachedWindows.Add(window, new Marker());
            string routine = ResolveRoutine(window, null);
            DebugService.Record(routine, $"Contexto da tela ativo | Tela: {WindowTitle(window)} | Classe: {window.GetType().Name}", DebugEntryLevel.System);

            if (window is MainWindow mainWindow)
                InitializeBusinessWatcher(mainWindow);

            ScanPaths(window, routine);
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", "Falha ao anexar o Debug semântico à janela.", DebugEntryLevel.Warning, ex);
        }
    }

    private static void Button_Click(object sender, RoutedEventArgs e)
    {
        if (!DebugService.IsEnabled || sender is not ButtonBase button)
            return;

        Window? window = Window.GetWindow(button);
        if (window is DebugWindow)
            return;

        try
        {
            string action = SafeLabel(button) ?? FriendlyElementName(button);
            Context context = Observe(window, button, action);
            var parts = new List<string> { $"Ação: {action}" };
            if (button is ToggleButton toggle)
                parts.Add($"Estado: {(toggle.IsChecked == true ? "marcado" : "desmarcado")}");
            if (!string.IsNullOrWhiteSpace(context.Entity))
                parts.Add($"Entidade: {context.Entity}");
            parts.Add($"Tela: {WindowTitle(window)}");
            DebugService.Record(context.Routine, string.Join(" | ", parts), DebugEntryLevel.Action);
            ScanPaths(window, context.Routine);
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", "Falha ao detalhar ação da interface.", DebugEntryLevel.Warning, ex);
        }
    }

    private static void TextBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!DebugService.IsEnabled || sender is not TextBox box || Window.GetWindow(box) is DebugWindow)
            return;

        var state = TextStates.GetValue(box, _ => new TextState());
        state.Original = box.Text ?? string.Empty;
        state.Dirty = false;
    }

    private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!DebugService.IsEnabled || sender is not TextBox box || !box.IsKeyboardFocusWithin || Window.GetWindow(box) is DebugWindow)
            return;

        TextStates.GetValue(box, _ => new TextState { Original = box.Text ?? string.Empty }).Dirty = true;
    }

    private static void TextBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!DebugService.IsEnabled || sender is not TextBox box || Window.GetWindow(box) is DebugWindow)
            return;
        if (!TextStates.TryGetValue(box, out var state) || !state.Dirty)
            return;

        try
        {
            string oldValue = state.Original;
            string newValue = box.Text ?? string.Empty;
            state.Original = newValue;
            state.Dirty = false;
            if (oldValue == newValue)
                return;

            Window? window = Window.GetWindow(box);
            string field = FriendlyElementName(box);
            Context context = Observe(window, box, $"Campo alterado: {field}");
            string change = IsSensitive(box)
                ? "Valor anterior/novo omitido por segurança"
                : $"Valor: {Quote(oldValue)} → {Quote(newValue)}";

            var parts = new List<string> { $"Campo: {field}", change };
            if (!string.IsNullOrWhiteSpace(context.Entity))
                parts.Add($"Entidade: {context.Entity}");
            parts.Add($"Tela: {WindowTitle(window)}");
            DebugService.Record(context.Routine, string.Join(" | ", parts), DebugEntryLevel.Action);
            ScanPaths(window, context.Routine);
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", "Falha ao detalhar alteração de campo.", DebugEntryLevel.Warning, ex);
        }
    }

    private static void ComboBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!DebugService.IsEnabled || sender is not ComboBox combo || Window.GetWindow(combo) is DebugWindow)
            return;
        ComboStates.GetValue(combo, _ => new ComboState()).Original = SelectionText(combo);
    }

    private static void ComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!DebugService.IsEnabled || sender is not ComboBox combo || Window.GetWindow(combo) is DebugWindow)
            return;
        if (!combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen)
            return;

        try
        {
            var state = ComboStates.GetValue(combo, _ => new ComboState());
            string oldValue = state.Original;
            string newValue = SelectionText(combo);
            state.Original = newValue;
            if (oldValue == newValue)
                return;

            Window? window = Window.GetWindow(combo);
            string field = FriendlyElementName(combo);
            Context context = Observe(window, combo, $"Seleção alterada: {field}");
            string change = IsSensitive(combo)
                ? "Valor anterior/novo omitido por segurança"
                : $"Valor: {Quote(oldValue)} → {Quote(newValue)}";

            var parts = new List<string> { $"Seleção: {field}", change };
            if (!string.IsNullOrWhiteSpace(context.Entity))
                parts.Add($"Entidade: {context.Entity}");
            parts.Add($"Tela: {WindowTitle(window)}");
            DebugService.Record(context.Routine, string.Join(" | ", parts), DebugEntryLevel.Action);
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", "Falha ao detalhar alteração de seleção.", DebugEntryLevel.Warning, ex);
        }
    }

    private static Context Observe(Window? window, FrameworkElement? element, string action)
    {
        string routine = ResolveRoutine(window, element);
        string? entity = ResolveEntity(element);

        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(entity) && _recent != null &&
                string.Equals(_recent.Routine, routine, StringComparison.OrdinalIgnoreCase) &&
                DateTime.Now - _recent.Timestamp < TimeSpan.FromMinutes(5))
            {
                entity = _recent.Entity;
            }

            _recent = new Context(routine, entity, action, DateTime.Now);
            return _recent;
        }
    }

    private static void InitializeBusinessWatcher(MainWindow mainWindow)
    {
        if (!string.IsNullOrWhiteSpace(_businessBasePath))
            return;

        try
        {
            MethodInfo? method = typeof(MainWindow).GetMethod("ObterCaminhoBase", BindingFlags.Instance | BindingFlags.NonPublic);
            string? path = method?.Invoke(mainWindow, null) as string;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            _businessBasePath = Path.GetFullPath(path);
            PreloadJson(_businessBasePath);
            WatchDirectory(_businessBasePath, includeSubdirectories: true);
            DebugService.Record("SYSTEM", $"Monitor de arquivos ativo | Base: {_businessBasePath}", DebugEntryLevel.System);
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", "Não foi possível iniciar o monitor de arquivos de negócio.", DebugEntryLevel.Warning, ex);
        }
    }

    private static void WatchDirectory(string directory, bool includeSubdirectories)
    {
        if (!Directory.Exists(directory))
            return;

        string fullPath;
        try { fullPath = Path.GetFullPath(directory); }
        catch { return; }

        lock (Sync)
        {
            if (WatchedDirectories.Contains(fullPath))
                return;
            if (!includeSubdirectories && WatchedDirectories.Count >= MaxExternalWatchers + 1)
                return;

            var watcher = new FileSystemWatcher(fullPath)
            {
                Filter = "*",
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, e) => QueueFileEvent("alterado", e.FullPath);
            watcher.Created += (_, e) => QueueFileEvent("criado", e.FullPath);
            watcher.Deleted += (_, e) => QueueFileEvent("excluído", e.FullPath);
            watcher.Renamed += (_, e) => QueueFileEvent("renomeado", e.FullPath, e.OldFullPath);
            Watchers.Add(watcher);
            WatchedDirectories.Add(fullPath);
        }
    }

    private static void ScanPaths(Window? window, string routine)
    {
        if (window == null || !window.IsLoaded)
            return;

        try
        {
            int visited = 0;
            ScanVisual(window, routine, ref visited);
        }
        catch { }
    }

    private static void ScanVisual(DependencyObject current, string routine, ref int visited)
    {
        if (visited++ > 1_500)
            return;

        if (current is FrameworkElement fe)
        {
            string? candidate = fe switch
            {
                TextBox box when !IsSensitive(box) => box.Text,
                TextBlock block => block.Text,
                ContentControl cc when cc.Content is string text => text,
                _ => null
            };
            DiscoverPath(candidate, routine);
            DiscoverPathsFromObject(fe.DataContext, routine);
        }

        int count;
        try { count = VisualTreeHelper.GetChildrenCount(current); }
        catch { return; }
        for (int i = 0; i < count; i++)
            ScanVisual(VisualTreeHelper.GetChild(current, i), routine, ref visited);
    }

    private static void DiscoverPathsFromObject(object? value, string routine)
    {
        if (value == null)
            return;
        try
        {
            Type type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive)
                return;

            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0 || SensitiveName(prop.Name))
                    continue;
                if (!prop.Name.Contains("Caminho", StringComparison.OrdinalIgnoreCase) &&
                    !prop.Name.Contains("Arquivo", StringComparison.OrdinalIgnoreCase) &&
                    !prop.Name.Contains("Pasta", StringComparison.OrdinalIgnoreCase) &&
                    !prop.Name.Contains("Path", StringComparison.OrdinalIgnoreCase))
                    continue;

                object? propertyValue;
                try { propertyValue = prop.GetValue(value); }
                catch { continue; }
                if (propertyValue is string text)
                    DiscoverPath(text, routine);
            }
        }
        catch { }
    }

    private static void DiscoverPath(string? candidate, string routine)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;

        string text = candidate.Trim().Trim('"');
        bool pathLike = text.StartsWith(@"\\", StringComparison.Ordinal) ||
                        (text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/'));
        if (!pathLike)
            return;

        string fullPath;
        try { fullPath = Path.GetFullPath(text); }
        catch { return; }
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            return;

        lock (Sync)
        {
            if (!AnnouncedResources.Add(fullPath))
                return;
        }

        if (File.Exists(fullPath))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                WatchDirectory(directory, includeSubdirectories: false);
            string size = string.Empty;
            try { size = $" | Tamanho: {FormatBytes(new FileInfo(fullPath).Length)}"; } catch { }
            DebugService.Record(routine, $"Arquivo em uso | Caminho: {fullPath} | Diretório: {directory}{size}", DebugEntryLevel.Background);
        }
        else
        {
            WatchDirectory(fullPath, includeSubdirectories: false);
            DebugService.Record(routine, $"Diretório em uso | Caminho: {fullPath}", DebugEntryLevel.Background);
        }
    }

    private static void PreloadJson(string root)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Take(250))
            {
                string? content = ReadSnapshot(file);
                if (content != null)
                    JsonSnapshots[file] = content;
            }
        }
        catch { }
    }

    private static void QueueFileEvent(string operation, string path, string? oldPath = null)
    {
        if (!DebugService.IsEnabled || IgnoreFile(path))
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            HandleFileEvent(operation, path, oldPath);
        });
    }

    private static void HandleFileEvent(string operation, string path, string? oldPath)
    {
        try
        {
            if (!DebugService.IsEnabled)
                return;

            DateTime now = DateTime.Now;
            string eventKey = operation + "|" + path;
            lock (Sync)
            {
                if (LastFileEvents.TryGetValue(eventKey, out DateTime last) && now - last < TimeSpan.FromMilliseconds(500))
                    return;
                LastFileEvents[eventKey] = now;
            }

            Context? context;
            lock (Sync)
                context = _recent != null && now - _recent.Timestamp < TimeSpan.FromSeconds(20) ? _recent : null;

            string routine = context?.Routine ?? ResolveRoutineFromPath(path);
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            var parts = new List<string> { $"Arquivo: {Path.GetFileName(path)}", $"Caminho: {path}", $"Diretório: {directory}" };
            if (File.Exists(path))
            {
                try { parts.Add($"Tamanho: {FormatBytes(new FileInfo(path).Length)}"); } catch { }
            }
            if (!string.IsNullOrWhiteSpace(oldPath)) parts.Add($"Caminho anterior: {oldPath}");
            if (!string.IsNullOrWhiteSpace(context?.Action)) parts.Add($"Ação relacionada: {context.Action}");
            if (!string.IsNullOrWhiteSpace(context?.Entity)) parts.Add($"Entidade: {context.Entity}");

            DebugService.Record(routine, $"Arquivo {operation} | {string.Join(" | ", parts)}", DebugEntryLevel.Background);
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                ProcessJsonChange(routine, operation, path);

            if (operation == "renomeado" && oldPath != null)
            {
                lock (Sync)
                {
                    if (JsonSnapshots.Remove(oldPath, out string? oldSnapshot))
                        JsonSnapshots[path] = oldSnapshot;
                }
            }
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", $"Falha ao processar arquivo: {path}", DebugEntryLevel.Warning, ex);
        }
    }

    private static void ProcessJsonChange(string routine, string operation, string path)
    {
        string? oldContent;
        lock (Sync) JsonSnapshots.TryGetValue(path, out oldContent);

        if (operation == "excluído")
        {
            lock (Sync) JsonSnapshots.Remove(path);
            return;
        }

        string? newContent = ReadSnapshot(path);
        if (newContent == null)
            return;
        lock (Sync) JsonSnapshots[path] = newContent;
        if (oldContent == null || oldContent == newContent)
            return;

        try { DescribeJsonDiff(routine, path, oldContent, newContent); }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", $"Não foi possível detalhar alteração JSON: {path}", DebugEntryLevel.Warning, ex);
        }
    }

    private static void DescribeJsonDiff(string routine, string path, string oldContent, string newContent)
    {
        using var oldDoc = JsonDocument.Parse(oldContent);
        using var newDoc = JsonDocument.Parse(newContent);
        if (oldDoc.RootElement.ValueKind != JsonValueKind.Array || newDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            LogBusiness(routine, "Conteúdo JSON atualizado", null, "Estrutura alterada", path, DebugEntryLevel.Action);
            return;
        }

        var oldItems = oldDoc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
        var newItems = newDoc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
        var matches = MatchItems(oldItems, newItems);
        int added = matches.Count(x => x.OldIndex < 0);
        int removed = matches.Count(x => x.NewIndex < 0);

        if (added > 0 || removed > 0)
            LogBusiness(routine, "Base JSON alterada", null, $"Total: {oldItems.Count} → {newItems.Count} | Incluídos: {added} | Removidos: {removed}", path, DebugEntryLevel.Action);

        int emitted = 0;
        foreach (JsonMatch match in matches)
        {
            if (emitted >= 12)
                break;

            if (match.OldIndex < 0)
            {
                JsonElement item = newItems[match.NewIndex];
                LogBusiness(routine, "Registro incluído", EntityLabel(item, path), ObjectSummary(item), path, DebugEntryLevel.Action);
                emitted++;
                continue;
            }
            if (match.NewIndex < 0)
            {
                JsonElement item = oldItems[match.OldIndex];
                LogBusiness(routine, "Registro removido", EntityLabel(item, path), ObjectSummary(item), path, DebugEntryLevel.Action);
                emitted++;
                continue;
            }

            JsonElement oldItem = oldItems[match.OldIndex];
            JsonElement newItem = newItems[match.NewIndex];
            if (JsonEqual(oldItem, newItem))
                continue;
            List<string> changes = CompareObjects(oldItem, newItem);
            if (changes.Count == 0)
                continue;

            LogBusiness(routine, "Dados alterados", EntityLabel(newItem, path) ?? EntityLabel(oldItem, path), string.Join("; ", changes), path, DebugEntryLevel.Action);
            emitted++;
        }

        if (emitted == 0 && oldItems.Count == newItems.Count && !JsonEqual(oldDoc.RootElement, newDoc.RootElement))
            LogBusiness(routine, "Base JSON atualizada", null, "Somente ordenação/estrutura mudou", path, DebugEntryLevel.Background);
    }

    private static List<JsonMatch> MatchItems(IReadOnlyList<JsonElement> oldItems, IReadOnlyList<JsonElement> newItems)
    {
        var result = new List<JsonMatch>();
        var oldRemaining = new HashSet<int>(Enumerable.Range(0, oldItems.Count));
        var newRemaining = new HashSet<int>(Enumerable.Range(0, newItems.Count));
        var oldGroups = oldRemaining.Select(i => (Index: i, Key: MatchKey(oldItems[i]))).Where(x => x.Key != null).GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(x => x.Index).ToList(), StringComparer.OrdinalIgnoreCase);
        var newGroups = newRemaining.Select(i => (Index: i, Key: MatchKey(newItems[i]))).Where(x => x.Key != null).GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(x => x.Index).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (string key in oldGroups.Keys.Intersect(newGroups.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var oldGroup = oldGroups[key].Where(oldRemaining.Contains).ToList();
            var newGroup = newGroups[key].Where(newRemaining.Contains).ToList();

            foreach (int oldIndex in oldGroup.ToList())
            {
                int exact = newGroup.Where(newIndex => newRemaining.Contains(newIndex) && JsonEqual(oldItems[oldIndex], newItems[newIndex])).DefaultIfEmpty(-1).First();
                if (exact < 0) continue;
                result.Add(new JsonMatch(oldIndex, exact));
                oldRemaining.Remove(oldIndex); newRemaining.Remove(exact); newGroup.Remove(exact);
            }

            oldGroup = oldGroup.Where(oldRemaining.Contains).ToList();
            newGroup = newGroup.Where(newRemaining.Contains).ToList();
            while (oldGroup.Count > 0 && newGroup.Count > 0)
            {
                int bestOld = -1, bestNew = -1, bestScore = int.MaxValue;
                foreach (int oldIndex in oldGroup)
                foreach (int newIndex in newGroup)
                {
                    int score = CompareObjects(oldItems[oldIndex], newItems[newIndex]).Count;
                    if (score < bestScore) { bestScore = score; bestOld = oldIndex; bestNew = newIndex; }
                }
                if (bestOld < 0 || bestNew < 0) break;
                result.Add(new JsonMatch(bestOld, bestNew));
                oldRemaining.Remove(bestOld); newRemaining.Remove(bestNew); oldGroup.Remove(bestOld); newGroup.Remove(bestNew);
            }
        }

        foreach (int oldIndex in oldRemaining.ToList())
        {
            int exact = newRemaining.Where(newIndex => JsonEqual(oldItems[oldIndex], newItems[newIndex])).DefaultIfEmpty(-1).First();
            if (exact < 0) continue;
            result.Add(new JsonMatch(oldIndex, exact));
            oldRemaining.Remove(oldIndex); newRemaining.Remove(exact);
        }

        result.AddRange(oldRemaining.OrderBy(x => x).Select(x => new JsonMatch(x, -1)));
        result.AddRange(newRemaining.OrderBy(x => x).Select(x => new JsonMatch(-1, x)));
        return result;
    }

    private static string? MatchKey(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        string? id = Scalar(element, "Id"); if (!string.IsNullOrWhiteSpace(id)) return "ID:" + id;
        string? code = Scalar(element, "Codigo"); if (!string.IsNullOrWhiteSpace(code)) return "CODIGO:" + code;
        string? supplier = Scalar(element, "CodigoFornecedor"); if (!string.IsNullOrWhiteSpace(supplier)) return "FORNECEDOR:" + supplier;
        string? name = StringValue(element, "NomeFornecedor") ?? StringValue(element, "FornecedorNome") ?? StringValue(element, "Nome") ?? StringValue(element, "Competencia");
        return string.IsNullOrWhiteSpace(name) ? null : "NOME:" + name.Trim();
    }

    private static List<string> CompareObjects(JsonElement oldElement, JsonElement newElement)
    {
        var changes = new List<string>();
        if (oldElement.ValueKind != JsonValueKind.Object || newElement.ValueKind != JsonValueKind.Object)
            return changes;
        var oldProps = oldElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var newProps = newElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        foreach (string name in oldProps.Keys.Union(newProps.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (SensitiveName(name)) continue;
            oldProps.TryGetValue(name, out JsonElement oldValue);
            newProps.TryGetValue(name, out JsonElement newValue);
            if (JsonEqual(oldValue, newValue)) continue;
            changes.Add($"{FriendlyPropertyName(name)}: {FormatJson(name, oldValue)} → {FormatJson(name, newValue)}");
            if (changes.Count >= 8) break;
        }
        return changes;
    }

    private static string? EntityLabel(JsonElement item, string path)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        string? name = StringValue(item, "NomeFornecedor") ?? StringValue(item, "FornecedorNome") ?? StringValue(item, "Nome") ?? StringValue(item, "Descricao") ?? StringValue(item, "Competencia");
        string? id = Scalar(item, "Id") ?? Scalar(item, "Codigo") ?? Scalar(item, "CodigoFornecedor");
        string type = Path.GetFileName(path).Contains("fornecedores", StringComparison.OrdinalIgnoreCase) ? "Fornecedor" : Path.GetFileName(path).Contains("previsoes_pagamento", StringComparison.OrdinalIgnoreCase) ? "Pagamento OPEX" : "Registro";
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) return $"{type} \"{Clean(name, 80)}\" ({id})";
        if (!string.IsNullOrWhiteSpace(name)) return $"{type} \"{Clean(name, 80)}\"";
        return !string.IsNullOrWhiteSpace(id) ? $"{type} ({id})" : null;
    }

    private static string ObjectSummary(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return "Conteúdo registrado";
        var parts = new List<string>();
        foreach (JsonProperty prop in item.EnumerateObject())
        {
            if (parts.Count >= 6 || SensitiveName(prop.Name) || prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
            parts.Add($"{FriendlyPropertyName(prop.Name)}={FormatJson(prop.Name, prop.Value)}");
        }
        return parts.Count == 0 ? "Conteúdo registrado" : string.Join("; ", parts);
    }

    private static void LogBusiness(string routine, string action, string? entity, string? details, string path, DebugEntryLevel level)
    {
        var parts = new List<string> { action };
        if (!string.IsNullOrWhiteSpace(entity)) parts.Add($"Entidade: {entity}");
        if (!string.IsNullOrWhiteSpace(details)) parts.Add(details);
        parts.Add($"Arquivo: {path}");
        parts.Add($"Diretório: {Path.GetDirectoryName(path) ?? string.Empty}");
        DebugService.Record(routine, string.Join(" | ", parts), level);
    }

    private static string ResolveRoutine(Window? window, FrameworkElement? element)
    {
        string type = window?.GetType().Name ?? string.Empty;
        if (type.Contains("Opcoes", StringComparison.OrdinalIgnoreCase)) return "CONFIGURACOES";
        if (type.Contains("Cnab", StringComparison.OrdinalIgnoreCase)) return "CNAB";
        if (type.Contains("Provisionamento", StringComparison.OrdinalIgnoreCase) || type.Contains("Deprovisionamento", StringComparison.OrdinalIgnoreCase) || type.Contains("Liquidacao", StringComparison.OrdinalIgnoreCase) || type.Contains("Movimentacao", StringComparison.OrdinalIgnoreCase)) return "OPEX";
        if (type.Contains("AnaliseFatura", StringComparison.OrdinalIgnoreCase) || type.Contains("AnaliseFaturas", StringComparison.OrdinalIgnoreCase) || type.Contains("LeituraFaturas", StringComparison.OrdinalIgnoreCase) || type.Contains("LeituraOver", StringComparison.OrdinalIgnoreCase) || type.Contains("VinculoBeneficiarios", StringComparison.OrdinalIgnoreCase) || type.Contains("ConfirmarNota", StringComparison.OrdinalIgnoreCase) || type.Contains("EditarExplicacaoAnalise", StringComparison.OrdinalIgnoreCase)) return "ANALISADOR_FATURAS";

        string names = SemanticNames(element);
        if (names.Contains("cnab", StringComparison.OrdinalIgnoreCase)) return "CNAB";
        if (names.Contains("fornecedor", StringComparison.OrdinalIgnoreCase)) return "FORNECEDORES";
        if (names.Contains("opex", StringComparison.OrdinalIgnoreCase) || names.Contains("pagamento", StringComparison.OrdinalIgnoreCase) || names.Contains("provision", StringComparison.OrdinalIgnoreCase) || names.Contains("liquid", StringComparison.OrdinalIgnoreCase)) return "OPEX";
        if (names.Contains("fatura", StringComparison.OrdinalIgnoreCase) || names.Contains("analise", StringComparison.OrdinalIgnoreCase)) return "ANALISADOR_FATURAS";
        if (names.Contains("comparar", StringComparison.OrdinalIgnoreCase) || names.Contains("compara", StringComparison.OrdinalIgnoreCase)) return "COMPARAR_VALORES";
        if (names.Contains("subconjunto", StringComparison.OrdinalIgnoreCase) || names.Contains("calc", StringComparison.OrdinalIgnoreCase)) return "CALC_SUBCONJUNTO";

        lock (Sync)
            if (_recent != null && DateTime.Now - _recent.Timestamp < TimeSpan.FromSeconds(10)) return _recent.Routine;
        return window is MainWindow ? "HUB" : "UI";
    }

    private static string SemanticNames(FrameworkElement? element)
    {
        var sb = new StringBuilder();
        DependencyObject? current = element;
        for (int i = 0; i < 12 && current != null; i++)
        {
            if (current is FrameworkElement fe)
            {
                if (!string.IsNullOrWhiteSpace(fe.Name)) sb.Append(' ').Append(fe.Name);
                if (fe is ContentControl cc && cc.Content is string text) sb.Append(' ').Append(text);
            }
            current = Parent(current);
        }
        return sb.ToString();
    }

    private static string? ResolveEntity(FrameworkElement? element)
    {
        DependencyObject? current = element;
        for (int i = 0; i < 14 && current != null; i++)
        {
            if (current is FrameworkElement fe && fe.DataContext != null)
            {
                string? summary = EntitySummary(fe.DataContext);
                if (!string.IsNullOrWhiteSpace(summary)) return summary;
            }
            current = Parent(current);
        }
        return null;
    }

    private static string? EntitySummary(object value)
    {
        try
        {
            Type type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true) return null;
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            string[] preferred = { "NomeFornecedor", "FornecedorNome", "Nome", "Competencia", "Descricao", "Id", "Codigo", "CodigoFornecedor", "Empresa", "Status", "DataPagamento", "Valor", "CaminhoArquivo" };
            var parts = new List<string>();
            foreach (string name in preferred)
            {
                if (parts.Count >= 5 || !props.TryGetValue(name, out PropertyInfo? prop) || SensitiveName(name)) continue;
                object? propertyValue; try { propertyValue = prop.GetValue(value); } catch { continue; }
                if (propertyValue == null) continue;
                string formatted = propertyValue switch
                {
                    DateTime dt => dt.ToString("dd/MM/yyyy"),
                    decimal amount when name.Contains("Valor", StringComparison.OrdinalIgnoreCase) => amount.ToString("C2", new System.Globalization.CultureInfo("pt-BR")),
                    string text => $"\"{Clean(text, 80)}\"",
                    _ => Clean(Convert.ToString(propertyValue, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty, 80)
                };
                if (!string.IsNullOrWhiteSpace(formatted)) parts.Add($"{FriendlyPropertyName(name)}={formatted}");
            }
            if (parts.Count == 0) return null;
            string entityType = type.Name.Contains("Fornecedor", StringComparison.OrdinalIgnoreCase) ? "Fornecedor" : type.Name.Contains("Pagamento", StringComparison.OrdinalIgnoreCase) ? "Pagamento" : type.Name.Contains("Analise", StringComparison.OrdinalIgnoreCase) ? "Análise" : type.Name.Contains("Cnab", StringComparison.OrdinalIgnoreCase) ? "CNAB" : type.Name;
            return $"{entityType} ({string.Join(", ", parts)})";
        }
        catch { return null; }
    }

    private static string SelectionText(ComboBox combo)
    {
        if (combo.SelectedItem == null) return string.Empty;
        return EntitySummary(combo.SelectedItem) ?? Clean(combo.SelectedItem.ToString() ?? string.Empty, 100);
    }

    private static string ResolveRoutineFromPath(string path)
    {
        string lower = path.ToLowerInvariant();
        if (lower.Contains("fornecedores")) return "FORNECEDORES";
        if (lower.Contains("previsoes_pagamento") || lower.Contains("provision") || lower.Contains("liquid") || lower.Contains("opex")) return "OPEX";
        if (lower.Contains("cnab")) return "CNAB";
        if (lower.Contains("fatura") || lower.Contains("analise") || lower.Contains("análise") || lower.Contains("notas")) return "ANALISADOR_FATURAS";
        return "FILE";
    }

    private static string? ReadSnapshot(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxSnapshotBytes) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch { return null; }
    }

    private static bool IgnoreFile(string path)
    {
        if (path.Equals(DebugService.CurrentLogPath, StringComparison.OrdinalIgnoreCase)) return true;
        string file = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(file) || new[] { ".tmp", ".temp", ".lock", ".lck", ".part", ".crdownload", "~" }.Any(suffix => file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSensitive(FrameworkElement element)
    {
        var parts = new List<string> { element.Name ?? string.Empty, element.GetType().Name };
        DependencyObject? current = element;
        for (int i = 0; i < 5 && current != null; i++)
        {
            if (current is FrameworkElement fe && !string.IsNullOrWhiteSpace(fe.Name)) parts.Add(fe.Name);
            current = Parent(current);
        }
        return SensitiveName(string.Join(" ", parts));
    }

    private static bool SensitiveName(string name)
    {
        string lower = name.ToLowerInvariant();
        return new[] { "senha", "password", "token", "secret", "segredo", "cvv", "pin", "agencia", "agência", "conta", "cpf", "documento", "chavepix", "chave_pix", "credencial", "credential" }.Any(lower.Contains);
    }

    private static string? StringValue(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static string? Scalar(JsonElement element, string name) => !element.TryGetProperty(name, out JsonElement p) ? null : p.ValueKind == JsonValueKind.String ? p.GetString() : p.ValueKind == JsonValueKind.Number ? p.GetRawText() : null;
    private static bool JsonEqual(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && (a.ValueKind == JsonValueKind.Undefined || string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal));

    private static string FormatJson(string name, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return "(ausente)";
        if (element.ValueKind == JsonValueKind.Null) return "null";
        if (element.ValueKind == JsonValueKind.String)
        {
            string text = element.GetString() ?? string.Empty;
            if (name.Contains("Data", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(text, out DateTime date)) return date.ToString("dd/MM/yyyy");
            return $"\"{Clean(text, 90)}\"";
        }
        if (element.ValueKind == JsonValueKind.Number && name.Contains("Valor", StringComparison.OrdinalIgnoreCase) && element.TryGetDecimal(out decimal amount)) return amount.ToString("C2", new System.Globalization.CultureInfo("pt-BR"));
        return Clean(element.GetRawText(), 90);
    }

    private static string FriendlyElementName(FrameworkElement element)
    {
        string? label = SafeLabel(element); if (!string.IsNullOrWhiteSpace(label)) return label;
        string name = element.Name; if (string.IsNullOrWhiteSpace(name)) return element.GetType().Name;
        foreach (string suffix in new[] { "TextBox", "ComboBox", "CheckBox", "RadioButton", "ToggleButton", "Button", "ItemsControl", "Border", "Grid", "Panel" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { name = name[..^suffix.Length]; break; }
        if (name.StartsWith("Btn", StringComparison.OrdinalIgnoreCase)) name = name[3..];
        return FriendlyPropertyName(name);
    }

    private static string FriendlyPropertyName(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++) { if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' '); sb.Append(name[i]); }
        return sb.ToString().Trim();
    }

    private static string? SafeLabel(FrameworkElement element)
    {
        if (element is ContentControl cc && cc.Content is string text && !string.IsNullOrWhiteSpace(text)) return Clean(text, 80);
        return null;
    }

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return $"\"{Clean(value, 120)}\"";
    }

    private static string Clean(string text, int max)
    {
        string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length > max ? normalized[..max] + "…" : normalized;
    }

    private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:N1} KB" : $"{bytes / (1024d * 1024d):N1} MB";
    private static string WindowTitle(Window? window) => string.IsNullOrWhiteSpace(window?.Title) ? window?.GetType().Name ?? "HUB" : window!.Title;

    private static DependencyObject? Parent(DependencyObject element)
    {
        try
        {
            if (element is Visual or System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(element);
            if (element is FrameworkContentElement fce) return fce.Parent;
            return LogicalTreeHelper.GetParent(element);
        }
        catch { return null; }
    }

    private static void TrimLogFile()
    {
        try
        {
            string path = DebugService.CurrentLogPath;
            if (!File.Exists(path)) return;
            int count = File.ReadLines(path).Count();
            if (count <= MaxLogLines) return;
            string[] lines = File.ReadLines(path).TakeLast(TrimToLines).ToArray();
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
        catch
        {
            // O arquivo pode estar sendo escrito no mesmo instante; tenta novamente no próximo ciclo.
        }
    }
}
