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

        Testar(testes, "Caso Ana Paula reconhece inclusão proporcional de 16 dias", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoInclusaoProporcional(
                dataInicio: new DateTime(2026, 6, 15),
                valorFatura: 524.71m,
                valorOver: 983.84m);

            RegraAnaliseResultado r = new RegraInclusaoProporcionalVigencia15().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.Explicada &&
                   r.ExplicaDivergencia &&
                   r.DadosUtilizados.Contains("16", StringComparison.OrdinalIgnoreCase) &&
                   r.DadosUtilizados.Contains("524,71", StringComparison.OrdinalIgnoreCase);
        });

        Testar(testes, "Inclusão no dia 15 com valor proporcional incorreto permanece divergência", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoInclusaoProporcional(
                dataInicio: new DateTime(2026, 6, 15),
                valorFatura: 500m,
                valorOver: 983.84m);

            RegraAnaliseResultado r = new RegraInclusaoProporcionalVigencia15().Avaliar(contexto);
            return r.Resultado == RegraAnaliseStatus.EvidenciaEncontrada &&
                   !r.ExplicaDivergencia;
        });

        Testar(testes, "Inclusão proporcional de Ana Paula termina como Compatível", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoInclusaoProporcional(
                dataInicio: new DateTime(2026, 6, 15),
                valorFatura: 524.71m,
                valorOver: 983.84m);
            ComparacaoPrincipalResultado comparacao = contexto.Comparacao;

            var arquivo = new FaturaBradescoArquivo
            {
                NomeArquivo = "fatura-junho.pdf",
                Competencia = new DateTime(2026, 6, 1)
            };
            var subfatura = new FaturaBradescoSubfatura
            {
                Numero = 2,
                Entidade = "CONSELHO FEDERAL"
            };
            subfatura.Beneficiarios.Add(new FaturaBradescoBeneficiario
            {
                Certificado = "0000067/00",
                Nome = "ANA PAULA GARCIA PAIVA",
                DataInicio = new DateTime(2026, 6, 15),
                Plano = "NS01"
            });
            arquivo.Subfaturas.Add(subfatura);

            AnaliseFinalDiagnostico diagnostico = new AnaliseFinalService().Gerar(
                new ComparacaoPrincipalDiagnostico
                {
                    CompetenciaAnalisada = new DateTime(2026, 6, 1),
                    Resultados = new[] { comparacao }
                },
                new LancamentosConsolidacaoDiagnostico
                {
                    Composicoes = new[] { contexto.Composicao! }
                },
                new ContextoTemporalDiagnostico
                {
                    CompetenciaAnalisada = new DateTime(2026, 6, 1)
                },
                new[] { arquivo },
                new OverArquivo
                {
                    NomeArquivo = "Over 062026.xlsx",
                    Competencia = new DateTime(2026, 6, 1)
                });

            AnaliseFinalResultado resultado = diagnostico.Resultados.Single();
            return resultado.Status == AnaliseFinalStatus.Compativel &&
                   resultado.RegraExplicativa.Contains("Inclusão proporcional por vigência 15", StringComparison.OrdinalIgnoreCase);
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

        Testar(testes, "Devolução proporcional mantém Divergência e calcula 17 dias", () =>
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
            return !r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.RevisaoManual &&
                   r.Justificativa.Contains("17 dias", StringComparison.OrdinalIgnoreCase) &&
                   r.Justificativa.Contains("permanece como Divergência", StringComparison.OrdinalIgnoreCase);
        });

        Testar(testes, "Caso Amanda exibe estimativa de 15 dias sem retirar a Divergência", () =>
        {
            var comparacao = new ComparacaoPrincipalResultado
            {
                IdResultado = "T-AMANDA",
                Certificado = "0002728/00",
                NomeFatura = "AMANDA RIBEIRO DE OLIVEIRA",
                Categoria = ComparacaoPrincipalCategoria.ValorMaiorNaFatura,
                ValorFatura = 3923.69m,
                ValorOverComparavel = 40.52m,
                DiferencaFaturaMenosOver = 3883.17m
            };

            var contexto = new RegraAnaliseContexto
            {
                CompetenciaAnalisada = new DateTime(2026, 6, 1),
                Comparacao = comparacao,
                DadosFatura = new DadosBeneficiarioFaturaAnalise
                {
                    DataInicio = new DateTime(2024, 2, 15)
                },
                Composicao = new ComposicaoBeneficiario
                {
                    Certificado = comparacao.Certificado,
                    NomeFatura = comparacao.NomeFatura,
                    ComponentesFatura = new[]
                    {
                        new ComponenteFatura
                        {
                            Competencia = new DateTime(2026, 6, 1),
                            Valor = 3923.69m,
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
                            CompetenciaFatura = new DateTime(2026, 8, 1),
                            CompetenciaLancamento = new DateTime(2026, 6, 1),
                            Movimento = "CR",
                            Valor = -1961.84m,
                            Arquivo = "fatura-agosto.pdf",
                            PaginaPdf = 5
                        }
                    }
                }
            };

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return !r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.RevisaoManual &&
                   r.ValorDevolucao == 1961.84m &&
                   r.DiasEquivalentesDevolucao == 15 &&
                   r.Justificativa.Contains("15 dias", StringComparison.OrdinalIgnoreCase) &&
                   r.Justificativa.Contains("R$ 1.961,84", StringComparison.OrdinalIgnoreCase) &&
                   r.Justificativa.Contains("dentro da tolerância", StringComparison.OrdinalIgnoreCase) &&
                   r.Justificativa.Contains("data de cancelamento", StringComparison.OrdinalIgnoreCase);
        });

        Testar(testes, "Checkbox de cancelados envia valor não proporcional para Atenção", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoCancelamento(
                ignorarClientesCancelados: true,
                valorDevolucaoBradesco: -1000m,
                incluirCancelamentoOver: true,
                dataInicio: new DateTime(2024, 2, 15));

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.RevisaoManual &&
                   r.ValorDevolucao == 1000m &&
                   r.Justificativa.Contains("direcionado para Atenção", StringComparison.OrdinalIgnoreCase);
        });

        Testar(testes, "Checkbox com devolução posterior dispensa cancelamento no Over", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoCancelamento(
                ignorarClientesCancelados: true,
                valorDevolucaoBradesco: -919.81m,
                incluirCancelamentoOver: false);

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.RevisaoManual &&
                   r.ValorDevolucao == 919.81m;
        });

        Testar(testes, "Vigência 15 com devolução de 15 dias fica Compatível", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoCancelamento(
                ignorarClientesCancelados: true,
                valorDevolucaoBradesco: -1961.84m,
                incluirCancelamentoOver: false,
                dataInicio: new DateTime(2024, 2, 15));

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return r.ExplicaDivergencia &&
                   !r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.Explicada &&
                   r.DiasEquivalentesDevolucao == 15 &&
                   r.ValorDevolucao == 1961.84m;
        });

        Testar(testes, "Vigência 15 com devolução de 16 dias fica Compatível", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoCancelamento(
                ignorarClientesCancelados: true,
                valorDevolucaoBradesco: -2092.63m,
                incluirCancelamentoOver: false,
                dataInicio: new DateTime(2024, 2, 15));

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return r.ExplicaDivergencia &&
                   !r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.Explicada &&
                   r.DiasEquivalentesDevolucao == 16 &&
                   r.ValorDevolucao == 2092.63m;
        });

        Testar(testes, "Vigência 15 com devolução de 14 dias permanece em Atenção", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoCancelamento(
                ignorarClientesCancelados: true,
                valorDevolucaoBradesco: -1831.06m,
                incluirCancelamentoOver: false,
                dataInicio: new DateTime(2024, 2, 15));

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.RevisaoManual &&
                   r.DiasEquivalentesDevolucao == 14;
        });

        Testar(testes, "Devolução de 15 dias sem vigência 15 permanece em Atenção", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoCancelamento(
                ignorarClientesCancelados: true,
                valorDevolucaoBradesco: -1961.84m,
                incluirCancelamentoOver: false,
                dataInicio: new DateTime(2024, 2, 10));

            RegraAnaliseResultado r = new RegraDevolucaoProporcionalCancelamento().Avaliar(contexto);
            return r.SinalizaAtencao &&
                   r.Resultado == RegraAnaliseStatus.RevisaoManual;
        });

        Testar(testes, "Cancelamento proporcional de vigência 15 termina como Compatível", () =>
        {
            RegraAnaliseContexto contexto = CriarCasoCancelamento(
                ignorarClientesCancelados: true,
                valorDevolucaoBradesco: -1961.84m,
                incluirCancelamentoOver: false,
                dataInicio: new DateTime(2024, 2, 15));
            ComparacaoPrincipalResultado comparacao = contexto.Comparacao;

            var arquivo = new FaturaBradescoArquivo
            {
                NomeArquivo = "fatura-junho.pdf",
                Competencia = new DateTime(2026, 6, 1)
            };
            var subfatura = new FaturaBradescoSubfatura
            {
                Numero = 20,
                Entidade = "CREMESP"
            };
            subfatura.Beneficiarios.Add(new FaturaBradescoBeneficiario
            {
                Certificado = comparacao.Certificado,
                Nome = comparacao.NomeFatura,
                DataInicio = new DateTime(2024, 2, 15),
                Plano = "NP03"
            });
            arquivo.Subfaturas.Add(subfatura);

            AnaliseFinalDiagnostico diagnostico = new AnaliseFinalService().Gerar(
                new ComparacaoPrincipalDiagnostico
                {
                    CompetenciaAnalisada = new DateTime(2026, 6, 1),
                    Resultados = new[] { comparacao }
                },
                new LancamentosConsolidacaoDiagnostico
                {
                    Composicoes = new[] { contexto.Composicao! }
                },
                new ContextoTemporalDiagnostico
                {
                    CompetenciaAnalisada = new DateTime(2026, 6, 1),
                    Resultados = new[] { contexto.ContextoTemporal! }
                },
                new[] { arquivo },
                new OverArquivo
                {
                    NomeArquivo = "Over 062026.xlsx",
                    Competencia = new DateTime(2026, 6, 1)
                },
                ignorarClientesCancelados: true);

            AnaliseFinalResultado resultado = diagnostico.Resultados.Single();
            return resultado.Status == AnaliseFinalStatus.Compativel &&
                   resultado.RegraExplicativa.Contains("Devolução proporcional por cancelamento", StringComparison.OrdinalIgnoreCase);
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

    private static RegraAnaliseContexto CriarCasoCancelamento(
        bool ignorarClientesCancelados,
        decimal valorDevolucaoBradesco,
        bool incluirCancelamentoOver,
        DateTime? dataInicio = null)
    {
        var comparacao = new ComparacaoPrincipalResultado
        {
            IdResultado = "T-CANCELADO",
            Certificado = "0002728/00",
            NomeFatura = "CLIENTE CANCELADA",
            Categoria = ComparacaoPrincipalCategoria.ValorMaiorNaFatura,
            ValorFatura = 3923.69m,
            ValorOverComparavel = 40.52m,
            DiferencaFaturaMenosOver = 3883.17m
        };

        var componentesOver = incluirCancelamentoOver
            ? new[]
            {
                new ComponenteOver
                {
                    NumeroLinha = 3825,
                    Evento = "007",
                    Descricao = "DESC. CANCELAMENTO - DIAS NAO UTILIZADOS",
                    ValorNET = -3792.90m,
                    Natureza = "Evento 007"
                }
            }
            : Array.Empty<ComponenteOver>();

        return new RegraAnaliseContexto
        {
            CompetenciaAnalisada = new DateTime(2026, 6, 1),
            IgnorarClientesCancelados = ignorarClientesCancelados,
            Comparacao = comparacao,
            DadosFatura = dataInicio.HasValue
                ? new DadosBeneficiarioFaturaAnalise
                {
                    Certificado = comparacao.Certificado,
                    Nome = comparacao.NomeFatura,
                    DataInicio = dataInicio
                }
                : null,
            Composicao = new ComposicaoBeneficiario
            {
                Certificado = comparacao.Certificado,
                NomeFatura = comparacao.NomeFatura,
                ComponentesFatura = new[]
                {
                    new ComponenteFatura
                    {
                        Competencia = new DateTime(2026, 6, 1),
                        Valor = 3923.69m,
                        ConsiderarNoComparavel = true
                    }
                },
                ComponentesOver = componentesOver
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
                        CompetenciaFatura = new DateTime(2026, 8, 1),
                        CompetenciaLancamento = new DateTime(2026, 6, 1),
                        Movimento = "CR",
                        Valor = valorDevolucaoBradesco,
                        Arquivo = "fatura-agosto.pdf",
                        PaginaPdf = 5
                    }
                }
            }
        };
    }

    private static RegraAnaliseContexto CriarCasoInclusaoProporcional(
        DateTime dataInicio,
        decimal valorFatura,
        decimal valorOver)
    {
        var comparacao = new ComparacaoPrincipalResultado
        {
            IdResultado = "T-ANA-PAULA",
            Certificado = "0000067/00",
            NomeFatura = "ANA PAULA GARCIA PAIVA",
            NomeOver = "ANA PAULA GARCIA PAIVA",
            Categoria = ComparacaoPrincipalCategoria.ValorMaiorNoOver,
            ValorFatura = valorFatura,
            ValorOverComparavel = valorOver,
            DiferencaFaturaMenosOver = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
                valorFatura - valorOver)
        };

        return new RegraAnaliseContexto
        {
            CompetenciaAnalisada = new DateTime(2026, 6, 1),
            Comparacao = comparacao,
            DadosFatura = new DadosBeneficiarioFaturaAnalise
            {
                Arquivo = "fatura-junho.pdf",
                Subfatura = 2,
                Entidade = "CONSELHO FEDERAL",
                Certificado = comparacao.Certificado,
                Nome = comparacao.NomeFatura,
                DataInicio = dataInicio,
                Plano = "NS01"
            },
            Composicao = new ComposicaoBeneficiario
            {
                Certificado = comparacao.Certificado,
                NomeFatura = comparacao.NomeFatura,
                NomeOver = comparacao.NomeOver,
                ComponentesFatura = new[]
                {
                    new ComponenteFatura
                    {
                        PaginaPdf = 2,
                        Subfatura = 2,
                        Entidade = "CONSELHO FEDERAL",
                        Movimento = "IM",
                        Competencia = new DateTime(2026, 6, 1),
                        Plano = "NS01",
                        Valor = valorFatura,
                        ConsiderarNoComparavel = true
                    }
                },
                ComponentesOver = new[]
                {
                    new ComponenteOver
                    {
                        NumeroLinha = 9161,
                        Competencia = new DateTime(2026, 6, 1),
                        Evento = "0070",
                        Descricao = "BRADESCO SAÚDE NACIONAL II Q CA R3 13",
                        ValorNET = valorOver,
                        ConsiderarNoNETComparavel = true
                    }
                }
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
