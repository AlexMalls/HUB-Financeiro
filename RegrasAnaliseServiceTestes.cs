using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public sealed record RegraAnaliseTesteResultado(string Nome, bool Sucesso, string Detalhe);

public static class RegrasAnaliseServiceTestes
{
    public static IReadOnlyList<RegraAnaliseTesteResultado> Executar()
    {
        var testes = new List<RegraAnaliseTesteResultado>();

        Testar(testes, "Devolução exata explica diferença", () =>
        {
            var contexto = CriarContexto(-100m, new ComponenteFatura { Movimento = "DR", Competencia = Mes(), Valor = -100m });
            RegraAnaliseResultado r = new RegraMensalidadesDevolvidas().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.Explicada;
        });

        Testar(testes, "Cancelamento exato explica diferença", () =>
        {
            var contexto = CriarContexto(-250m, new ComponenteFatura { Movimento = "CR", Competencia = Mes(), Valor = -250m });
            RegraAnaliseResultado r = new RegraCancelamentos().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.Explicada;
        });

        Testar(testes, "Inclusão exata explica diferença", () =>
        {
            var contexto = CriarContexto(315.45m, new ComponenteFatura { Movimento = "IM", Competencia = Mes(), Valor = 315.45m });
            RegraAnaliseResultado r = new RegraInclusaoAlteracao().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.Explicada;
        });

        Testar(testes, "Retroativo presente mas diferente vira evidência", () =>
        {
            var contexto = CriarContexto(100m, new ComponenteFatura { Movimento = "IR", Competencia = Mes(), Valor = 80m });
            RegraAnaliseResultado r = new RegraRetroativos().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.EvidenciaEncontrada;
        });

        Testar(testes, "Contexto de vigência posterior explica", () =>
        {
            var contexto = CriarContexto(100m);
            contexto = new RegraAnaliseContexto
            {
                CompetenciaAnalisada = contexto.CompetenciaAnalisada,
                Comparacao = contexto.Comparacao,
                Composicao = contexto.Composicao,
                ContextoTemporal = new ContextoTemporalResultado
                {
                    ComparacaoOriginal = contexto.Comparacao,
                    Status = ContextoTemporalStatus.ExplicadaPorVigenciaPosterior,
                    Explicada = true,
                    DivergenciaPermanece = false,
                    Observacao = "Vigência posterior comprovada."
                }
            };
            RegraAnaliseResultado r = new RegraVigenciaPosterior().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.Explicada;
        });

        Testar(testes, "Recém-nascido nunca é encerrado automaticamente", () =>
        {
            var contexto = CriarContexto(100m);
            contexto = new RegraAnaliseContexto
            {
                CompetenciaAnalisada = contexto.CompetenciaAnalisada,
                Comparacao = contexto.Comparacao,
                Composicao = contexto.Composicao,
                DadosFatura = new DadosBeneficiarioFaturaAnalise
                {
                    DataNascimento = new DateTime(2026, 7, 12),
                    DataInicio = new DateTime(2026, 7, 12)
                }
            };
            RegraAnaliseResultado r = new RegraRecemNascido().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.RevisaoManual && !r.ExplicaDivergencia;
        });

        Testar(testes, "Devolução proporcional sinaliza Atenção e calcula 17 dias", () =>
        {
            var comparacao = new ComparacaoPrincipalResultado
            {
                IdResultado = "T-ATENCAO",
                Certificado = "0001234/00",
                NomeFatura = "ELOISA TESTE",
                Categoria = ComparacaoPrincipalCategoria.NaoEncontradoNoOver,
                ValorFatura = 1256.64m,
                ValorOverComparavel = 0m,
                DiferencaFaturaMenosOver = 1256.64m
            };

            var contexto = new RegraAnaliseContexto
            {
                CompetenciaAnalisada = Mes(),
                Comparacao = comparacao,
                Composicao = new ComposicaoBeneficiario
                {
                    Certificado = comparacao.Certificado,
                    NomeFatura = comparacao.NomeFatura,
                    ComponentesFatura = new[]
                    {
                        new ComponenteFatura
                        {
                            Competencia = Mes(),
                            Valor = 1256.64m,
                            ConsiderarNoComparavel = true
                        }
                    }
                },
                ContextoTemporal = new ContextoTemporalResultado
                {
                    ComparacaoOriginal = comparacao,
                    Status = ContextoTemporalStatus.ContextoEncontradoSemJustificativa,
                    DivergenciaPermanece = true,
                    Evidencias = new[]
                    {
                        new ContextoTemporalEvidencia
                        {
                            CompetenciaFatura = new DateTime(2026, 9, 1),
                            CompetenciaLancamento = Mes(),
                            Movimento = "CR",
                            Valor = -712.09m,
                            Arquivo = "fatura-setembro.pdf",
                            PaginaPdf = 31
                        }
                    }
                }
            };

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.RevisaoManual &&
                   r.Justificativa.Contains("17 dias", StringComparison.OrdinalIgnoreCase);
        });

        Testar(testes, "Competência anterior ignorada não participa das exceções", () =>
        {
            var contexto = CriarContexto(100m, new ComponenteFatura
            {
                Movimento = "IR",
                Competencia = new DateTime(2026, 6, 1),
                Valor = 100m,
                ConsiderarNoComparavel = false
            });

            RegraAnaliseResultado r = new RegraRetroativos().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.NaoAplicavel;
        });

        Testar(testes, "IOF não participa das regras de exceção", () =>
        {
            var contexto = CriarContexto(35.76m);
            var resultados = new RegrasAnaliseService().Avaliar(contexto);
            return resultados.All(x => x.Resultado != RegraAnaliseStatus.Explicada);
        });

        Testar(testes, "Valor compatível não precisa de exceções", () =>
        {
            var comparacao = new ComparacaoPrincipalResultado
            {
                Categoria = ComparacaoPrincipalCategoria.EncontradoValorCompativel,
                DiferencaFaturaMenosOver = 0m
            };
            var contexto = new RegraAnaliseContexto { CompetenciaAnalisada = Mes(), Comparacao = comparacao };
            return new RegrasAnaliseService().Avaliar(contexto).Count == 0;
        });

        return testes;
    }

    private static RegraAnaliseContexto CriarContexto(decimal diferenca, params ComponenteFatura[] componentes)
    {
        var comparacao = new ComparacaoPrincipalResultado
        {
            IdResultado = "T",
            Certificado = "0000004/00",
            NomeFatura = "BENEFICIARIO TESTE",
            Categoria = diferenca > 0m
                ? ComparacaoPrincipalCategoria.ValorMaiorNaFatura
                : ComparacaoPrincipalCategoria.ValorMaiorNoOver,
            DiferencaFaturaMenosOver = diferenca
        };

        return new RegraAnaliseContexto
        {
            CompetenciaAnalisada = Mes(),
            Comparacao = comparacao,
            Composicao = new ComposicaoBeneficiario
            {
                Certificado = "0000004/00",
                NomeFatura = "BENEFICIARIO TESTE",
                ComponentesFatura = componentes.ToList()
            }
        };
    }

    private static DateTime Mes() => new(2026, 7, 1);

    private static void Testar(List<RegraAnaliseTesteResultado> testes, string nome, Func<bool> acao)
    {
        try
        {
            bool ok = acao();
            testes.Add(new RegraAnaliseTesteResultado(nome, ok, ok ? "OK" : "Resultado inesperado"));
        }
        catch (Exception ex)
        {
            testes.Add(new RegraAnaliseTesteResultado(nome, false, ex.Message));
        }
    }
}
