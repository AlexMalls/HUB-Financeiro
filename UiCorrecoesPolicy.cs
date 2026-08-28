namespace HubFinanceiro;

public static class UiCorrecoesPolicy
{
    public const bool IgnorarClientesCanceladosPorPadrao = true;
    public const double LarguraMinimaRegistrarPagamento = 195d;
    public const double LarguraConferirPagamentos = 185d;

    // A lista de fornecedores do Envio de E-mails replica a geometria visual da O.P.E.X.
    public const double AlturaCabecalhoFornecedorEmail = 45d;
    public const double AlturaLinhaFornecedorEmail = 38d;
    public const string CabecalhoFornecedorEmail = "Fornecedor";
    public const string CabecalhoEmailFornecedor = "E-mail";
    public const string FundoTabelaFornecedorEmail = "#99252526";
    public const string FundoCabecalhoFornecedorEmail = "#992A2A2D";
    public const string CorSeparadorFornecedorEmail = "#2D2D30";
    public const double OpacidadeSeparadorFornecedorEmail = 0.3d;

    public static List<Fornecedor> FiltrarFornecedoresEmail(IEnumerable<Fornecedor> fornecedores, string? termo)
    {
        termo = termo?.Trim() ?? string.Empty;

        return fornecedores
            .Where(f => f.Ativo && FornecedorPodeReceberEmail(f))
            .Where(f => string.IsNullOrWhiteSpace(termo)
                || f.Nome.Contains(termo, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(f => f.Nome)
            .ToList();
    }

    public static bool FornecedorPodeReceberEmail(Fornecedor fornecedor)
    {
        return fornecedor != null && !string.IsNullOrWhiteSpace(fornecedor.Email);
    }
}
