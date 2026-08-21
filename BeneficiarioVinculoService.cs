using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HubFinanceiro;

public enum VinculoBeneficiarioStatus
{
    EncontradoUnico,
    EncontradoPorNome,
    Ambiguo,
    NaoEncontrado
}

public enum VinculoBeneficiarioDirecao
{
    OverParaFatura,
    FaturaParaOver
}

public sealed class VinculoBeneficiariosDiagnostico
{
    public IReadOnlyList<VinculoBeneficiarioResultado> Resultados { get; init; } = Array.Empty<VinculoBeneficiarioResultado>();
    public int TotalOverOcorrencias { get; init; }
    public int TotalFaturaOcorrencias { get; init; }

    public int TotalEncontradoUnico => Resultados.Count(x => x.Status == VinculoBeneficiarioStatus.EncontradoUnico);
    public int TotalEncontradoPorNome => Resultados.Count(x => x.Status == VinculoBeneficiarioStatus.EncontradoPorNome);
    public int TotalAmbiguo => Resultados.Count(x => x.Status == VinculoBeneficiarioStatus.Ambiguo);
    public int TotalNaoEncontrado => Resultados.Count(x => x.Status == VinculoBeneficiarioStatus.NaoEncontrado);
}

public sealed class VinculoBeneficiarioResultado
{
    public VinculoBeneficiarioDirecao Direcao { get; init; }
    public VinculoBeneficiarioStatus Status { get; init; }
    public string Certificado { get; init; } = string.Empty;
    public string NomeOrigem { get; init; } = string.Empty;
    public string NomeDestino { get; init; } = string.Empty;
    public string NomeNormalizadoOrigem { get; init; } = string.Empty;
    public string OrigemDetalhe { get; init; } = string.Empty;
    public string DestinoDetalhe { get; init; } = string.Empty;
    public int QuantidadeCandidatosCertificado { get; init; }
    public int QuantidadeCandidatosNome { get; init; }
    public int QuantidadeLancamentosOrigem { get; init; }
    public string Observacao { get; init; } = string.Empty;
}

/// <summary>
/// Resolve exclusivamente a identidade de beneficiários entre Over e fatura.
/// Não soma valores, não aplica tolerância financeira e não consulta Excel legado.
/// Regras:
/// 1) certificado único -> vínculo direto;
/// 2) certificado duplicado -> certificado + nome normalizado;
/// 3) ambiguidade nunca é escondida;
/// 4) nome nunca é utilizado sozinho quando o certificado não existe/não normaliza.
/// </summary>
public sealed class BeneficiarioVinculoService
{
    public VinculoBeneficiariosDiagnostico CriarDiagnostico(
        IReadOnlyList<FaturaBradescoArquivo> faturas,
        OverArquivo over)
    {
        if (faturas == null)
            throw new ArgumentNullException(nameof(faturas));
        if (over == null)
            throw new ArgumentNullException(nameof(over));

        List<FaturaOcorrencia> ocorrenciasFatura = CriarOcorrenciasFatura(faturas);
        List<OverOcorrencia> ocorrenciasOver = CriarOcorrenciasOver(over);

        var resultados = new List<VinculoBeneficiarioResultado>(
            ocorrenciasFatura.Count + ocorrenciasOver.Count);

        resultados.AddRange(ResolverOverParaFatura(ocorrenciasOver, ocorrenciasFatura));
        resultados.AddRange(ResolverFaturaParaOver(ocorrenciasFatura, ocorrenciasOver));

        return new VinculoBeneficiariosDiagnostico
        {
            Resultados = resultados,
            TotalOverOcorrencias = ocorrenciasOver.Count,
            TotalFaturaOcorrencias = ocorrenciasFatura.Count
        };
    }

    private static List<VinculoBeneficiarioResultado> ResolverOverParaFatura(
        IReadOnlyList<OverOcorrencia> origens,
        IReadOnlyList<FaturaOcorrencia> destinos)
    {
        Dictionary<string, List<FaturaOcorrencia>> indice = destinos
            .Where(x => !string.IsNullOrWhiteSpace(x.Certificado))
            .GroupBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return origens
            .Select(origem => Resolver(
                VinculoBeneficiarioDirecao.OverParaFatura,
                origem.Certificado,
                origem.Nome,
                origem.NomeNormalizado,
                origem.Detalhe,
                origem.QuantidadeLancamentos,
                indice.TryGetValue(origem.Certificado, out List<FaturaOcorrencia>? candidatos)
                    ? candidatos.Select(x => new Candidato(x.Nome, x.NomeNormalizado, x.Detalhe)).ToList()
                    : new List<Candidato>()))
            .ToList();
    }

    private static List<VinculoBeneficiarioResultado> ResolverFaturaParaOver(
        IReadOnlyList<FaturaOcorrencia> origens,
        IReadOnlyList<OverOcorrencia> destinos)
    {
        Dictionary<string, List<OverOcorrencia>> indice = destinos
            .Where(x => !string.IsNullOrWhiteSpace(x.Certificado))
            .GroupBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return origens
            .Select(origem => Resolver(
                VinculoBeneficiarioDirecao.FaturaParaOver,
                origem.Certificado,
                origem.Nome,
                origem.NomeNormalizado,
                origem.Detalhe,
                origem.QuantidadeLancamentos,
                indice.TryGetValue(origem.Certificado, out List<OverOcorrencia>? candidatos)
                    ? candidatos.Select(x => new Candidato(x.Nome, x.NomeNormalizado, x.Detalhe)).ToList()
                    : new List<Candidato>()))
            .ToList();
    }

    private static VinculoBeneficiarioResultado Resolver(
        VinculoBeneficiarioDirecao direcao,
        string certificado,
        string nomeOrigem,
        string nomeNormalizadoOrigem,
        string origemDetalhe,
        int quantidadeLancamentosOrigem,
        IReadOnlyList<Candidato> candidatos)
    {
        string cert = certificado ?? string.Empty;
        string nomeNorm = nomeNormalizadoOrigem ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cert))
        {
            return Resultado(
                direcao,
                VinculoBeneficiarioStatus.NaoEncontrado,
                cert,
                nomeOrigem,
                nomeNorm,
                origemDetalhe,
                quantidadeLancamentosOrigem,
                candidatos,
                Array.Empty<Candidato>(),
                "O certificado não pôde ser normalizado. O nome não é usado isoladamente como chave.");
        }

        if (candidatos.Count == 0)
        {
            return Resultado(
                direcao,
                VinculoBeneficiarioStatus.NaoEncontrado,
                cert,
                nomeOrigem,
                nomeNorm,
                origemDetalhe,
                quantidadeLancamentosOrigem,
                candidatos,
                Array.Empty<Candidato>(),
                "Nenhuma ocorrência com este certificado foi encontrada no destino.");
        }

        if (candidatos.Count == 1)
        {
            Candidato candidato = candidatos[0];
            bool nomeDiferente = !string.IsNullOrWhiteSpace(nomeNorm) &&
                                 !string.Equals(nomeNorm, candidato.NomeNormalizado, StringComparison.Ordinal);

            return Resultado(
                direcao,
                VinculoBeneficiarioStatus.EncontradoUnico,
                cert,
                nomeOrigem,
                nomeNorm,
                origemDetalhe,
                quantidadeLancamentosOrigem,
                candidatos,
                candidatos,
                nomeDiferente
                    ? "Certificado único: vínculo direto mantido. Os nomes normalizados são diferentes e ficam registrados para conferência."
                    : "Certificado único: vínculo direto.");
        }

        if (string.IsNullOrWhiteSpace(nomeNorm))
        {
            return Resultado(
                direcao,
                VinculoBeneficiarioStatus.Ambiguo,
                cert,
                nomeOrigem,
                nomeNorm,
                origemDetalhe,
                quantidadeLancamentosOrigem,
                candidatos,
                Array.Empty<Candidato>(),
                "O certificado possui mais de uma ocorrência e o nome de origem está vazio; não é possível desempatar.");
        }

        List<Candidato> porNome = candidatos
            .Where(x => string.Equals(x.NomeNormalizado, nomeNorm, StringComparison.Ordinal))
            .ToList();

        if (porNome.Count == 1)
        {
            return Resultado(
                direcao,
                VinculoBeneficiarioStatus.EncontradoPorNome,
                cert,
                nomeOrigem,
                nomeNorm,
                origemDetalhe,
                quantidadeLancamentosOrigem,
                candidatos,
                porNome,
                "Certificado duplicado: um único candidato permaneceu após o desempate por nome normalizado.");
        }

        if (porNome.Count > 1)
        {
            return Resultado(
                direcao,
                VinculoBeneficiarioStatus.Ambiguo,
                cert,
                nomeOrigem,
                nomeNorm,
                origemDetalhe,
                quantidadeLancamentosOrigem,
                candidatos,
                porNome,
                "Mesmo certificado e mesmo nome aparecem em mais de uma ocorrência; a ambiguidade foi preservada.");
        }

        return Resultado(
            direcao,
            VinculoBeneficiarioStatus.Ambiguo,
            cert,
            nomeOrigem,
            nomeNorm,
            origemDetalhe,
            quantidadeLancamentosOrigem,
            candidatos,
            porNome,
            "O certificado é duplicado, mas nenhum candidato possui o mesmo nome normalizado; não há vínculo seguro.");
    }

    private static VinculoBeneficiarioResultado Resultado(
        VinculoBeneficiarioDirecao direcao,
        VinculoBeneficiarioStatus status,
        string certificado,
        string nomeOrigem,
        string nomeNormalizadoOrigem,
        string origemDetalhe,
        int quantidadeLancamentosOrigem,
        IReadOnlyList<Candidato> candidatosCertificado,
        IReadOnlyList<Candidato> candidatosNome,
        string observacao)
    {
        IReadOnlyList<Candidato> escolhidos = status switch
        {
            VinculoBeneficiarioStatus.EncontradoUnico => candidatosCertificado.Take(1).ToList(),
            VinculoBeneficiarioStatus.EncontradoPorNome => candidatosNome.Take(1).ToList(),
            _ => Array.Empty<Candidato>()
        };

        return new VinculoBeneficiarioResultado
        {
            Direcao = direcao,
            Status = status,
            Certificado = certificado,
            NomeOrigem = nomeOrigem ?? string.Empty,
            NomeDestino = escolhidos.Count == 1 ? escolhidos[0].Nome : string.Empty,
            NomeNormalizadoOrigem = nomeNormalizadoOrigem ?? string.Empty,
            OrigemDetalhe = origemDetalhe ?? string.Empty,
            DestinoDetalhe = escolhidos.Count == 1
                ? escolhidos[0].Detalhe
                : candidatosCertificado.Count == 0
                    ? string.Empty
                    : string.Join("  |  ", candidatosCertificado.Select(x => x.Detalhe).Distinct(StringComparer.OrdinalIgnoreCase).Take(5)),
            QuantidadeCandidatosCertificado = candidatosCertificado.Count,
            QuantidadeCandidatosNome = candidatosNome.Count,
            QuantidadeLancamentosOrigem = quantidadeLancamentosOrigem,
            Observacao = observacao
        };
    }

    private static List<FaturaOcorrencia> CriarOcorrenciasFatura(IReadOnlyList<FaturaBradescoArquivo> faturas)
    {
        var resultado = new List<FaturaOcorrencia>();

        foreach (FaturaBradescoArquivo arquivo in faturas)
        {
            foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
            {
                foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
                {
                    string certificado = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(beneficiario.Certificado)
                        ?? string.Empty;
                    string nomeNorm = AnaliseFaturasNormalizador.NormalizarNome(beneficiario.Nome);

                    string paginas = string.Join(",", beneficiario.Lancamentos
                        .Select(x => x.PaginaPdf)
                        .Distinct()
                        .OrderBy(x => x));

                    string detalhe = $"{arquivo.NomeArquivo} • Subf. {subfatura.Numero} {subfatura.Entidade}";
                    if (!string.IsNullOrWhiteSpace(paginas))
                        detalhe += $" • Pág. PDF {paginas}";

                    resultado.Add(new FaturaOcorrencia(
                        certificado,
                        beneficiario.Nome,
                        nomeNorm,
                        detalhe,
                        beneficiario.Lancamentos.Count));
                }
            }
        }

        return resultado;
    }

    private static List<OverOcorrencia> CriarOcorrenciasOver(OverArquivo over)
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
                OverLancamento primeiro = g.OrderBy(x => x.Lancamento.NumeroLinha).First().Lancamento;
                string linhas = string.Join(",", g.Select(x => x.Lancamento.NumeroLinha).Distinct().OrderBy(x => x));
                string detalhe = $"{over.NomeArquivo} • linha(s) {linhas}";
                if (!string.IsNullOrWhiteSpace(primeiro.Entidade))
                    detalhe += $" • {primeiro.Entidade}";
                if (!string.IsNullOrWhiteSpace(primeiro.Matricula))
                    detalhe += $" • Matr. {primeiro.Matricula}";

                return new OverOcorrencia(
                    g.Key.Certificado,
                    primeiro.Beneficiario,
                    g.Key.NomeNormalizado,
                    detalhe,
                    g.Count());
            })
            .OrderBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string Compactar(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? string.Empty : string.Join(" ", texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record Candidato(string Nome, string NomeNormalizado, string Detalhe);
    private sealed record FaturaOcorrencia(string Certificado, string Nome, string NomeNormalizado, string Detalhe, int QuantidadeLancamentos);
    private sealed record OverOcorrencia(string Certificado, string Nome, string NomeNormalizado, string Detalhe, int QuantidadeLancamentos);
}
