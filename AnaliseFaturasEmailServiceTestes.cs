using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public sealed record AnaliseFaturasEmailTesteResultado(string Nome, bool Sucesso, string Detalhe);

public static class AnaliseFaturasEmailServiceTestes
{
    public static IReadOnlyList<AnaliseFaturasEmailTesteResultado> Executar()
    {
        var testes = new List<AnaliseFaturasEmailTesteResultado>();

        Testar(testes, "Seleciona somente Divergências sem explicação", () =>
        {
            var resultados = new[]
            {
                Criar(AnaliseFinalStatus.DivergenciaPendente, ""),
                Criar(AnaliseFinalStatus.Ambiguo, "   "),
                Criar(AnaliseFinalStatus.DivergenciaPendente, "Remissão"),
                Criar(AnaliseFinalStatus.Atencao, ""),
                Criar(AnaliseFinalStatus.Compativel, "")
            };

            IReadOnlyList<AnaliseFinalResultado> selecionados =
                AnaliseFaturasEmailService.SelecionarDivergenciasSemExplicacao(resultados);

            return selecionados.Count == 2 &&
                   selecionados.All(x => string.IsNullOrWhiteSpace(x.JustificativaManual));
        });

        Testar(testes, "Assunto apresenta Protheus e competência", () =>
        {
            string assunto = AnaliseFaturasEmailService.CriarAssunto(
                "Valor maior no Over",
                new DateTime(2026, 6, 1));

            return assunto.Contains("Valor maior no Protheus", StringComparison.Ordinal) &&
                   !assunto.Contains("Over", StringComparison.OrdinalIgnoreCase) &&
                   assunto.Contains("06/2026", StringComparison.Ordinal);
        });

        Testar(testes, "Saudação da manhã", () =>
        {
            string html = AnaliseFaturasEmailService.CriarCorpoHtml(
                "Não encontrado na fatura",
                new DateTime(2026, 6, 1),
                new DateTime(2026, 8, 24, 9, 30, 0),
                "<div>ASSINATURA</div>");

            return html.Contains("Prezados, bom dia.", StringComparison.Ordinal) &&
                   html.Contains("ASSINATURA", StringComparison.Ordinal);
        });

        Testar(testes, "Saudação da tarde", () =>
        {
            string html = AnaliseFaturasEmailService.CriarCorpoHtml(
                "Não encontrado no Over",
                new DateTime(2026, 6, 1),
                new DateTime(2026, 8, 24, 15, 0, 0),
                "<div>ASSINATURA</div>");

            return html.Contains("Prezados, boa tarde.", StringComparison.Ordinal) &&
                   html.Contains("Não encontrado no Protheus", StringComparison.Ordinal) &&
                   !html.Contains("Over", StringComparison.OrdinalIgnoreCase);
        });

        return testes;
    }

    private static AnaliseFinalResultado Criar(AnaliseFinalStatus status, string explicacao)
        => new()
        {
            Status = status,
            JustificativaManual = explicacao
        };

    private static void Testar(
        List<AnaliseFaturasEmailTesteResultado> testes,
        string nome,
        Func<bool> acao)
    {
        try
        {
            bool sucesso = acao();
            testes.Add(new AnaliseFaturasEmailTesteResultado(
                nome,
                sucesso,
                sucesso ? "OK" : "Resultado inesperado"));
        }
        catch (Exception ex)
        {
            testes.Add(new AnaliseFaturasEmailTesteResultado(nome, false, ex.Message));
        }
    }
}
