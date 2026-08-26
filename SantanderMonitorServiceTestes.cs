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

        Assert(string.Equals(safe, "Consultar compromissos", StringComparison.Ordinal), "rótulo seguro");
        Assert(account == null, "rótulo com identificador");
        Assert(amount == null, "rótulo com valor");
        Assert(unrelated == null, "texto fora da navegação");
        Assert(string.Equals(potentiallySensitive, "Pagamentos", StringComparison.Ordinal), "canonização de rótulo");
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste do monitor Santander: {scenario}.");
    }
}
