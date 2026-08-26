$ErrorActionPreference = 'Stop'

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

# -----------------------------------------------------------------------------
# SantanderCommitmentMemoryService.cs - memória persistente em JSON
# -----------------------------------------------------------------------------
$path = 'SantanderCommitmentMemoryService.cs'
$text = Get-Content $path -Raw

if ($text -notmatch 'using System\.Text\.Json;') {
    $text = $text.Replace(
        'using System.Diagnostics;',
        "using System.Diagnostics;`r`nusing System.IO;`r`nusing System.Text.Json;")
}

$text = $text.Replace(
    '/// Memória temporária dos últimos resultados efetivamente observados na tela',
    '/// Histórico persistente dos últimos resultados efetivamente observados na tela')
$text = $text.Replace(
    '/// Consultar compromissos. Nada é persistido em disco.',
    '/// Consultar compromissos. Os snapshots são persistidos em JSON na pasta data do HUB.')

if ($text -notmatch 'PersistenceFilePath') {
    $needle = '    private static readonly object Sync = new();'
    if (-not $text.Contains($needle)) { throw 'Santander: Sync marker not found' }

    $replacement = @'
    private static readonly object Sync = new();
    private static readonly object PersistenceSync = new();
    private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly string PersistenceDirectory = ResolvePersistenceDirectory();
    private static readonly string PersistencePath = Path.Combine(PersistenceDirectory, "santander_contextos.json");

    public static string PersistenceFilePath => PersistencePath;
'@
    $text = $text.Replace($needle, $replacement.TrimEnd("`r", "`n"))
}

if ($text -notmatch 'LoadPersistedEntries\(\);') {
    $pattern = '(?s)(public static void Initialize\(\)\s*\{.*?Application\.Current\.Exit \+= Application_Exit;\s*\})\s*(if \(DebugService\.IsEnabled\))'
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { throw 'Santander: Initialize marker not found' }
    $replacement = $match.Groups[1].Value + "`r`n`r`n        LoadPersistedEntries();`r`n`r`n        " + $match.Groups[2].Value
    $text = $text.Substring(0, $match.Index) + $replacement + $text.Substring($match.Index + $match.Length)
}

if ($text -notmatch 'PersistEntriesToDisk\(\);\s*\r?\n\s*Application\.Current\?\.Dispatcher') {
    $pattern = '(?s)(public static void Clear\(\)\s*\{\s*lock \(Sync\)\s*\{.*?_nextContextProbeUtc = DateTime\.MinValue;\s*\})\s*(Application\.Current\?\.Dispatcher)'
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { throw 'Santander: Clear marker not found' }
    $replacement = $match.Groups[1].Value + "`r`n`r`n        PersistEntriesToDisk();`r`n`r`n        " + $match.Groups[2].Value
    $text = $text.Substring(0, $match.Index) + $replacement + $text.Substring($match.Index + $match.Length)
}

$text = $text.Replace(
    'Memória temporária de compromissos foi limpa pelo usuário.',
    'Histórico persistente de compromissos foi limpo pelo usuário.')

$exitPattern = '(?s)(private static void Application_Exit\(object sender, ExitEventArgs e\)\s*\{\s*StopMonitoring\(\);)\s*lock \(Sync\)\s*Entries\.Clear\(\);'
if ([regex]::IsMatch($text, $exitPattern)) {
    $text = [regex]::Replace($text, $exitPattern, '$1', 1)
}

$text = $text.Replace(
    '$"Memória contextual ativa | temporária em RAM | retenção: {MaxPeriodsPerContext} períodos por contexto / {MaxEntriesTotal} snapshots no total."',
    '$"Histórico contextual ativo | persistência: {PersistencePath} | retenção: {MaxPeriodsPerContext} períodos por contexto / {MaxEntriesTotal} snapshots no total."')

if ($text -notmatch 'TrimUnsafe\(entry\.ContextKey\);\s*\}\s*\r?\n\s*PersistEntriesToDisk\(\);') {
    $pattern = '(Entries\[entry\.StorageKey\] = entry;\s*\r?\n\s*TrimUnsafe\(entry\.ContextKey\);\s*\})'
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { throw 'Santander: Store marker not found' }
    $replacement = $match.Groups[1].Value + "`r`n`r`n        PersistEntriesToDisk();"
    $text = $text.Substring(0, $match.Index) + $replacement + $text.Substring($match.Index + $match.Length)
}

if ($text -notmatch 'private static void LoadPersistedEntries\(') {
    $marker = '    private static ContextDetection DetectCompanyContext()'
    if (-not $text.Contains($marker)) { throw 'Santander: DetectCompanyContext marker not found' }

    $methods = @'
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

            lock (Sync)
            {
                Entries.Clear();
                foreach (var entry in savedEntries.OrderBy(entry => entry.AtualizadoEm))
                {
                    if (!string.IsNullOrWhiteSpace(entry.StorageKey))
                        Entries[entry.StorageKey] = entry;
                }

                var contextKeys = Entries.Values
                    .Select(entry => entry.ContextKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var contextKey in contextKeys)
                    TrimUnsafe(contextKey);
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

'@
    $text = $text.Replace($marker, $methods + $marker)
}

Write-Utf8NoBom $path $text

# -----------------------------------------------------------------------------
# OpexDebugInspectorWindow.xaml - linguagem persistente
# -----------------------------------------------------------------------------
$path = 'OpexDebugInspectorWindow.xaml'
$text = Get-Content $path -Raw
$text = $text.Replace('Title="Debug O.P.E.X. - Memória temporária"', 'Title="O.P.E.X. - Contextos salvos"')
$text = $text.Replace('Memória temporária dos últimos resultados realmente observados nos contextos bancários.', 'Histórico persistente dos últimos resultados realmente observados nos contextos bancários.')
$text = $text.Replace('Content="Limpar memória"', 'Content="Limpar histórico"')
$text = $text.Replace('Text="Nenhum contexto em memória"', 'Text="Nenhum contexto salvo"')
$text = $text.Replace('RAM da sessão • até 4 períodos por contexto • nenhum snapshot é salvo em disco', 'Histórico em disco • até 4 períodos por contexto • salvo em data\santander_contextos.json')
Write-Utf8NoBom $path $text

# -----------------------------------------------------------------------------
# OpexDebugInspectorWindow.xaml.cs - mensagens persistentes
# -----------------------------------------------------------------------------
$path = 'OpexDebugInspectorWindow.xaml.cs'
$text = Get-Content $path -Raw
$text = $text.Replace('Inspetor de memória temporária aberto.', 'Inspetor de histórico persistente aberto.')
$text = $text.Replace('"1 snapshot em memória"', '"1 snapshot salvo"')
$text = $text.Replace('$"{entries.Count} snapshots em memória"', '$"{entries.Count} snapshots salvos"')
$text = $text.Replace('"Nenhum contexto em memória"', '"Nenhum contexto salvo"')
$text = $text.Replace('"Limpar todos os snapshots temporários desta sessão?"', '"Limpar todos os snapshots salvos no histórico? Esta ação também atualizará o arquivo JSON."')
$text = $text.Replace('"Memória O.P.E.X."', '"Histórico O.P.E.X."')
Write-Utf8NoBom $path $text

# -----------------------------------------------------------------------------
# MainWindow.xaml - clona o visual do botão Opções para os dois novos itens
# -----------------------------------------------------------------------------
$path = 'MainWindow.xaml'
$text = Get-Content $path -Raw

if ($text -notmatch 'InfosPositivaMenu_Click') {
    $pattern = '(?s)(?<block><Button\s+Content="Opções".*?Click="OpcoesMenu_Click".*?</Button>)'
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { throw 'MainWindow: botão Opções não encontrado' }

    $block = $match.Groups['block'].Value
    $info = $block.Replace('Content="Opções"', 'Content="Infos Positiva"').Replace('Click="OpcoesMenu_Click"', 'Click="InfosPositivaMenu_Click"')
    $sobre = $block.Replace('Content="Opções"', 'Content="Sobre"').Replace('Click="OpcoesMenu_Click"', 'Click="SobreMenu_Click"')
    $insert = $block + "`r`n" + $info + "`r`n" + $sobre

    $text = $text.Substring(0, $match.Index) + $insert + $text.Substring($match.Index + $match.Length)
}

Write-Utf8NoBom $path $text

Write-Host 'Patch aplicado com sucesso.'
