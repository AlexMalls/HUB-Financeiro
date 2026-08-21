using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

/// <summary>
/// Regras financeiras pequenas e explícitas compartilhadas pelas etapas de composição
/// e comparação. Não acessa tela, arquivo nem Excel legado.
/// </summary>
public static class AnaliseFaturasRegrasComparacao
{
    public const decimal ToleranciaComparacaoPrincipal = 0.30m;

    public static bool EhIof(string? evento, string? descricao)
    {
        string ev = (evento ?? string.Empty).Trim();
        string desc = AnaliseFaturasNormalizador.NormalizarNome(descricao);
        return ev == "9001" || desc.Contains("IOF", StringComparison.Ordinal);
    }

    public static bool EhCoparticipacao(string? evento, string? descricao)
    {
        string ev = (evento ?? string.Empty).Trim();
        string desc = AnaliseFaturasNormalizador.NormalizarNome(descricao);

        return ev == "116" ||
               desc.Contains("COPARTICIP", StringComparison.Ordinal) ||
               desc.Contains("CO PARTICIP", StringComparison.Ordinal) ||
               desc.Contains("FATOR MODERADOR", StringComparison.Ordinal) ||
               desc.Contains("FT MODERADOR", StringComparison.Ordinal);
    }

    public static bool ConsiderarNoNetComparavel(
        OverLancamento lancamento,
        bool ignorarCoparticipacao = true)
    {
        if (lancamento == null)
            return false;

        if (EhIof(lancamento.Evento, lancamento.Descricao))
            return false;

        if (ignorarCoparticipacao && EhCoparticipacao(lancamento.Evento, lancamento.Descricao))
            return false;

        return true;
    }

    public static decimal SomarNetComparavel(
        IEnumerable<OverLancamento> lancamentos,
        bool ignorarCoparticipacao = true)
        => ArredondarCentavos((lancamentos ?? Array.Empty<OverLancamento>())
            .Where(x => ConsiderarNoNetComparavel(x, ignorarCoparticipacao))
            .Sum(x => x.ValorNET ?? 0m));


    public static bool ConsiderarCompetenciaFatura(
        DateTime competenciaLancamento,
        DateTime competenciaAnalisada,
        bool ignorarCompetenciasAnteriores = true)
    {
        if (!ignorarCompetenciasAnteriores)
            return true;

        DateTime lancamento = new(competenciaLancamento.Year, competenciaLancamento.Month, 1);
        DateTime analisada = new(competenciaAnalisada.Year, competenciaAnalisada.Month, 1);
        return lancamento >= analisada;
    }

    public static decimal ArredondarCentavos(decimal valor)
        => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static bool ValoresIguaisCentavo(decimal a, decimal b)
        => ArredondarCentavos(a) == ArredondarCentavos(b);

    public static bool DentroToleranciaComparacaoPrincipal(decimal diferenca)
        => Math.Abs(ArredondarCentavos(diferenca)) <= ToleranciaComparacaoPrincipal;
}
