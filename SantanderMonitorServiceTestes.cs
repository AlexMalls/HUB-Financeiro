namespace HubFinanceiro;

public static class SantanderMonitorServiceTestes
{
    public static void Executar()
    {
        DeveAceitarSomenteDominiosSantander();
        DeveRemoverDadosDeSessaoDaUrl();
        DeveOcultarIdentificadoresNoCaminho();
        DeveIdentificarPaginaInicial();
        DeveAceitarSomenteRotulosDeNavegacaoSeguros();
        DeveBloquearSnapshotAnteriorATrocaDeConvenio();
    }

    private static void DeveOcultarIdentificadoresNoCaminho()
    {
        const string original = "https://pj.santandernetibe.com.br/contas/identificador_de_teste_longo/detalhes.xhtml";
        const string expected = "https://pj.santandernetibe.com.br/contas/%7Boculto%7D/detalhes.xhtml";
        var actual = SantanderMonitorService.SanitizeSantanderUrl(original);
        Assert(string.Equals(actual, expected, StringComparison.Ordinal), "ocultação de identificador na rota");
    }

    private static void DeveAceitarSomenteDominiosSantander()
    {
        Assert(SantanderMonitorService.IsSantanderHost("pj.santandernetibe.com.br"), "subdomínio IBE");
        Assert(SantanderMonitorService.IsSantanderHost("www.santander.com.br"), "site Santander");
        Assert(!SantanderMonitorService.IsSantanderHost("santander.com.br.exemplo.test"), "domínio semelhante");
    }

    private static void DeveRemoverDadosDeSessaoDaUrl()
    {
        const string original = "https://pj.santandernetibe.com.br/ibeweb/pages/home/home.xhtml?parametro=valor#secao";
        const string expected = "https://pj.santandernetibe.com.br/ibeweb/pages/home/home.xhtml";
        var actual = SantanderMonitorService.SanitizeSantanderUrl(original);
        Assert(string.Equals(actual, expected, StringComparison.Ordinal), "sanitização da URL");
    }

    private static void DeveIdentificarPaginaInicial()
    {
        const string url = "https://pj.santandernetibe.com.br/ibeweb/pages/home/home.xhtml";
        var page = SantanderMonitorService.InferPage(url, "Internet Banking");
        Assert(string.Equals(page, "Início", StringComparison.Ordinal), "rota da página inicial");
    }

    private static void DeveAceitarSomenteRotulosDeNavegacaoSeguros()
    {
        var safe = SantanderMonitorService.SanitizeNavigationLabel("  Consultar   compromissos  ");
        var account = SantanderMonitorService.SanitizeNavigationLabel("Consultar conta 123456");
        var amount = SantanderMonitorService.SanitizeNavigationLabel("Confirmar pagamento R$ 900,00");
        var unrelated = SantanderMonitorService.SanitizeNavigationLabel("Texto aleatório");
        var potentiallySensitive = SantanderMonitorService.SanitizeNavigationLabel("Pagamento de Pessoa Exemplo");
        var detail = SantanderMonitorService.SanitizeNavigationLabel("Detalhe do Pagamento");
        var receiver = SantanderMonitorService.SanitizeNavigationLabel("Dados do Recebedor");
        var pdf = SantanderMonitorService.SanitizeNavigationLabel("Salvar em PDF");

        Assert(string.Equals(safe, "Consultar compromissos", StringComparison.Ordinal), "rótulo seguro");
        Assert(account == null, "rótulo com identificador");
        Assert(amount == null, "rótulo com valor");
        Assert(unrelated == null, "texto fora da navegação");
        Assert(string.Equals(potentiallySensitive, "Pagamentos", StringComparison.Ordinal), "canonização de rótulo");
        Assert(string.Equals(detail, "Detalhe do pagamento", StringComparison.Ordinal), "contexto do modal");
        Assert(string.Equals(receiver, "Dados do recebedor", StringComparison.Ordinal), "seção segura do modal");
        Assert(string.Equals(pdf, "Salvar em PDF", StringComparison.Ordinal), "ação segura do modal");
    }

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
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste do monitor Santander: {scenario}.");
    }
}
