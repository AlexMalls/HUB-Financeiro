$ErrorActionPreference = 'Stop'

function Read-N([string]$Path) {
    return ([System.IO.File]::ReadAllText($Path)).Replace("`r`n", "`n")
}

function Write-N([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text.Replace("`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))
}

$servicePath = 'OpexPaymentReconciliationService.cs'
$service = Read-N $servicePath
$old = @'
            // Se ainda sobrarem itens de mesmo valor, pareia por quantidade.
            // Isto preserva a semântica de multiconjunto e resolve casos em que o
            // nome Santander vem abreviado/diferente do cadastro do HUB.
            var remainingHub = availableHub.Where(index => !usedHub[index]).ToList();
            var remainingBank = availableBank.Where(index => !usedBank[index]).ToList();
            var fallbackCount = Math.Min(remainingHub.Count, remainingBank.Count);

            for (var i = 0; i < fallbackCount; i++)
            {
                var match = new PaymentMatch(remainingHub[i], remainingBank[i], 0);
                usedHub[match.HubIndex] = true;
                usedBank[match.BankIndex] = true;
                matches.Add(match);
            }
'@.Replace("`r`n", "`n")
$new = @'
            // Valor sozinho não comprova identidade. Se os dois lados expõem nome
            // e não há qualquer afinidade entre eles, deixamos os itens sem casar
            // para que a divergência apareça ao usuário em vez de gerar falso OK.
            // O fallback só é permitido no caso único em que uma das pontas não
            // expõe nome suficiente para comparação.
            var remainingHub = availableHub.Where(index => !usedHub[index]).ToList();
            var remainingBank = availableBank.Where(index => !usedBank[index]).ToList();

            if (remainingHub.Count == 1 && remainingBank.Count == 1)
            {
                var hubIndex = remainingHub[0];
                var bankIndex = remainingBank[0];
                var hubName = NormalizeName(hub[hubIndex].NomeFornecedor);
                var bankName = NormalizeName(bank[bankIndex].Favorecido);

                if (string.IsNullOrWhiteSpace(hubName) || string.IsNullOrWhiteSpace(bankName))
                {
                    var match = new PaymentMatch(hubIndex, bankIndex, 0);
                    usedHub[match.HubIndex] = true;
                    usedBank[match.BankIndex] = true;
                    matches.Add(match);
                }
            }
'@.Replace("`r`n", "`n")
if (-not $service.Contains($old)) { throw 'Bloco de fallback não encontrado.' }
$service = $service.Replace($old, $new)
Write-N $servicePath $service

$testsPath = 'OpexPaymentReconciliationServiceTestes.cs'
$tests = Read-N $testsPath
$oldCall = '        DeveDistinguirPagamentosDeMesmoValorPorNome();'
$newCall = $oldCall + "`n        DeveNaoCasarSomentePorValorQuandoFavorecidosDiferem();"
if (-not $tests.Contains($oldCall)) { throw 'Chamada de teste não encontrada.' }
$tests = $tests.Replace($oldCall, $newCall)

$anchor = "    private static void DeveAvisarQuandoNaoHaMemoriaBancariaDaEmpresa()`n"
$test = @'
    private static void DeveNaoCasarSomentePorValorQuandoFavorecidosDiferem()
    {
        var data = new DateTime(2026, 8, 26);
        var hub = new[] { Hub(1, "Alpha Serviços Ltda", 500m, "ADM", "No Banco", data) };
        var banco = new[] { Banco("Beta Comércio Ltda", 500m, data, 99) };

        var result = OpexPaymentReconciliationService.Conferir(
            data,
            hub,
            new[] { Memoria("Administradora", data, banco), Memoria("Corretora", data, Array.Empty<SantanderCommitmentItem>(), 0) });

        Assert(result.Administradora.Divergencias.Count(i => i.Tipo == OpexPaymentReconciliationIssueKind.HubNoBancoAusenteNoSantander) == 1,
            "mesmo valor com favorecido diferente não pode validar o No Banco");
        Assert(result.Administradora.Divergencias.Count(i => i.Tipo == OpexPaymentReconciliationIssueKind.SantanderAusenteNoHub) == 1,
            "mesmo valor com favorecido diferente deve manter a linha Santander sem correspondência");
    }

'@.Replace("`r`n", "`n")
if (-not $tests.Contains($anchor)) { throw 'Âncora para novo teste não encontrada.' }
$tests = $tests.Replace($anchor, $test + $anchor)
Write-N $testsPath $tests

Write-Host 'Pareamento seguro aplicado.'
