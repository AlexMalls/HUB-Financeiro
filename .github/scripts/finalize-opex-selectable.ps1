$ErrorActionPreference = 'Stop'

function Read-Normalized([string]$Path) {
    return ([System.IO.File]::ReadAllText($Path)).Replace("`r`n", "`n")
}

function Write-Normalized([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

$memoryPath = 'SantanderCommitmentMemoryService.cs'
$memory = Read-Normalized $memoryPath
$old = @'
            _lastStoredCompositeSignature = string.Empty;
            _lastDetectedContextKey = string.Empty;
            _lastContextDiagnostic = string.Empty;
            _nextContextProbeUtc = DateTime.MinValue;
'@.Replace("`r`n", "`n")
$new = @'
            _lastStoredCompositeSignature = string.Empty;
            _lastDetectedContextKey = string.Empty;
            _pendingContextKey = string.Empty;
            _pendingContextBaselineTableSignature = string.Empty;
            _pendingContextDetectedAt = DateTime.MinValue;
            _pendingContextSawEmptyResult = false;
            _lastContextDiagnostic = string.Empty;
            _nextContextProbeUtc = DateTime.MinValue;
'@.Replace("`r`n", "`n")
if (-not $memory.Contains($old)) { throw 'Bloco Start nao encontrado.' }
$memory = $memory.Replace($old, $new)
Write-Normalized $memoryPath $memory

$infoPath = 'InfosPositivaWindow.xaml'
$info = Read-Normalized $infoPath
$oldInfo = '            <Setter Property="Cursor" Value="IBeam"/>'
$newInfo = "            <Setter Property=`"Cursor`" Value=`"IBeam`"/>`n            <Setter Property=`"IsTabStop`" Value=`"False`"/>"
if (-not $info.Contains($oldInfo)) { throw 'Style SelectableText nao encontrado.' }
$info = $info.Replace($oldInfo, $newInfo)
Write-Normalized $infoPath $info

Write-Host 'Ajustes finais aplicados.'
