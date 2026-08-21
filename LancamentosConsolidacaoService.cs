using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public sealed class LancamentosConsolidacaoDiagnostico
{
    public IReadOnlyList<ComposicaoBeneficiario> Composicoes { get; init; } = Array.Empty<ComposicaoBeneficiario>();
    public int TotalOcorrenciasOver { get; init; }
    public int TotalVinculosResolvidos { get; init; }
    public int TotalVinculosNaoConsolidados { get; init; }
    public bool IgnorandoCompetenciasAnteriores { get; init; } = true;
}

public sealed class ComposicaoBeneficiario
{
    public string Certificado { get; init; } = string.Empty;
    public string NomeFatura { get; init; } = string.Empty;
    public string NomeOver { get; init; } = string.Empty;
    public VinculoBeneficiarioStatus StatusVinculo { get; init; }
    public string OrigemFatura { get; init; } = string.Empty;
    public string OrigemOver { get; init; } = string.Empty;
    public IReadOnlyList<ComponenteFatura> ComponentesFatura { get; init; } = Array.Empty<ComponenteFatura>();
    public IReadOnlyList<ComponenteOver> ComponentesOver { get; init; } = Array.Empty<ComponenteOver>();

    public decimal TotalValorFaturaBruto => ComponentesFatura.Sum(x => x.Valor);
    public decimal TotalValorFatura => ComponentesFatura
        .Where(x => x.ConsiderarNoComparavel)
        .Sum(x => x.Valor);
    public decimal TotalValorFaturaIgnoradoCompetenciasAnteriores => ComponentesFatura
        .Where(x => !x.ConsiderarNoComparavel)
        .Sum(x => x.Valor);
    public decimal TotalParticipacaoFatura => ComponentesFatura
        .Where(x => x.ConsiderarNoComparavel)
        .Sum(x => x.Participacao ?? 0m);
    public decimal TotalPVOver => ComponentesOver.Sum(x => x.ValorPV ?? 0m);
    public decimal TotalNETBrutoOver => ComponentesOver.Sum(x => x.ValorNET ?? 0m);
    public decimal TotalIOFNETIgnorado => ComponentesOver
        .Where(x => !x.ConsiderarNoNETComparavel && AnaliseFaturasRegrasComparacao.EhIof(x.Evento, x.Descricao))
        .Sum(x => x.ValorNET ?? 0m);
    public decimal TotalCopartNETIgnorado => ComponentesOver
        .Where(x => !x.ConsiderarNoNETComparavel && AnaliseFaturasRegrasComparacao.EhCoparticipacao(x.Evento, x.Descricao))
        .Sum(x => x.ValorNET ?? 0m);
    public decimal TotalNETIgnorado => ComponentesOver
        .Where(x => !x.ConsiderarNoNETComparavel)
        .Sum(x => x.ValorNET ?? 0m);
    public decimal TotalNETOver => ComponentesOver
        .Where(x => x.ConsiderarNoNETComparavel)
        .Sum(x => x.ValorNET ?? 0m);
    public decimal TotalOver => ComponentesOver.Sum(x => x.ValorOver ?? 0m);
}

public sealed class ComponenteFatura
{
    public int PaginaPdf { get; init; }
    public int? PaginaFatura { get; init; }
    public int Subfatura { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public string Movimento { get; init; } = string.Empty;
    public DateTime Competencia { get; init; }
    public string Plano { get; init; } = string.Empty;
    public decimal Valor { get; init; }
    public decimal? Participacao { get; init; }
    public string Natureza { get; init; } = string.Empty;
    public string TextoOrigem { get; init; } = string.Empty;
    public bool ConsiderarNoComparavel { get; init; } = true;
    public string RegraComparacao { get; init; } = "Considerado no valor comparável";
}

public sealed class ComponenteOver
{
    public int NumeroLinha { get; init; }
    public DateTime? Competencia { get; init; }
    public string Evento { get; init; } = string.Empty;
    public string Descricao { get; init; } = string.Empty;
    public decimal? ValorPV { get; init; }
    public decimal? ValorNET { get; init; }
    public decimal? ValorOver { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public string Matricula { get; init; } = string.Empty;
    public string Cartao { get; init; } = string.Empty;
    public string Natureza { get; init; } = string.Empty;
    public bool ConsiderarNoNETComparavel { get; init; } = true;
    public string RegraComparacao { get; init; } = string.Empty;
}

/// <summary>
/// Monta uma visão explicável dos lançamentos associados a beneficiários cujo vínculo
/// entre Over e fatura foi resolvido com segurança.
///
/// Esta etapa preserva os componentes brutos e registra regras de composição
/// já validadas. O IOF permanece visível para rastreabilidade, mas NÃO compõe
/// o NET comparável, pois não é uma cobrança da Bradesco.
/// Ainda NÃO aplica tolerância, NÃO gera divergência e NÃO consulta o Excel legado.
/// </summary>
public sealed class LancamentosConsolidacaoService
{
    public LancamentosConsolidacaoDiagnostico CriarDiagnostico(
        IReadOnlyList<FaturaBradescoArquivo> faturas,
        OverArquivo over,
        bool ignorarCoparticipacao = true,
        bool ignorarCompetenciasAnteriores = true)
    {
        if (faturas == null)
            throw new ArgumentNullException(nameof(faturas));
        if (over == null)
            throw new ArgumentNullException(nameof(over));

        DateTime competenciaAnalisada = ObterCompetenciaAnalise(faturas, over);

        List<FaturaReferencia> referenciasFatura = CriarReferenciasFatura(faturas);
        List<OverReferencia> referenciasOver = CriarReferenciasOver(over);

        Dictionary<string, List<FaturaReferencia>> indiceFatura = referenciasFatura
            .Where(x => !string.IsNullOrWhiteSpace(x.Certificado))
            .GroupBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var composicoes = new List<ComposicaoBeneficiario>();
        int naoConsolidados = 0;

        foreach (OverReferencia origem in referenciasOver)
        {
            if (string.IsNullOrWhiteSpace(origem.Certificado) ||
                !indiceFatura.TryGetValue(origem.Certificado, out List<FaturaReferencia>? candidatos) ||
                candidatos.Count == 0)
            {
                naoConsolidados++;
                continue;
            }

            FaturaReferencia? escolhido = null;
            VinculoBeneficiarioStatus status;

            if (candidatos.Count == 1)
            {
                escolhido = candidatos[0];
                status = VinculoBeneficiarioStatus.EncontradoUnico;
            }
            else
            {
                List<FaturaReferencia> porNome = candidatos
                    .Where(x => string.Equals(x.NomeNormalizado, origem.NomeNormalizado, StringComparison.Ordinal))
                    .ToList();

                if (porNome.Count == 1)
                {
                    escolhido = porNome[0];
                    status = VinculoBeneficiarioStatus.EncontradoPorNome;
                }
                else
                {
                    naoConsolidados++;
                    continue;
                }
            }

            IReadOnlyList<ComponenteFatura> componentesFatura = escolhido.Beneficiario.Lancamentos
                .Select(x => new ComponenteFatura
                {
                    PaginaPdf = x.PaginaPdf,
                    PaginaFatura = x.PaginaFatura,
                    Subfatura = escolhido.Subfatura.Numero,
                    Entidade = escolhido.Subfatura.Entidade,
                    Movimento = x.Movimento,
                    Competencia = x.Competencia,
                    Plano = string.IsNullOrWhiteSpace(x.Plano) ? escolhido.Beneficiario.Plano : x.Plano,
                    Valor = x.Valor,
                    Participacao = x.Participacao,
                    Natureza = ClassificarFatura(x.Movimento),
                    TextoOrigem = x.TextoOrigem,
                    ConsiderarNoComparavel = AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                        x.Competencia,
                        competenciaAnalisada,
                        ignorarCompetenciasAnteriores),
                    RegraComparacao = CriarRegraComparacaoFatura(
                        x.Competencia,
                        competenciaAnalisada,
                        ignorarCompetenciasAnteriores)
                })
                .OrderBy(x => x.Competencia)
                .ThenBy(x => x.PaginaPdf)
                .ToList();

            IReadOnlyList<ComponenteOver> componentesOver = origem.Lancamentos
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
                    Natureza = ClassificarOver(x.Evento, x.Descricao),
                    ConsiderarNoNETComparavel = AnaliseFaturasRegrasComparacao.ConsiderarNoNetComparavel(x, ignorarCoparticipacao),
                    RegraComparacao = CriarRegraComparacaoOver(x, ignorarCoparticipacao)
                })
                .OrderBy(x => x.NumeroLinha)
                .ToList();

            composicoes.Add(new ComposicaoBeneficiario
            {
                Certificado = origem.Certificado,
                NomeFatura = escolhido.Beneficiario.Nome,
                NomeOver = origem.Nome,
                StatusVinculo = status,
                OrigemFatura = escolhido.Detalhe,
                OrigemOver = origem.Detalhe,
                ComponentesFatura = componentesFatura,
                ComponentesOver = componentesOver
            });
        }

        List<ComposicaoBeneficiario> ordenadas = composicoes
            .OrderBy(x => x.NomeFatura, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LancamentosConsolidacaoDiagnostico
        {
            Composicoes = ordenadas,
            TotalOcorrenciasOver = referenciasOver.Count,
            TotalVinculosResolvidos = ordenadas.Count,
            TotalVinculosNaoConsolidados = naoConsolidados,
            IgnorandoCompetenciasAnteriores = ignorarCompetenciasAnteriores
        };
    }

    private static List<FaturaReferencia> CriarReferenciasFatura(IReadOnlyList<FaturaBradescoArquivo> faturas)
    {
        var resultado = new List<FaturaReferencia>();

        foreach (FaturaBradescoArquivo arquivo in faturas)
        {
            foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
            {
                foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
                {
                    string certificado = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(beneficiario.Certificado)
                        ?? string.Empty;
                    string nomeNormalizado = AnaliseFaturasNormalizador.NormalizarNome(beneficiario.Nome);
                    string paginas = string.Join(",", beneficiario.Lancamentos
                        .Select(x => x.PaginaPdf)
                        .Distinct()
                        .OrderBy(x => x));

                    string detalhe = $"{arquivo.NomeArquivo} • Subf. {subfatura.Numero} {subfatura.Entidade}";
                    if (!string.IsNullOrWhiteSpace(paginas))
                        detalhe += $" • Pág. PDF {paginas}";

                    resultado.Add(new FaturaReferencia(
                        certificado,
                        beneficiario.Nome,
                        nomeNormalizado,
                        detalhe,
                        subfatura,
                        beneficiario));
                }
            }
        }

        return resultado;
    }

    private static List<OverReferencia> CriarReferenciasOver(OverArquivo over)
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
                List<OverLancamento> lancamentos = g
                    .Select(x => x.Lancamento)
                    .OrderBy(x => x.NumeroLinha)
                    .ToList();

                OverLancamento primeiro = lancamentos[0];
                string linhas = string.Join(",", lancamentos.Select(x => x.NumeroLinha));
                string detalhe = $"{over.NomeArquivo} • linha(s) {linhas}";
                if (!string.IsNullOrWhiteSpace(primeiro.Entidade))
                    detalhe += $" • {primeiro.Entidade}";
                if (!string.IsNullOrWhiteSpace(primeiro.Matricula))
                    detalhe += $" • Matr. {primeiro.Matricula}";

                return new OverReferencia(
                    g.Key.Certificado,
                    primeiro.Beneficiario,
                    g.Key.NomeNormalizado,
                    detalhe,
                    lancamentos);
            })
            .OrderBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }


    private static string CriarRegraComparacaoFatura(
        DateTime competenciaLancamento,
        DateTime competenciaAnalisada,
        bool ignorarCompetenciasAnteriores)
    {
        if (ignorarCompetenciasAnteriores &&
            !AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                competenciaLancamento,
                competenciaAnalisada,
                true))
        {
            return $"Ignorado no valor comparável — competência {competenciaLancamento:MM/yyyy} anterior à analisada {competenciaAnalisada:MM/yyyy}";
        }

        return "Considerado no valor comparável";
    }

    private static DateTime ObterCompetenciaAnalise(
        IReadOnlyList<FaturaBradescoArquivo> faturas,
        OverArquivo over)
    {
        DateTime? competencia = over.Competencia
            ?? faturas.Select(x => x.Competencia).FirstOrDefault(x => x.HasValue)
            ?? faturas
                .SelectMany(x => x.Subfaturas)
                .SelectMany(x => x.Beneficiarios)
                .SelectMany(x => x.Lancamentos)
                .Select(x => (DateTime?)x.Competencia)
                .FirstOrDefault(x => x.HasValue && x.Value.Year > 2000)
            ?? over.Lancamentos
                .Select(x => x.Competencia)
                .FirstOrDefault(x => x.HasValue);

        if (!competencia.HasValue)
            throw new InvalidOperationException("Não foi possível determinar a competência da análise.");

        return new DateTime(competencia.Value.Year, competencia.Value.Month, 1);
    }

    private static string ClassificarFatura(string? movimento)
    {
        string mov = (movimento ?? string.Empty).Trim().ToUpperInvariant();
        return mov switch
        {
            "" => "Sem MOV / cobrança base",
            "AC" => "AC • Acerto cobrar",
            "RS" => "RS • Recuperação de sinistro",
            "IM" => "IM • Inclusão no mês",
            "IR" => "IR • Inclusão retroativa",
            "RM" => "RM • Reativação no mês",
            "RR" => "RR • Reativação retroativa",
            "TM" => "TM • Transferência no mês",
            "TR" => "TR • Transferência retroativa",
            "AM" => "AM • Alteração no mês",
            "AR" => "AR • Alteração retroativa",
            "AD" => "AD • Acerto devolver",
            "CM" => "CM • Cancelamento no mês",
            "CR" => "CR • Cancelamento retroativo",
            "DM" => "DM • Devolução remido no mês",
            "DR" => "DR • Devolução remido retroativa",
            _ => $"MOV {mov}"
        };
    }

    private static bool EhIof(string? evento, string? descricao)
        => AnaliseFaturasRegrasComparacao.EhIof(evento, descricao);

    private static string CriarRegraComparacaoOver(OverLancamento lancamento, bool ignorarCoparticipacao)
    {
        if (AnaliseFaturasRegrasComparacao.EhIof(lancamento.Evento, lancamento.Descricao))
            return "Ignorado no NET comparável — IOF não cobrado pela Bradesco";

        if (ignorarCoparticipacao && AnaliseFaturasRegrasComparacao.EhCoparticipacao(lancamento.Evento, lancamento.Descricao))
            return "Ignorado no NET comparável — coparticipação não cobrada pela Bradesco da mesma forma";

        if (!ignorarCoparticipacao && AnaliseFaturasRegrasComparacao.EhCoparticipacao(lancamento.Evento, lancamento.Descricao))
            return "Considerado no NET — opção de ignorar coparticipação desmarcada";

        return "Considerado no NET comparável";
    }

    private static string ClassificarOver(string? evento, string? descricao)
    {
        string ev = (evento ?? string.Empty).Trim();
        string desc = AnaliseFaturasNormalizador.NormalizarNome(descricao);

        if (AnaliseFaturasRegrasComparacao.EhIof(ev, desc))
            return "IOF";

        if (ev == "116" ||
            desc.Contains("COPARTICIP", StringComparison.Ordinal) ||
            desc.Contains("CO PARTICIP", StringComparison.Ordinal))
        {
            return "Coparticipação / fator moderador";
        }

        return string.IsNullOrWhiteSpace(ev) ? "Evento sem código" : $"Evento {ev}";
    }

    private static string Compactar(string? texto)
        => string.IsNullOrWhiteSpace(texto)
            ? string.Empty
            : string.Join(" ", texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record FaturaReferencia(
        string Certificado,
        string Nome,
        string NomeNormalizado,
        string Detalhe,
        FaturaBradescoSubfatura Subfatura,
        FaturaBradescoBeneficiario Beneficiario);

    private sealed record OverReferencia(
        string Certificado,
        string Nome,
        string NomeNormalizado,
        string Detalhe,
        IReadOnlyList<OverLancamento> Lancamentos);
}
