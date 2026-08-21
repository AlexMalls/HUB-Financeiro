using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HubFinanceiro;

public enum CnabCriacaoFonteTipo
{
    Desconhecido,
    ValeTransporte
}

public sealed class CnabDadosBancariosFuncionario
{
    public int CodigoFuncionario { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Documento { get; set; } = string.Empty;
    public string BancoCodigo { get; set; } = "033";
    public string Agencia { get; set; } = string.Empty;
    public string AgenciaDv { get; set; } = string.Empty;
    public string Conta { get; set; } = string.Empty;
    public string ContaDv { get; set; } = string.Empty;
    public string AgenciaContaDv { get; set; } = string.Empty;

    // Segmento B: opcionais, mas preservados quando aprendidos de um CNAB anterior.
    public string Logradouro { get; set; } = string.Empty;
    public string NumeroEndereco { get; set; } = string.Empty;
    public string Complemento { get; set; } = string.Empty;
    public string Bairro { get; set; } = string.Empty;
    public string Cidade { get; set; } = string.Empty;
    public string Cep { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;

    public DateTime UltimaAtualizacao { get; set; }
    public string OrigemDados { get; set; } = string.Empty;

    public string UltimaAtualizacaoTexto => UltimaAtualizacao == default
        ? "Sem histórico"
        : $"Atualizado em {UltimaAtualizacao:dd/MM/yyyy HH:mm}";

    public string DocumentoFormatado
    {
        get
        {
            string d = SomenteDigitos(Documento);
            return d.Length == 11
                ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}"
                : d;
        }
    }

    public string BancoTexto => string.IsNullOrWhiteSpace(BancoCodigo)
        ? "—"
        : $"{BancoCodigo} · {CnabDecoderService.NomeBanco(BancoCodigo)}";

    public string AgenciaFormatada => string.IsNullOrWhiteSpace(AgenciaDv)
        ? Agencia
        : $"{Agencia}-{AgenciaDv}";

    public string ContaFormatada => string.IsNullOrWhiteSpace(ContaDv)
        ? Conta
        : $"{Conta}-{ContaDv}";

    public string DescricaoCadastro =>
        $"Matrícula: {(CodigoFuncionario > 0 ? CodigoFuncionario.ToString() : "—")} · {UltimaAtualizacaoTexto}";

    public bool EstaCompleto
    {
        get
        {
            string documento = SomenteDigitos(Documento);
            string banco = SomenteDigitos(BancoCodigo);
            string agencia = SomenteDigitos(Agencia);
            string conta = SomenteDigitos(Conta);
            string contaDv = SomenteAlfanumerico(ContaDv);

            return documento.Length == 11 &&
                   banco.Length == 3 &&
                   agencia.Length > 0 && agencia.Length <= 5 &&
                   conta.Length > 0 && conta.Length <= 12 &&
                   contaDv.Length == 1;
        }
    }

    public CnabDadosBancariosFuncionario Clone() => new()
    {
        CodigoFuncionario = CodigoFuncionario,
        Nome = Nome,
        Documento = Documento,
        BancoCodigo = BancoCodigo,
        Agencia = Agencia,
        AgenciaDv = AgenciaDv,
        Conta = Conta,
        ContaDv = ContaDv,
        AgenciaContaDv = AgenciaContaDv,
        Logradouro = Logradouro,
        NumeroEndereco = NumeroEndereco,
        Complemento = Complemento,
        Bairro = Bairro,
        Cidade = Cidade,
        Cep = Cep,
        Uf = Uf,
        UltimaAtualizacao = UltimaAtualizacao,
        OrigemDados = OrigemDados
    };

    internal static string SomenteDigitos(string? valor)
        => new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());

    internal static string SomenteAlfanumerico(string? valor)
        => new string((valor ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
}

public sealed class CnabCriacaoPagamentoItem : INotifyPropertyChanged
{
    private string _valorTexto = string.Empty;
    private string _dataTexto = string.Empty;
    private bool _dataIndividualHabilitada;
    private CnabDadosBancariosFuncionario? _dadosBancarios;

    public int CodigoFuncionario { get; init; }
    public string Nome { get; init; } = string.Empty;
    public decimal ValorOriginal { get; init; }
    public DateTime DataOriginal { get; init; }

    public string ValorTexto
    {
        get => _valorTexto;
        set
        {
            if (_valorTexto == value) return;
            _valorTexto = value;
            OnPropertyChanged(nameof(ValorTexto));
        }
    }

    public string DataTexto
    {
        get => _dataTexto;
        set
        {
            if (_dataTexto == value) return;
            _dataTexto = value;
            OnPropertyChanged(nameof(DataTexto));
        }
    }

    public bool DataIndividualHabilitada
    {
        get => _dataIndividualHabilitada;
        set
        {
            if (_dataIndividualHabilitada == value) return;
            _dataIndividualHabilitada = value;
            OnPropertyChanged(nameof(DataIndividualHabilitada));
        }
    }

    public CnabDadosBancariosFuncionario? DadosBancarios
    {
        get => _dadosBancarios;
        set
        {
            _dadosBancarios = value;
            OnPropertyChanged(nameof(DadosBancarios));
            OnPropertyChanged(nameof(DadosBancariosOk));
            OnPropertyChanged(nameof(StatusDadosBancarios));
        }
    }

    public bool DadosBancariosOk => DadosBancarios?.EstaCompleto == true;

    public string StatusDadosBancarios => DadosBancariosOk
        ? $"CPF: {FormatarDocumentoCurto(DadosBancarios!.Documento)} · Banco {DadosBancarios.BancoCodigo}"
        : "Dados bancários pendentes";

    public bool TentarObterValor(out decimal valor)
        => CnabDecoderService.TentarParsearValor(ValorTexto, out valor);

    public bool TentarObterData(out DateTime data)
        => CnabDecoderService.TentarParsearData(DataTexto, out data);

    public void AtualizarDadosBancarios(CnabDadosBancariosFuncionario dados)
    {
        DadosBancarios = dados;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string nome)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));

    private static string FormatarDocumentoCurto(string documento)
    {
        string d = CnabDadosBancariosFuncionario.SomenteDigitos(documento);
        if (d.Length == 11)
            return $"***.{d[3..6]}.{d[6..9]}-**";
        return string.IsNullOrWhiteSpace(d) ? "não informado" : d;
    }
}

public sealed class CnabCriacaoArquivo
{
    public required string CaminhoOriginal { get; init; }
    public CnabCriacaoFonteTipo FonteTipo { get; init; }
    public string DescricaoFonte { get; init; } = string.Empty;
    public string Competencia { get; init; } = string.Empty;
    public DateTime CompetenciaData { get; init; }
    public DateTime DataPagamentoSugerida { get; init; }
    public string Empresa { get; init; } = string.Empty;
    public string CnpjEmpresa { get; init; } = string.Empty;
    public required List<CnabCriacaoPagamentoItem> Pagamentos { get; init; }
}

public sealed class CnabCriacaoNaoSuportadaException : Exception
{
    public CnabCriacaoNaoSuportadaException(string message) : base(message) { }
}

public static class ValeTransporteCnabService
{
    private static readonly CultureInfo CulturaBr = CultureInfo.GetCultureInfo("pt-BR");

    public static CnabCriacaoArquivo Carregar(
        string caminhoPdf,
        string textoPdf,
        IReadOnlyCollection<CnabDadosBancariosFuncionario> baseBancaria)
    {
        if (string.IsNullOrWhiteSpace(textoPdf))
            throw new CnabCriacaoNaoSuportadaException("Não foi possível extrair conteúdo do PDF.");

        string normalizado = RemoverAcentos(textoPdf).ToUpperInvariant();
        bool ehValeTransporte = normalizado.Contains("VALE TRANSPORTE", StringComparison.Ordinal) &&
                                (normalizado.Contains("RECIBO DE ENTREGA", StringComparison.Ordinal) ||
                                 normalizado.Contains("RELACAO PARA COMPRA", StringComparison.Ordinal));

        if (!ehValeTransporte)
            throw new CnabCriacaoNaoSuportadaException(
                "O PDF foi lido, mas não foi identificado como relatório de Vale Transporte. " +
                "Nesta primeira versão, a aba Criar CNAB aceita apenas o relatório de Vale Transporte da Administradora.");

        var competenciaMatch = Regex.Match(
            textoPdf,
            @"COMPET[ÊE]NCIA\s*:\s*(?<mes>\d{2})\/(?<ano>\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!competenciaMatch.Success)
        {
            competenciaMatch = Regex.Match(
                textoPdf,
                @"COMPETENCIA\s*:\s*(?<mes>\d{2})\/(?<ano>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!competenciaMatch.Success ||
            !int.TryParse(competenciaMatch.Groups["mes"].Value, out int mes) ||
            !int.TryParse(competenciaMatch.Groups["ano"].Value, out int ano) ||
            mes < 1 || mes > 12)
        {
            throw new CnabCriacaoNaoSuportadaException("Não foi possível identificar a competência do Vale Transporte.");
        }

        DateTime competencia = new(ano, mes, 1);
        DateTime dataSugerida = CalcularDataPagamentoValeTransporte(competencia);

        string empresa = ExtrairPrimeiro(textoPdf, @"Empresa\s*:\s*\d+\s*-\s*(?<v>[^\r\n]+)");
        string cnpj = ExtrairPrimeiro(textoPdf, @"CNPJ\s*:\s*(?<v>[\d\.\/\-]+)");

        string cnpjLimpo = CnabDadosBancariosFuncionario.SomenteDigitos(cnpj);
        if (cnpjLimpo.Length > 0 && cnpjLimpo != "25006061000197")
        {
            throw new CnabCriacaoNaoSuportadaException(
                $"O relatório foi identificado como Vale Transporte, porém o CNPJ da empresa é {cnpj}. " +
                "Nesta versão, a criação está habilitada apenas para a Positiva Administradora (25.006.061/0001-97).");
        }

        var pagamentosBrutos = ExtrairPagamentosDaRelacaoConsolidada(textoPdf);
        if (pagamentosBrutos.Count == 0)
            pagamentosBrutos = ExtrairPagamentosDosRecibos(textoPdf);

        if (pagamentosBrutos.Count == 0)
            throw new CnabCriacaoNaoSuportadaException("O PDF é de Vale Transporte, mas nenhum funcionário/valor pôde ser identificado.");

        var itens = new List<CnabCriacaoPagamentoItem>();
        foreach (var p in pagamentosBrutos.OrderBy(p => p.Codigo))
        {
            var dados = EncontrarDadosBancarios(p.Codigo, p.Nome, baseBancaria)?.Clone();
            if (dados != null)
            {
                dados.CodigoFuncionario = p.Codigo;
                dados.Nome = p.Nome;
            }

            itens.Add(new CnabCriacaoPagamentoItem
            {
                CodigoFuncionario = p.Codigo,
                Nome = p.Nome,
                ValorOriginal = p.Valor,
                DataOriginal = dataSugerida,
                ValorTexto = p.Valor.ToString("N2", CulturaBr),
                DataTexto = dataSugerida.ToString("dd/MM/yyyy"),
                DataIndividualHabilitada = false,
                DadosBancarios = dados
            });
        }

        return new CnabCriacaoArquivo
        {
            CaminhoOriginal = caminhoPdf,
            FonteTipo = CnabCriacaoFonteTipo.ValeTransporte,
            DescricaoFonte = "Vale Transporte · Santander CNAB 240 · ADM",
            Competencia = competencia.ToString("MM/yyyy"),
            CompetenciaData = competencia,
            DataPagamentoSugerida = dataSugerida,
            Empresa = string.IsNullOrWhiteSpace(empresa) ? "POSITIVA ADMINISTRADORA DE BENEFICIOS LT" : empresa.Trim(),
            CnpjEmpresa = string.IsNullOrWhiteSpace(cnpj) ? "25.006.061/0001-97" : cnpj.Trim(),
            Pagamentos = itens
        };
    }

    public static DateTime CalcularDataPagamentoValeTransporte(DateTime competencia)
    {
        DateTime data = new(competencia.Year, competencia.Month, 25);
        return data.DayOfWeek switch
        {
            DayOfWeek.Saturday => data.AddDays(-1),
            DayOfWeek.Sunday => data.AddDays(-2),
            _ => data
        };
    }

    private static List<(int Codigo, string Nome, decimal Valor)> ExtrairPagamentosDaRelacaoConsolidada(string texto)
    {
        string semCr = texto.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        int pos = RemoverAcentos(semCr).ToUpperInvariant().LastIndexOf(
            "RELACAO PARA COMPRA DE VALE TRANSPORTE", StringComparison.Ordinal);

        if (pos < 0)
            return new();

        string relacao = semCr[pos..];
        var matches = Regex.Matches(
            relacao,
            @"(?ms)^\s*(?<codigo>\d{1,6})\s*-\s*(?<nome>[A-ZÀ-Ü][A-ZÀ-Ü '\.-]+?)\s*$\s*(?<corpo>.*?)(?=^\s*\d{1,6}\s*-\s*[A-ZÀ-Ü][A-ZÀ-Ü '\.-]+?\s*$|\z)",
            RegexOptions.CultureInvariant);

        var resultado = new List<(int, string, decimal)>();
        foreach (Match match in matches)
        {
            if (!int.TryParse(match.Groups["codigo"].Value, out int codigo))
                continue;

            string nome = NormalizarNome(match.Groups["nome"].Value);
            string corpo = match.Groups["corpo"].Value;
            var totalMatches = Regex.Matches(corpo, @"Total\s*:\s*(?<v>[\d\.]+,\d{2})", RegexOptions.IgnoreCase);
            if (totalMatches.Count == 0)
                continue;

            string valorRaw = totalMatches[0].Groups["v"].Value;
            if (!decimal.TryParse(valorRaw, NumberStyles.Number, CulturaBr, out decimal valor) || valor <= 0)
                continue;

            resultado.Add((codigo, nome, valor));
        }

        return resultado
            .GroupBy(x => x.Item1)
            .Select(g => g.First())
            .ToList();
    }

    private static List<(int Codigo, string Nome, decimal Valor)> ExtrairPagamentosDosRecibos(string texto)
    {
        string[] linhas = texto.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var encontrados = new Dictionary<int, (string Nome, decimal Valor)>();
        int? codigoAtual = null;
        string nomeAtual = string.Empty;

        foreach (string linha in linhas)
        {
            Match nomeMatch = Regex.Match(linha, @"Nome\s*:\s*(?<codigo>\d+)\s*-\s*(?<nome>.+?)(?:\s{2,}|$)", RegexOptions.IgnoreCase);
            if (nomeMatch.Success && int.TryParse(nomeMatch.Groups["codigo"].Value, out int codigo))
            {
                codigoAtual = codigo;
                nomeAtual = NormalizarNome(nomeMatch.Groups["nome"].Value);
                continue;
            }

            if (!codigoAtual.HasValue)
                continue;

            Match totalMatch = Regex.Match(linha, @"Total Geral\s*:\s*R\$\s*(?<v>[\d\.]+,\d{2})", RegexOptions.IgnoreCase);
            if (!totalMatch.Success)
                continue;

            if (decimal.TryParse(totalMatch.Groups["v"].Value, NumberStyles.Number, CulturaBr, out decimal valor) && valor > 0)
                encontrados[codigoAtual.Value] = (nomeAtual, valor);
        }

        return encontrados
            .Select(kv => (kv.Key, kv.Value.Nome, kv.Value.Valor))
            .ToList();
    }

    private static CnabDadosBancariosFuncionario? EncontrarDadosBancarios(
        int codigo,
        string nome,
        IReadOnlyCollection<CnabDadosBancariosFuncionario> baseBancaria)
    {
        var porCodigo = baseBancaria.FirstOrDefault(x => x.CodigoFuncionario > 0 && x.CodigoFuncionario == codigo);
        if (porCodigo != null) return porCodigo;

        string chave = NormalizarChaveNome(nome);
        return baseBancaria.FirstOrDefault(x => NormalizarChaveNome(x.Nome) == chave);
    }

    private static string ExtrairPrimeiro(string texto, string padrao)
    {
        Match m = Regex.Match(texto, padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? m.Groups["v"].Value.Trim() : string.Empty;
    }

    internal static string NormalizarChaveNome(string? nome)
    {
        string semAcento = RemoverAcentos(nome ?? string.Empty).ToUpperInvariant();
        return new string(semAcento.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string NormalizarNome(string nome)
        => Regex.Replace(nome.Trim(), @"\s+", " ");

    private static string RemoverAcentos(string texto)
    {
        string formD = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public static class CnabDadosBancariosRepository
{
    public static List<CnabDadosBancariosFuncionario> Carregar(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return new();
            string json = File.ReadAllText(caminho, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<CnabDadosBancariosFuncionario>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Salvar(string caminho, IEnumerable<CnabDadosBancariosFuncionario> registros)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho) ?? ".");
        var lista = registros
            .OrderBy(x => x.CodigoFuncionario <= 0 ? int.MaxValue : x.CodigoFuncionario)
            .ThenBy(x => x.Nome)
            .ToList();

        string json = JsonSerializer.Serialize(lista, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(caminho, json, new UTF8Encoding(false));
    }

    public static void Upsert(string caminho, CnabDadosBancariosFuncionario registro)
    {
        var lista = Carregar(caminho);
        var existente = EncontrarCorrespondente(lista, registro);

        if (registro.UltimaAtualizacao == default)
            registro.UltimaAtualizacao = DateTime.Now;
        if (string.IsNullOrWhiteSpace(registro.OrigemDados))
            registro.OrigemDados = "Cadastro manual";

        if (existente == null)
        {
            lista.Add(registro.Clone());
        }
        else
        {
            Copiar(registro, existente);
        }

        Salvar(caminho, lista);
    }

    public static void Remover(string caminho, CnabDadosBancariosFuncionario registro)
    {
        var lista = Carregar(caminho);
        var existente = EncontrarCorrespondente(lista, registro);
        if (existente != null)
        {
            lista.Remove(existente);
            Salvar(caminho, lista);
        }
    }

    public static void AprenderDoCnab(string caminho, CnabArquivo arquivo)
    {
        var lista = Carregar(caminho);

        foreach (var p in arquivo.PagamentosOriginais)
        {
            int codigo = 0;
            string docClienteDigitos = CnabDadosBancariosFuncionario.SomenteDigitos(p.DocumentoCliente);
            if (docClienteDigitos.Length > 0)
                int.TryParse(docClienteDigitos.TrimStart('0'), out codigo);

            var novo = new CnabDadosBancariosFuncionario
            {
                CodigoFuncionario = codigo,
                Nome = p.Nome,
                Documento = p.DocumentoFavorecido,
                BancoCodigo = p.BancoCodigo,
                Agencia = p.Agencia,
                AgenciaDv = p.AgenciaDv,
                Conta = p.Conta,
                ContaDv = p.ContaDv,
                AgenciaContaDv = p.AgenciaContaDv,
                Logradouro = p.LogradouroFavorecido,
                NumeroEndereco = p.NumeroEnderecoFavorecido,
                Complemento = p.ComplementoEnderecoFavorecido,
                Bairro = p.BairroFavorecido,
                Cidade = p.CidadeFavorecido,
                Cep = p.CepFavorecido,
                Uf = p.UfFavorecido,
                UltimaAtualizacao = DateTime.Now,
                OrigemDados = Path.GetFileName(arquivo.CaminhoOriginal)
            };

            var existente = EncontrarCorrespondente(lista, novo);

            if (existente == null) lista.Add(novo);
            else Copiar(novo, existente);
        }

        Salvar(caminho, lista);
    }

    private static CnabDadosBancariosFuncionario? EncontrarCorrespondente(
        IEnumerable<CnabDadosBancariosFuncionario> lista,
        CnabDadosBancariosFuncionario registro)
    {
        if (registro.CodigoFuncionario > 0)
        {
            var porCodigo = lista.FirstOrDefault(x => x.CodigoFuncionario == registro.CodigoFuncionario);
            if (porCodigo != null) return porCodigo;
        }

        string documento = CnabDadosBancariosFuncionario.SomenteDigitos(registro.Documento);
        if (documento.Length == 11)
        {
            var porDocumento = lista.FirstOrDefault(x =>
                CnabDadosBancariosFuncionario.SomenteDigitos(x.Documento) == documento);
            if (porDocumento != null) return porDocumento;
        }

        string chaveNome = ValeTransporteCnabService.NormalizarChaveNome(registro.Nome);
        if (!string.IsNullOrWhiteSpace(chaveNome))
            return lista.FirstOrDefault(x =>
                ValeTransporteCnabService.NormalizarChaveNome(x.Nome) == chaveNome);

        return null;
    }

    private static void Copiar(CnabDadosBancariosFuncionario origem, CnabDadosBancariosFuncionario destino)
    {
        destino.CodigoFuncionario = origem.CodigoFuncionario;
        destino.Nome = origem.Nome;
        destino.Documento = origem.Documento;
        destino.BancoCodigo = origem.BancoCodigo;
        destino.Agencia = origem.Agencia;
        destino.AgenciaDv = origem.AgenciaDv;
        destino.Conta = origem.Conta;
        destino.ContaDv = origem.ContaDv;
        destino.AgenciaContaDv = origem.AgenciaContaDv;
        destino.Logradouro = origem.Logradouro;
        destino.NumeroEndereco = origem.NumeroEndereco;
        destino.Complemento = origem.Complemento;
        destino.Bairro = origem.Bairro;
        destino.Cidade = origem.Cidade;
        destino.Cep = origem.Cep;
        destino.Uf = origem.Uf;
        destino.UltimaAtualizacao = origem.UltimaAtualizacao;
        destino.OrigemDados = origem.OrigemDados;
    }
}

public sealed class CnabAdmConfiguracao
{
    public int UltimoSequencialArquivo { get; set; } = 657;
}

public static class CnabAdmConfiguracaoRepository
{
    public static CnabAdmConfiguracao Carregar(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return new CnabAdmConfiguracao();
            return JsonSerializer.Deserialize<CnabAdmConfiguracao>(File.ReadAllText(caminho)) ?? new CnabAdmConfiguracao();
        }
        catch
        {
            return new CnabAdmConfiguracao();
        }
    }

    public static void AtualizarComCnabExistente(string caminho, int sequencial)
    {
        if (sequencial <= 0) return;
        var cfg = Carregar(caminho);
        if (sequencial > cfg.UltimoSequencialArquivo)
        {
            cfg.UltimoSequencialArquivo = sequencial;
            Salvar(caminho, cfg);
        }
    }

    public static int ObterProximo(string caminho)
        => checked(Carregar(caminho).UltimoSequencialArquivo + 1);

    public static void ConfirmarUso(string caminho, int sequencial)
    {
        var cfg = Carregar(caminho);
        if (sequencial > cfg.UltimoSequencialArquivo)
            cfg.UltimoSequencialArquivo = sequencial;
        Salvar(caminho, cfg);
    }

    private static void Salvar(string caminho, CnabAdmConfiguracao cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho) ?? ".");
        File.WriteAllText(caminho, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
}

/// <summary>
/// Gerador do layout Santander 033 / Folha de Pagamento / CNAB 240 usado pela ADM.
/// O perfil do pagador foi obtido do CNAB de referência fornecido pelo usuário.
/// </summary>
public static class CnabSantanderFolhaAdmGenerator
{
    private const string Banco = "033";
    private const string CnpjEmpresa = "25006061000197";
    private const string Convenio = "00333409008301894341";
    private const string AgenciaEmpresa = "03409";
    private const string ContaEmpresa = "000013065321";
    private const string ContaEmpresaDv = "9";
    private const string NomeEmpresa = "POSITIVA ADMINISTRADORA DE BEN";
    private const string NomeBanco = "SANTANDER BRASIL";

    public static void GerarArquivo(
        IEnumerable<CnabCriacaoPagamentoItem> pagamentos,
        int numeroSequencialArquivo,
        string destino,
        DateTime? dataHoraGeracao = null)
    {
        var itens = pagamentos.ToList();
        if (itens.Count == 0)
            throw new InvalidOperationException("Não é possível gerar um CNAB sem pagamentos.");

        if (numeroSequencialArquivo <= 10 || numeroSequencialArquivo > 999999)
            throw new InvalidOperationException("Número sequencial do arquivo inválido. O sequencial deve estar entre 000011 e 999999.");

        // O CNAB de referência de folha usa um único lote 0001, serviço 30 e forma 01.
        // Nesta primeira versão o criador de VT mantém exatamente esse perfil e aceita contas Santander.
        foreach (var item in itens)
        {
            if (!item.TentarObterValor(out decimal valor) || valor <= 0)
                throw new InvalidOperationException($"Valor inválido para '{item.Nome}'.");
            if (!item.TentarObterData(out _))
                throw new InvalidOperationException($"Data inválida para '{item.Nome}'.");
            if (item.DadosBancarios?.EstaCompleto != true)
                throw new InvalidOperationException($"Dados bancários incompletos para '{item.Nome}'.");
            if (CnabDadosBancariosFuncionario.SomenteDigitos(item.DadosBancarios.BancoCodigo) != Banco)
                throw new InvalidOperationException(
                    $"'{item.Nome}' está cadastrado no banco {item.DadosBancarios.BancoCodigo}. " +
                    "O criador de Vale Transporte desta versão gera o mesmo lote do CNAB de referência (crédito em conta Santander). " +
                    "Contas de outros bancos serão suportadas em uma evolução futura.");
        }

        DateTime agora = dataHoraGeracao ?? DateTime.Now;
        var linhas = new List<string>
        {
            CriarHeaderArquivo(agora, numeroSequencialArquivo),
            CriarHeaderLote()
        };

        int sequencialDetalhe = 1;
        decimal total = 0m;
        foreach (var item in itens)
        {
            item.TentarObterValor(out decimal valor);
            item.TentarObterData(out DateTime data);
            total += valor;

            linhas.Add(CriarSegmentoA(item, sequencialDetalhe++, data, valor));
            linhas.Add(CriarSegmentoB(item, sequencialDetalhe++, data, valor));
        }

        int qtdRegistrosLote = 2 + (itens.Count * 2); // header lote + detalhes + trailer lote
        linhas.Add(CriarTrailerLote(qtdRegistrosLote, total));
        linhas.Add(CriarTrailerArquivo(linhas.Count + 1));

        if (linhas.Any(l => l.Length != 240))
            throw new InvalidOperationException("Falha interna: foi gerado registro CNAB diferente de 240 posições.");

        string texto = string.Join("\r\n", linhas) + "\r\n";
        File.WriteAllText(destino, texto, Encoding.Latin1);
    }

    private static string CriarHeaderArquivo(DateTime agora, int sequencialArquivo)
    {
        char[] r = NovoRegistro();
        Set(r, 1, 3, Banco, alinhamentoDireita: false, preenchimento: '0');
        Set(r, 4, 4, "0000", false, '0');
        Set(r, 8, 1, "0", false, '0');
        Set(r, 18, 1, "2", false, '0');
        Set(r, 19, 14, CnpjEmpresa, true, '0');
        Set(r, 33, 20, Convenio, false, ' ');
        Set(r, 53, 5, AgenciaEmpresa, true, '0');
        Set(r, 59, 12, ContaEmpresa, true, '0');
        Set(r, 71, 1, ContaEmpresaDv, false, ' ');
        Set(r, 73, 30, NomeEmpresa, false, ' ');
        Set(r, 103, 30, NomeBanco, false, ' ');
        Set(r, 143, 1, "1", false, '0');
        Set(r, 144, 8, agora.ToString("ddMMyyyy"), false, '0');
        Set(r, 152, 6, agora.ToString("HHmmss"), false, '0');
        Set(r, 158, 6, sequencialArquivo.ToString("D6"), true, '0');
        Set(r, 164, 3, "060", false, '0');
        Set(r, 167, 5, "00000", false, '0');
        return new string(r);
    }

    private static string CriarHeaderLote()
    {
        char[] r = NovoRegistro();
        Set(r, 1, 3, Banco, false, '0');
        Set(r, 4, 4, "0001", false, '0');
        Set(r, 8, 1, "1", false, '0');
        Set(r, 9, 1, "C", false, ' ');
        Set(r, 10, 2, "30", false, '0');
        Set(r, 12, 2, "01", false, '0');
        Set(r, 14, 3, "031", false, '0');
        Set(r, 18, 1, "2", false, '0');
        Set(r, 19, 14, CnpjEmpresa, true, '0');
        Set(r, 33, 20, Convenio, false, ' ');
        Set(r, 53, 5, AgenciaEmpresa, true, '0');
        Set(r, 59, 12, ContaEmpresa, true, '0');
        Set(r, 71, 1, ContaEmpresaDv, false, ' ');
        Set(r, 73, 30, NomeEmpresa, false, ' ');
        Set(r, 173, 5, "00000", true, '0');
        Set(r, 213, 5, "00000", true, '0');
        return new string(r);
    }

    private static string CriarSegmentoA(CnabCriacaoPagamentoItem item, int sequencial, DateTime data, decimal valor)
    {
        var d = item.DadosBancarios!;
        char[] r = NovoRegistro();
        Set(r, 1, 3, Banco, false, '0');
        Set(r, 4, 4, "0001", false, '0');
        Set(r, 8, 1, "3", false, '0');
        Set(r, 9, 5, sequencial.ToString("D5"), true, '0');
        Set(r, 14, 1, "A", false, ' ');
        Set(r, 15, 1, "0", false, '0');
        Set(r, 16, 2, "00", false, '0');
        Set(r, 18, 3, "000", false, '0'); // mesma câmara do CNAB de referência: crédito em conta Santander
        Set(r, 21, 3, Banco, true, '0');
        Set(r, 24, 5, CnabDadosBancariosFuncionario.SomenteDigitos(d.Agencia), true, '0');
        Set(r, 29, 1, Limitar(d.AgenciaDv, 1), false, ' ');
        Set(r, 30, 12, CnabDadosBancariosFuncionario.SomenteDigitos(d.Conta), true, '0');
        Set(r, 42, 1, Limitar(d.ContaDv, 1), false, ' ');
        Set(r, 43, 1, Limitar(d.AgenciaContaDv, 1), false, ' ');
        Set(r, 44, 30, NormalizarAlfa(item.Nome), false, ' ');
        Set(r, 74, 20, item.CodigoFuncionario.ToString(CultureInfo.InvariantCulture), true, '0');
        Set(r, 94, 8, data.ToString("ddMMyyyy"), false, '0');
        Set(r, 102, 3, "BRL", false, ' ');
        Set(r, 105, 15, "000000000000000", true, '0');
        Set(r, 120, 15, FormatarValor(valor, 15), true, '0');
        // Mantém o mesmo padrão do arquivo de referência do Protheus nas posições de retorno.
        Set(r, 155, 8, data.ToString("ddMMyyyy"), false, '0');
        Set(r, 163, 15, FormatarValor(valor, 15), true, '0');
        Set(r, 178, 34, new string('0', 34), false, '0');
        Set(r, 230, 1, "0", false, '0');
        return new string(r);
    }

    private static string CriarSegmentoB(CnabCriacaoPagamentoItem item, int sequencial, DateTime data, decimal valor)
    {
        var d = item.DadosBancarios!;
        char[] r = NovoRegistro();
        Set(r, 1, 3, Banco, false, '0');
        Set(r, 4, 4, "0001", false, '0');
        Set(r, 8, 1, "3", false, '0');
        Set(r, 9, 5, sequencial.ToString("D5"), true, '0');
        Set(r, 14, 1, "B", false, ' ');
        Set(r, 18, 1, "1", false, '0');
        Set(r, 19, 14, CnabDadosBancariosFuncionario.SomenteDigitos(d.Documento), true, '0');
        Set(r, 33, 30, NormalizarAlfa(d.Logradouro), false, ' ');
        Set(r, 63, 5, CnabDadosBancariosFuncionario.SomenteDigitos(d.NumeroEndereco), true, '0');
        Set(r, 68, 15, NormalizarAlfa(d.Complemento), false, ' ');
        Set(r, 83, 15, NormalizarAlfa(d.Bairro), false, ' ');
        Set(r, 98, 20, NormalizarAlfa(d.Cidade), false, ' ');
        Set(r, 118, 8, CnabDadosBancariosFuncionario.SomenteDigitos(d.Cep), true, '0');
        Set(r, 126, 2, NormalizarAlfa(d.Uf), false, ' ');
        Set(r, 128, 8, data.ToString("ddMMyyyy"), false, '0');
        Set(r, 136, 15, FormatarValor(valor, 15), true, '0');
        Set(r, 166, 15, "000000000000000", true, '0');
        Set(r, 181, 15, "000000000000000", true, '0');
        Set(r, 196, 15, "000000000000000", true, '0');
        Set(r, 211, 4, "0000", true, '0');
        Set(r, 226, 3, "020", true, '0');
        Set(r, 229, 2, "07", true, '0');
        return new string(r);
    }

    private static string CriarTrailerLote(int quantidadeRegistros, decimal total)
    {
        char[] r = NovoRegistro();
        Set(r, 1, 3, Banco, false, '0');
        Set(r, 4, 4, "0001", false, '0');
        Set(r, 8, 1, "5", false, '0');
        Set(r, 18, 6, quantidadeRegistros.ToString("D6"), true, '0');
        Set(r, 24, 18, FormatarValor(total, 18), true, '0');
        Set(r, 42, 18, new string('0', 18), true, '0');
        Set(r, 60, 6, "000000", true, '0');
        return new string(r);
    }

    private static string CriarTrailerArquivo(int quantidadeRegistrosArquivo)
    {
        char[] r = NovoRegistro();
        Set(r, 1, 3, Banco, false, '0');
        Set(r, 4, 4, "9999", false, '0');
        Set(r, 8, 1, "9", false, '0');
        Set(r, 18, 6, "000001", true, '0');
        Set(r, 24, 6, quantidadeRegistrosArquivo.ToString("D6"), true, '0');
        return new string(r);
    }

    private static char[] NovoRegistro()
        => Enumerable.Repeat(' ', 240).ToArray();

    private static void Set(char[] registro, int de, int tamanho, string? valor, bool alinhamentoDireita, char preenchimento)
    {
        string bruto = valor ?? string.Empty;
        if (bruto.Length > tamanho)
            bruto = bruto[..tamanho];

        string ajustado = alinhamentoDireita
            ? bruto.PadLeft(tamanho, preenchimento)
            : bruto.PadRight(tamanho, preenchimento);

        int indice = de - 1;
        for (int i = 0; i < tamanho; i++)
            registro[indice + i] = ajustado[i];
    }

    private static string FormatarValor(decimal valor, int tamanho)
    {
        long centavos = checked((long)Math.Round(valor * 100m, 0, MidpointRounding.AwayFromZero));
        string bruto = centavos.ToString(CultureInfo.InvariantCulture);
        if (bruto.Length > tamanho)
            throw new InvalidOperationException("Valor maior do que o campo permitido pelo layout CNAB.");
        return bruto.PadLeft(tamanho, '0');
    }

    private static string NormalizarAlfa(string? valor)
    {
        string texto = valor ?? string.Empty;
        string formD = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (c >= 32 && c <= 126)
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Limitar(string? valor, int tamanho)
    {
        string limpo = CnabDadosBancariosFuncionario.SomenteAlfanumerico(valor);
        return limpo.Length > tamanho ? limpo[..tamanho] : limpo;
    }
}
