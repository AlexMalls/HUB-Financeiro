namespace HubFinanceiro;

public static class UiCorrecoesTestes
{
    public static void Executar()
    {
        DeveFiltrarFornecedoresAtivosPorNome();
        DeveManterFornecedorSemEmailNaPesquisaMasBloquearSelecao();
        DeveUsarMedidasDaTabelaOpexNaListaDeFornecedores();
        DeveIgnorarClientesCanceladosPorPadrao();
        DeveReservarLarguraSuficienteParaBotoesOpex();
    }

    private static void DeveFiltrarFornecedoresAtivosPorNome()
    {
        var fornecedores = new[]
        {
            new Fornecedor { Nome = "Alpha Serviços", Email = "alpha@teste.com", Ativo = true },
            new Fornecedor { Nome = "Beta Comércio", Email = "beta@teste.com", Ativo = true },
            new Fornecedor { Nome = "Alpha Inativo", Email = "inativo@teste.com", Ativo = false }
        };

        var resultado = UiCorrecoesPolicy.FiltrarFornecedoresEmail(fornecedores, "alpha");

        Assert(resultado.Count == 1, "a pesquisa deve considerar apenas fornecedores ativos");
        Assert(resultado[0].Nome == "Alpha Serviços", "a pesquisa deve filtrar pelo nome do fornecedor");
    }

    private static void DeveManterFornecedorSemEmailNaPesquisaMasBloquearSelecao()
    {
        var semEmail = new Fornecedor { Nome = "Fornecedor sem e-mail", Email = "   ", Ativo = true };
        var resultado = UiCorrecoesPolicy.FiltrarFornecedoresEmail(new[] { semEmail }, string.Empty);

        Assert(resultado.Count == 1, "fornecedor sem e-mail deve continuar visível na lista");
        Assert(!UiCorrecoesPolicy.FornecedorPodeReceberEmail(semEmail), "fornecedor sem e-mail deve ficar bloqueado para seleção");
    }

    private static void DeveUsarMedidasDaTabelaOpexNaListaDeFornecedores()
    {
        Assert(UiCorrecoesPolicy.AlturaCabecalhoFornecedorEmail == 45d,
            "o cabeçalho da lista de fornecedores deve ter a mesma altura da tabela O.P.E.X.");
        Assert(UiCorrecoesPolicy.AlturaLinhaFornecedorEmail == 38d,
            "as linhas da lista de fornecedores devem ter a mesma altura da tabela O.P.E.X.");
        Assert(UiCorrecoesPolicy.CabecalhoFornecedorEmail == "Nome do Fornecedor",
            "a lista deve exibir o cabeçalho Nome do Fornecedor");
    }

    private static void DeveIgnorarClientesCanceladosPorPadrao()
    {
        Assert(UiCorrecoesPolicy.IgnorarClientesCanceladosPorPadrao,
            "Ignorar clientes cancelados deve iniciar marcado");
    }

    private static void DeveReservarLarguraSuficienteParaBotoesOpex()
    {
        Assert(UiCorrecoesPolicy.LarguraMinimaRegistrarPagamento >= 185,
            "Registrar/Alterar Pagamento precisa de largura suficiente para o texto completo");
        Assert(UiCorrecoesPolicy.LarguraConferirPagamentos >= 175,
            "Conferir Pagamentos precisa de largura suficiente para o texto completo");
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste de correções de UI: {scenario}.");
    }
}
