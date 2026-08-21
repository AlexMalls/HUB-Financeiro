using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

/// <summary>
/// Visão financeira efetiva de um resultado, após considerar ajustes lançados
/// em faturas posteriores para a competência analisada.
/// </summary>
public sealed class AnaliseFaturasVisaoFinanceira
{
    public decimal? DiferencaOriginal { get; init; }
    public decimal AjusteContexto { get; init; }
    public decimal? ValorFaturaLiquida { get; init; }
    public decimal? DiferencaResidual { get; init; }
    public bool ReconstruidaDeHistoricoLegado { get; init; }

    public bool PossuiAjusteContexto => AjusteContexto != 0m;

    public string CriarResumo(decimal? valorFaturaOriginal, decimal? valorOver)
    {
        if (!PossuiAjusteContexto)
            return string.Empty;

        return
            $"Fatura original: R$ {valorFaturaOriginal.GetValueOrDefault():N2}. " +
            $"Ajustes posteriores da competência: R$ {AjusteContexto:N2}. " +
            $"Fatura líquida: R$ {ValorFaturaLiquida.GetValueOrDefault():N2}. " +
            $"Over comparável: R$ {valorOver.GetValueOrDefault():N2}. " +
            $"Diferença residual: R$ {DiferencaResidual.GetValueOrDefault():N2}.";
    }
}

/// <summary>
/// Centraliza o cálculo exibido em tela e no Excel. Para resultados V3, usa os
/// valores persistidos pelo motor. Para históricos antigos, reconstrói o ajuste
/// exclusivamente a partir das evidências futuras da competência analisada.
/// </summary>
public static class AnaliseFaturasVisaoFinanceiraService
{
    public static AnaliseFaturasVisaoFinanceira Calcular(AnaliseFinalResultado resultado)
    {
        if (resultado == null)
            throw new ArgumentNullException(nameof(resultado));

        bool historicoLegado = resultado.VersaoCalculo < 3;
        decimal ajuste = historicoLegado
            ? CalcularAjusteContexto(resultado.ContextoTemporal?.Evidencias, resultado.Competencia)
            : resultado.AjusteContextoFinanceiro;

        decimal? diferencaOriginal = historicoLegado
            ? resultado.Diferenca ?? Subtrair(resultado.ValorFatura, resultado.ValorOver)
            : resultado.DiferencaOriginal ?? resultado.Diferenca;

        decimal? valorFaturaLiquida = !historicoLegado && resultado.ValorFaturaAjustado.HasValue
            ? resultado.ValorFaturaAjustado
            : Somar(resultado.ValorFatura, ajuste);

        decimal? diferencaResidual = !historicoLegado && resultado.DiferencaResidual.HasValue
            ? resultado.DiferencaResidual
            : Somar(diferencaOriginal, ajuste);

        if (!diferencaResidual.HasValue)
            diferencaResidual = Subtrair(valorFaturaLiquida, resultado.ValorOver);

        return new AnaliseFaturasVisaoFinanceira
        {
            DiferencaOriginal = diferencaOriginal,
            AjusteContexto = ajuste,
            ValorFaturaLiquida = valorFaturaLiquida,
            DiferencaResidual = diferencaResidual,
            ReconstruidaDeHistoricoLegado = historicoLegado && ajuste != 0m
        };
    }

    public static bool EhAjusteContextoAplicado(
        ContextoTemporalEvidencia evidencia,
        DateTime competenciaAnalisada)
    {
        if (evidencia == null)
            throw new ArgumentNullException(nameof(evidencia));

        DateTime competenciaBase = PrimeiroDia(competenciaAnalisada);
        DateTime competenciaFatura = PrimeiroDia(evidencia.CompetenciaFatura);
        DateTime competenciaLancamento = PrimeiroDia(evidencia.CompetenciaLancamento);

        return competenciaFatura > competenciaBase &&
               competenciaLancamento == competenciaBase;
    }

    private static decimal CalcularAjusteContexto(
        IReadOnlyList<ContextoTemporalEvidencia>? evidencias,
        DateTime competenciaAnalisada)
    {
        if (evidencias == null || evidencias.Count == 0)
            return 0m;

        decimal ajuste = evidencias
            .Where(x => EhAjusteContextoAplicado(x, competenciaAnalisada))
            // Históricos antigos podem repetir a mesma evidência. Sem um ID de
            // lançamento, esta é a chave mais conservadora disponível.
            .GroupBy(x => new
            {
                x.CompetenciaFatura,
                x.CompetenciaLancamento,
                x.Subfatura,
                x.Movimento,
                x.Valor,
                x.Entidade,
                x.Arquivo,
                x.PaginaPdf
            })
            .Select(g => g.First().Valor)
            .Sum();

        return AnaliseFaturasRegrasComparacao.ArredondarCentavos(ajuste);
    }

    private static DateTime PrimeiroDia(DateTime data) => new(data.Year, data.Month, 1);

    private static decimal? Somar(decimal? valor, decimal ajuste)
        => valor.HasValue
            ? AnaliseFaturasRegrasComparacao.ArredondarCentavos(valor.Value + ajuste)
            : null;

    private static decimal? Subtrair(decimal? esquerda, decimal? direita)
        => esquerda.HasValue && direita.HasValue
            ? AnaliseFaturasRegrasComparacao.ArredondarCentavos(esquerda.Value - direita.Value)
            : null;
}
