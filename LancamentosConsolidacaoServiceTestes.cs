using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public sealed record ConsolidacaoTesteResultado(string Nome, bool Sucesso, string Detalhe);

public static class LancamentosConsolidacaoServiceTestes
{
    public static IReadOnlyList<ConsolidacaoTesteResultado> Executar()
    {
        var resultados = new List<ConsolidacaoTesteResultado>();

        Executar(resultados, "Preserva componentes e totais brutos", () =>
        {
            (List<FaturaBradescoArquivo> faturas, OverArquivo over) = CriarBaseSimples();
            ComposicaoBeneficiario comp = new LancamentosConsolidacaoService()
                .CriarDiagnostico(faturas, over).Composicoes.Single();

            return comp.ComponentesFatura.Count == 2 &&
                   comp.ComponentesOver.Count == 3 &&
                   comp.TotalValorFatura == 900m &&
                   comp.TotalPVOver == 900m &&
                   comp.TotalNETBrutoOver == 765m &&
                   comp.TotalIOFNETIgnorado == 17m &&
                   comp.TotalCopartNETIgnorado == 85m &&
                   comp.TotalNETOver == 663m &&
                   comp.TotalOver == 135m;
        });

        Executar(resultados, "Preserva valores negativos", () =>
        {
            (List<FaturaBradescoArquivo> faturas, OverArquivo over) = CriarBaseSimples(valorNegativo: true);
            ComposicaoBeneficiario comp = new LancamentosConsolidacaoService()
                .CriarDiagnostico(faturas, over).Composicoes.Single();

            return comp.ComponentesFatura.Any(x => x.Valor < 0) &&
                   comp.ComponentesOver.Any(x => (x.ValorPV ?? 0) < 0);
        });

        Executar(resultados, "Certificado duplicado é resolvido por nome", () =>
        {
            List<FaturaBradescoArquivo> faturas = CriarFaturasDuplicadas();
            OverArquivo over = CriarOver("952490000004001", "JOAO D AVILA", 100m);
            LancamentosConsolidacaoDiagnostico diag = new LancamentosConsolidacaoService().CriarDiagnostico(faturas, over);

            return diag.Composicoes.Count == 1 &&
                   diag.Composicoes[0].StatusVinculo == VinculoBeneficiarioStatus.EncontradoPorNome &&
                   AnaliseFaturasNormalizador.NormalizarNome(diag.Composicoes[0].NomeFatura) == "JOAO D AVILA";
        });

        Executar(resultados, "Ambiguidade permanece fora da consolidação", () =>
        {
            List<FaturaBradescoArquivo> faturas = CriarFaturasMesmoNomeDuplicado();
            OverArquivo over = CriarOver("952490000004001", "JOAO D AVILA", 100m);
            LancamentosConsolidacaoDiagnostico diag = new LancamentosConsolidacaoService().CriarDiagnostico(faturas, over);

            return diag.Composicoes.Count == 0 && diag.TotalVinculosNaoConsolidados == 1;
        });

        Executar(resultados, "IOF e copart ficam visíveis, mas fora do NET comparável por padrão", () =>
        {
            (List<FaturaBradescoArquivo> faturas, OverArquivo over) = CriarBaseSimples();
            ComposicaoBeneficiario comp = new LancamentosConsolidacaoService()
                .CriarDiagnostico(faturas, over).Composicoes.Single();

            ComponenteOver iof = comp.ComponentesOver.Single(x => x.Evento == "9001");
            ComponenteOver copart = comp.ComponentesOver.Single(x => x.Evento == "116");

            return iof.Natureza == "IOF" &&
                   !iof.ConsiderarNoNETComparavel &&
                   iof.RegraComparacao.Contains("Ignorado", StringComparison.OrdinalIgnoreCase) &&
                   !copart.ConsiderarNoNETComparavel &&
                   copart.RegraComparacao.Contains("coparticipação", StringComparison.OrdinalIgnoreCase) &&
                   copart.Natureza.Contains("Coparticipação", StringComparison.Ordinal) &&
                   comp.TotalNETBrutoOver == 765m &&
                   comp.TotalIOFNETIgnorado == 17m &&
                   comp.TotalCopartNETIgnorado == 85m &&
                   comp.TotalNETOver == 663m;
        });

        Executar(resultados, "competência anterior fica visível mas fora do total comparável", () =>
        {
            (List<FaturaBradescoArquivo> faturas, OverArquivo over) = CriarBaseSimples();
            FaturaBradescoBeneficiario ben = faturas[0].Subfaturas[0].Beneficiarios[0];
            ben.Lancamentos.Add(new FaturaBradescoLancamento
            {
                PaginaPdf = 7,
                Competencia = new DateTime(2026, 6, 1),
                Movimento = "IR",
                Valor = 50m
            });

            ComposicaoBeneficiario comp = new LancamentosConsolidacaoService()
                .CriarDiagnostico(faturas, over).Composicoes.Single();

            ComponenteFatura anterior = comp.ComponentesFatura.Single(x => x.Competencia.Month == 6);
            return !anterior.ConsiderarNoComparavel &&
                   comp.TotalValorFaturaBruto == 950m &&
                   comp.TotalValorFatura == 900m;
        });

        Executar(resultados, "copart pode voltar ao NET quando opção é desmarcada", () =>
        {
            (List<FaturaBradescoArquivo> faturas, OverArquivo over) = CriarBaseSimples();
            ComposicaoBeneficiario comp = new LancamentosConsolidacaoService()
                .CriarDiagnostico(faturas, over, ignorarCoparticipacao: false).Composicoes.Single();

            ComponenteOver copart = comp.ComponentesOver.Single(x => x.Evento == "116");
            return copart.ConsiderarNoNETComparavel &&
                   comp.TotalCopartNETIgnorado == 0m &&
                   comp.TotalNETOver == 748m;
        });

        return resultados;
    }

    private static void Executar(List<ConsolidacaoTesteResultado> resultados, string nome, Func<bool> teste)
    {
        try
        {
            bool ok = teste();
            resultados.Add(new ConsolidacaoTesteResultado(nome, ok, ok ? "OK" : "Resultado inesperado"));
        }
        catch (Exception ex)
        {
            resultados.Add(new ConsolidacaoTesteResultado(nome, false, ex.Message));
        }
    }

    private static (List<FaturaBradescoArquivo>, OverArquivo) CriarBaseSimples(bool valorNegativo = false)
    {
        var arquivo = new FaturaBradescoArquivo { NomeArquivo = "fatura.pdf" };
        var sub = new FaturaBradescoSubfatura { Numero = 1, Entidade = "ENTIDADE" };
        var ben = new FaturaBradescoBeneficiario { Certificado = "0000004/00", Nome = "JOÃO D'ÁVILA" };
        ben.Lancamentos.Add(new FaturaBradescoLancamento
        {
            PaginaPdf = 5,
            PaginaFatura = 4,
            Movimento = string.Empty,
            Competencia = new DateTime(2026, 7, 1),
            Valor = 800m,
            Participacao = 0m
        });
        ben.Lancamentos.Add(new FaturaBradescoLancamento
        {
            PaginaPdf = 5,
            PaginaFatura = 4,
            Movimento = valorNegativo ? "CR" : "RS",
            Competencia = new DateTime(2026, 7, 1),
            Valor = valorNegativo ? -100m : 100m,
            Participacao = 0m
        });
        sub.Beneficiarios.Add(ben);
        arquivo.Subfaturas.Add(sub);

        decimal terceiro = valorNegativo ? -100m : 100m;
        var over = new OverArquivo
        {
            NomeArquivo = "over.xlsx",
            Lancamentos = new List<OverLancamento>
            {
                new() { NumeroLinha = 3, Beneficiario = "JOAO D AVILA", Cartao = "952490000004001", Evento = "0027", Descricao = "PLANO", ValorPV = 780m, ValorNET = 663m, ValorOver = 117m },
                new() { NumeroLinha = 4, Beneficiario = "JOAO D AVILA", Cartao = "952490000004001", Evento = "9001", Descricao = "IOF SEGURADORA", ValorPV = 20m, ValorNET = 17m, ValorOver = 3m },
                new() { NumeroLinha = 5, Beneficiario = "JOAO D AVILA", Cartao = "952490000004001", Evento = "116", Descricao = "Ft Moderador/Co-Participacao", ValorPV = terceiro, ValorNET = valorNegativo ? -85m : 85m, ValorOver = valorNegativo ? -15m : 15m }
            }
        };

        return (new List<FaturaBradescoArquivo> { arquivo }, over);
    }

    private static List<FaturaBradescoArquivo> CriarFaturasDuplicadas()
    {
        var arquivo = new FaturaBradescoArquivo { NomeArquivo = "fatura.pdf" };
        var sub = new FaturaBradescoSubfatura { Numero = 1, Entidade = "ENTIDADE" };
        sub.Beneficiarios.Add(CriarBeneficiario("0000004/00", "JOÃO D'ÁVILA", 100m));
        sub.Beneficiarios.Add(CriarBeneficiario("0000004/00", "MARIA TESTE", 200m));
        arquivo.Subfaturas.Add(sub);
        return new List<FaturaBradescoArquivo> { arquivo };
    }

    private static List<FaturaBradescoArquivo> CriarFaturasMesmoNomeDuplicado()
    {
        var arquivo = new FaturaBradescoArquivo { NomeArquivo = "fatura.pdf" };
        var sub1 = new FaturaBradescoSubfatura { Numero = 1, Entidade = "A" };
        var sub2 = new FaturaBradescoSubfatura { Numero = 2, Entidade = "B" };
        sub1.Beneficiarios.Add(CriarBeneficiario("0000004/00", "JOÃO D'ÁVILA", 100m));
        sub2.Beneficiarios.Add(CriarBeneficiario("0000004/00", "JOAO D AVILA", 100m));
        arquivo.Subfaturas.Add(sub1);
        arquivo.Subfaturas.Add(sub2);
        return new List<FaturaBradescoArquivo> { arquivo };
    }

    private static FaturaBradescoBeneficiario CriarBeneficiario(string cert, string nome, decimal valor)
    {
        var ben = new FaturaBradescoBeneficiario { Certificado = cert, Nome = nome };
        ben.Lancamentos.Add(new FaturaBradescoLancamento
        {
            PaginaPdf = 5,
            Movimento = string.Empty,
            Competencia = new DateTime(2026, 7, 1),
            Valor = valor
        });
        return ben;
    }

    private static OverArquivo CriarOver(string cartao, string nome, decimal valor)
        => new()
        {
            NomeArquivo = "over.xlsx",
            Lancamentos = new List<OverLancamento>
            {
                new() { NumeroLinha = 3, Beneficiario = nome, Cartao = cartao, Evento = "0027", ValorPV = valor, ValorNET = valor, ValorOver = 0m }
            }
        };
}
