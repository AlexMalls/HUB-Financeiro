using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HubFinanceiro;

public sealed class DebugSemanticContext
{
    public string Routine { get; init; } = "UI";
    public string? Entity { get; init; }
    public string? Action { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

internal static class DebugBusinessInstrumentation
{
    private sealed class Marker { }
    private sealed record JsonMatch(int OldIndex, int NewIndex);

    private static readonly ConditionalWeakTable<Window, Marker> Attached = new();
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> JsonSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> LastFileEvents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<FileSystemWatcher> Watchers = new();
    private static DebugSemanticContext? _recent;
    private static string? _businessBasePath;

    private const int RecentContextSeconds = 20;
    private const int MaxSnapshotBytes = 4 * 1024 * 1024;

    public static void Attach(Window window)
    {
        if (!DebugService.IsEnabled || window is DebugWindow)
            return;

        try
        {
            if (Attached.TryGetValue(window, out _))
                return;

            Attached.Add(window, new Marker());
            string routine = ResolveRoutine(window, null);
            DebugService.Record(routine, $"Tela aberta: {Title(window)} | Classe: {window.GetType().Name}", DebugEntryLevel.System);

            window.Closed += (_, _) =>
            {
                if (DebugService.IsEnabled)
                    DebugService.Record(routine, $"Tela fechada: {Title(window)}", DebugEntryLevel.System);
            };

            if (window is MainWindow mainWindow)
                InitializeBusinessWatcher(mainWindow);
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", "Falha ao anexar contexto de negócio à janela.", DebugEntryLevel.Warning, ex);
        }
    }

    public static DebugSemanticContext ObserveUiAction(Window? window, FrameworkElement? element, string action)
    {
        string routine = ResolveRoutine(window, element);
        string? entity = ResolveEntity(element);

        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(entity) &&
                _recent != null &&
                string.Equals(_recent.Routine, routine, StringComparison.OrdinalIgnoreCase) &&
                DateTime.Now - _recent.Timestamp < TimeSpan.FromMinutes(5))
            {
                entity = _recent.Entity;
            }

            _recent = new DebugSemanticContext
            {
                Routine = routine,
                Entity = entity,
                Action = action,
                Timestamp = DateTime.Now
            };

            return _recent;
        }
    }

    public static string DescribeValue(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is string text)
            return Clean(text, 100);

        Type type = value.GetType();
        if (type.IsPrimitive || value is decimal or DateTime or DateTimeOffset or Guid)
            return Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

        return EntitySummary(value) ?? Clean(value.ToString() ?? type.Name, 100);
    }

    public static void SchedulePathScan(Window? window, string routine)
    {
        if (!DebugService.IsEnabled || window == null || window is DebugWindow)
            return;

        try
        {
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                int visited = 0;
                ScanPaths(window, routine, ref visited);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch { }
    }

    private static void ScanPaths(DependencyObject current, string routine, ref int visited)
    {
        if (visited++ > 1200)
            return;

        if (current is FrameworkElement fe)
        {
            string? candidate = fe switch
            {
                TextBox tb when !Sensitive(tb.Name) => tb.Text,
                TextBlock textBlock => textBlock.Text,
                ContentControl cc when cc.Content is string content => content,
                _ => null
            };

            LogPathIfPresent(candidate, routine);
        }

        int count;
        try { count = VisualTreeHelper.GetChildrenCount(current); }
        catch { return; }

        for (int i = 0; i < count; i++)
            ScanPaths(VisualTreeHelper.GetChild(current, i), routine, ref visited);
    }

    private static void LogPathIfPresent(string? candidate, string routine)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;

        string text = candidate.Trim().Trim('"');
        bool looksLikePath = text.StartsWith(@"\\", StringComparison.Ordinal) ||
                             (text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' &&
                              (text[2] == '\\' || text[2] == '/'));
        if (!looksLikePath)
            return;

        string fullPath;
        try { fullPath = Path.GetFullPath(text); }
        catch { return; }

        if (!File.Exists(fullPath))
            return;

        string key = "RESOURCE|" + fullPath;
        lock (Sync)
        {
            if (LastFileEvents.TryGetValue(key, out DateTime last) &&
                DateTime.Now - last < TimeSpan.FromMinutes(2))
                return;
            LastFileEvents[key] = DateTime.Now;
        }

        DebugService.RecordBusiness(
            routine,
            "Arquivo em uso na rotina",
            details: $"Tamanho: {FormatBytes(new FileInfo(fullPath).Length)}",
            filePath: fullPath,
            level: DebugEntryLevel.Background);
    }

    private static void InitializeBusinessWatcher(MainWindow mainWindow)
    {
        if (!string.IsNullOrWhiteSpace(_businessBasePath))
            return;

        try
        {
            var method = typeof(MainWindow).GetMethod("ObterCaminhoBase", BindingFlags.Instance | BindingFlags.NonPublic);
            string? path = method?.Invoke(mainWindow, null) as string;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            _businessBasePath = Path.GetFullPath(path);
            PreloadJson(_businessBasePath);

            var watcher = new FileSystemWatcher(_businessBasePath)
            {
                Filter = "*",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += (_, e) => QueueFileEvent("alterado", e.FullPath);
            watcher.Created += (_, e) => QueueFileEvent("criado", e.FullPath);
            watcher.Deleted += (_, e) => QueueFileEvent("excluído", e.FullPath);
            watcher.Renamed += (_, e) => QueueFileEvent("renomeado", e.FullPath, e.OldFullPath);
            Watchers.Add(watcher);

            DebugService.RecordBusiness("SYSTEM", "Monitor de arquivos de negócio ativo", details: $"Base de dados: {_businessBasePath}");
        }
        catch (Exception ex)
        {
            DebugService.Record("DEBUG", "Não foi possível iniciar o monitor de arquivos de negócio.", DebugEntryLevel.Warning, ex);
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

            string key = operation + "|" + path;
            DateTime now = DateTime.Now;

            lock (Sync)
            {
                if (LastFileEvents.TryGetValue(key, out DateTime last) && now - last < TimeSpan.FromMilliseconds(500))
                    return;
                LastFileEvents[key] = now;
            }

            DebugSemanticContext? context;
            lock (Sync)
            {
                context = _recent != null && now - _recent.Timestamp < TimeSpan.FromSeconds(RecentContextSeconds)
                    ? _recent
                    : null;
            }

            string routine = context?.Routine ?? ResolveRoutineFromPath(path);
            var details = new List<string>
            {
                $"Caminho: {path}",
                $"Diretório: {Path.GetDirectoryName(path) ?? string.Empty}"
            };

            if (File.Exists(path))
            {
                try { details.Add($"Tamanho: {FormatBytes(new FileInfo(path).Length)}"); }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(oldPath))
                details.Add($"Caminho anterior: {oldPath}");
            if (!string.IsNullOrWhiteSpace(context?.Action))
                details.Add($"Ação relacionada: {context.Action}");
            if (!string.IsNullOrWhiteSpace(context?.Entity))
                details.Add($"Entidade: {context.Entity}");

            DebugService.Record(routine, $"Arquivo {operation} | {string.Join(" | ", details)}", DebugEntryLevel.Background);

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
            DebugService.Record("DEBUG", $"Falha ao processar evento de arquivo: {path}", DebugEntryLevel.Warning, ex);
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
        if (oldContent == null || string.Equals(oldContent, newContent, StringComparison.Ordinal))
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
            DebugService.RecordBusiness(routine, "Conteúdo JSON atualizado", details: "Estrutura alterada", filePath: path, level: DebugEntryLevel.Action);
            return;
        }

        var oldItems = oldDoc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
        var newItems = newDoc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
        var matches = MatchItems(oldItems, newItems);

        int added = matches.Count(x => x.OldIndex < 0);
        int removed = matches.Count(x => x.NewIndex < 0);
        if (added > 0 || removed > 0)
        {
            DebugService.RecordBusiness(
                routine,
                "Base JSON alterada",
                details: $"Total: {oldItems.Count} → {newItems.Count} | Incluídos: {added} | Removidos: {removed}",
                filePath: path,
                level: DebugEntryLevel.Action);
        }

        int emitted = 0;
        foreach (var match in matches)
        {
            if (emitted >= 12)
                break;

            if (match.OldIndex < 0)
            {
                JsonElement item = newItems[match.NewIndex];
                DebugService.RecordBusiness(routine, "Registro incluído", EntityLabel(item, path), ObjectSummary(item), path, DebugEntryLevel.Action);
                emitted++;
                continue;
            }

            if (match.NewIndex < 0)
            {
                JsonElement item = oldItems[match.OldIndex];
                DebugService.RecordBusiness(routine, "Registro removido", EntityLabel(item, path), ObjectSummary(item), path, DebugEntryLevel.Action);
                emitted++;
                continue;
            }

            JsonElement oldItem = oldItems[match.OldIndex];
            JsonElement newItem = newItems[match.NewIndex];
            if (JsonEqual(oldItem, newItem))
                continue;

            var changes = CompareObjects(oldItem, newItem);
            if (changes.Count == 0)
                continue;

            DebugService.RecordBusiness(
                routine,
                "Dados alterados",
                EntityLabel(newItem, path) ?? EntityLabel(oldItem, path),
                string.Join("; ", changes),
                path,
                DebugEntryLevel.Action);
            emitted++;
        }

        if (emitted == 0 && oldItems.Count == newItems.Count && !JsonEqual(oldDoc.RootElement, newDoc.RootElement))
            DebugService.RecordBusiness(routine, "Base JSON atualizada", details: "Somente ordenação/estrutura mudou", filePath: path);
    }

    private static List<JsonMatch> MatchItems(IReadOnlyList<JsonElement> oldItems, IReadOnlyList<JsonElement> newItems)
    {
        var result = new List<JsonMatch>();
        var oldRemaining = new HashSet<int>(Enumerable.Range(0, oldItems.Count));
        var newRemaining = new HashSet<int>(Enumerable.Range(0, newItems.Count));

        var oldGroups = oldRemaining.Select(i => (Index: i, Key: MatchKey(oldItems[i])))
            .Where(x => x.Key != null).GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Index).ToList(), StringComparer.OrdinalIgnoreCase);
        var newGroups = newRemaining.Select(i => (Index: i, Key: MatchKey(newItems[i])))
            .Where(x => x.Key != null).GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Index).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (string key in oldGroups.Keys.Intersect(newGroups.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var og = oldGroups[key].Where(oldRemaining.Contains).ToList();
            var ng = newGroups[key].Where(newRemaining.Contains).ToList();

            foreach (int oi in og.ToList())
            {
                int exact = ng.Where(ni => newRemaining.Contains(ni) && JsonEqual(oldItems[oi], newItems[ni])).DefaultIfEmpty(-1).First();
                if (exact < 0) continue;
                result.Add(new JsonMatch(oi, exact));
                oldRemaining.Remove(oi); newRemaining.Remove(exact); ng.Remove(exact);
            }

            og = og.Where(oldRemaining.Contains).ToList();
            ng = ng.Where(newRemaining.Contains).ToList();
            while (og.Count > 0 && ng.Count > 0)
            {
                int bo = -1, bn = -1, score = int.MaxValue;
                foreach (int oi in og)
                foreach (int ni in ng)
                {
                    int current = CompareObjects(oldItems[oi], newItems[ni]).Count;
                    if (current < score) { score = current; bo = oi; bn = ni; }
                }
                if (bo < 0 || bn < 0) break;
                result.Add(new JsonMatch(bo, bn));
                oldRemaining.Remove(bo); newRemaining.Remove(bn); og.Remove(bo); ng.Remove(bn);
            }
        }

        foreach (int oi in oldRemaining.ToList())
        {
            int exact = newRemaining.Where(ni => JsonEqual(oldItems[oi], newItems[ni])).DefaultIfEmpty(-1).First();
            if (exact < 0) continue;
            result.Add(new JsonMatch(oi, exact));
            oldRemaining.Remove(oi); newRemaining.Remove(exact);
        }

        result.AddRange(oldRemaining.OrderBy(x => x).Select(x => new JsonMatch(x, -1)));
        result.AddRange(newRemaining.OrderBy(x => x).Select(x => new JsonMatch(-1, x)));
        return result;
    }

    private static string? MatchKey(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        string? id = Scalar(element, "Id");
        if (!string.IsNullOrWhiteSpace(id)) return "ID:" + id;
        string? code = Scalar(element, "Codigo");
        if (!string.IsNullOrWhiteSpace(code)) return "CODIGO:" + code;
        string? supplier = Scalar(element, "CodigoFornecedor");
        if (!string.IsNullOrWhiteSpace(supplier)) return "FORNECEDOR:" + supplier;
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
            if (Sensitive(name)) continue;
            oldProps.TryGetValue(name, out JsonElement oldValue);
            newProps.TryGetValue(name, out JsonElement newValue);
            if (JsonEqual(oldValue, newValue)) continue;
            changes.Add($"{Friendly(name)}: {FormatJson(name, oldValue)} → {FormatJson(name, newValue)}");
            if (changes.Count >= 8) break;
        }
        return changes;
    }

    private static string? EntityLabel(JsonElement item, string path)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        string? name = StringValue(item, "NomeFornecedor") ?? StringValue(item, "FornecedorNome") ?? StringValue(item, "Nome") ?? StringValue(item, "Descricao") ?? StringValue(item, "Competencia");
        string? id = Scalar(item, "Id") ?? Scalar(item, "Codigo") ?? Scalar(item, "CodigoFornecedor");
        string type = Path.GetFileName(path).Contains("fornecedores", StringComparison.OrdinalIgnoreCase) ? "Fornecedor" :
                      Path.GetFileName(path).Contains("previsoes_pagamento", StringComparison.OrdinalIgnoreCase) ? "Pagamento OPEX" : "Registro";
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) return $"{type} \"{Clean(name, 80)}\" ({id})";
        if (!string.IsNullOrWhiteSpace(name)) return $"{type} \"{Clean(name, 80)}\"";
        return !string.IsNullOrWhiteSpace(id) ? $"{type} ({id})" : null;
    }

    private static string ObjectSummary(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return "Conteúdo registrado";
        var parts = new List<string>();
        foreach (var p in item.EnumerateObject())
        {
            if (parts.Count >= 6 || Sensitive(p.Name) || p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
            parts.Add($"{Friendly(p.Name)}={FormatJson(p.Name, p.Value)}");
        }
        return parts.Count == 0 ? "Conteúdo registrado" : string.Join("; ", parts);
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
        {
            if (_recent != null && DateTime.Now - _recent.Timestamp < TimeSpan.FromSeconds(10))
                return _recent.Routine;
        }
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
                if (parts.Count >= 5 || !props.TryGetValue(name, out PropertyInfo? prop) || Sensitive(name)) continue;
                object? v; try { v = prop.GetValue(value); } catch { continue; }
                if (v == null) continue;
                string formatted = v switch
                {
                    DateTime dt => dt.ToString("dd/MM/yyyy"),
                    decimal d when name.Contains("Valor", StringComparison.OrdinalIgnoreCase) => d.ToString("C2", new System.Globalization.CultureInfo("pt-BR")),
                    string text => $"\"{Clean(text, 80)}\"",
                    _ => Clean(Convert.ToString(v, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty, 80)
                };
                if (!string.IsNullOrWhiteSpace(formatted)) parts.Add($"{Friendly(name)}={formatted}");
            }
            if (parts.Count == 0) return null;
            string entityType = type.Name.Contains("Fornecedor", StringComparison.OrdinalIgnoreCase) ? "Fornecedor" : type.Name.Contains("Pagamento", StringComparison.OrdinalIgnoreCase) ? "Pagamento" : type.Name.Contains("Analise", StringComparison.OrdinalIgnoreCase) ? "Análise" : type.Name.Contains("Cnab", StringComparison.OrdinalIgnoreCase) ? "CNAB" : type.Name;
            return $"{entityType} ({string.Join(", ", parts)})";
        }
        catch { return null; }
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
        return string.IsNullOrWhiteSpace(file) || new[] { ".tmp", ".temp", ".lock", ".lck", ".part", ".crdownload", "~" }.Any(s => file.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Sensitive(string name)
    {
        string lower = name.ToLowerInvariant();
        return new[] { "senha", "password", "token", "secret", "segredo", "cvv", "pin", "agencia", "agência", "conta", "cpf", "documento", "chavepix", "chave_pix", "credencial", "credential" }.Any(lower.Contains);
    }

    private static string? StringValue(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static string? Scalar(JsonElement e, string name) => !e.TryGetProperty(name, out JsonElement p) ? null : p.ValueKind == JsonValueKind.String ? p.GetString() : p.ValueKind == JsonValueKind.Number ? p.GetRawText() : null;
    private static bool JsonEqual(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && (a.ValueKind == JsonValueKind.Undefined || string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal));

    private static string FormatJson(string name, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Undefined) return "(ausente)";
        if (e.ValueKind == JsonValueKind.Null) return "null";
        if (e.ValueKind == JsonValueKind.String)
        {
            string text = e.GetString() ?? string.Empty;
            if (name.Contains("Data", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(text, out DateTime dt)) return dt.ToString("dd/MM/yyyy");
            return $"\"{Clean(text, 90)}\"";
        }
        if (e.ValueKind == JsonValueKind.Number && name.Contains("Valor", StringComparison.OrdinalIgnoreCase) && e.TryGetDecimal(out decimal amount)) return amount.ToString("C2", new System.Globalization.CultureInfo("pt-BR"));
        return Clean(e.GetRawText(), 90);
    }

    private static string Friendly(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++) { if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' '); sb.Append(name[i]); }
        return sb.ToString();
    }

    private static string Clean(string text, int max) { string s = text.Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length > max ? s[..max] + "…" : s; }
    private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:N1} KB" : $"{bytes / (1024d * 1024d):N1} MB";
    private static string Title(Window window) => string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title;

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
}
