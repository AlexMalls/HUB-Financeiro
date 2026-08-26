$ErrorActionPreference = 'Stop'

function Read-Normalized([string]$Path) {
    return ([System.IO.File]::ReadAllText($Path)).Replace("`r`n", "`n")
}

function Write-Normalized([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Replace-Exact([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $oldNorm = $Old.Replace("`r`n", "`n")
    $newNorm = $New.Replace("`r`n", "`n")
    if (-not $Text.Contains($oldNorm)) {
        throw "Trecho nao encontrado: $Label"
    }
    return $Text.Replace($oldNorm, $newNorm)
}

$memoryPath = 'SantanderCommitmentMemoryService.cs'
$memory = Read-Normalized $memoryPath

$memory = Replace-Exact $memory @'
    private static string _lastDetectedContextKey = string.Empty;
    private static DateTime _nextContextProbeUtc;
    private static bool _captureInProgress;
    private static string _lastContextDiagnostic = string.Empty;
'@ @'
    private static string _lastDetectedContextKey = string.Empty;
    private static DateTime _nextContextProbeUtc;
    private static bool _captureInProgress;
    private static string _lastContextDiagnostic = string.Empty;
    private static string _pendingContextKey = string.Empty;
    private static string _pendingContextBaselineTableSignature = string.Empty;
    private static DateTime _pendingContextDetectedAt = DateTime.MinValue;
    private static bool _pendingContextSawEmptyResult;
'@ 'campos de transicao de contexto'

$oldReset = @'
            _lastDetectedContextKey = string.Empty;
            _nextContextProbeUtc = DateTime.MinValue;
'@.Replace("`r`n", "`n")
$newReset = @'
            _lastDetectedContextKey = string.Empty;
            _pendingContextKey = string.Empty;
            _pendingContextBaselineTableSignature = string.Empty;
            _pendingContextDetectedAt = DateTime.MinValue;
            _pendingContextSawEmptyResult = false;
            _nextContextProbeUtc = DateTime.MinValue;
'@.Replace("`r`n", "`n")
$memory = $memory.Replace($oldReset, $newReset)

$memory = Replace-Exact $memory @'
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

            var now = DateTime.UtcNow;
            var tableSignature = BuildTableSignature(snapshot!);
            bool tableChanged;

            lock (Sync)
            {
                tableChanged = !string.Equals(tableSignature, _lastSeenTableSignature, StringComparison.Ordinal);
                if (tableChanged)
                    _lastSeenTableSignature = tableSignature;

                // Se a tabela mudou, verificamos imediatamente. Se não mudou, fazemos
                // apenas uma sonda contextual leve a cada ~1,6s enquanto a tela correta
                // está ativa. Isso detecta Trocar Convênio sem consultar rede/banco.
                if (!tableChanged && now < _nextContextProbeUtc)
                    return;

                _nextContextProbeUtc = now.AddMilliseconds(ContextProbeIntervalMilliseconds);
                _captureInProgress = true;
            }

            var frozenSnapshot = CloneSnapshot(snapshot!);
            _ = Task.Run(() => CaptureContextAndStore(frozenSnapshot, tableSignature));
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
'@ @'
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
'@ 'Tick do historico contextual'

$memory = Replace-Exact $memory @'
    private static void CaptureContextAndStore(SantanderCommitmentSnapshot snapshot, string tableSignature)
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
            var contextChanged = false;
            string previousContext;

            lock (Sync)
            {
                previousContext = _lastDetectedContextKey;
                if (!string.Equals(previousContext, contextKey, StringComparison.OrdinalIgnoreCase))
                {
                    _lastDetectedContextKey = contextKey;
                    contextChanged = true;
                }
                _lastContextDiagnostic = string.Empty;
            }

            if (contextChanged)
            {
                var previousDisplay = string.IsNullOrWhiteSpace(previousContext)
                    ? "nenhum"
                    : previousContext.Replace("|", " — ");

                DebugService.Record(
                    "SANTANDER",
                    $"Contexto bancário detectado/alterado | {previousDisplay} → {BankName} — {detection.Contexto} | Convênio: {detection.Convenio} | Empresa: {company}.",
                    DebugEntryLevel.Action);
            }

            // O número do convênio é a fonte de verdade do contexto. A assinatura
            // não inclui o período dos inputs para não associar uma tabela antiga a
            // uma data recém-selecionada antes do resultado realmente ser carregado.
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
'@ @'
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
'@ 'captura e classificacao por contexto'

$memory = Replace-Exact $memory @'
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
'@ @'
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
'@ 'migracao de duplicatas persistidas'

$memory = Replace-Exact $memory @'
    private static string ResolvePersistenceDirectory()
'@ @'
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
'@ 'limpeza de duplicatas cross-context'

Write-Normalized $memoryPath $memory

$infoPath = 'InfosPositivaWindow.xaml'
$info = Read-Normalized $infoPath

$info = Replace-Exact $info @'
        <Style x:Key="SectionLabel" TargetType="TextBlock">
            <Setter Property="Foreground" Value="#8F8F98"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
        </Style>
        <Style x:Key="CardTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="17"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="White"/>
        </Style>
'@ @'
        <Style x:Key="SelectableText" TargetType="TextBox">
            <Setter Property="IsReadOnly" Value="True"/>
            <Setter Property="IsReadOnlyCaretVisible" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="TextWrapping" Value="Wrap"/>
            <Setter Property="Cursor" Value="IBeam"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="SelectionBrush" Value="#6E3AA4"/>
            <Setter Property="SelectionOpacity" Value="0.75"/>
        </Style>
        <Style x:Key="SectionLabel" TargetType="TextBox" BasedOn="{StaticResource SelectableText}">
            <Setter Property="Foreground" Value="#8F8F98"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
        </Style>
        <Style x:Key="CardTitle" TargetType="TextBox" BasedOn="{StaticResource SelectableText}">
            <Setter Property="FontSize" Value="17"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="White"/>
        </Style>
'@ 'styles selecionaveis'

$pattern = [regex]'(?s)<TextBlock\b.*?/>'
$info = $pattern.Replace($info, {
    param($m)
    $tag = $m.Value
    if ($tag.Contains('FontFamily="Segoe MDL2 Assets"')) { return $tag }

    $tag = $tag.Replace('<TextBlock', '<TextBox')
    if (-not $tag.Contains(' Style='))
    {
        $tag = $tag.Replace('<TextBox', '<TextBox Style="{StaticResource SelectableText}"', 1)
    }
    return $tag
})

Write-Normalized $infoPath $info

$testsPath = 'SantanderMonitorServiceTestes.cs'
$tests = Read-Normalized $testsPath
$tests = Replace-Exact $tests @'
        DeveAceitarSomenteRotulosDeNavegacaoSeguros();
'@ @'
        DeveAceitarSomenteRotulosDeNavegacaoSeguros();
        DeveBloquearSnapshotAnteriorATrocaDeConvenio();
'@ 'chamada do teste de contexto'

$tests = Replace-Exact $tests @'
    private static void Assert(bool condition, string scenario)
'@ @'
    private static void DeveBloquearSnapshotAnteriorATrocaDeConvenio()
    {
        var troca = new DateTime(2026, 8, 26, 18, 15, 20, DateTimeKind.Local);
        const string tabelaAnterior = "2|R$ 6.405,24|ADM";
        const string tabelaNova = "0|R$ 0,00|COR";

        Assert(
            !SantanderCommitmentMemoryService.IsSafeSnapshotAfterContextSwitch(
                troca.AddMilliseconds(-100), troca, tabelaAnterior, tabelaAnterior, false),
            "snapshot anterior à troca de convênio");

        Assert(
            !SantanderCommitmentMemoryService.IsSafeSnapshotAfterContextSwitch(
                troca.AddSeconds(2), troca, tabelaAnterior, tabelaAnterior, false),
            "mesma tabela antiga sem transição vazia");

        Assert(
            SantanderCommitmentMemoryService.IsSafeSnapshotAfterContextSwitch(
                troca.AddSeconds(2), troca, tabelaAnterior, tabelaAnterior, true),
            "resultado após tela vazia");

        Assert(
            SantanderCommitmentMemoryService.IsSafeSnapshotAfterContextSwitch(
                troca.AddSeconds(2), troca, tabelaNova, tabelaAnterior, false),
            "nova composição após troca de convênio");
    }

    private static void Assert(bool condition, string scenario)
'@ 'teste da barreira de contexto'

Write-Normalized $testsPath $tests
Write-Host 'Patch aplicado com sucesso.'
