using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public sealed class ContextoTemporalTesteResultado
{
    public string Nome { get; init; } = string.Empty;
    public bool Sucesso { get; init; }
    public string Detalhe { get; init; } = string.Empty;
}

public static class ContextoTemporalServiceTestes
{
    public static IReadOnlyList<ContextoTemporalTesteResultado> Executar()
    {
        var testes = new List<ContextoTemporalTesteResultado>();

        Executar(testes, "retroativo futuro reconcilia diferença", () =>
        {
            ComparacaoPrincipalDiagnostico principal = Principal(ComparacaoPrincipalCategoria.ValorMaiorNoOver, 90m, 100m, -10m);
            FaturaBradescoArquivo agosto = Contexto(new DateTime(2026, 8, 1), "IR", new DateTime(2026, 7, 1), 10m);
            FaturaBradescoArquivo setembro = Contexto(new DateTime(2026, 9, 1), "", new DateTime(2026, 9, 1), 100m);
            ContextoTemporalResultado r = new ContextoTemporalService().Analisar(principal, new[] { agosto }, new[] { setembro }).Resultados.Single();
            return r.Explicada && r.Status == ContextoTemporalStatus.ExplicadaPorInclusao && r.ValorAjustesContexto == 10m;
        });

        Executar(testes, "cancelamento futuro reconcilia diferença", () =>
        {
            ComparacaoPrincipalDiagnostico principal = Principal(ComparacaoPrincipalCategoria.ValorMaiorNaFatura, 100m, 90m, 10m);
            FaturaBradescoArquivo agosto = Contexto(new DateTime(2026, 8, 1), "CR", new DateTime(2026, 7, 1), -10m);
            FaturaBradescoArquivo setembro = Contexto(new DateTime(2026, 9, 1), "", new DateTime(2026, 9, 1), 100m);
            ContextoTemporalResultado r = new ContextoTemporalService().Analisar(principal, new[] { agosto }, new[] { setembro }).Resultados.Single();
            return r.Explicada && r.Status == ContextoTemporalStatus.ExplicadaPorCancelamento;
        });

        Executar(testes, "inclusão posterior explica ausência na fatura", () =>
        {
            ComparacaoPrincipalDiagnostico principal = Principal(ComparacaoPrincipalCategoria.NaoEncontradoNaFatura, 0m, 100m, -100m);
            FaturaBradescoArquivo agosto = Contexto(new DateTime(2026, 8, 1), "IM", new DateTime(2026, 8, 1), 100m, new DateTime(2026, 8, 1));
            FaturaBradescoArquivo setembro = Contexto(new DateTime(2026, 9, 1), "", new DateTime(2026, 9, 1), 100m, new DateTime(2026, 8, 1));
            ContextoTemporalResultado r = new ContextoTemporalService().Analisar(principal, new[] { agosto }, new[] { setembro }).Resultados.Single();
            return r.Explicada && r.Status == ContextoTemporalStatus.ExplicadaPorInclusao;
        });

        Executar(testes, "contexto com valor diferente não elimina divergência", () =>
        {
            ComparacaoPrincipalDiagnostico principal = Principal(ComparacaoPrincipalCategoria.ValorMaiorNaFatura, 100m, 90m, 10m);
            FaturaBradescoArquivo agosto = Contexto(new DateTime(2026, 8, 1), "CR", new DateTime(2026, 7, 1), -5m);
            FaturaBradescoArquivo setembro = Contexto(new DateTime(2026, 9, 1), "", new DateTime(2026, 9, 1), 100m);
            ContextoTemporalResultado r = new ContextoTemporalService().Analisar(principal, new[] { agosto }, new[] { setembro }).Resultados.Single();
            return !r.Explicada && r.DivergenciaPermanece && r.Status == ContextoTemporalStatus.ContextoEncontradoSemJustificativa && r.ValorAjustesContexto == -5m && r.DiferencaResidual == 5m;
        });

        Executar(testes, "valor compatível não entra no contexto", () =>
        {
            ComparacaoPrincipalDiagnostico principal = Principal(ComparacaoPrincipalCategoria.EncontradoValorCompativel, 100m, 100m, 0m);
            FaturaBradescoArquivo agosto = Contexto(new DateTime(2026, 8, 1), "IR", new DateTime(2026, 7, 1), 10m);
            FaturaBradescoArquivo setembro = Contexto(new DateTime(2026, 9, 1), "", new DateTime(2026, 9, 1), 100m);
            ContextoTemporalDiagnostico d = new ContextoTemporalService().Analisar(principal, new[] { agosto }, new[] { setembro });
            return d.Resultados.Count == 0;
        });

        Executar(testes, "contexto não altera valores originais", () =>
        {
            ComparacaoPrincipalDiagnostico principal = Principal(ComparacaoPrincipalCategoria.ValorMaiorNaFatura, 100m, 90m, 10m);
            FaturaBradescoArquivo agosto = Contexto(new DateTime(2026, 8, 1), "CR", new DateTime(2026, 7, 1), -10m);
            FaturaBradescoArquivo setembro = Contexto(new DateTime(2026, 9, 1), "", new DateTime(2026, 9, 1), 100m);
            ContextoTemporalResultado r = new ContextoTemporalService().Analisar(principal, new[] { agosto }, new[] { setembro }).Resultados.Single();
            return r.ComparacaoOriginal.ValorFatura == 100m && r.ComparacaoOriginal.ValorOverComparavel == 90m && r.ComparacaoOriginal.DiferencaFaturaMenosOver == 10m;
        });

        Executar(testes, "histórico legado reconstrói fatura líquida e diferença residual", () =>
        {
            DateTime competencia = new(2026, 6, 1);
            var resultadoLegado = new AnaliseFinalResultado
            {
                Competencia = competencia,
                ValorFatura = 3923.69m,
                ValorOver = 40.52m,
                Diferenca = 3883.17m,
                // VersaoCalculo 0 representa o formato exibido na imagem do erro.
                ContextoTemporal = new ContextoTemporalResultado
                {
                    Evidencias = new[]
                    {
                        Evidencia(new DateTime(2026, 7, 1), new DateTime(2026, 7, 1), "", 3923.69m),
                        Evidencia(new DateTime(2026, 8, 1), new DateTime(2026, 8, 1), "", 3923.69m),
                        Evidencia(new DateTime(2026, 8, 1), new DateTime(2026, 8, 1), "CM", -3923.69m),
                        Evidencia(new DateTime(2026, 8, 1), new DateTime(2026, 7, 1), "CR", -3923.69m),
                        Evidencia(new DateTime(2026, 8, 1), competencia, "CR", -1961.84m)
                    }
                }
            };

            AnaliseFaturasVisaoFinanceira visao = AnaliseFaturasVisaoFinanceiraService.Calcular(resultadoLegado);
            return visao.ReconstruidaDeHistoricoLegado &&
                   visao.AjusteContexto == -1961.84m &&
                   visao.ValorFaturaLiquida == 1961.85m &&
                   visao.DiferencaResidual == 1921.33m;
        });

        return testes;
    }

    private static void Executar(List<ContextoTemporalTesteResultado> testes, string nome, Func<bool> acao)
    {
        try
        {
            bool ok = acao();
            testes.Add(new ContextoTemporalTesteResultado { Nome = nome, Sucesso = ok, Detalhe = ok ? "OK" : "resultado inesperado" });
        }
        catch (Exception ex)
        {
            testes.Add(new ContextoTemporalTesteResultado { Nome = nome, Sucesso = false, Detalhe = ex.Message });
        }
    }

    private static ComparacaoPrincipalDiagnostico Principal(ComparacaoPrincipalCategoria categoria, decimal fatura, decimal over, decimal diferenca)
        => new()
        {
            CompetenciaAnalisada = new DateTime(2026, 7, 1),
            Resultados = new[]
            {
                new ComparacaoPrincipalResultado
                {
                    IdResultado = "T1",
                    Certificado = "0000004/00",
                    NomeFatura = categoria == ComparacaoPrincipalCategoria.NaoEncontradoNaFatura ? string.Empty : "CRISTINA FREIRE WEFFORT",
                    NomeOver = "CRISTINA FREIRE WEFFORT",
                    Categoria = categoria,
                    StatusVinculo = categoria is ComparacaoPrincipalCategoria.NaoEncontradoNaFatura or ComparacaoPrincipalCategoria.NaoEncontradoNoOver
                        ? VinculoBeneficiarioStatus.NaoEncontrado
                        : VinculoBeneficiarioStatus.EncontradoUnico,
                    ValorFatura = fatura,
                    ValorOverComparavel = over,
                    DiferencaFaturaMenosOver = diferenca
                }
            }
        };

    private static FaturaBradescoArquivo Contexto(DateTime competenciaFatura, string movimento, DateTime competenciaLancamento, decimal valor, DateTime? inicio = null)
    {
        var arquivo = new FaturaBradescoArquivo { NomeArquivo = $"fat_{competenciaFatura:yyyyMM}.pdf", Competencia = competenciaFatura, Apolice = "0005249" };
        var sub = new FaturaBradescoSubfatura { Numero = 1, Entidade = "TESTE" };
        var ben = new FaturaBradescoBeneficiario { Certificado = "0000004/00", Nome = "CRISTINA FREIRE WEFFORT", Plano = "TQSD", DataInicio = inicio ?? new DateTime(2024, 1, 1) };
        ben.Lancamentos.Add(new FaturaBradescoLancamento { PaginaPdf = 5, Movimento = movimento, Competencia = competenciaLancamento, Valor = valor, DataInicio = ben.DataInicio });
        sub.Beneficiarios.Add(ben);
        arquivo.Subfaturas.Add(sub);
        return arquivo;
    }

    private static ContextoTemporalEvidencia Evidencia(
        DateTime competenciaFatura,
        DateTime competenciaLancamento,
        string movimento,
        decimal valor)
        => new()
        {
            CompetenciaFatura = competenciaFatura,
            CompetenciaLancamento = competenciaLancamento,
            Movimento = movimento,
            Valor = valor,
            Arquivo = $"fat_{competenciaFatura:yyyyMM}.pdf",
            PaginaPdf = 5,
            Subfatura = 20,
            Entidade = "CREMESP"
        };
}
