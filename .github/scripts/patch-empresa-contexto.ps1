$ErrorActionPreference = 'Stop'

$path = 'SantanderCommitmentMemoryService.cs'
$text = Get-Content $path -Raw

if ($text -notmatch 'AdministradoraCompanyName') {
    $needle = '    private const string CorretoraConvenio = "0033-4268-004905078983";'
    if (-not $text.Contains($needle)) { throw 'Convenio constants target not found' }

    $replacement = $needle + "`n" +
        '    private const string AdministradoraCompanyName = "POSITIVA ADMINISTRADORA DE BENEFÍCIOS LTDA";' + "`n" +
        '    private const string CorretoraCompanyName = "POSITIVA CORRETORA DE SEGUROS LTDA";'
    $text = $text.Replace($needle, $replacement)
}

if ($text.Contains('            company ?? string.Empty,')) {
    $text = $text.Replace(
        '            company ?? string.Empty,',
        '            CanonicalCompanyNameFromConvenio(convenio),')
}

if ($text -notmatch 'private static string CanonicalCompanyNameFromConvenio') {
    $marker = '    private static string ClassifyContextFromConvenio(string convenio)'
    if (-not $text.Contains($marker)) { throw 'ClassifyContext marker not found' }

    $method = @'
    private static string CanonicalCompanyNameFromConvenio(string convenio)
    {
        if (string.Equals(convenio, CorretoraConvenio, StringComparison.OrdinalIgnoreCase))
            return CorretoraCompanyName;

        if (string.Equals(convenio, AdministradoraConvenio, StringComparison.OrdinalIgnoreCase))
            return AdministradoraCompanyName;

        return "Empresa não identificada";
    }

'@
    $text = $text.Replace($marker, $method + $marker)
}

Set-Content -Path $path -Value $text -Encoding utf8
