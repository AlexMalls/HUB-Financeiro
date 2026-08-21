using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public enum ContextoTemporalStatus
{
    NaoAplicavelValorCompativel,
    NaoAplicavelAmbiguo,
    SemContexto,
    AmbiguoNoContexto,
    ContextoEncontradoSemJustificativa,
    ExplicadaPorVigenciaPosterior,
    ExplicadaPorInclusao,
    ExplicadaPorRetroativo,
    ExplicadaPorCancelamento,
    ExplicadaPorReativacao,
    ExplicadaPorAlteracao,
    ExplicadaPorTransferencia,
    ExplicadaPorDevolucao
}

public sealed class ContextoTemporalDiagnostico
{
    public bool Disponivel { get; init; }
    public DateTime CompetenciaAnalisada { get; init; }
    public IReadOnlyList<ContextoTemporalResultado> Resultados { get; init; } = Array.Empty<ContextoTemporalResultado>();
    public string Mensagem { get; init; } = string.Empty;

    public int TotalExplicadas => Resultados.Count(x => x.Explicada);
    public int TotalPermanecem => Resultados.Count(x => x.DivergenciaPermanece);
    public int TotalSemContexto => Resultados.Count(x => x.Status == ContextoTemporalStatus.SemContexto);
}

public sealed class ContextoTemporalResultado
{
    public ComparacaoPrincipalResultado ComparacaoOriginal { get; init; } = new();
    public ContextoTemporalStatus Status { get; init; }
    public bool Explicada { get; init; }
    public bool DivergenciaPermanece { get; init; }
    public decimal ValorAjustesContexto { get; init; }
    public bool ExplicadaPorTolerancia { get; init; }
    public decimal DiferencaResidual { get; init; }
    public IReadOnlyList<ContextoTemporalEvidencia> Evidencias { get; init; } = Array.Empty<ContextoTemporalEvidencia>();
    public string Observacao { get; init; } = string.Empty;

    public string Certificado => ComparacaoOriginal.Certificado;
    public string Nome => ComparacaoOriginal.NomeReferencia;
}

public sealed class ContextoTemporalEvidencia
{
    public DateTime CompetenciaFatura { get; init; }
    public string Arquivo { get; init; } = string.Empty;
    public int Subfatura { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public DateTime? DataInicio { get; init; }
    public int PaginaPdf { get; init; }
    public string Movimento { get; init; } = string.Empty;
    public DateTime CompetenciaLancamento { get; init; }
    public decimal Valor { get; init; }
}

/// <summary>
/// Consulta somente as faturas do mês atual e do mês seguinte para explicar
/// divergências já produzidas pela comparação principal.
/// Os valores originais da comparação NÃO são alterados nem recalculados.
/// </summary>
public sealed class ContextoTemporalService
{
    private static readonly HashSet<string> MovimentosRetroativos = new(StringComparer.OrdinalIgnoreCase)
    {
        "IR", "RR", "TR", "AR", "CR", "DR"
    };

    public ContextoTemporalDiagnostico Analisar(
        ComparacaoPrincipalDiagnostico comparacaoPrincipal,
        IReadOnlyList<FaturaBradescoArquivo> faturasMesAtual,
        IReadOnlyList<FaturaBradescoArquivo> faturasMesSeguinte)
    {
        if (comparacaoPrincipal == null)
            throw new ArgumentNullException(nameof(comparacaoPrincipal));
        if (faturasMesAtual == null)
            throw new ArgumentNullException(nameof(faturasMesAtual));
        if (faturasMesSeguinte == null)
            throw new ArgumentNullException(nameof(faturasMesSeguinte));

        DateTime competencia = new(
            comparacaoPrincipal.CompetenciaAnalisada.Year,
            comparacaoPrincipal.CompetenciaAnalisada.Month,
            1);

        ValidarCompetenciaContexto(faturasMesAtual, competencia.AddMonths(1), "mês atual");
        ValidarCompetenciaContexto(faturasMesSeguinte, competencia.AddMonths(2), "mês que vem");

        List<OcorrenciaContexto> ocorrencias = CriarOcorrencias(faturasMesAtual)
            .Concat(CriarOcorrencias(faturasMesSeguinte))
            .ToList();

        var resultados = new List<ContextoTemporalResultado>();
        foreach (ComparacaoPrincipalResultado original in comparacaoPrincipal.Resultados.Where(x => x.EhDivergencia))
            resultados.Add(AnalisarResultado(original, competencia, ocorrencias));

        return new ContextoTemporalDiagnostico
        {
            Disponivel = true,
            CompetenciaAnalisada = competencia,
            Resultados = resultados,
            Mensagem = "Contexto aplicado somente como explicação. Totais da comparação principal permanecem intactos."
        };
    }

    public ContextoTemporalDiagnostico CriarIndisponivel(ComparacaoPrincipalDiagnostico comparacaoPrincipal, string mensagem)
    {
        DateTime competencia = comparacaoPrincipal?.CompetenciaAnalisada ?? DateTime.MinValue;
        return new ContextoTemporalDiagnostico
        {
            Disponivel = false,
            CompetenciaAnalisada = competencia,
            Resultados = Array.Empty<ContextoTemporalResultado>(),
            Mensagem = mensagem
        };
    }

    private static ContextoTemporalResultado AnalisarResultado(
        ComparacaoPrincipalResultado original,
        DateTime competencia,
        IReadOnlyList<OcorrenciaContexto> ocorrencias)
    {
        if (original.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel)
        {
            return Resultado(original, ContextoTemporalStatus.NaoAplicavelValorCompativel, false, false, 0m,
                Array.Empty<ContextoTemporalEvidencia>(), "Valor já compatível na comparação principal; contexto não é necessário.");
        }

        if (original.Categoria == ComparacaoPrincipalCategoria.Ambiguo || string.IsNullOrWhiteSpace(original.Certificado))
        {
            return Resultado(original, ContextoTemporalStatus.NaoAplicavelAmbiguo, false, true, 0m,
                Array.Empty<ContextoTemporalEvidencia>(), "Ambiguidade de identidade não é resolvida por regra temporal.");
        }

        List<OcorrenciaContexto> candidatosCertificado = ocorrencias
            .Where(x => string.Equals(x.Certificado, original.Certificado, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidatosCertificado.Count == 0)
        {
            string mensagemSemContexto = original.Categoria == ComparacaoPrincipalCategoria.NaoEncontradoNaFatura
                ? "Cliente não encontrado em nenhuma das faturas analisadas."
                : "Cliente não encontrado nas faturas posteriores.";

            return Resultado(original, ContextoTemporalStatus.SemContexto, false, true, 0m,
                Array.Empty<ContextoTemporalEvidencia>(), mensagemSemContexto);
        }

        string nomeNorm = AnaliseFaturasNormalizador.NormalizarNome(original.NomeReferencia);
        var selecionados = new List<OcorrenciaContexto>();
        bool ambiguo = false;

        foreach (IGrouping<DateTime, OcorrenciaContexto> grupoMes in candidatosCertificado.GroupBy(x => x.CompetenciaFatura))
        {
            List<OcorrenciaContexto> grupo = grupoMes.ToList();
            if (grupo.Count == 1)
            {
                selecionados.Add(grupo[0]);
                continue;
            }

            List<OcorrenciaContexto> porNome = grupo
                .Where(x => string.Equals(x.NomeNormalizado, nomeNorm, StringComparison.Ordinal))
                .ToList();

            if (porNome.Count == 1)
                selecionados.Add(porNome[0]);
            else if (porNome.Count > 1)
                ambiguo = true;
            else
                ambiguo = true;
        }

        if (ambiguo && selecionados.Count == 0)
        {
            return Resultado(original, ContextoTemporalStatus.AmbiguoNoContexto, false, true, 0m,
                Array.Empty<ContextoTemporalEvidencia>(), "Cliente encontrado mais de uma vez nas faturas posteriores, sem identificação única pelo nome.");
        }

        List<ContextoTemporalEvidencia> evidencias = selecionados
            .SelectMany(CriarEvidencias)
            .OrderBy(x => x.CompetenciaFatura)
            .ThenBy(x => x.PaginaPdf)
            .ToList();

        List<ContextoTemporalEvidencia> ajustesDaCompetencia = evidencias
            .Where(x => MesmoMes(x.CompetenciaLancamento, competencia))
            .ToList();

        decimal valorAjustes = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            ajustesDaCompetencia.Sum(x => x.Valor));

        // HUB_CONTEXTO_RESIDUAL_V3
        // O residual existe mesmo quando a compensacao e apenas parcial.
        decimal diferencaResidualContexto = original.DiferencaFaturaMenosOver.HasValue
            ? AnaliseFaturasRegrasComparacao.ArredondarCentavos(
                original.DiferencaFaturaMenosOver.Value + valorAjustes)
            : 0m;

        if (original.DiferencaFaturaMenosOver.HasValue && valorAjustes != 0m)
        {
            decimal diferencaResidual = diferencaResidualContexto;
            bool reconciliouExatamente = AnaliseFaturasRegrasComparacao.ValoresIguaisCentavo(
                diferencaResidual,
                0m);
            bool reconciliouPorTolerancia = !reconciliouExatamente &&
                AnaliseFaturasRegrasComparacao.DentroToleranciaComparacaoPrincipal(diferencaResidual);

            if (reconciliouExatamente || reconciliouPorTolerancia)
            {
                ContextoTemporalStatus status = ClassificarExplicacao(ajustesDaCompetencia);
                string observacao = reconciliouPorTolerancia
                    ? $"Ajustes posteriores de R$ {valorAjustes:N2} referentes a {competencia:MM/yyyy} deixam diferença residual de R$ {diferencaResidual:N2}, dentro da tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}."
                    : $"Ajustes posteriores de R$ {valorAjustes:N2} referentes a {competencia:MM/yyyy} explicam integralmente a diferença de R$ {original.DiferencaFaturaMenosOver.Value:N2}.";

                return Resultado(
                    original,
                    status,
                    true,
                    false,
                    valorAjustes,
                    evidencias,
                    observacao,
                    reconciliouPorTolerancia,
                    diferencaResidual);
            }
        }

        bool possuiInclusao = evidencias.Any(x => EhMovimento(x.Movimento, "IM", "IR"));
        bool vigenciaPosterior = selecionados.Any(x =>
            x.DataInicio.HasValue && x.DataInicio.Value.Date > UltimoDiaMes(competencia));

        if (original.Categoria == ComparacaoPrincipalCategoria.NaoEncontradoNaFatura && possuiInclusao)
        {
            return Resultado(original, ContextoTemporalStatus.ExplicadaPorInclusao, true, false, valorAjustes, evidencias,
                "Cliente encontrado posteriormente com movimento de inclusão (IM/IR).");
        }

        if (original.Categoria == ComparacaoPrincipalCategoria.NaoEncontradoNaFatura && vigenciaPosterior)
        {
            return Resultado(original, ContextoTemporalStatus.ExplicadaPorVigenciaPosterior, true, false, valorAjustes, evidencias,
                $"Cliente encontrado posteriormente com início de vigência após {competencia:MM/yyyy}.");
        }

        if (evidencias.Count == 0)
        {
            return Resultado(original, ContextoTemporalStatus.SemContexto, false, true, 0m,
                evidencias, "Cliente encontrado em faturas posteriores, mas sem lançamentos disponíveis para análise.");
        }
        return Resultado(original, ContextoTemporalStatus.ContextoEncontradoSemJustificativa, false, true, valorAjustes,
            evidencias,
            valorAjustes == 0m
                ? $"Cliente encontrado em faturas posteriores, mas sem lançamento referente a {competencia:MM/yyyy} que explique a diferença."
                : $"Ajustes posteriores de R$ {valorAjustes:N2} referentes a {competencia:MM/yyyy} não fecham a diferença de R$ {original.DiferencaFaturaMenosOver.GetValueOrDefault():N2}.",
            false,
            diferencaResidualContexto);
    }

    private static ContextoTemporalStatus ClassificarExplicacao(IReadOnlyList<ContextoTemporalEvidencia> evidencias)
    {
        string[] movimentos = evidencias
            .Select(x => (x.Movimento ?? string.Empty).Trim().ToUpperInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (movimentos.Any(x => x is "CM" or "CR"))
            return ContextoTemporalStatus.ExplicadaPorCancelamento;
        if (movimentos.Any(x => x is "AD" or "DM" or "DR"))
            return ContextoTemporalStatus.ExplicadaPorDevolucao;
        if (movimentos.Any(x => x is "IM" or "IR"))
            return ContextoTemporalStatus.ExplicadaPorInclusao;
        if (movimentos.Any(x => x is "RM" or "RR"))
            return ContextoTemporalStatus.ExplicadaPorReativacao;
        if (movimentos.Any(x => x is "AM" or "AR"))
            return ContextoTemporalStatus.ExplicadaPorAlteracao;
        if (movimentos.Any(x => x is "TM" or "TR"))
            return ContextoTemporalStatus.ExplicadaPorTransferencia;
        if (movimentos.Any(x => MovimentosRetroativos.Contains(x)))
            return ContextoTemporalStatus.ExplicadaPorRetroativo;

        return ContextoTemporalStatus.ExplicadaPorRetroativo;
    }

    private static IEnumerable<ContextoTemporalEvidencia> CriarEvidencias(OcorrenciaContexto ocorrencia)
    {
        foreach (FaturaBradescoLancamento lancamento in ocorrencia.Beneficiario.Lancamentos)
        {
            yield return new ContextoTemporalEvidencia
            {
                CompetenciaFatura = ocorrencia.CompetenciaFatura,
                Arquivo = ocorrencia.Arquivo,
                Subfatura = ocorrencia.Subfatura,
                Entidade = ocorrencia.Entidade,
                DataInicio = ocorrencia.DataInicio,
                PaginaPdf = lancamento.PaginaPdf,
                Movimento = lancamento.Movimento,
                CompetenciaLancamento = lancamento.Competencia,
                Valor = lancamento.Valor
            };
        }
    }

    private static List<OcorrenciaContexto> CriarOcorrencias(IReadOnlyList<FaturaBradescoArquivo> arquivos)
    {
        var resultado = new List<OcorrenciaContexto>();
        foreach (FaturaBradescoArquivo arquivo in arquivos)
        {
            if (!arquivo.Competencia.HasValue)
                continue;

            DateTime competenciaFatura = new(arquivo.Competencia.Value.Year, arquivo.Competencia.Value.Month, 1);
            foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
            {
                foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
                {
                    resultado.Add(new OcorrenciaContexto(
                        AnaliseFaturasNormalizador.NormalizarCertificadoFatura(beneficiario.Certificado) ?? string.Empty,
                        beneficiario.Nome,
                        AnaliseFaturasNormalizador.NormalizarNome(beneficiario.Nome),
                        competenciaFatura,
                        arquivo.NomeArquivo,
                        subfatura.Numero,
                        subfatura.Entidade,
                        beneficiario.DataInicio,
                        beneficiario));
                }
            }
        }
        return resultado;
    }

    private static void ValidarCompetenciaContexto(
        IReadOnlyList<FaturaBradescoArquivo> arquivos,
        DateTime esperada,
        string nomeGrupo)
    {
        if (arquivos.Count == 0)
            throw new InvalidOperationException($"As faturas de {nomeGrupo} não foram carregadas para o contexto temporal.");

        foreach (FaturaBradescoArquivo arquivo in arquivos)
        {
            if (!arquivo.Competencia.HasValue)
                throw new InvalidOperationException($"Não foi possível identificar a competência de {arquivo.NomeArquivo}.");

            DateTime detectada = new(arquivo.Competencia.Value.Year, arquivo.Competencia.Value.Month, 1);
            if (detectada != esperada)
            {
                throw new InvalidOperationException(
                    $"Contexto temporal inválido: {arquivo.NomeArquivo} está em {detectada:MM/yyyy}; esperado para {nomeGrupo}: {esperada:MM/yyyy}.");
            }
        }
    }

    private static ContextoTemporalResultado Resultado(
        ComparacaoPrincipalResultado original,
        ContextoTemporalStatus status,
        bool explicada,
        bool permanece,
        decimal ajustes,
        IReadOnlyList<ContextoTemporalEvidencia> evidencias,
        string observacao,
        bool explicadaPorTolerancia = false,
        decimal diferencaResidual = 0m)
        => new()
        {
            ComparacaoOriginal = original,
            Status = status,
            Explicada = explicada,
            DivergenciaPermanece = permanece,
            ValorAjustesContexto = ajustes,
            ExplicadaPorTolerancia = explicadaPorTolerancia,
            DiferencaResidual = diferencaResidual,
            Evidencias = evidencias,
            Observacao = observacao
        };

    private static bool MesmoMes(DateTime a, DateTime b)
        => a.Year == b.Year && a.Month == b.Month;

    private static bool EhMovimento(string? movimento, params string[] codigos)
    {
        string mov = (movimento ?? string.Empty).Trim();
        return codigos.Any(x => string.Equals(x, mov, StringComparison.OrdinalIgnoreCase));
    }

    private static DateTime UltimoDiaMes(DateTime competencia)
        => new DateTime(competencia.Year, competencia.Month, 1).AddMonths(1).AddDays(-1);

    private sealed record OcorrenciaContexto(
        string Certificado,
        string Nome,
        string NomeNormalizado,
        DateTime CompetenciaFatura,
        string Arquivo,
        int Subfatura,
        string Entidade,
        DateTime? DataInicio,
        FaturaBradescoBeneficiario Beneficiario);
}
