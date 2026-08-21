using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public enum ComparacaoPrincipalCategoria
{
    EncontradoValorCompativel,
    ValorMaiorNaFatura,
    ValorMaiorNoOver,
    NaoEncontradoNaFatura,
    NaoEncontradoNoOver,
    Ambiguo
}

public sealed class ComparacaoPrincipalDiagnostico
{
    public DateTime CompetenciaAnalisada { get; init; }
    public IReadOnlyList<ComparacaoPrincipalResultado> Resultados { get; init; } = Array.Empty<ComparacaoPrincipalResultado>();

    public int Total => Resultados.Count;
    public int TotalCompativeis => Resultados.Count(x => x.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel);
    public int TotalMaiorFatura => Resultados.Count(x => x.Categoria == ComparacaoPrincipalCategoria.ValorMaiorNaFatura);
    public int TotalMaiorOver => Resultados.Count(x => x.Categoria == ComparacaoPrincipalCategoria.ValorMaiorNoOver);
    public int TotalNaoFatura => Resultados.Count(x => x.Categoria == ComparacaoPrincipalCategoria.NaoEncontradoNaFatura);
    public int TotalNaoOver => Resultados.Count(x => x.Categoria == ComparacaoPrincipalCategoria.NaoEncontradoNoOver);
    public int TotalAmbiguos => Resultados.Count(x => x.Categoria == ComparacaoPrincipalCategoria.Ambiguo);
    public int TotalDivergencias => Resultados.Count(x => x.Categoria != ComparacaoPrincipalCategoria.EncontradoValorCompativel);
    public int TotalCompativeisPorTolerancia => Resultados.Count(x => x.CompativelPorTolerancia);
    public decimal SomaToleranciaFaturaMaior => AnaliseFaturasRegrasComparacao.ArredondarCentavos(
        Resultados.Where(x => x.CompativelPorTolerancia && (x.DiferencaFaturaMenosOver ?? 0m) > 0m)
            .Sum(x => x.DiferencaFaturaMenosOver ?? 0m));
    public decimal SomaToleranciaOverMaior => AnaliseFaturasRegrasComparacao.ArredondarCentavos(
        Resultados.Where(x => x.CompativelPorTolerancia && (x.DiferencaFaturaMenosOver ?? 0m) < 0m)
            .Sum(x => Math.Abs(x.DiferencaFaturaMenosOver ?? 0m)));
    public decimal SaldoToleranciaLiquido => AnaliseFaturasRegrasComparacao.ArredondarCentavos(
        SomaToleranciaFaturaMaior - SomaToleranciaOverMaior);
}

public sealed class ComparacaoPrincipalResultado
{
    public string IdResultado { get; init; } = string.Empty;
    public string Certificado { get; init; } = string.Empty;
    public string NomeFatura { get; init; } = string.Empty;
    public string NomeOver { get; init; } = string.Empty;
    public VinculoBeneficiarioStatus StatusVinculo { get; init; }
    public ComparacaoPrincipalCategoria Categoria { get; init; }
    public decimal? ValorFatura { get; init; }
    public decimal? ValorOverComparavel { get; init; }
    public decimal? DiferencaFaturaMenosOver { get; init; }
    public string OrigemFatura { get; init; } = string.Empty;
    public string OrigemOver { get; init; } = string.Empty;
    public string FaturaOcorrenciaId { get; init; } = string.Empty;
    public string OverOcorrenciaId { get; init; } = string.Empty;
    public string Observacao { get; init; } = string.Empty;
    public bool CompativelPorTolerancia { get; init; }

    public string NomeReferencia => !string.IsNullOrWhiteSpace(NomeFatura) ? NomeFatura : NomeOver;
    public bool EhDivergencia => Categoria != ComparacaoPrincipalCategoria.EncontradoValorCompativel;
}

/// <summary>
/// Executa apenas a comparação principal da competência analisada:
/// fatura do mês passado x Over do mês passado.
/// Não consulta mês atual/mês seguinte, não aplica exceções temporais e não consulta Excel legado.
/// O valor do Over é NET comparável, com IOF sempre excluído e coparticipação
/// excluída por padrão (configurável pelo usuário).
/// Lançamentos da fatura com competência anterior à competência analisada também
/// são excluídos por padrão do valor principal, permanecendo disponíveis nos detalhes.
/// Diferenças de até R$ 0,30 para mais ou para menos são consideradas compatíveis,
/// mas permanecem registradas para somatória e rastreabilidade no resultado final.
/// </summary>
public sealed class ComparacaoPrincipalService
{
    public ComparacaoPrincipalDiagnostico Comparar(
        IReadOnlyList<FaturaBradescoArquivo> faturasMesPassado,
        OverArquivo overMesPassado,
        bool ignorarCoparticipacao = true,
        bool ignorarCompetenciasAnteriores = true)
    {
        if (faturasMesPassado == null)
            throw new ArgumentNullException(nameof(faturasMesPassado));
        if (overMesPassado == null)
            throw new ArgumentNullException(nameof(overMesPassado));

        DateTime competencia = ObterCompetencia(faturasMesPassado, overMesPassado);
        List<FaturaOcorrencia> faturas = CriarOcorrenciasFatura(
            faturasMesPassado,
            competencia,
            ignorarCompetenciasAnteriores);
        List<OverOcorrencia> overs = CriarOcorrenciasOver(overMesPassado, ignorarCoparticipacao);

        Dictionary<string, List<FaturaOcorrencia>> indiceFatura = faturas
            .Where(x => !string.IsNullOrWhiteSpace(x.Certificado))
            .GroupBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<OverOcorrencia>> indiceOver = overs
            .Where(x => !string.IsNullOrWhiteSpace(x.Certificado))
            .GroupBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, Resolucao> overParaFatura = overs.ToDictionary(
            x => x.Id,
            x => Resolver(x.Certificado, x.NomeNormalizado,
                indiceFatura.TryGetValue(x.Certificado, out List<FaturaOcorrencia>? candidatos)
                    ? candidatos.Select(c => new Candidato(c.Id, c.Nome, c.NomeNormalizado)).ToList()
                    : new List<Candidato>()));

        Dictionary<string, Resolucao> faturaParaOver = faturas.ToDictionary(
            x => x.Id,
            x => Resolver(x.Certificado, x.NomeNormalizado,
                indiceOver.TryGetValue(x.Certificado, out List<OverOcorrencia>? candidatos)
                    ? candidatos.Select(c => new Candidato(c.Id, c.Nome, c.NomeNormalizado)).ToList()
                    : new List<Candidato>()));

        var resultados = new List<ComparacaoPrincipalResultado>();
        var faturasRepresentadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (OverOcorrencia over in overs)
        {
            Resolucao ida = overParaFatura[over.Id];

            if (ida.Status == VinculoBeneficiarioStatus.NaoEncontrado)
            {
                resultados.Add(CriarNaoEncontradoNaFatura(over));
                continue;
            }

            if (ida.Status == VinculoBeneficiarioStatus.Ambiguo || string.IsNullOrWhiteSpace(ida.CandidatoEscolhidoId))
            {
                resultados.Add(CriarAmbiguo(over, null,
                    "O certificado não produz um único vínculo seguro entre Over e fatura."));
                continue;
            }

            FaturaOcorrencia? fatura = faturas.FirstOrDefault(x =>
                string.Equals(x.Id, ida.CandidatoEscolhidoId, StringComparison.OrdinalIgnoreCase));

            if (fatura == null)
            {
                resultados.Add(CriarAmbiguo(over, null,
                    "O candidato de fatura resolvido não pôde ser recuperado."));
                continue;
            }

            Resolucao volta = faturaParaOver[fatura.Id];
            bool relacaoBiunivoca =
                (volta.Status == VinculoBeneficiarioStatus.EncontradoUnico ||
                 volta.Status == VinculoBeneficiarioStatus.EncontradoPorNome) &&
                string.Equals(volta.CandidatoEscolhidoId, over.Id, StringComparison.OrdinalIgnoreCase);

            if (!relacaoBiunivoca)
            {
                faturasRepresentadas.Add(fatura.Id);
                resultados.Add(CriarAmbiguo(over, fatura,
                    "O vínculo parece resolvido em uma direção, mas não é unívoco na direção inversa. A comparação financeira foi bloqueada."));
                continue;
            }

            faturasRepresentadas.Add(fatura.Id);
            resultados.Add(CriarComparacaoSegura(over, fatura, ida.Status));
        }

        foreach (FaturaOcorrencia fatura in faturas)
        {
            if (faturasRepresentadas.Contains(fatura.Id))
                continue;

            Resolucao volta = faturaParaOver[fatura.Id];
            if (volta.Status == VinculoBeneficiarioStatus.NaoEncontrado)
            {
                resultados.Add(CriarNaoEncontradoNoOver(fatura));
            }
            else if (volta.Status == VinculoBeneficiarioStatus.Ambiguo)
            {
                resultados.Add(CriarAmbiguo(null, fatura,
                    "A ocorrência da fatura possui mais de um candidato possível no Over."));
            }
            else
            {
                // Se chegou aqui, o destino existe mas não formou uma relação biunívoca com esta ocorrência.
                resultados.Add(CriarAmbiguo(null, fatura,
                    "Existe candidato no Over, porém o vínculo não é biunívoco. A comparação financeira foi bloqueada."));
            }
        }

        List<ComparacaoPrincipalResultado> ordenados = resultados
            .OrderBy(x => OrdemCategoria(x.Categoria))
            .ThenBy(x => x.NomeReferencia, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ComparacaoPrincipalDiagnostico
        {
            CompetenciaAnalisada = competencia,
            Resultados = ordenados
        };
    }

    private static ComparacaoPrincipalResultado CriarComparacaoSegura(
        OverOcorrencia over,
        FaturaOcorrencia fatura,
        VinculoBeneficiarioStatus status)
    {
        decimal valorFatura = AnaliseFaturasRegrasComparacao.ArredondarCentavos(fatura.ValorTotal);
        decimal valorOver = AnaliseFaturasRegrasComparacao.ArredondarCentavos(over.NetComparavel);
        decimal diferenca = AnaliseFaturasRegrasComparacao.ArredondarCentavos(valorFatura - valorOver);

        bool compativelPorTolerancia = diferenca != 0m &&
            AnaliseFaturasRegrasComparacao.DentroToleranciaComparacaoPrincipal(diferenca);

        ComparacaoPrincipalCategoria categoria =
            diferenca == 0m || compativelPorTolerancia
                ? ComparacaoPrincipalCategoria.EncontradoValorCompativel
                : diferenca > 0m
                    ? ComparacaoPrincipalCategoria.ValorMaiorNaFatura
                    : ComparacaoPrincipalCategoria.ValorMaiorNoOver;

        return new ComparacaoPrincipalResultado
        {
            IdResultado = $"C|{fatura.Id}|{over.Id}",
            Certificado = over.Certificado,
            NomeFatura = fatura.Nome,
            NomeOver = over.Nome,
            StatusVinculo = status,
            Categoria = categoria,
            ValorFatura = valorFatura,
            ValorOverComparavel = valorOver,
            DiferencaFaturaMenosOver = diferenca,
            CompativelPorTolerancia = compativelPorTolerancia,
            OrigemFatura = fatura.Detalhe,
            OrigemOver = over.Detalhe,
            FaturaOcorrenciaId = fatura.Id,
            OverOcorrenciaId = over.Id,
            Observacao = compativelPorTolerancia
                ? $"Diferença de {diferenca:N2} considerada compatível pela tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}."
                : categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel
                    ? "Valores iguais até o centavo."
                    : "Diferença principal sem aplicar contexto de meses seguintes."
        };
    }

    private static ComparacaoPrincipalResultado CriarNaoEncontradoNaFatura(OverOcorrencia over)
    {
        decimal valorOver = AnaliseFaturasRegrasComparacao.ArredondarCentavos(over.NetComparavel);
        return new ComparacaoPrincipalResultado
        {
            IdResultado = $"OF|{over.Id}",
            Certificado = over.Certificado,
            NomeOver = over.Nome,
            StatusVinculo = VinculoBeneficiarioStatus.NaoEncontrado,
            Categoria = ComparacaoPrincipalCategoria.NaoEncontradoNaFatura,
            ValorFatura = 0m,
            ValorOverComparavel = valorOver,
            DiferencaFaturaMenosOver = AnaliseFaturasRegrasComparacao.ArredondarCentavos(-valorOver),
            OrigemOver = over.Detalhe,
            OverOcorrenciaId = over.Id,
            Observacao = "Cliente encontrado no Over, mas não encontrado na fatura da competência analisada."
        };
    }

    private static ComparacaoPrincipalResultado CriarNaoEncontradoNoOver(FaturaOcorrencia fatura)
    {
        decimal valorFatura = AnaliseFaturasRegrasComparacao.ArredondarCentavos(fatura.ValorTotal);
        return new ComparacaoPrincipalResultado
        {
            IdResultado = $"FO|{fatura.Id}",
            Certificado = fatura.Certificado,
            NomeFatura = fatura.Nome,
            StatusVinculo = VinculoBeneficiarioStatus.NaoEncontrado,
            Categoria = ComparacaoPrincipalCategoria.NaoEncontradoNoOver,
            ValorFatura = valorFatura,
            ValorOverComparavel = 0m,
            DiferencaFaturaMenosOver = valorFatura,
            OrigemFatura = fatura.Detalhe,
            FaturaOcorrenciaId = fatura.Id,
            Observacao = "Cliente encontrado na fatura, mas não encontrado no Over da competência analisada."
        };
    }

    private static ComparacaoPrincipalResultado CriarAmbiguo(
        OverOcorrencia? over,
        FaturaOcorrencia? fatura,
        string observacao)
    {
        return new ComparacaoPrincipalResultado
        {
            IdResultado = $"A|{fatura?.Id ?? "-"}|{over?.Id ?? "-"}",
            Certificado = over?.Certificado ?? fatura?.Certificado ?? string.Empty,
            NomeFatura = fatura?.Nome ?? string.Empty,
            NomeOver = over?.Nome ?? string.Empty,
            StatusVinculo = VinculoBeneficiarioStatus.Ambiguo,
            Categoria = ComparacaoPrincipalCategoria.Ambiguo,
            ValorFatura = fatura == null ? null : AnaliseFaturasRegrasComparacao.ArredondarCentavos(fatura.ValorTotal),
            ValorOverComparavel = over == null ? null : AnaliseFaturasRegrasComparacao.ArredondarCentavos(over.NetComparavel),
            DiferencaFaturaMenosOver = null,
            OrigemFatura = fatura?.Detalhe ?? string.Empty,
            OrigemOver = over?.Detalhe ?? string.Empty,
            FaturaOcorrenciaId = fatura?.Id ?? string.Empty,
            OverOcorrenciaId = over?.Id ?? string.Empty,
            Observacao = observacao
        };
    }

    private static Resolucao Resolver(string certificado, string nomeNormalizado, IReadOnlyList<Candidato> candidatos)
    {
        if (string.IsNullOrWhiteSpace(certificado) || candidatos.Count == 0)
            return new Resolucao(VinculoBeneficiarioStatus.NaoEncontrado, string.Empty);

        if (candidatos.Count == 1)
            return new Resolucao(VinculoBeneficiarioStatus.EncontradoUnico, candidatos[0].Id);

        if (string.IsNullOrWhiteSpace(nomeNormalizado))
            return new Resolucao(VinculoBeneficiarioStatus.Ambiguo, string.Empty);

        List<Candidato> porNome = candidatos
            .Where(x => string.Equals(x.NomeNormalizado, nomeNormalizado, StringComparison.Ordinal))
            .ToList();

        return porNome.Count == 1
            ? new Resolucao(VinculoBeneficiarioStatus.EncontradoPorNome, porNome[0].Id)
            : new Resolucao(VinculoBeneficiarioStatus.Ambiguo, string.Empty);
    }

    private static List<FaturaOcorrencia> CriarOcorrenciasFatura(
        IReadOnlyList<FaturaBradescoArquivo> arquivos,
        DateTime competenciaAnalisada,
        bool ignorarCompetenciasAnteriores)
    {
        var resultado = new List<FaturaOcorrencia>();

        foreach (FaturaBradescoArquivo arquivo in arquivos)
        {
            foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
            {
                foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
                {
                    string certificado = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(beneficiario.Certificado) ?? string.Empty;
                    string nomeNormalizado = AnaliseFaturasNormalizador.NormalizarNome(beneficiario.Nome);
                    string paginas = string.Join(",", beneficiario.Lancamentos.Select(x => x.PaginaPdf).Distinct().OrderBy(x => x));
                    string detalhe = $"{arquivo.NomeArquivo} • Subf. {subfatura.Numero} {subfatura.Entidade}";
                    if (!string.IsNullOrWhiteSpace(paginas))
                        detalhe += $" • Pág. PDF {paginas}";

                    string id = $"F|{arquivo.NomeArquivo}|{subfatura.Numero}|{certificado}|{nomeNormalizado}";
                    decimal valor = beneficiario.Lancamentos
                        .Where(x => AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                            x.Competencia,
                            competenciaAnalisada,
                            ignorarCompetenciasAnteriores))
                        .Sum(x => x.Valor);

                    resultado.Add(new FaturaOcorrencia(id, certificado, beneficiario.Nome, nomeNormalizado, detalhe, valor));
                }
            }
        }

        return resultado;
    }

    private static List<OverOcorrencia> CriarOcorrenciasOver(OverArquivo over, bool ignorarCoparticipacao)
    {
        return over.Lancamentos
            .Select(x => new
            {
                Lancamento = x,
                Certificado = AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(x.Cartao) ?? string.Empty,
                NomeNormalizado = AnaliseFaturasNormalizador.NormalizarNome(x.Beneficiario),
                MatriculaNormalizada = Compactar(x.Matricula),
                EntidadeNormalizada = Compactar(x.Entidade),
                ApoliceNormalizada = Compactar(x.Apolice)
            })
            .GroupBy(x => new
            {
                x.Certificado,
                x.NomeNormalizado,
                x.MatriculaNormalizada,
                x.EntidadeNormalizada,
                x.ApoliceNormalizada
            })
            .Select(g =>
            {
                List<OverLancamento> lancamentos = g.Select(x => x.Lancamento).OrderBy(x => x.NumeroLinha).ToList();
                OverLancamento primeiro = lancamentos[0];
                string linhas = string.Join(",", lancamentos.Select(x => x.NumeroLinha));
                string detalhe = $"{over.NomeArquivo} • linha(s) {linhas}";
                if (!string.IsNullOrWhiteSpace(primeiro.Entidade))
                    detalhe += $" • {primeiro.Entidade}";
                if (!string.IsNullOrWhiteSpace(primeiro.Matricula))
                    detalhe += $" • Matr. {primeiro.Matricula}";

                string id = $"O|{linhas}";
                decimal net = AnaliseFaturasRegrasComparacao.SomarNetComparavel(lancamentos, ignorarCoparticipacao);
                return new OverOcorrencia(id, g.Key.Certificado, primeiro.Beneficiario, g.Key.NomeNormalizado, detalhe, net);
            })
            .OrderBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DateTime ObterCompetencia(IReadOnlyList<FaturaBradescoArquivo> faturas, OverArquivo over)
    {
        DateTime? competenciaFatura = faturas.Select(x => x.Competencia).FirstOrDefault(x => x.HasValue);
        DateTime? competenciaOver = over.Competencia;

        if (!competenciaFatura.HasValue || !competenciaOver.HasValue)
            throw new InvalidOperationException("Não foi possível determinar a competência principal para a comparação.");

        DateTime f = new(competenciaFatura.Value.Year, competenciaFatura.Value.Month, 1);
        DateTime o = new(competenciaOver.Value.Year, competenciaOver.Value.Month, 1);
        if (f != o)
            throw new InvalidOperationException($"Competências incompatíveis: fatura {f:MM/yyyy} e Over {o:MM/yyyy}.");

        if (faturas.Any(x => x.Competencia.HasValue && new DateTime(x.Competencia.Value.Year, x.Competencia.Value.Month, 1) != f))
            throw new InvalidOperationException("As faturas do mês passado não possuem uma única competência.");

        return f;
    }

    private static int OrdemCategoria(ComparacaoPrincipalCategoria categoria) => categoria switch
    {
        ComparacaoPrincipalCategoria.Ambiguo => 0,
        ComparacaoPrincipalCategoria.NaoEncontradoNaFatura => 1,
        ComparacaoPrincipalCategoria.NaoEncontradoNoOver => 2,
        ComparacaoPrincipalCategoria.ValorMaiorNaFatura => 3,
        ComparacaoPrincipalCategoria.ValorMaiorNoOver => 4,
        _ => 5
    };

    private static string Compactar(string? texto)
        => string.IsNullOrWhiteSpace(texto)
            ? string.Empty
            : string.Join(" ", texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record FaturaOcorrencia(string Id, string Certificado, string Nome, string NomeNormalizado, string Detalhe, decimal ValorTotal);
    private sealed record OverOcorrencia(string Id, string Certificado, string Nome, string NomeNormalizado, string Detalhe, decimal NetComparavel);
    private sealed record Candidato(string Id, string Nome, string NomeNormalizado);
    private sealed record Resolucao(VinculoBeneficiarioStatus Status, string CandidatoEscolhidoId);
}
