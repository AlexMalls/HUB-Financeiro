namespace HubFinanceiro;

public static class UiCorrecoesPolicy
{
    public const bool IgnorarClientesCanceladosPorPadrao = true;
    public const double LarguraMinimaRegistrarPagamento = 195d;
    public const double LarguraConferirPagamentos = 185d;
    public const double AlturaCabecalhoFornecedorEmail = 45d;
    public const double AlturaLinhaFornecedorEmail = 38d;
    public const string CabecalhoFornecedorEmail = "Nome do Fornecedor";

    public static List<Fornecedor> FiltrarFornecedoresEmail(IEnumerable<Fornecedor> fornecedores, string? termo)
    {
        termo = termo?.Trim() ?? string.Empty;

        return fornecedores
            .Where(f => f.Ativo)
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
