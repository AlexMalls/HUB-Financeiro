using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HubFinanceiro;

public sealed class AnaliseFaturasExplicacaoRecorrenteRegistro
{
    public string ChaveCliente { get; init; } = string.Empty;
    public string Beneficiario { get; init; } = string.Empty;
    public string Certificado { get; init; } = string.Empty;
    public string Entidade { get; init; } = string.Empty;
    public string Explicacao { get; init; } = string.Empty;
    public DateTime AtualizadoEm { get; init; }
}

internal sealed class AnaliseFaturasExplicacoesRecorrentesArquivo
{
    public int Versao { get; init; } = 1;
    public IReadOnlyList<AnaliseFaturasExplicacaoRecorrenteRegistro> Registros { get; init; }
        = Array.Empty<AnaliseFaturasExplicacaoRecorrenteRegistro>();
}

/// <summary>
/// Mantém explicações manuais que devem ser reaplicadas ao mesmo cliente em análises futuras.
/// A identidade prioriza Entidade + Certificado; o nome é usado apenas quando o certificado
/// não está disponível.
/// </summary>
public sealed class AnaliseFaturasExplicacaoRecorrenteService
{
    private const string NomeArquivo = "explicacoes_recorrentes.json";
    private readonly string _caminhoArquivo;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AnaliseFaturasExplicacaoRecorrenteService(string caminhoBaseData)
    {
        if (string.IsNullOrWhiteSpace(caminhoBaseData))
            throw new ArgumentException("O caminho-base de dados não foi informado.", nameof(caminhoBaseData));

        _caminhoArquivo = Path.Combine(
            caminhoBaseData,
            "Relatórios de Analise",
            NomeArquivo);
    }

    public AnaliseFaturasExplicacaoRecorrenteRegistro? Obter(AnaliseFinalResultado resultado)
    {
        string? chave = CriarChaveCliente(resultado);
        if (chave == null)
            return null;

        return CarregarRegistros().FirstOrDefault(x =>
            string.Equals(x.ChaveCliente, chave, StringComparison.Ordinal));
    }

    public void Salvar(AnaliseFinalResultado resultado, string explicacao)
    {
        string texto = explicacao?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException("A explicação recorrente não pode ficar vazia.", nameof(explicacao));

        string chave = CriarChaveCliente(resultado)
            ?? throw new InvalidOperationException("O cliente não possui certificado ou nome suficiente para criar a recorrência.");

        List<AnaliseFaturasExplicacaoRecorrenteRegistro> registros = CarregarRegistros();
        registros.RemoveAll(x => string.Equals(x.ChaveCliente, chave, StringComparison.Ordinal));
        registros.Add(new AnaliseFaturasExplicacaoRecorrenteRegistro
        {
            ChaveCliente = chave,
            Beneficiario = resultado.Beneficiario?.Trim() ?? string.Empty,
            Certificado = resultado.Certificado?.Trim() ?? string.Empty,
            Entidade = resultado.Entidade?.Trim() ?? string.Empty,
            Explicacao = texto,
            AtualizadoEm = DateTime.Now
        });

        GravarRegistros(registros);
    }

    public bool Remover(AnaliseFinalResultado resultado)
    {
        string? chave = CriarChaveCliente(resultado);
        if (chave == null)
            return false;

        List<AnaliseFaturasExplicacaoRecorrenteRegistro> registros = CarregarRegistros();
        int removidos = registros.RemoveAll(x =>
            string.Equals(x.ChaveCliente, chave, StringComparison.Ordinal));
        if (removidos == 0)
            return false;

        GravarRegistros(registros);
        return true;
    }

    public int AplicarEmAnaliseNova(IEnumerable<AnaliseFinalResultado> resultados)
    {
        Dictionary<string, AnaliseFaturasExplicacaoRecorrenteRegistro> porCliente = CarregarRegistros()
            .GroupBy(x => x.ChaveCliente, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AtualizadoEm).First(), StringComparer.Ordinal);

        int aplicadas = 0;
        foreach (AnaliseFinalResultado resultado in resultados)
        {
            if (!PodeReceberExplicacaoManual(resultado) ||
                !string.IsNullOrWhiteSpace(resultado.JustificativaManual))
            {
                continue;
            }

            string? chave = CriarChaveCliente(resultado);
            if (chave == null || !porCliente.TryGetValue(chave, out AnaliseFaturasExplicacaoRecorrenteRegistro? registro))
                continue;

            resultado.JustificativaManual = registro.Explicacao;
            aplicadas++;
        }

        return aplicadas;
    }

    public static bool PodeReceberExplicacaoManual(AnaliseFinalResultado resultado)
        => resultado.Status == AnaliseFinalStatus.DivergenciaPendente ||
           resultado.Status == AnaliseFinalStatus.Ambiguo;

    private List<AnaliseFaturasExplicacaoRecorrenteRegistro> CarregarRegistros()
    {
        if (!File.Exists(_caminhoArquivo))
            return new List<AnaliseFaturasExplicacaoRecorrenteRegistro>();

        string json = File.ReadAllText(_caminhoArquivo);
        AnaliseFaturasExplicacoesRecorrentesArquivo? arquivo =
            JsonSerializer.Deserialize<AnaliseFaturasExplicacoesRecorrentesArquivo>(json, JsonOptions);

        return (arquivo?.Registros ?? Array.Empty<AnaliseFaturasExplicacaoRecorrenteRegistro>())
            .Where(x => !string.IsNullOrWhiteSpace(x.ChaveCliente) && !string.IsNullOrWhiteSpace(x.Explicacao))
            .ToList();
    }

    private void GravarRegistros(IReadOnlyList<AnaliseFaturasExplicacaoRecorrenteRegistro> registros)
    {
        string? pasta = Path.GetDirectoryName(_caminhoArquivo);
        if (string.IsNullOrWhiteSpace(pasta))
            throw new InvalidOperationException("Não foi possível determinar a pasta das explicações recorrentes.");

        Directory.CreateDirectory(pasta);
        string temporario = Path.Combine(pasta, $".__explicacoes_{Guid.NewGuid():N}.tmp");

        try
        {
            var arquivo = new AnaliseFaturasExplicacoesRecorrentesArquivo
            {
                Registros = registros
                    .OrderBy(x => x.Entidade, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Beneficiario, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Certificado, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };

            File.WriteAllText(temporario, JsonSerializer.Serialize(arquivo, JsonOptions));
            File.Move(temporario, _caminhoArquivo, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporario))
                    File.Delete(temporario);
            }
            catch { }
        }
    }

    private static string? CriarChaveCliente(AnaliseFinalResultado resultado)
    {
        string entidade = AnaliseFaturasNormalizador.NormalizarNome(resultado.Entidade);
        string certificado = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(resultado.Certificado)
            ?? new string((resultado.Certificado ?? string.Empty).Where(char.IsDigit).ToArray());

        if (!string.IsNullOrWhiteSpace(certificado))
            return $"{entidade}|CERTIFICADO|{certificado}";

        string beneficiario = AnaliseFaturasNormalizador.NormalizarNome(resultado.Beneficiario);
        return string.IsNullOrWhiteSpace(beneficiario)
            ? null
            : $"{entidade}|BENEFICIARIO|{beneficiario}";
    }
}
