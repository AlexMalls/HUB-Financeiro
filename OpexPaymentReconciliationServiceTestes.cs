namespace HubFinanceiro;

public static class OpexPaymentReconciliationServiceTestes
{
    public static void Executar()
    {
        DeveDetectarPagamentoDoBancoAusenteNoHubSemReanalisarOsJaCasados();
        DeveDetectarPagamentoPendenteQueJaExisteNoHub();
        DeveDetectarNoBancoAusenteNoSantander();
        DeveDistinguirPagamentosDeMesmoValorPorNome();
        DeveNaoCasarSomentePorValorQuandoFavorecidosDiferem();
        DeveAvisarQuandoNaoHaMemoriaBancariaDaEmpresa();
    }

    private static void DeveDetectarPagamentoDoBancoAusenteNoHubSemReanalisarOsJaCasados()
    {
        var data = new DateTime(2026, 8, 26);
        var hub = Enumerable.Range(1, 5)
            .Select(i => Hub(i, $"Fornecedor {i}", 100m * i, "ADM", "No Banco", data))
            .ToList();

        var banco = Enumerable.Range(1, 5)
            .Select(i => Banco($"Fornecedor {i}", 100m * i, data, i))
            .Append(Banco("Fornecedor Extra", 999m, data, 99))
            .ToList();

        var result = OpexPaymentReconciliationService.Conferir(
            data,
            hub,
            new[] { Memoria("Administradora", data, banco), Memoria("Corretora", data, Array.Empty<SantanderCommitmentItem>(), 0) });

        Assert(result.Administradora.CorrespondenciasNoBanco == 5, "os cinco No Banco devem casar uma única vez");
        Assert(result.Administradora.Divergencias.Count(i => i.Tipo == OpexPaymentReconciliationIssueKind.SantanderAusenteNoHub) == 1,
            "apenas o sexto pagamento deve faltar no HUB");
        Assert(!result.Administradora.Divergencias.Any(i => i.Tipo == OpexPaymentReconciliationIssueKind.HubNoBancoAusenteNoSantander),
            "nenhum dos cinco pagamentos válidos pode ser apontado como ausente no Santander");
    }

    private static void DeveDetectarPagamentoPendenteQueJaExisteNoHub()
    {
        var data = new DateTime(2026, 8, 26);
        var hub = new List<PrevisaoPagamento>
        {
            Hub(1, "Fornecedor Confirmado", 100m, "ADM", "No Banco", data),
            Hub(2, "Fornecedor Pendente", 250m, "ADM", "Pendente", data)
        };

        var banco = new[]
        {
            Banco("Fornecedor Confirmado", 100m, data, 1),
            Banco("Fornecedor Pendente", 250m, data, 2)
        };

        var result = OpexPaymentReconciliationService.Conferir(
            data,
            hub,
            new[] { Memoria("Administradora", data, banco), Memoria("Corretora", data, Array.Empty<SantanderCommitmentItem>(), 0) });

        var statusIssue = result.Administradora.Divergencias.Single(i => i.Tipo == OpexPaymentReconciliationIssueKind.StatusHubDivergente);
        Assert(statusIssue.StatusHub == "Pendente", "o status Pendente deve ser exposto");
        Assert(!result.Administradora.Divergencias.Any(i => i.Tipo == OpexPaymentReconciliationIssueKind.SantanderAusenteNoHub),
            "pagamento pendente existente no HUB não pode ser tratado como ausente");
    }

    private static void DeveDetectarNoBancoAusenteNoSantander()
    {
        var data = new DateTime(2026, 8, 26);
        var hub = new[]
        {
            Hub(1, "Fornecedor Presente", 100m, "ADM", "No Banco", data),
            Hub(2, "Fornecedor Ausente", 200m, "ADM", "No Banco", data)
        };

        var banco = new[] { Banco("Fornecedor Presente", 100m, data, 1) };
        var result = OpexPaymentReconciliationService.Conferir(
            data,
            hub,
            new[] { Memoria("Administradora", data, banco), Memoria("Corretora", data, Array.Empty<SantanderCommitmentItem>(), 0) });

        var issue = result.Administradora.Divergencias.Single(i => i.Tipo == OpexPaymentReconciliationIssueKind.HubNoBancoAusenteNoSantander);
        Assert(issue.Fornecedor == "Fornecedor Ausente", "deve identificar qual No Banco está faltando no Santander");
    }

    private static void DeveDistinguirPagamentosDeMesmoValorPorNome()
    {
        var data = new DateTime(2026, 8, 26);
        var hub = new[]
        {
            Hub(1, "Alpha Serviços Ltda", 500m, "ADM", "No Banco", data),
            Hub(2, "Beta Comércio Ltda", 500m, "ADM", "No Banco", data)
        };

        var banco = new[] { Banco("BETA COMERCIO", 500m, data, 2) };
        var result = OpexPaymentReconciliationService.Conferir(
            data,
            hub,
            new[] { Memoria("Administradora", data, banco), Memoria("Corretora", data, Array.Empty<SantanderCommitmentItem>(), 0) });

        var issue = result.Administradora.Divergencias.Single(i => i.Tipo == OpexPaymentReconciliationIssueKind.HubNoBancoAusenteNoSantander);
        Assert(issue.Fornecedor == "Alpha Serviços Ltda", "afinidade de nome deve preservar o fornecedor correto quando valores se repetem");
    }

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
    private static void DeveAvisarQuandoNaoHaMemoriaBancariaDaEmpresa()
    {
        var data = new DateTime(2026, 8, 26);
        var hub = new[] { Hub(1, "Fornecedor", 100m, "COR", "No Banco", data) };
        var result = OpexPaymentReconciliationService.Conferir(
            data,
            hub,
            new[] { Memoria("Administradora", data, Array.Empty<SantanderCommitmentItem>(), 0) });

        Assert(!result.Corretora.DadosBancoDisponiveis, "COR deve ficar sem cobertura quando não há snapshot");
        Assert(result.Corretora.Divergencias.Single().Tipo == OpexPaymentReconciliationIssueKind.DadosBancoIndisponiveis,
            "ausência de memória não pode virar falso pagamento ausente no banco");
    }

    private static PrevisaoPagamento Hub(int id, string nome, decimal valor, string empresa, string status, DateTime data)
    {
        return new PrevisaoPagamento
        {
            Id = id,
            CodigoFornecedor = id,
            NomeFornecedor = nome,
            Natureza = 1000 + id,
            TipoPagamento = 1,
            Valor = valor,
            DataPagamento = data,
            Status = status,
            Empresa = empresa
        };
    }

    private static SantanderCommitmentItem Banco(string favorecido, decimal valor, DateTime data, int numero)
    {
        return new SantanderCommitmentItem(
            data.ToString("dd/MM/yyyy"),
            favorecido,
            numero.ToString(),
            numero.ToString(),
            valor.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
            valor,
            "PIX",
            "Agendado",
            "Internet Banking");
    }

    private static SantanderCommitmentMemoryEntry Memoria(
        string contexto,
        DateTime data,
        IEnumerable<SantanderCommitmentItem> pagamentos,
        int? total = null)
    {
        var rows = pagamentos.ToList();
        var isAdm = contexto.Equals("Administradora", StringComparison.OrdinalIgnoreCase);
        return new SantanderCommitmentMemoryEntry(
            "Santander",
            contexto,
            isAdm ? "0033-3409-004902845301" : "0033-4268-004905078983",
            isAdm ? "POSITIVA ADMINISTRADORA DE BENEFÍCIOS LTDA" : "POSITIVA CORRETORA DE SEGUROS LTDA",
            DateTime.Now,
            data.ToString("dd/MM/yyyy"),
            data.ToString("dd/MM/yyyy"),
            "Todos",
            total ?? rows.Count,
            rows.Sum(p => p.ValorNumerico ?? 0m).ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
            rows.Sum(p => p.ValorNumerico ?? 0m),
            rows);
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste de Conferir Pagamentos: {scenario}.");
    }
}
