using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public enum AnaliseFinalStatus
{
    Compativel,
    DivergenciaExplicada,
    DivergenciaPendente,
    Atencao,
    Ambiguo
}

public sealed class AnaliseFinalDiagnostico
{
    public DateTime Competencia { get; init; }
    public IReadOnlyList<AnaliseFinalResultado> Resultados { get; init; } = Array.Empty<AnaliseFinalResultado>();

    public int Total => Resultados.Count;
    public int TotalCompativeis => Resultados.Count(x =>
        x.Status is AnaliseFinalStatus.Compativel or AnaliseFinalStatus.DivergenciaExplicada);
    public int TotalExplicadas => Resultados.Count(x => x.Status == AnaliseFinalStatus.DivergenciaExplicada);
    public int TotalPendentes => Resultados.Count(x => x.Status == AnaliseFinalStatus.DivergenciaPendente);
    public int TotalAtencao => Resultados.Count(x => x.Status == AnaliseFinalStatus.Atencao);
    public int TotalAmbiguos => Resultados.Count(x => x.Status == AnaliseFinalStatus.Ambiguo);
    public bool IgnorandoCoparticipacao { get; init; } = true;
    public bool IgnorandoCompetenciasAnteriores { get; init; } = true;
    public bool IgnorandoClientesCancelados { get; init; }
    public decimal ToleranciaFinanceiraUtilizada { get; init; } = AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal;
    public int TotalCompativeisPorTolerancia => Resultados.Count(x => x.CompativelPorToleranciaEfetiva);
    public decimal SomaToleranciaFaturaMaior => AnaliseFaturasRegrasComparacao.ArredondarCentavos(
        Resultados.Where(x => x.CompativelPorToleranciaEfetiva && (x.DiferencaToleranciaEfetiva ?? x.Diferenca ?? 0m) > 0m)
            .Sum(x => x.DiferencaToleranciaEfetiva ?? x.Diferenca ?? 0m));
    public decimal SomaToleranciaOverMaior => AnaliseFaturasRegrasComparacao.ArredondarCentavos(
        Resultados.Where(x => x.CompativelPorToleranciaEfetiva && (x.DiferencaToleranciaEfetiva ?? x.Diferenca ?? 0m) < 0m)
            .Sum(x => Math.Abs(x.DiferencaToleranciaEfetiva ?? x.Diferenca ?? 0m)));
    public decimal SaldoToleranciaLiquido => AnaliseFaturasRegrasComparacao.ArredondarCentavos(
        SomaToleranciaFaturaMaior - SomaToleranciaOverMaior);
}

public sealed class AnaliseFinalResultado
{
    public string IdResultado { get; init; } = string.Empty;
    public string Beneficiario { get; init; } = string.Empty;
    public string Certificado { get; init; } = string.Empty;
    public string TipoDivergencia { get; init; } = string.Empty;
    public decimal? Diferenca { get; init; }

    // V3: significado financeiro explicito.
    // Em analises novas, Diferenca e DiferencaResidual representam o valor efetivo.
    // Historicos antigos desserializam VersaoCalculo = 0 e permanecem em modo legado.
    public decimal? DiferencaOriginal { get; init; }
    public decimal AjusteContextoFinanceiro { get; init; }
    public decimal? DiferencaResidual { get; init; }
    public decimal? ValorFaturaAjustado { get; init; }
    public int VersaoCalculo { get; init; }    public decimal? ValorFatura { get; init; }
    public decimal? ValorOver { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public DateTime Competencia { get; init; }
    public AnaliseFinalStatus Status { get; init; }
    public string RegraExplicativa { get; init; } = string.Empty;
    public string JustificativaFinal { get; init; } = string.Empty;
    // Explicação manual preenchida pelo usuário na tela de resultado.
    // É persistida no histórico e não altera o cálculo/classificação automática.
    public string JustificativaManual { get; set; } = string.Empty;
    public string OrigemFatura { get; init; } = string.Empty;
    public string OrigemOver { get; init; } = string.Empty;
    public ComparacaoPrincipalResultado ComparacaoPrincipal { get; init; } = new();
    public ContextoTemporalResultado? ContextoTemporal { get; init; }
    public IReadOnlyList<RegraAnaliseResultado> RegrasTestadas { get; init; } = Array.Empty<RegraAnaliseResultado>();
    public IReadOnlyList<ComponenteFatura> ComponentesFatura { get; init; } = Array.Empty<ComponenteFatura>();
    // Visão de investigação: todas as ocorrências do beneficiário/certificado nas faturas
    // do mês analisado, com o nome do arquivo preservado. Não altera os cálculos financeiros.
    public IReadOnlyList<LancamentoFaturaInvestigacao> LancamentosFaturaInvestigacao { get; init; } = Array.Empty<LancamentoFaturaInvestigacao>();
    public IReadOnlyList<ComponenteOver> ComponentesOver { get; init; } = Array.Empty<ComponenteOver>();
    public DadosBeneficiarioFaturaAnalise? DadosFatura { get; init; }

    // Pode ser verdadeiro mesmo quando a comparação principal ficou tecnicamente ambígua.
    // Nesse caso, os valores financeiros exibidos nas abas Fatura/Over foram suficientes
    // para comprovar que a diferença está dentro da margem de ±R$ 0,30.
    public bool CompativelPorToleranciaEfetiva { get; init; }
    public decimal? DiferencaToleranciaEfetiva { get; init; }
}

public sealed class LancamentoFaturaInvestigacao
{
    public DateTime CompetenciaFatura { get; init; }
    public string Arquivo { get; init; } = string.Empty;
    public int PaginaPdf { get; init; }
    public int? PaginaFatura { get; init; }
    public int Subfatura { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public string Movimento { get; init; } = string.Empty;
    public DateTime CompetenciaLancamento { get; init; }
    public string Natureza { get; init; } = string.Empty;
    public string Plano { get; init; } = string.Empty;
    public decimal Valor { get; init; }
    public decimal? Participacao { get; init; }
    public string UsoComparacao { get; init; } = string.Empty;
    public string TextoOrigem { get; init; } = string.Empty;
}

/// <summary>
/// Monta o resultado final investigável das Etapas 9 e 10.
/// Não persiste histórico e não usa o Excel antigo como referência.
/// </summary>
public sealed class AnaliseFinalService
{
    private readonly RegrasAnaliseService _regras = new();

    public AnaliseFinalDiagnostico Gerar(
        ComparacaoPrincipalDiagnostico comparacao,
        LancamentosConsolidacaoDiagnostico consolidacao,
        ContextoTemporalDiagnostico contextoTemporal,
        IReadOnlyList<FaturaBradescoArquivo> faturasMesPassado,
        OverArquivo overMesPassado,
        bool ignorarCoparticipacao = true,
        bool ignorarCompetenciasAnteriores = true,
        bool ignorarClientesCancelados = false)
    {
        if (comparacao == null) throw new ArgumentNullException(nameof(comparacao));
        if (consolidacao == null) throw new ArgumentNullException(nameof(consolidacao));
        if (contextoTemporal == null) throw new ArgumentNullException(nameof(contextoTemporal));
        if (faturasMesPassado == null) throw new ArgumentNullException(nameof(faturasMesPassado));
        if (overMesPassado == null) throw new ArgumentNullException(nameof(overMesPassado));

        var resultados = new List<AnaliseFinalResultado>();

        foreach (ComparacaoPrincipalResultado item in comparacao.Resultados)
        {
            ContextoTemporalResultado? contexto = contextoTemporal.Resultados
                .FirstOrDefault(x => string.Equals(
                    x.ComparacaoOriginal.IdResultado,
                    item.IdResultado,
                    StringComparison.OrdinalIgnoreCase));

            ComposicaoBeneficiario? composicao = EncontrarComposicao(consolidacao.Composicoes, item);
            DadosBeneficiarioFaturaAnalise? dadosFatura = EncontrarDadosFatura(faturasMesPassado, item);

            IReadOnlyList<ComponenteFatura> componentesFatura = composicao?.ComponentesFatura
                ?? CriarComponentesFatura(
                    faturasMesPassado,
                    item,
                    comparacao.CompetenciaAnalisada,
                    ignorarCompetenciasAnteriores);
            IReadOnlyList<ComponenteOver> componentesOver = composicao?.ComponentesOver
                ?? CriarComponentesOver(overMesPassado, item, ignorarCoparticipacao);
            IReadOnlyList<LancamentoFaturaInvestigacao> lancamentosFaturaInvestigacao = CriarLancamentosFaturaInvestigacao(
                faturasMesPassado,
                item,
                comparacao.CompetenciaAnalisada,
                ignorarCompetenciasAnteriores);

            var regraContexto = new RegraAnaliseContexto
            {
                CompetenciaAnalisada = comparacao.CompetenciaAnalisada,
                IgnorarClientesCancelados = ignorarClientesCancelados,
                Comparacao = item,
                Composicao = composicao ?? CriarComposicaoVirtual(item, componentesFatura, componentesOver),
                ContextoTemporal = contexto,
                DadosFatura = dadosFatura
            };

            IReadOnlyList<RegraAnaliseResultado> regras = _regras.Avaliar(regraContexto);
            List<RegraAnaliseResultado> explicativas = regras.Where(x => x.ExplicaDivergencia).ToList();
            RegraAnaliseResultado? regraAtencao = regras.FirstOrDefault(x => x.SinalizaAtencao);

            // O resultado final deve refletir os valores financeiros realmente exibidos
            // nas abas de investigação. Isso é especialmente importante quando o vínculo
            // técnico ficou ambíguo: se existe uma composição identificável dos dois lados,
            // ainda podemos medir o impacto financeiro sem fingir que a ambiguidade não existiu.
            decimal? valorFaturaFinal = item.ValorFatura;
            decimal? valorOverFinal = item.ValorOverComparavel;
            decimal? diferencaFinal = item.DiferencaFaturaMenosOver;

            bool valoresObtidosDosComponentes = false;
            if (item.Categoria == ComparacaoPrincipalCategoria.Ambiguo &&
                TentarCalcularValoresComparaveisDosComponentes(
                    componentesFatura,
                    componentesOver,
                    out decimal valorFaturaComponentes,
                    out decimal valorOverComponentes,
                    out decimal diferencaComponentes))
            {
                valorFaturaFinal = valorFaturaComponentes;
                valorOverFinal = valorOverComponentes;
                diferencaFinal = diferencaComponentes;
                valoresObtidosDosComponentes = true;
            }
            else if (!diferencaFinal.HasValue && valorFaturaFinal.HasValue && valorOverFinal.HasValue)
            {
                diferencaFinal = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
                    valorFaturaFinal.Value - valorOverFinal.Value);
            }
            // HUB_DIFERENCA_RESIDUAL_V3
            // A comparacao principal fornece a diferenca ORIGINAL.
            // ContextoTemporalService fornece ajuste e residual.
            // AnaliseFinalService apenas consolida os valores para as telas/historico.
            decimal? diferencaOriginalAntesContexto = diferencaFinal;
            decimal ajusteContextoFinanceiro = contexto?.ValorAjustesContexto ?? 0m;

            bool houveAjusteContextoFinanceiro =
                diferencaOriginalAntesContexto.HasValue &&
                contexto != null &&
                ajusteContextoFinanceiro != 0m;

            if (houveAjusteContextoFinanceiro)
                diferencaFinal = contexto!.DiferencaResidual;

            decimal? diferencaResidualFinal = diferencaFinal;

            decimal? valorFaturaAjustado = valorFaturaFinal.HasValue
                ? AnaliseFaturasRegrasComparacao.ArredondarCentavos(
                    valorFaturaFinal.Value + ajusteContextoFinanceiro)
                : null;

            // A tolerância financeira vale também quando havia ambiguidade técnica, desde
            // que os componentes exibidos permitam formar um total comparável dos dois lados.
            // IOF e demais componentes marcados como ignorados não entram neste cálculo.
            bool compativelPorToleranciaContexto = contexto?.ExplicadaPorTolerancia == true;
            bool compativelPorToleranciaEfetiva =
                item.CompativelPorTolerancia ||
                compativelPorToleranciaContexto ||
                (item.Categoria == ComparacaoPrincipalCategoria.Ambiguo &&
                 valoresObtidosDosComponentes &&
                 diferencaFinal.HasValue &&
                 diferencaFinal.Value != 0m &&
                 AnaliseFaturasRegrasComparacao.DentroToleranciaComparacaoPrincipal(diferencaFinal.Value));

            decimal? diferencaToleranciaEfetiva = compativelPorToleranciaContexto
                ? contexto!.DiferencaResidual
                : compativelPorToleranciaEfetiva
                    ? diferencaFinal
                    : null;

            // Regra de limpeza financeira: se uma ocorrência tecnicamente ficou como
            // não encontrada/ambígua, mas o impacto financeiro é exatamente R$ 0,00,
            // ela não deve permanecer como divergência final.
            bool semImpactoFinanceiro =
                item.Categoria != ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                diferencaFinal.HasValue &&
                diferencaFinal.Value == 0m;

            // Uma explicação completa do contexto/regra tem prioridade sobre um simples
            // sinal de Atenção. Ex.: cancelamento futuro que zera integralmente a diferença.
            bool divergenciaExplicada = explicativas.Count > 0 || contexto?.Explicada == true;

            AnaliseFinalStatus status = item.Categoria switch
            {
                ComparacaoPrincipalCategoria.EncontradoValorCompativel => AnaliseFinalStatus.Compativel,
                // R$ 0,00 de impacto financeiro sempre prevalece sobre ambiguidade/Atenção.
                _ when semImpactoFinanceiro => AnaliseFinalStatus.Compativel,
                // Mesmo com ambiguidade técnica, uma diferença financeira dentro da margem
                // de ±R$ 0,30 é considerada compatível e entra no resumo da tolerância.
                _ when compativelPorToleranciaEfetiva => AnaliseFinalStatus.Compativel,
                // Se o contexto ou uma regra reconciliou integralmente o caso, ele está OK.
                _ when divergenciaExplicada => AnaliseFinalStatus.Compativel,
                _ when regraAtencao != null => AnaliseFinalStatus.Atencao,
                ComparacaoPrincipalCategoria.Ambiguo => AnaliseFinalStatus.Ambiguo,
                _ => AnaliseFinalStatus.DivergenciaPendente
            };

            string regraExplicativa;
            if (semImpactoFinanceiro)
            {
                regraExplicativa = "Sem impacto financeiro (R$ 0,00)";
            }
            else if (compativelPorToleranciaEfetiva)
            {
                regraExplicativa = compativelPorToleranciaContexto
                    ? $"Contexto temporal + Tolerância ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}"
                    : $"Tolerância ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}";
            }
            else if (explicativas.Count > 0)
            {
                regraExplicativa = string.Join(" + ", explicativas.Select(x => x.NomeDaRegra).Distinct());
            }
            else if (contexto?.Explicada == true)
            {
                regraExplicativa = TraduzirContextoExplicativo(contexto.Status);
            }
            else if (regraAtencao != null)
            {
                regraExplicativa = regraAtencao.NomeDaRegra;
            }
            else
            {
                regraExplicativa = string.Empty;
            }

            string justificativa = status switch
            {
                AnaliseFinalStatus.Atencao when regraAtencao != null
                    => regraAtencao.Justificativa,
                AnaliseFinalStatus.Compativel when semImpactoFinanceiro
                    => $"Sem impacto financeiro: Fatura e Over resultam em diferença de R$ 0,00. Classificação técnica original: {TraduzirCategoria(item.Categoria)}. " +
                       (item.Categoria == ComparacaoPrincipalCategoria.Ambiguo
                           ? "O vínculo continua registrado como ambíguo para auditoria, mas não há divergência financeira."
                           : "O registro permanece disponível para auditoria entre os compatíveis."),
                AnaliseFinalStatus.Compativel when compativelPorToleranciaContexto && contexto != null
                    => contexto.Observacao,
                AnaliseFinalStatus.Compativel when compativelPorToleranciaEfetiva
                    => item.Categoria == ComparacaoPrincipalCategoria.Ambiguo
                        ? $"Diferença financeira de {(diferencaFinal ?? 0m):N2} ignorada pela tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}. Os totais foram formados pelos componentes exibidos nas abas Fatura e Over; componentes marcados como ignorados, como IOF, não entram. A ambiguidade técnica original permanece registrada apenas para rastreabilidade."
                        : $"Diferença de {(diferencaFinal ?? 0m):N2} ignorada pela tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}. O valor permanece registrado no resumo de diferenças ignoradas.",
                AnaliseFinalStatus.Compativel when explicativas.Count > 0
                    => string.Join(" | ", explicativas.Select(x => x.Justificativa).Distinct()),
                AnaliseFinalStatus.Compativel when contexto?.Explicada == true
                    => contexto.Observacao,
                AnaliseFinalStatus.Compativel
                    => "Valores da fatura e do NET comparável do Over estão iguais até o centavo.",
                AnaliseFinalStatus.Ambiguo => item.Observacao,
                AnaliseFinalStatus.DivergenciaExplicada
                    => string.Join(" | ", explicativas.Select(x => x.Justificativa).Distinct()),
                _ => CriarJustificativaPendente(item, contexto, regras)
            };

            if (houveAjusteContextoFinanceiro &&
                diferencaOriginalAntesContexto.HasValue &&
                diferencaFinal.HasValue)
            {
                string resumoResidual = CriarResumoDiferencaResidual(
                    diferencaOriginalAntesContexto.Value,
                    ajusteContextoFinanceiro,
                    diferencaFinal.Value,
                    comparacao.CompetenciaAnalisada);

                // Se a justificativa atual for apenas a observacao antiga do contexto,
                // troca pela explicacao atualizada. Caso contrario, preserva a regra
                // existente e acrescenta o resumo financeiro na frente.
                if (contexto != null &&
                    string.Equals(
                        justificativa?.Trim(),
                        contexto.Observacao?.Trim(),
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    justificativa = resumoResidual;
                }
                else
                {
                    justificativa = string.IsNullOrWhiteSpace(justificativa)
                        ? resumoResidual
                        : $"{resumoResidual} {justificativa}";
                }
            }
            string entidade = componentesFatura.Select(x => x.Entidade).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? componentesOver.Select(x => x.Entidade).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? dadosFatura?.Entidade
                ?? string.Empty;

            resultados.Add(new AnaliseFinalResultado
            {
                IdResultado = item.IdResultado,
                Beneficiario = item.NomeReferencia,
                Certificado = item.Certificado,
                TipoDivergencia = semImpactoFinanceiro
                    ? "Sem impacto financeiro"
                    : compativelPorToleranciaEfetiva
                        ? "Valor compatível"
                        : divergenciaExplicada
                            ? "Compatível por regra de negócio"
                            : regraAtencao != null
                                ? "Devolução proporcional por cancelamento"
                                : TraduzirCategoria(item.Categoria),
                Diferenca = diferencaResidualFinal,
                DiferencaOriginal = diferencaOriginalAntesContexto,
                AjusteContextoFinanceiro = ajusteContextoFinanceiro,
                DiferencaResidual = diferencaResidualFinal,
                ValorFaturaAjustado = valorFaturaAjustado,
                VersaoCalculo = 3,
                ValorFatura = valorFaturaFinal,
                ValorOver = valorOverFinal,
                Entidade = entidade,
                Competencia = comparacao.CompetenciaAnalisada,
                Status = status,
                RegraExplicativa = regraExplicativa,
                JustificativaFinal = justificativa,
                OrigemFatura = item.OrigemFatura,
                OrigemOver = item.OrigemOver,
                ComparacaoPrincipal = item,
                ContextoTemporal = contexto,
                RegrasTestadas = regras,
                ComponentesFatura = componentesFatura,
                LancamentosFaturaInvestigacao = lancamentosFaturaInvestigacao,
                ComponentesOver = componentesOver,
                DadosFatura = dadosFatura,
                CompativelPorToleranciaEfetiva = compativelPorToleranciaEfetiva,
                DiferencaToleranciaEfetiva = diferencaToleranciaEfetiva
            });
        }

        return new AnaliseFinalDiagnostico
        {
            Competencia = comparacao.CompetenciaAnalisada,
            IgnorandoCoparticipacao = ignorarCoparticipacao,
            IgnorandoCompetenciasAnteriores = ignorarCompetenciasAnteriores,
            IgnorandoClientesCancelados = ignorarClientesCancelados,
            ToleranciaFinanceiraUtilizada = AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal,
            Resultados = resultados
                .OrderBy(x => OrdemStatus(x.Status))
                .ThenByDescending(x => Math.Abs(x.Diferenca ?? 0m))
                .ThenBy(x => x.Beneficiario, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    /// <summary>
    /// Soma, com sinal, os lancamentos encontrados em FATURAS POSTERIORES
    /// que pertencem exatamente a competencia analisada.
    /// </summary>
    private static bool TentarCalcularAjusteContextoFinanceiro(
        ContextoTemporalResultado? contexto,
        DateTime competenciaAnalisada,
        out decimal ajuste)
    {
        ajuste = 0m;

        if (contexto?.Evidencias == null || contexto.Evidencias.Count == 0)
            return false;

        DateTime competenciaBase = new(
            competenciaAnalisada.Year,
            competenciaAnalisada.Month,
            1);

        var aplicaveis = contexto.Evidencias
            .Where(x =>
            {
                DateTime competenciaFatura = new(
                    x.CompetenciaFatura.Year,
                    x.CompetenciaFatura.Month,
                    1);

                DateTime competenciaLancamento = new(
                    x.CompetenciaLancamento.Year,
                    x.CompetenciaLancamento.Month,
                    1);

                return competenciaFatura > competenciaBase &&
                       competenciaLancamento == competenciaBase;
            })
            // Deduplicacao conservadora: a mesma evidencia nao entra duas vezes
            // caso o contexto a tenha registrado repetidamente.
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
            .Select(g => g.First())
            .ToList();

        if (aplicaveis.Count == 0)
            return false;

        ajuste = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            aplicaveis.Sum(x => x.Valor));

        return ajuste != 0m;
    }

    private static string CriarResumoDiferencaResidual(
        decimal diferencaOriginal,
        decimal ajusteContexto,
        decimal diferencaResidual,
        DateTime competencia)
    {
        string conclusao =
            diferencaResidual == 0m
                ? "Os ajustes reconciliam integralmente a divergência."
                : AnaliseFaturasRegrasComparacao.DentroToleranciaComparacaoPrincipal(diferencaResidual)
                    ? $"O residual está dentro da tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}."
                    : "Permanece diferença financeira após a compensação.";

        return
            $"Ajustes posteriores de R$ {ajusteContexto:N2} vinculados a {competencia:MM/yyyy} " +
            $"foram aplicados à diferença original de R$ {diferencaOriginal:N2}. " +
            $"Diferença residual: R$ {diferencaResidual:N2}. {conclusao}";
    }
    private static string TraduzirContextoExplicativo(ContextoTemporalStatus status) => status switch
    {
        ContextoTemporalStatus.ExplicadaPorVigenciaPosterior => "Vigência posterior",
        ContextoTemporalStatus.ExplicadaPorInclusao => "Inclusão posterior",
        ContextoTemporalStatus.ExplicadaPorRetroativo => "Retroativo",
        ContextoTemporalStatus.ExplicadaPorCancelamento => "Cancelamento",
        ContextoTemporalStatus.ExplicadaPorReativacao => "Reativação",
        ContextoTemporalStatus.ExplicadaPorAlteracao => "Alteração",
        ContextoTemporalStatus.ExplicadaPorTransferencia => "Transferência",
        ContextoTemporalStatus.ExplicadaPorDevolucao => "Devolução",
        _ => "Contexto temporal"
    };

    private static string CriarJustificativaPendente(
        ComparacaoPrincipalResultado item,
        ContextoTemporalResultado? contexto,
        IReadOnlyList<RegraAnaliseResultado> regras)
    {
        List<RegraAnaliseResultado> sinais = regras
            .Where(x => x.Resultado is RegraAnaliseStatus.EvidenciaEncontrada or RegraAnaliseStatus.RevisaoManual)
            .ToList();

        if (sinais.Count > 0)
        {
            return string.Join(" | ", sinais.Select(x => $"{x.NomeDaRegra}: {x.Justificativa}"));
        }

        if (contexto != null && !string.IsNullOrWhiteSpace(contexto.Observacao))
            return contexto.Observacao;

        return item.Observacao;
    }

    private static bool TentarCalcularValoresComparaveisDosComponentes(
        IReadOnlyList<ComponenteFatura> componentesFatura,
        IReadOnlyList<ComponenteOver> componentesOver,
        out decimal valorFatura,
        out decimal valorOver,
        out decimal diferenca)
    {
        valorFatura = 0m;
        valorOver = 0m;
        diferenca = 0m;

        List<ComponenteFatura> faturaConsiderada = componentesFatura
            .Where(x => x.ConsiderarNoComparavel)
            .ToList();

        List<ComponenteOver> overConsiderado = componentesOver
            .Where(x => x.ConsiderarNoNETComparavel && x.ValorNET.HasValue)
            .ToList();

        // Não usamos ausência de componentes como zero. A promoção financeira de uma
        // ambiguidade só é permitida quando há evidência comparável dos dois lados.
        if (faturaConsiderada.Count == 0 || overConsiderado.Count == 0)
            return false;

        valorFatura = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            faturaConsiderada.Sum(x => x.Valor));
        valorOver = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            overConsiderado.Sum(x => x.ValorNET ?? 0m));
        diferenca = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            valorFatura - valorOver);

        return true;
    }

    private static ComposicaoBeneficiario? EncontrarComposicao(
        IReadOnlyList<ComposicaoBeneficiario> composicoes,
        ComparacaoPrincipalResultado item)
    {
        string cert = item.Certificado;
        string nome = AnaliseFaturasNormalizador.NormalizarNome(item.NomeReferencia);

        List<ComposicaoBeneficiario> candidatos = composicoes
            .Where(x => string.Equals(x.Certificado, cert, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidatos.Count == 1)
            return candidatos[0];

        List<ComposicaoBeneficiario> porNome = candidatos.Where(x =>
            string.Equals(AnaliseFaturasNormalizador.NormalizarNome(x.NomeFatura), nome, StringComparison.Ordinal) ||
            string.Equals(AnaliseFaturasNormalizador.NormalizarNome(x.NomeOver), nome, StringComparison.Ordinal))
            .ToList();

        return porNome.Count == 1 ? porNome[0] : null;
    }

    private static DadosBeneficiarioFaturaAnalise? EncontrarDadosFatura(
        IReadOnlyList<FaturaBradescoArquivo> arquivos,
        ComparacaoPrincipalResultado item)
    {
        string cert = item.Certificado;
        string nome = AnaliseFaturasNormalizador.NormalizarNome(item.NomeReferencia);

        var candidatos = new List<(FaturaBradescoArquivo Arquivo, FaturaBradescoSubfatura Subfatura, FaturaBradescoBeneficiario Beneficiario)>();

        foreach (FaturaBradescoArquivo arquivo in arquivos)
        foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
        foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
        {
            string certNormal = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(beneficiario.Certificado) ?? string.Empty;
            if (string.Equals(certNormal, cert, StringComparison.OrdinalIgnoreCase))
                candidatos.Add((arquivo, subfatura, beneficiario));
        }

        if (candidatos.Count > 1)
            candidatos = candidatos.Where(x =>
                string.Equals(AnaliseFaturasNormalizador.NormalizarNome(x.Beneficiario.Nome), nome, StringComparison.Ordinal)).ToList();

        if (candidatos.Count != 1)
            return null;

        var c = candidatos[0];
        return new DadosBeneficiarioFaturaAnalise
        {
            Arquivo = c.Arquivo.NomeArquivo,
            Subfatura = c.Subfatura.Numero,
            Entidade = c.Subfatura.Entidade,
            Certificado = c.Beneficiario.Certificado,
            Nome = c.Beneficiario.Nome,
            DataNascimento = c.Beneficiario.DataNascimento,
            DataInicio = c.Beneficiario.DataInicio,
            Plano = c.Beneficiario.Plano
        };
    }

    private static IReadOnlyList<LancamentoFaturaInvestigacao> CriarLancamentosFaturaInvestigacao(
        IReadOnlyList<FaturaBradescoArquivo> arquivos,
        ComparacaoPrincipalResultado item,
        DateTime competenciaAnalisada,
        bool ignorarCompetenciasAnteriores)
    {
        string cert = item.Certificado;
        string nome = AnaliseFaturasNormalizador.NormalizarNome(item.NomeReferencia);
        var resultado = new List<LancamentoFaturaInvestigacao>();

        foreach (FaturaBradescoArquivo arquivo in arquivos)
        foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
        foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
        {
            string certNormal = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(beneficiario.Certificado) ?? string.Empty;
            if (!string.Equals(certNormal, cert, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(nome) &&
                !string.Equals(AnaliseFaturasNormalizador.NormalizarNome(beneficiario.Nome), nome, StringComparison.Ordinal))
                continue;

            foreach (FaturaBradescoLancamento lancamento in beneficiario.Lancamentos)
            {
                bool considerar = AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                    lancamento.Competencia,
                    competenciaAnalisada,
                    ignorarCompetenciasAnteriores);
                string uso = ignorarCompetenciasAnteriores &&
                    !AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                        lancamento.Competencia,
                        competenciaAnalisada,
                        true)
                    ? $"Ignorado no valor comparável — competência {lancamento.Competencia:MM/yyyy} anterior à analisada {competenciaAnalisada:MM/yyyy}"
                    : considerar
                        ? "Considerado no valor comparável"
                        : "Não considerado no valor comparável";

                resultado.Add(new LancamentoFaturaInvestigacao
                {
                    CompetenciaFatura = arquivo.Competencia ?? competenciaAnalisada,
                    Arquivo = arquivo.NomeArquivo,
                    PaginaPdf = lancamento.PaginaPdf,
                    PaginaFatura = lancamento.PaginaFatura,
                    Subfatura = subfatura.Numero,
                    Entidade = subfatura.Entidade,
                    Movimento = lancamento.Movimento,
                    CompetenciaLancamento = lancamento.Competencia,
                    Natureza = string.IsNullOrWhiteSpace(lancamento.Movimento) ? "Mensalidade / lançamento base" : lancamento.Movimento,
                    Plano = string.IsNullOrWhiteSpace(lancamento.Plano) ? beneficiario.Plano : lancamento.Plano,
                    Valor = lancamento.Valor,
                    Participacao = lancamento.Participacao,
                    UsoComparacao = uso,
                    TextoOrigem = lancamento.TextoOrigem
                });
            }
        }

        return resultado
            .OrderBy(x => x.CompetenciaFatura)
            .ThenBy(x => x.Arquivo, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.PaginaPdf)
            .ThenBy(x => x.CompetenciaLancamento)
            .ToList();
    }

    private static IReadOnlyList<ComponenteFatura> CriarComponentesFatura(
        IReadOnlyList<FaturaBradescoArquivo> arquivos,
        ComparacaoPrincipalResultado item,
        DateTime competenciaAnalisada,
        bool ignorarCompetenciasAnteriores)
    {
        string cert = item.Certificado;
        string nome = AnaliseFaturasNormalizador.NormalizarNome(item.NomeReferencia);
        var resultado = new List<ComponenteFatura>();

        foreach (FaturaBradescoArquivo arquivo in arquivos)
        foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
        foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
        {
            string certNormal = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(beneficiario.Certificado) ?? string.Empty;
            if (!string.Equals(certNormal, cert, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(nome) &&
                !string.Equals(AnaliseFaturasNormalizador.NormalizarNome(beneficiario.Nome), nome, StringComparison.Ordinal))
                continue;

            resultado.AddRange(beneficiario.Lancamentos.Select(x => new ComponenteFatura
            {
                PaginaPdf = x.PaginaPdf,
                PaginaFatura = x.PaginaFatura,
                Subfatura = subfatura.Numero,
                Entidade = subfatura.Entidade,
                Movimento = x.Movimento,
                Competencia = x.Competencia,
                Plano = string.IsNullOrWhiteSpace(x.Plano) ? beneficiario.Plano : x.Plano,
                Valor = x.Valor,
                Participacao = x.Participacao,
                Natureza = string.IsNullOrWhiteSpace(x.Movimento) ? "Mensalidade / lançamento base" : x.Movimento,
                TextoOrigem = x.TextoOrigem,
                ConsiderarNoComparavel = AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                    x.Competencia,
                    competenciaAnalisada,
                    ignorarCompetenciasAnteriores),
                RegraComparacao = ignorarCompetenciasAnteriores &&
                    !AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                        x.Competencia,
                        competenciaAnalisada,
                        true)
                        ? $"Ignorado no valor comparável — competência {x.Competencia:MM/yyyy} anterior à analisada {competenciaAnalisada:MM/yyyy}"
                        : "Considerado no valor comparável"
            }));
        }

        return resultado.OrderBy(x => x.Competencia).ThenBy(x => x.PaginaPdf).ToList();
    }

    private static IReadOnlyList<ComponenteOver> CriarComponentesOver(
        OverArquivo over,
        ComparacaoPrincipalResultado item,
        bool ignorarCoparticipacao)
    {
        string cert = item.Certificado;
        string nome = AnaliseFaturasNormalizador.NormalizarNome(item.NomeReferencia);

        return over.Lancamentos
            .Where(x => string.Equals(
                AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(x.Cartao) ?? string.Empty,
                cert,
                StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(nome) ||
                string.Equals(AnaliseFaturasNormalizador.NormalizarNome(x.Beneficiario), nome, StringComparison.Ordinal))
            .Select(x => new ComponenteOver
            {
                NumeroLinha = x.NumeroLinha,
                Competencia = x.Competencia,
                Evento = x.Evento,
                Descricao = x.Descricao,
                ValorPV = x.ValorPV,
                ValorNET = x.ValorNET,
                ValorOver = x.ValorOver,
                Entidade = x.Entidade,
                Matricula = x.Matricula,
                Cartao = x.Cartao,
                Natureza = AnaliseFaturasRegrasComparacao.EhIof(x.Evento, x.Descricao)
                    ? "IOF"
                    : AnaliseFaturasRegrasComparacao.EhCoparticipacao(x.Evento, x.Descricao)
                        ? "Coparticipação / fator moderador"
                        : x.Descricao,
                ConsiderarNoNETComparavel = AnaliseFaturasRegrasComparacao.ConsiderarNoNetComparavel(x, ignorarCoparticipacao),
                RegraComparacao = CriarRegraComparacaoOver(x, ignorarCoparticipacao)
            })
            .OrderBy(x => x.NumeroLinha)
            .ToList();
    }

    private static string CriarRegraComparacaoOver(OverLancamento lancamento, bool ignorarCoparticipacao)
    {
        if (AnaliseFaturasRegrasComparacao.EhIof(lancamento.Evento, lancamento.Descricao))
            return "Ignorado no NET comparável — IOF não cobrado pela Bradesco";

        if (ignorarCoparticipacao && AnaliseFaturasRegrasComparacao.EhCoparticipacao(lancamento.Evento, lancamento.Descricao))
            return "Ignorado no NET comparável — coparticipação";

        if (!ignorarCoparticipacao && AnaliseFaturasRegrasComparacao.EhCoparticipacao(lancamento.Evento, lancamento.Descricao))
            return "Considerado no NET — opção de ignorar coparticipação desmarcada";

        return "Considerado no NET comparável";
    }

    private static ComposicaoBeneficiario CriarComposicaoVirtual(
        ComparacaoPrincipalResultado item,
        IReadOnlyList<ComponenteFatura> fatura,
        IReadOnlyList<ComponenteOver> over)
        => new()
        {
            Certificado = item.Certificado,
            NomeFatura = item.NomeFatura,
            NomeOver = item.NomeOver,
            StatusVinculo = item.StatusVinculo,
            OrigemFatura = item.OrigemFatura,
            OrigemOver = item.OrigemOver,
            ComponentesFatura = fatura,
            ComponentesOver = over
        };

    public static string TraduzirStatus(AnaliseFinalStatus status) => status switch
    {
        AnaliseFinalStatus.Compativel => "Compatível",
        AnaliseFinalStatus.DivergenciaExplicada => "Compatível",
        AnaliseFinalStatus.DivergenciaPendente => "Divergência",
        AnaliseFinalStatus.Atencao => "Atenção",
        AnaliseFinalStatus.Ambiguo => "Ambígua",
        _ => status.ToString()
    };

    public static string TraduzirCategoria(ComparacaoPrincipalCategoria categoria) => categoria switch
    {
        ComparacaoPrincipalCategoria.EncontradoValorCompativel => "Valor compatível",
        ComparacaoPrincipalCategoria.ValorMaiorNaFatura => "Valor maior na fatura",
        ComparacaoPrincipalCategoria.ValorMaiorNoOver => "Valor maior no Over",
        ComparacaoPrincipalCategoria.NaoEncontradoNaFatura => "Não encontrado na fatura",
        ComparacaoPrincipalCategoria.NaoEncontradoNoOver => "Não encontrado no Over",
        ComparacaoPrincipalCategoria.Ambiguo => "Ambíguo",
        _ => categoria.ToString()
    };

    private static int OrdemStatus(AnaliseFinalStatus status) => status switch
    {
        AnaliseFinalStatus.Ambiguo => 0,
        AnaliseFinalStatus.Atencao => 1,
        AnaliseFinalStatus.DivergenciaPendente => 2,
        AnaliseFinalStatus.DivergenciaExplicada => 3,
        _ => 4
    };
}
