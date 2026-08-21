using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HubFinanceiro;

public enum CnabFormato
{
    Desconhecido,
    Cnab240,
    Cnab400
}

public enum CnabLayoutTipo
{
    Desconhecido,
    SantanderPagamentoAB240
}

public sealed class CnabPagamentoItem : INotifyPropertyChanged
{
    private string _valorTexto = string.Empty;
    private string _dataTexto = string.Empty;
    private bool _dataIndividualHabilitada;

    public string Nome { get; init; } = string.Empty;
    public string BancoCodigo { get; init; } = string.Empty;
    public string Agencia { get; init; } = string.Empty;
    public string AgenciaDv { get; init; } = string.Empty;
    public string Conta { get; init; } = string.Empty;
    public string ContaDv { get; init; } = string.Empty;
    public string AgenciaContaDv { get; init; } = string.Empty;
    public string DocumentoFavorecido { get; init; } = string.Empty;
    public string DocumentoCliente { get; init; } = string.Empty;

    // Segmento B clássico Santander/FEBRABAN (pagamentos/TED).
    // Mantidos para que o Criador CNAB possa aprender e reutilizar os dados
    // cadastrais de um CNAB base já validado pelo banco.
    public string LogradouroFavorecido { get; init; } = string.Empty;
    public string NumeroEnderecoFavorecido { get; init; } = string.Empty;
    public string ComplementoEnderecoFavorecido { get; init; } = string.Empty;
    public string BairroFavorecido { get; init; } = string.Empty;
    public string CidadeFavorecido { get; init; } = string.Empty;
    public string CepFavorecido { get; init; } = string.Empty;
    public string UfFavorecido { get; init; } = string.Empty;

    public string LoteServico { get; init; } = string.Empty;
    public decimal ValorOriginal { get; init; }
    public DateTime DataOriginal { get; init; }
    public decimal ValorDocumentoSegmentoBOriginal { get; init; }

    internal int IndiceSegmentoA { get; init; }
    internal int IndiceSegmentoB { get; init; }

    public string ValorTexto
    {
        get => _valorTexto;
        set
        {
            if (_valorTexto == value) return;
            _valorTexto = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValorTexto)));
        }
    }

    public string DataTexto
    {
        get => _dataTexto;
        set
        {
            if (_dataTexto == value) return;
            _dataTexto = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataTexto)));
        }
    }

    public bool DataIndividualHabilitada
    {
        get => _dataIndividualHabilitada;
        set
        {
            if (_dataIndividualHabilitada == value) return;
            _dataIndividualHabilitada = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataIndividualHabilitada)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TentarObterValor(out decimal valor)
        => CnabDecoderService.TentarParsearValor(ValorTexto, out valor);

    public bool TentarObterData(out DateTime data)
        => CnabDecoderService.TentarParsearData(DataTexto, out data);
}

public sealed class CnabArquivo
{
    public required string CaminhoOriginal { get; init; }
    public required List<string> LinhasOriginais { get; init; }
    public required List<CnabPagamentoItem> PagamentosOriginais { get; init; }
    public required Encoding Codificacao { get; init; }
    public required string QuebraLinha { get; init; }
    public bool TerminaComQuebraLinha { get; init; }
    public CnabFormato Formato { get; init; }
    public CnabLayoutTipo Layout { get; init; }
    public string BancoOrigem { get; init; } = string.Empty;
    public string TipoServico { get; init; } = string.Empty;
    public string FormaLancamento { get; init; } = string.Empty;
    public int NumeroSequencialArquivo { get; init; }

    public string DescricaoLayout => Layout switch
    {
        CnabLayoutTipo.SantanderPagamentoAB240 => "Santander · CNAB 240 · Segmentos A/B",
        _ => "Layout não identificado"
    };
}

public sealed class CnabNaoSuportadoException : Exception
{
    public CnabNaoSuportadoException(string message) : base(message) { }
}

/// <summary>
/// Ponto central de identificação, leitura e regravação de CNAB.
/// A ideia é adicionar novos layouts futuramente sem colocar regras específicas na MainWindow.
/// </summary>
public static class CnabDecoderService
{
    private static readonly CultureInfo CulturaBr = CultureInfo.GetCultureInfo("pt-BR");

    public static CnabArquivo Carregar(string caminho)
    {
        byte[] bytes = File.ReadAllBytes(caminho);
        var (texto, codificacao) = DecodificarPreservandoBytes(bytes);

        string quebraLinha = texto.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool terminaComQuebra = texto.EndsWith("\r\n", StringComparison.Ordinal)
            || texto.EndsWith("\n", StringComparison.Ordinal)
            || texto.EndsWith("\r", StringComparison.Ordinal);

        var linhas = texto
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

        if (linhas.Count > 0 && linhas[^1].Length == 0)
            linhas.RemoveAt(linhas.Count - 1);

        if (linhas.Count == 0 || linhas.All(string.IsNullOrWhiteSpace))
            throw new CnabNaoSuportadoException("O arquivo está vazio ou não contém registros CNAB.");

        if (linhas.Any(l => l.Length == 0))
            throw new CnabNaoSuportadoException("O arquivo possui linhas vazias entre os registros. Verifique o CNAB original.");

        CnabFormato formato = DetectarFormato(linhas);
        if (formato == CnabFormato.Cnab400)
            throw new CnabNaoSuportadoException("CNAB 400 identificado. Este layout será suportado futuramente; por enquanto o Decodificador aceita apenas o CNAB 240 Santander com Segmentos A/B.");

        if (formato != CnabFormato.Cnab240)
            throw new CnabNaoSuportadoException("Não foi possível identificar o arquivo como CNAB 240 ou CNAB 400.");

        string banco = Campo(linhas[0], 1, 3);
        bool temHeaderArquivo = linhas.Any(l => TipoRegistro(l) == '0');
        bool temHeaderLote = linhas.Any(l => TipoRegistro(l) == '1');
        bool temTrailerLote = linhas.Any(l => TipoRegistro(l) == '5');
        bool temTrailerArquivo = linhas.Any(l => TipoRegistro(l) == '9');
        bool temA = linhas.Any(l => TipoRegistro(l) == '3' && Segmento(l) == 'A');
        bool temB = linhas.Any(l => TipoRegistro(l) == '3' && Segmento(l) == 'B');
        bool somenteSegmentosAB = linhas
            .Where(l => TipoRegistro(l) == '3')
            .All(l => Segmento(l) is 'A' or 'B');

        CnabLayoutTipo layout = banco == "033" && temHeaderArquivo && temHeaderLote && temTrailerLote && temTrailerArquivo && temA && temB && somenteSegmentosAB
            ? CnabLayoutTipo.SantanderPagamentoAB240
            : CnabLayoutTipo.Desconhecido;

        if (layout == CnabLayoutTipo.Desconhecido)
        {
            throw new CnabNaoSuportadoException(
                $"CNAB 240 identificado, porém o layout ainda não é suportado. Banco detectado: {banco}. " +
                "Nesta versão são aceitos apenas arquivos Santander (033) de pagamentos com Segmentos A e B.");
        }

        var pagamentos = LerPagamentosSantanderAb(linhas);
        if (pagamentos.Count == 0)
            throw new CnabNaoSuportadoException("O arquivo foi reconhecido como Santander CNAB 240, mas nenhum pagamento A+B pôde ser decodificado.");

        string headerLote = linhas.First(l => TipoRegistro(l) == '1');

        return new CnabArquivo
        {
            CaminhoOriginal = caminho,
            LinhasOriginais = linhas,
            PagamentosOriginais = pagamentos,
            Codificacao = codificacao,
            QuebraLinha = quebraLinha,
            TerminaComQuebraLinha = terminaComQuebra,
            Formato = formato,
            Layout = layout,
            BancoOrigem = banco,
            TipoServico = Campo(headerLote, 10, 2),
            FormaLancamento = Campo(headerLote, 12, 2),
            NumeroSequencialArquivo = LerInteiro(Campo(linhas.First(l => TipoRegistro(l) == '0'), 158, 6))
        };
    }

    public static void Salvar(CnabArquivo arquivo, IEnumerable<CnabPagamentoItem> pagamentosAtuais, string destino)
    {
        var atuais = pagamentosAtuais.ToList();
        if (atuais.Count == 0)
            throw new InvalidOperationException("Não é possível gerar um CNAB sem nenhum pagamento.");

        foreach (var item in atuais)
        {
            if (!item.TentarObterValor(out decimal valor) || valor <= 0)
                throw new InvalidOperationException($"Valor inválido para '{item.Nome}'.");

            if (!item.TentarObterData(out _))
                throw new InvalidOperationException($"Data inválida para '{item.Nome}'. Use DD/MM/AAAA.");
        }

        var linhas = new List<string>(arquivo.LinhasOriginais);
        var atuaisSet = new HashSet<CnabPagamentoItem>(atuais);

        // Primeiro altera os registros ainda existentes usando os índices originais.
        foreach (var item in atuais)
        {
            item.TentarObterValor(out decimal novoValor);
            item.TentarObterData(out DateTime novaData);

            string segA = linhas[item.IndiceSegmentoA];
            segA = SubstituirCampo(segA, 94, 8, novaData.ToString("ddMMyyyy"));
            segA = SubstituirCampo(segA, 120, 15, FormatarValor(novoValor, 15));
            linhas[item.IndiceSegmentoA] = segA;

            string segB = linhas[item.IndiceSegmentoB];
            segB = SubstituirCampo(segB, 128, 8, novaData.ToString("ddMMyyyy"));

            // O Valor do Documento no B é opcional. Só o atualizamos se o arquivo original já o utilizava.
            if (item.ValorDocumentoSegmentoBOriginal > 0)
                segB = SubstituirCampo(segB, 136, 15, FormatarValor(novoValor, 15));

            linhas[item.IndiceSegmentoB] = segB;
        }

        // Remove A+B dos pagamentos excluídos.
        var indicesRemover = arquivo.PagamentosOriginais
            .Where(p => !atuaisSet.Contains(p))
            .SelectMany(p => new[] { p.IndiceSegmentoA, p.IndiceSegmentoB })
            .ToHashSet();

        var resultado = linhas
            .Where((_, indice) => !indicesRemover.Contains(indice))
            .ToList();

        RenumerarDetalhesPorLote(resultado);
        RecalcularTrailers(resultado, atuais);

        if (resultado.Any(l => l.Length != 240))
            throw new InvalidOperationException("A geração resultou em registro diferente de 240 posições. O arquivo não foi salvo.");

        string textoFinal = string.Join(arquivo.QuebraLinha, resultado);
        if (arquivo.TerminaComQuebraLinha)
            textoFinal += arquivo.QuebraLinha;

        File.WriteAllText(destino, textoFinal, arquivo.Codificacao);
    }

    public static CnabFormato DetectarFormato(IReadOnlyList<string> linhas)
    {
        if (linhas.Count == 0) return CnabFormato.Desconhecido;
        if (linhas.All(l => l.Length == 240)) return CnabFormato.Cnab240;
        if (linhas.All(l => l.Length == 400)) return CnabFormato.Cnab400;
        return CnabFormato.Desconhecido;
    }

    public static bool TentarParsearData(string? texto, out DateTime data)
    {
        return DateTime.TryParseExact(
            texto?.Trim(),
            new[] { "dd/MM/yyyy", "d/M/yyyy", "ddMMyyyy" },
            CulturaBr,
            DateTimeStyles.None,
            out data);
    }

    public static bool TentarParsearValor(string? texto, out decimal valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        string normalizado = texto.Trim().Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "");

        if (decimal.TryParse(normalizado, NumberStyles.Number, CulturaBr, out valor))
            return true;

        if (decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out valor))
            return true;

        normalizado = normalizado.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }

    public static string NomeBanco(string codigo) => codigo switch
    {
        "001" => "Banco do Brasil",
        "033" => "Santander",
        "041" => "Banrisul",
        "077" => "Banco Inter",
        "104" => "Caixa Econômica Federal",
        "237" => "Bradesco",
        "260" => "Nubank",
        "336" => "C6 Bank",
        "341" => "Itaú Unibanco",
        "422" => "Safra",
        "748" => "Sicredi",
        "756" => "Sicoob",
        _ => $"Banco código {codigo}"
    };

    private static List<CnabPagamentoItem> LerPagamentosSantanderAb(IReadOnlyList<string> linhas)
    {
        var pagamentos = new List<CnabPagamentoItem>();

        for (int i = 0; i < linhas.Count; i++)
        {
            string linha = linhas[i];
            if (TipoRegistro(linha) != '3' || Segmento(linha) != 'A')
                continue;

            int indiceB = -1;
            for (int j = i + 1; j < linhas.Count; j++)
            {
                if (TipoRegistro(linhas[j]) != '3') break;
                char seg = Segmento(linhas[j]);
                if (seg == 'A') break;
                if (seg == 'B') { indiceB = j; break; }
            }

            if (indiceB < 0)
                continue;

            string segB = linhas[indiceB];
            string dataRaw = Campo(linha, 94, 8);
            string valorRaw = Campo(linha, 120, 15);

            if (!DateTime.TryParseExact(dataRaw, "ddMMyyyy", CulturaBr, DateTimeStyles.None, out DateTime data))
                continue;

            decimal valor = ParseValorCnab(valorRaw);
            decimal valorDocumentoB = ParseValorCnab(Campo(segB, 136, 15));

            var item = new CnabPagamentoItem
            {
                Nome = Campo(linha, 44, 30).Trim(),
                BancoCodigo = Campo(linha, 21, 3),
                Agencia = LimparZerosEsquerda(Campo(linha, 24, 5)),
                AgenciaDv = Campo(linha, 29, 1).Trim(),
                Conta = LimparZerosEsquerda(Campo(linha, 30, 12)),
                ContaDv = Campo(linha, 42, 1).Trim(),
                AgenciaContaDv = Campo(linha, 43, 1).Trim(),
                DocumentoFavorecido = NormalizarDocumentoFavorecido(Campo(segB, 18, 1), Campo(segB, 19, 14)),
                DocumentoCliente = Campo(linha, 74, 20).Trim(),
                LogradouroFavorecido = Campo(segB, 33, 30).Trim(),
                NumeroEnderecoFavorecido = Campo(segB, 63, 5).Trim(),
                ComplementoEnderecoFavorecido = Campo(segB, 68, 15).Trim(),
                BairroFavorecido = Campo(segB, 83, 15).Trim(),
                CidadeFavorecido = Campo(segB, 98, 20).Trim(),
                CepFavorecido = Campo(segB, 118, 8).Trim(),
                UfFavorecido = Campo(segB, 126, 2).Trim(),
                LoteServico = Campo(linha, 4, 4),
                ValorOriginal = valor,
                DataOriginal = data,
                ValorDocumentoSegmentoBOriginal = valorDocumentoB,
                IndiceSegmentoA = i,
                IndiceSegmentoB = indiceB,
                ValorTexto = valor.ToString("N2", CulturaBr),
                DataTexto = data.ToString("dd/MM/yyyy"),
                DataIndividualHabilitada = false
            };

            pagamentos.Add(item);
        }

        return pagamentos;
    }

    private static void RenumerarDetalhesPorLote(List<string> linhas)
    {
        string loteAtual = string.Empty;
        int sequencial = 0;

        for (int i = 0; i < linhas.Count; i++)
        {
            char tipo = TipoRegistro(linhas[i]);
            if (tipo == '1')
            {
                loteAtual = Campo(linhas[i], 4, 4);
                sequencial = 0;
            }
            else if (tipo == '3')
            {
                string lote = Campo(linhas[i], 4, 4);
                if (!string.Equals(lote, loteAtual, StringComparison.Ordinal))
                {
                    loteAtual = lote;
                    sequencial = 0;
                }

                sequencial++;
                linhas[i] = SubstituirCampo(linhas[i], 9, 5, sequencial.ToString("D5"));
            }
        }
    }

    private static void RecalcularTrailers(List<string> linhas, IReadOnlyCollection<CnabPagamentoItem> pagamentos)
    {
        // Trailer de cada lote: quantidade tipo 1+3+5 e somatória dos pagamentos do lote.
        for (int i = 0; i < linhas.Count; i++)
        {
            if (TipoRegistro(linhas[i]) != '5') continue;

            string lote = Campo(linhas[i], 4, 4);
            int quantidadeLote = linhas.Count(l =>
                Campo(l, 4, 4) == lote && (TipoRegistro(l) == '1' || TipoRegistro(l) == '3' || TipoRegistro(l) == '5'));

            decimal soma = 0m;
            foreach (var pagamento in pagamentos.Where(p => p.LoteServico == lote))
            {
                if (!pagamento.TentarObterValor(out decimal valor))
                    throw new InvalidOperationException($"Valor inválido para '{pagamento.Nome}'.");
                soma += valor;
            }

            string trailer = linhas[i];
            trailer = SubstituirCampo(trailer, 18, 6, quantidadeLote.ToString("D6"));
            trailer = SubstituirCampo(trailer, 24, 18, FormatarValor(soma, 18));
            linhas[i] = trailer;
        }

        int quantidadeLotes = linhas.Count(l => TipoRegistro(l) == '1');
        int quantidadeRegistros = linhas.Count;

        for (int i = 0; i < linhas.Count; i++)
        {
            if (TipoRegistro(linhas[i]) != '9') continue;

            string trailer = linhas[i];
            trailer = SubstituirCampo(trailer, 18, 6, quantidadeLotes.ToString("D6"));
            trailer = SubstituirCampo(trailer, 24, 6, quantidadeRegistros.ToString("D6"));
            linhas[i] = trailer;
        }
    }

    private static (string texto, Encoding codificacao) DecodificarPreservandoBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            return (utf8Bom.GetString(bytes, 3, bytes.Length - 3), utf8Bom);
        }

        // CNAB tradicional é arquivo posicional de um byte por caractere. Latin1 garante round-trip byte a byte.
        return (Encoding.Latin1.GetString(bytes), Encoding.Latin1);
    }

    private static char TipoRegistro(string linha)
        => linha.Length >= 8 ? linha[7] : '\0';

    private static char Segmento(string linha)
        => linha.Length >= 14 ? linha[13] : '\0';

    private static int LerInteiro(string valor)
    {
        return int.TryParse(valor.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int numero)
            ? numero
            : 0;
    }

    private static string Campo(string linha, int de, int tamanho)
    {
        int indice = de - 1;
        if (indice < 0 || tamanho < 0 || indice + tamanho > linha.Length)
            return string.Empty;
        return linha.Substring(indice, tamanho);
    }

    private static string SubstituirCampo(string linha, int de, int tamanho, string valor)
    {
        int indice = de - 1;
        if (linha.Length < indice + tamanho)
            throw new InvalidOperationException($"Registro CNAB menor que o esperado ao alterar posições {de}-{de + tamanho - 1}.");

        string ajustado = valor.Length > tamanho ? valor[..tamanho] : valor.PadRight(tamanho, ' ');
        return linha.Remove(indice, tamanho).Insert(indice, ajustado);
    }

    private static decimal ParseValorCnab(string campo)
    {
        if (!long.TryParse(campo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long centavos))
            return 0m;
        return centavos / 100m;
    }

    private static string FormatarValor(decimal valor, int tamanho)
    {
        long centavos = checked((long)Math.Round(valor * 100m, 0, MidpointRounding.AwayFromZero));
        string bruto = centavos.ToString(CultureInfo.InvariantCulture);
        if (bruto.Length > tamanho)
            throw new InvalidOperationException("Valor maior do que o campo permitido pelo layout CNAB.");
        return bruto.PadLeft(tamanho, '0');
    }

    private static string NormalizarDocumentoFavorecido(string tipoInscricao, string campoDocumento)
    {
        string digitos = new string(campoDocumento.Where(char.IsDigit).ToArray());

        // Santander/FEBRABAN: CPF costuma vir alinhado em campo de 14 posições com zeros à esquerda.
        if (tipoInscricao == "1" && digitos.Length >= 11)
            return digitos[^11..];
        if (tipoInscricao == "2" && digitos.Length >= 14)
            return digitos[^14..];

        return digitos.TrimStart('0');
    }

    private static string LimparZerosEsquerda(string valor)
    {
        string limpo = valor.Trim().TrimStart('0');
        return string.IsNullOrEmpty(limpo) ? "0" : limpo;
    }
}
