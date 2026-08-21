using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HubFinanceiro;

public sealed class AnaliseFaturasHistoricoConfiguracao
{
    public bool IgnorarCoparticipacao { get; init; } = true;
    public bool IgnorarCompetenciasAnteriores { get; init; } = true;
    public bool IgnorarClientesCancelados { get; init; }
    public decimal ToleranciaFinanceira { get; init; } = AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal;
}

public sealed class AnaliseFaturasHistoricoTotais
{
    public int Total { get; init; }
    public int Compativeis { get; init; }
    public int Atencoes { get; init; }
    public int Pendentes { get; init; }
    public int Ambiguas { get; init; }
    public int CompativeisPorTolerancia { get; init; }
    public decimal ToleranciaFaturaMaior { get; init; }
    public decimal ToleranciaOverMaior { get; init; }
    public decimal SaldoToleranciaLiquido { get; init; }

    public static AnaliseFaturasHistoricoTotais Criar(AnaliseFinalDiagnostico diagnostico)
    {
        if (diagnostico == null)
            throw new ArgumentNullException(nameof(diagnostico));

        return new AnaliseFaturasHistoricoTotais
        {
            Total = diagnostico.Total,
            Compativeis = diagnostico.TotalCompativeis,
            Atencoes = diagnostico.TotalAtencao,
            Pendentes = diagnostico.TotalPendentes,
            Ambiguas = diagnostico.TotalAmbiguos,
            CompativeisPorTolerancia = diagnostico.TotalCompativeisPorTolerancia,
            ToleranciaFaturaMaior = diagnostico.SomaToleranciaFaturaMaior,
            ToleranciaOverMaior = diagnostico.SomaToleranciaOverMaior,
            SaldoToleranciaLiquido = diagnostico.SaldoToleranciaLiquido
        };
    }
}

public sealed class AnaliseFaturasHistoricoArquivo
{
    public string Grupo { get; init; } = string.Empty;
    public string NomeArquivo { get; init; } = string.Empty;
    public string CaminhoOriginal { get; init; } = string.Empty;
    public string CaminhoArquivado { get; set; } = string.Empty;
    public long TamanhoBytes { get; init; }
    public DateTime? UltimaModificacao { get; init; }
}

public sealed class AnaliseFaturasHistoricoSnapshot
{
    [JsonIgnore]
    public string CaminhoArquivo { get; set; } = string.Empty;
    public int VersaoFormato { get; init; } = 1;
    public string Modulo { get; init; } = "Analise de Faturas";
    public DateTime Competencia { get; init; }
    public DateTime DataHoraAnalise { get; init; }
    public AnaliseFaturasHistoricoConfiguracao Configuracoes { get; init; } = new();
    public AnaliseFaturasHistoricoTotais Totais { get; init; } = new();
    public IReadOnlyList<AnaliseFaturasHistoricoArquivo> ArquivosUtilizados { get; init; } = Array.Empty<AnaliseFaturasHistoricoArquivo>();
    public AnaliseFinalDiagnostico Resultado { get; init; } = new();
}

/// <summary>
/// Metadados da análise corrente necessários para criar um snapshot persistente.
/// Nenhum arquivo-fonte é necessário depois que o snapshot é salvo.
/// </summary>
public sealed class AnaliseFaturasHistoricoContextoCriacao
{
    public string CaminhoBaseData { get; init; } = string.Empty;
    public DateTime DataHoraAnalise { get; init; } = DateTime.Now;
    public bool IgnorarCoparticipacao { get; init; } = true;
    public bool IgnorarCompetenciasAnteriores { get; init; } = true;
    public bool IgnorarClientesCancelados { get; init; }
    public decimal ToleranciaFinanceira { get; init; } = AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal;
    public IReadOnlyList<AnaliseFaturasHistoricoArquivo> ArquivosUtilizados { get; init; } = Array.Empty<AnaliseFaturasHistoricoArquivo>();

    public AnaliseFaturasHistoricoSnapshot CriarSnapshot(AnaliseFinalDiagnostico diagnostico)
    {
        if (diagnostico == null)
            throw new ArgumentNullException(nameof(diagnostico));

        DateTime competencia = new(diagnostico.Competencia.Year, diagnostico.Competencia.Month, 1);

        return new AnaliseFaturasHistoricoSnapshot
        {
            VersaoFormato = 1,
            Competencia = competencia,
            DataHoraAnalise = DataHoraAnalise,
            Configuracoes = new AnaliseFaturasHistoricoConfiguracao
            {
                IgnorarCoparticipacao = IgnorarCoparticipacao,
                IgnorarCompetenciasAnteriores = IgnorarCompetenciasAnteriores,
                IgnorarClientesCancelados = IgnorarClientesCancelados,
                ToleranciaFinanceira = ToleranciaFinanceira
            },
            Totais = AnaliseFaturasHistoricoTotais.Criar(diagnostico),
            ArquivosUtilizados = ArquivosUtilizados.ToList(),
            Resultado = diagnostico
        };
    }

    public static AnaliseFaturasHistoricoContextoCriacao Criar(
        string caminhoBaseData,
        IReadOnlyList<string> faturasMesPassado,
        IReadOnlyList<string> faturasMesAtual,
        IReadOnlyList<string> faturasMesSeguinte,
        string arquivoOver,
        bool ignorarCoparticipacao,
        bool ignorarCompetenciasAnteriores,
        bool ignorarClientesCancelados,
        DateTime? dataHoraAnalise = null)
    {
        var arquivos = new List<AnaliseFaturasHistoricoArquivo>();
        AdicionarArquivos(arquivos, "Faturas mês passado", faturasMesPassado);
        AdicionarArquivos(arquivos, "Faturas mês atual", faturasMesAtual);
        AdicionarArquivos(arquivos, "Faturas mês que vem", faturasMesSeguinte);

        if (!string.IsNullOrWhiteSpace(arquivoOver))
            arquivos.Add(CriarArquivo("Relatório Over", arquivoOver));

        return new AnaliseFaturasHistoricoContextoCriacao
        {
            CaminhoBaseData = caminhoBaseData,
            DataHoraAnalise = dataHoraAnalise ?? DateTime.Now,
            IgnorarCoparticipacao = ignorarCoparticipacao,
            IgnorarCompetenciasAnteriores = ignorarCompetenciasAnteriores,
            IgnorarClientesCancelados = ignorarClientesCancelados,
            ToleranciaFinanceira = AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal,
            ArquivosUtilizados = arquivos
        };
    }

    private static void AdicionarArquivos(
        ICollection<AnaliseFaturasHistoricoArquivo> destino,
        string grupo,
        IEnumerable<string> arquivos)
    {
        foreach (string arquivo in arquivos.Where(x => !string.IsNullOrWhiteSpace(x)))
            destino.Add(CriarArquivo(grupo, arquivo));
    }

    private static AnaliseFaturasHistoricoArquivo CriarArquivo(string grupo, string caminho)
    {
        var info = new FileInfo(caminho);
        return new AnaliseFaturasHistoricoArquivo
        {
            Grupo = grupo,
            NomeArquivo = Path.GetFileName(caminho),
            CaminhoOriginal = caminho,
            TamanhoBytes = info.Exists ? info.Length : 0,
            UltimaModificacao = info.Exists ? info.LastWriteTime : null
        };
    }
}

public sealed class AnaliseFaturasHistoricoResumo
{
    public string NomeArquivo { get; init; } = string.Empty;
    public DateTime Competencia { get; init; }
    public DateTime DataHoraAnalise { get; init; }
    public AnaliseFaturasHistoricoConfiguracao Configuracoes { get; init; } = new();
    public AnaliseFaturasHistoricoTotais Totais { get; init; } = new();

    [JsonIgnore]
    public string CaminhoArquivo { get; set; } = string.Empty;

    [JsonIgnore]
    public string Titulo => $"Análise {Competencia:MM/yyyy}";

    [JsonIgnore]
    public string DataAnaliseTexto => $"Salva em {DataHoraAnalise:dd/MM/yyyy HH:mm}";

    [JsonIgnore]
    public string TotaisTexto =>
        $"{Totais.Compativeis:N0} compatíveis  •  {Totais.Atencoes:N0} atenção  •  {Totais.Pendentes + Totais.Ambiguas:N0} divergências";

    [JsonIgnore]
    public string ConfiguracoesTexto =>
        $"Copart: {(Configuracoes.IgnorarCoparticipacao ? "ignorada" : "incluída")}  •  " +
        $"Competências anteriores: {(Configuracoes.IgnorarCompetenciasAnteriores ? "ignoradas" : "incluídas")}  •  " +
        $"Cancelados: {(Configuracoes.IgnorarClientesCancelados ? "em Atenção" : "mantidos nas divergências")}  •  " +
        $"Tolerância: ±R$ {Configuracoes.ToleranciaFinanceira.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))}";
}

internal sealed class AnaliseFaturasHistoricoIndice
{
    public int VersaoFormato { get; init; } = 1;
    public IReadOnlyList<AnaliseFaturasHistoricoResumo> Analises { get; init; } = Array.Empty<AnaliseFaturasHistoricoResumo>();
}

/// <summary>
/// Persistência independente da pasta temporária "Analise de Faturas".
/// Cada competência possui uma pasta com o snapshot e cópias dos arquivos utilizados.
/// </summary>
public sealed class AnaliseFaturasHistoricoService
{
    private const string NomePastaHistorico = "Analise de Faturas - Histórico";
    private const string NomeIndice = "indice_historico.json";
    private readonly string _pastaHistorico;

    private static readonly JsonSerializerOptions JsonOptions = CriarJsonOptions();

    public AnaliseFaturasHistoricoService(string caminhoBaseData)
    {
        if (string.IsNullOrWhiteSpace(caminhoBaseData))
            throw new ArgumentException("O caminho-base de dados não foi informado.", nameof(caminhoBaseData));

        _pastaHistorico = Path.Combine(
            caminhoBaseData,
            "Relatórios de Analise",
            NomePastaHistorico);
    }

    public string PastaHistorico => _pastaHistorico;

    public string ObterCaminhoRegistro(DateTime competencia)
    {
        DateTime mes = new(competencia.Year, competencia.Month, 1);
        return Path.Combine(
            ObterPastaAnalise(mes),
            $"analise_{mes:yyyy_MM}.json");
    }

    public bool Existe(DateTime competencia) => File.Exists(ObterCaminhoRegistroExistente(competencia));

    public string Salvar(AnaliseFaturasHistoricoSnapshot snapshot)
    {
        ValidarSnapshot(snapshot);
        Directory.CreateDirectory(_pastaHistorico);

        DateTime competencia = new(snapshot.Competencia.Year, snapshot.Competencia.Month, 1);
        string pastaDestino = ObterPastaAnalise(competencia);
        string destino = ObterCaminhoRegistro(snapshot.Competencia);
        string pastaTemporaria = Path.Combine(_pastaHistorico, $".__analise_{Guid.NewGuid():N}.tmp");
        string pastaAnterior = Path.Combine(_pastaHistorico, $".__analise_anterior_{Guid.NewGuid():N}.tmp");
        string legado = ObterCaminhoRegistroLegado(competencia);
        bool pastaAnteriorMovida = false;
        bool pastaNovaMovida = false;
        var caminhosArquivadosAnteriores = snapshot.ArquivosUtilizados
            .ToDictionary(x => x, x => x.CaminhoArquivado);

        try
        {
            Directory.CreateDirectory(pastaTemporaria);
            CopiarArquivosUtilizados(snapshot.ArquivosUtilizados, pastaTemporaria, pastaDestino);

            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(
                Path.Combine(pastaTemporaria, Path.GetFileName(destino)),
                json);

            if (Directory.Exists(pastaDestino))
            {
                Directory.Move(pastaDestino, pastaAnterior);
                pastaAnteriorMovida = true;
            }

            Directory.Move(pastaTemporaria, pastaDestino);
            pastaNovaMovida = true;
            snapshot.CaminhoArquivo = destino;

            if (File.Exists(legado))
                File.Delete(legado);

            if (pastaAnteriorMovida && Directory.Exists(pastaAnterior))
                Directory.Delete(pastaAnterior, recursive: true);

            string caminhoRelativo = Path.GetRelativePath(_pastaHistorico, destino);
            try
            {
                AtualizarIndice(CriarResumo(snapshot, caminhoRelativo));
            }
            catch
            {
                // O índice é reconstruído automaticamente a partir dos snapshots.
            }

            return destino;
        }
        catch
        {
            try
            {
                if (Directory.Exists(pastaTemporaria))
                    Directory.Delete(pastaTemporaria, recursive: true);

                if (pastaNovaMovida && Directory.Exists(pastaDestino))
                    Directory.Delete(pastaDestino, recursive: true);

                if (pastaAnteriorMovida && Directory.Exists(pastaAnterior))
                    Directory.Move(pastaAnterior, pastaDestino);
            }
            catch { }

            foreach ((AnaliseFaturasHistoricoArquivo arquivo, string caminhoAnterior) in caminhosArquivadosAnteriores)
                arquivo.CaminhoArquivado = caminhoAnterior;

            throw;
        }
    }

    public bool Excluir(DateTime competencia)
    {
        string caminho = ObterCaminhoRegistroExistente(competencia);
        if (!File.Exists(caminho))
            return false;

        string? pastaAnalise = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrWhiteSpace(pastaAnalise) &&
            !string.Equals(pastaAnalise, _pastaHistorico, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(pastaAnalise, recursive: true);
        }
        else
        {
            File.Delete(caminho);
        }

        // O índice é apenas uma visão leve do histórico. Se a atualização dele falhar,
        // Listar() detectará a divergência e o reconstruirá a partir dos snapshots restantes.
        try
        {
            List<AnaliseFaturasHistoricoResumo> itens = TentarLerIndice() ?? new List<AnaliseFaturasHistoricoResumo>();
            itens.RemoveAll(x => x.Competencia.Year == competencia.Year && x.Competencia.Month == competencia.Month);
            GravarIndice(itens);
        }
        catch
        {
            // Não restauramos o snapshot excluído apenas por falha no índice.
        }

        return true;
    }

    public AnaliseFaturasHistoricoSnapshot Carregar(string caminhoArquivo)
    {
        if (string.IsNullOrWhiteSpace(caminhoArquivo) || !File.Exists(caminhoArquivo))
            throw new FileNotFoundException("O arquivo do histórico não foi encontrado.", caminhoArquivo);

        string json = File.ReadAllText(caminhoArquivo);
        AnaliseFaturasHistoricoSnapshot? snapshot = JsonSerializer.Deserialize<AnaliseFaturasHistoricoSnapshot>(json, JsonOptions);
        if (snapshot == null)
            throw new InvalidDataException("O histórico está vazio ou não pôde ser interpretado.");

        ValidarSnapshot(snapshot);
        snapshot.CaminhoArquivo = caminhoArquivo;
        return snapshot;
    }

    /// <summary>
    /// Atualiza um snapshot que já foi carregado do histórico, preservando a competência,
    /// data original e índice. Usado para persistir explicações manuais sem recalcular a análise.
    /// </summary>
    public static void AtualizarArquivoCarregado(AnaliseFaturasHistoricoSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        ValidarSnapshot(snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.CaminhoArquivo))
            throw new InvalidOperationException("O snapshot não possui caminho de histórico associado.");

        string destino = snapshot.CaminhoArquivo;
        string pasta = Path.GetDirectoryName(destino)
            ?? throw new InvalidOperationException("Não foi possível localizar a pasta do histórico.");
        Directory.CreateDirectory(pasta);

        string temporario = Path.Combine(pasta, $".__historico_edicao_{Guid.NewGuid():N}.tmp");
        try
        {
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(temporario, json);
            File.Move(temporario, destino, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporario))
                    File.Delete(temporario);
            }
            catch { }

            throw;
        }
    }

    public IReadOnlyList<AnaliseFaturasHistoricoResumo> Listar()
    {
        if (!Directory.Exists(_pastaHistorico))
            return Array.Empty<AnaliseFaturasHistoricoResumo>();

        List<string> snapshots = Directory.EnumerateFiles(_pastaHistorico, "analise_*.json", SearchOption.AllDirectories)
            .Where(x => !Path.GetRelativePath(_pastaHistorico, x)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(parte => parte.StartsWith(".__", StringComparison.Ordinal)))
            .Select(x => Path.GetRelativePath(_pastaHistorico, x))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (snapshots.Count == 0)
            return Array.Empty<AnaliseFaturasHistoricoResumo>();

        List<AnaliseFaturasHistoricoResumo>? indice = TentarLerIndice();
        bool indiceValido = indice != null &&
            indice.Select(x => x.NomeArquivo).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(snapshots, StringComparer.OrdinalIgnoreCase);

        if (!indiceValido)
            indice = RecriarIndice(snapshots);

        foreach (AnaliseFaturasHistoricoResumo item in indice!)
            item.CaminhoArquivo = Path.Combine(_pastaHistorico, item.NomeArquivo);

        return indice!
            .OrderByDescending(x => x.Competencia)
            .ThenByDescending(x => x.DataHoraAnalise)
            .ToList();
    }

    private void AtualizarIndice(AnaliseFaturasHistoricoResumo resumo)
    {
        List<AnaliseFaturasHistoricoResumo> itens = TentarLerIndice() ?? new List<AnaliseFaturasHistoricoResumo>();
        itens.RemoveAll(x => x.Competencia.Year == resumo.Competencia.Year && x.Competencia.Month == resumo.Competencia.Month);
        itens.Add(resumo);

        GravarIndice(itens);
    }

    private List<AnaliseFaturasHistoricoResumo> RecriarIndice(IReadOnlyList<string> nomesArquivos)
    {
        var itens = new List<AnaliseFaturasHistoricoResumo>();

        foreach (string nome in nomesArquivos)
        {
            string caminho = Path.Combine(_pastaHistorico, nome);
            try
            {
                AnaliseFaturasHistoricoSnapshot snapshot = Carregar(caminho);
                itens.Add(CriarResumo(snapshot, nome));
            }
            catch
            {
                // Um arquivo isolado inválido não impede a leitura dos demais históricos.
            }
        }

        try { GravarIndice(itens); }
        catch { }

        return itens;
    }

    private List<AnaliseFaturasHistoricoResumo>? TentarLerIndice()
    {
        string caminho = Path.Combine(_pastaHistorico, NomeIndice);
        if (!File.Exists(caminho))
            return null;

        try
        {
            string json = File.ReadAllText(caminho);
            AnaliseFaturasHistoricoIndice? indice = JsonSerializer.Deserialize<AnaliseFaturasHistoricoIndice>(json, JsonOptions);
            return indice?.Analises?.ToList();
        }
        catch
        {
            return null;
        }
    }

    private void GravarIndice(IEnumerable<AnaliseFaturasHistoricoResumo> itens)
    {
        Directory.CreateDirectory(_pastaHistorico);
        string destino = Path.Combine(_pastaHistorico, NomeIndice);
        string temporario = Path.Combine(_pastaHistorico, $".__indice_{Guid.NewGuid():N}.tmp");

        try
        {
            var indice = new AnaliseFaturasHistoricoIndice
            {
                Analises = itens
                    .OrderByDescending(x => x.Competencia)
                    .ToList()
            };

            File.WriteAllText(temporario, JsonSerializer.Serialize(indice, JsonOptions));
            File.Move(temporario, destino, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporario))
                    File.Delete(temporario);
            }
            catch { }
            throw;
        }
    }

    private static AnaliseFaturasHistoricoResumo CriarResumo(AnaliseFaturasHistoricoSnapshot snapshot, string nomeArquivo)
        => new()
        {
            NomeArquivo = nomeArquivo,
            Competencia = snapshot.Competencia,
            DataHoraAnalise = snapshot.DataHoraAnalise,
            Configuracoes = snapshot.Configuracoes,
            Totais = snapshot.Totais
        };

    private string ObterPastaAnalise(DateTime competencia)
        => Path.Combine(_pastaHistorico, $"Analise_{competencia:yyyy_MM}");

    private string ObterCaminhoRegistroLegado(DateTime competencia)
        => Path.Combine(_pastaHistorico, $"analise_{competencia:yyyy_MM}.json");

    private string ObterCaminhoRegistroExistente(DateTime competencia)
    {
        string atual = ObterCaminhoRegistro(competencia);
        return File.Exists(atual)
            ? atual
            : ObterCaminhoRegistroLegado(new DateTime(competencia.Year, competencia.Month, 1));
    }

    private static void CopiarArquivosUtilizados(
        IEnumerable<AnaliseFaturasHistoricoArquivo> arquivos,
        string pastaTemporaria,
        string pastaDestinoFinal)
    {
        foreach (AnaliseFaturasHistoricoArquivo arquivo in arquivos)
        {
            string origem = File.Exists(arquivo.CaminhoOriginal)
                ? arquivo.CaminhoOriginal
                : arquivo.CaminhoArquivado;

            if (string.IsNullOrWhiteSpace(origem) || !File.Exists(origem))
            {
                throw new FileNotFoundException(
                    $"O arquivo utilizado na análise não está mais disponível: {arquivo.NomeArquivo}",
                    arquivo.CaminhoOriginal);
            }

            string grupo = NormalizarNomePasta(arquivo.Grupo);
            string pastaGrupoTemporaria = Path.Combine(pastaTemporaria, "Arquivos utilizados", grupo);
            string pastaGrupoFinal = Path.Combine(pastaDestinoFinal, "Arquivos utilizados", grupo);
            Directory.CreateDirectory(pastaGrupoTemporaria);

            string destinoTemporario = Path.Combine(pastaGrupoTemporaria, arquivo.NomeArquivo);
            File.Copy(origem, destinoTemporario, overwrite: false);
            arquivo.CaminhoArquivado = Path.Combine(pastaGrupoFinal, arquivo.NomeArquivo);
        }
    }

    private static string NormalizarNomePasta(string nome)
    {
        string resultado = string.IsNullOrWhiteSpace(nome) ? "Outros" : nome.Trim();
        foreach (char caractere in Path.GetInvalidFileNameChars())
            resultado = resultado.Replace(caractere, '_');
        return resultado;
    }

    private static void ValidarSnapshot(AnaliseFaturasHistoricoSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.VersaoFormato <= 0 || snapshot.VersaoFormato > 1)
            throw new InvalidDataException($"Versão de histórico não suportada: {snapshot.VersaoFormato}.");
        if (snapshot.Competencia == default)
            throw new InvalidDataException("O histórico não possui competência válida.");
        if (snapshot.Resultado == null)
            throw new InvalidDataException("O histórico não possui o resultado consolidado.");

        DateTime competenciaSnapshot = new(snapshot.Competencia.Year, snapshot.Competencia.Month, 1);
        DateTime competenciaResultado = new(snapshot.Resultado.Competencia.Year, snapshot.Resultado.Competencia.Month, 1);
        if (competenciaSnapshot != competenciaResultado)
            throw new InvalidDataException("A competência do histórico não corresponde à competência do resultado salvo.");
    }

    private static JsonSerializerOptions CriarJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
