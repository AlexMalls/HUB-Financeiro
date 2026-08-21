using System.Text.RegularExpressions;

namespace HubFinanceiro;

// =============================================================================
// NfseSpParser — Parser de Nota Fiscal Eletrônica de Serviços (NFS-e) — SP
// =============================================================================
// Suporte atual: NFS-e emitidas pelo município de São Paulo (Prefeitura de SP /
//                Secretaria Municipal da Fazenda).
//
// ⚠️  TODO: Implementar suporte a NFS-e de outros estados/municípios à medida
//           que forem identificados. Criar subclasses ou strategies por UF/cidade.
//
// Dependência de extração de texto: itext7 (NuGet)
//
// NOTA SOBRE EXTRAÇÃO iText7:
//   O iText7 (LocationTextExtractionStrategy) lê colunas de forma diferente
//   do pdfplumber: labels e valores do cabeçalho da nota ficam em linhas
//   separadas e fora de ordem. Por isso alguns campos usam regex direto no
//   padrão do valor (ex: DataEmissao) em vez de buscar pelo label.
// =============================================================================

/// <summary>
/// Dados extraídos de uma NFS-e do município de São Paulo.
/// </summary>
public sealed class NfseSpDados
{
    public string NumeroNota        { get; set; } = string.Empty;
    public string DataEmissao       { get; set; } = string.Empty;
    public string CodigoVerificacao { get; set; } = string.Empty;
    public string PrestadorNome     { get; set; } = string.Empty;
    public string PrestadorCnpj     { get; set; } = string.Empty;
    public string TomadorNome       { get; set; } = string.Empty;
    public string TomadorCnpj       { get; set; } = string.Empty;
    public string CodigoServico     { get; set; } = string.Empty;
    public string DescricaoServico  { get; set; } = string.Empty;
    public string Discriminacao     { get; set; } = string.Empty;
    public string ValorTotal        { get; set; } = string.Empty;
    public string Vencimento        { get; set; } = string.Empty;
    public string BaseCalculo       { get; set; } = string.Empty;
    public string Aliquota          { get; set; } = string.Empty;
    public string ValorIss          { get; set; } = string.Empty;
    public bool   EhNfseSpValida    { get; set; }
}

/// <summary>
/// Parser de NFS-e emitidas pelo município de São Paulo.
/// </summary>
public static class NfseSpParser
{
    private const string AssinaturaPrefeitura = "PREFEITURA DO MUNICÍPIO DE SÃO PAULO";
    private const string AssinaturaNfse       = "NOTA FISCAL ELETRÔNICA DE SERVIÇOS - NFS-e";

    public static NfseSpDados Parse(string texto)
    {
        var dados = new NfseSpDados();

        dados.EhNfseSpValida = texto.Contains(AssinaturaPrefeitura)
                            && texto.Contains(AssinaturaNfse);

        if (!dados.EhNfseSpValida)
            return dados;

        // ── Identificação ────────────────────────────────────────

        // Padrão 1: PDF com texto nativo — número fica em linha separada como dígitos puros
        dados.NumeroNota = CapturarLinha(texto,
            @"Número da Nota\s+[^\n]+\s+(\d{5,})");

        // Padrão 2 (fallback OCR): o número aparece na mesma linha que "PREFEITURA DO MUNICÍPIO",
        // após "|" ou "|"" — o OCR frequentemente garbleia dígitos como letras parecidas,
        // mas às vezes captura pelo menos alguns dígitos consecutivos
        if (string.IsNullOrEmpty(dados.NumeroNota))
        {
            // Tenta capturar sequência de dígitos após "|" na linha da prefeitura
            dados.NumeroNota = CapturarLinha(texto,
                @"PREFEITURA DO MUNICÍPIO[^\n]*\|\W*(\d{4,})");
        }

        // Padrão 3 (fallback OCR agressivo): captura qualquer sequência 5+ dígitos
        // que apareça na linha da prefeitura (após o "|"), mesmo misturada com letras
        if (string.IsNullOrEmpty(dados.NumeroNota))
        {
            var mPref = System.Text.RegularExpressions.Regex.Match(texto,
                @"PREFEITURA DO MUNICÍPIO[^\n]*\|[^\n]*");
            if (mPref.Success)
            {
                // Extrai todos os dígitos do trecho e verifica se parecem um número de nota
                string apenasDigitos = new string(mPref.Value.Where(char.IsDigit).ToArray());
                if (apenasDigitos.Length >= 4)
                    dados.NumeroNota = apenasDigitos.TrimStart('0').PadLeft(1, '0');
            }
        }

        // iText7 separa labels e valores do cabeçalho em linhas distintas,
        // fora de ordem visual. O formato dd/MM/yyyy HH:mm:ss (com hora e
        // segundos) é ÚNICO na nota inteira — capturamos direto pelo padrão.
        dados.DataEmissao = CapturarLinha(texto,
            @"(\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}:\d{2})");

        dados.CodigoVerificacao = CapturarLinha(texto,
            @"Código de Verificação\s*[^\n]*?([A-Z0-9]{4}-[A-Z0-9]{4})");

        // ── Prestador ────────────────────────────────────────────
        var blocoPrestador = CapturarBloco(texto,
            @"PRESTADOR DE SERVIÇOS\s*(.*?)TOMADOR DE SERVIÇOS");

        if (!string.IsNullOrEmpty(blocoPrestador))
        {
            dados.PrestadorCnpj = CapturarLinha(blocoPrestador,
                @"CPF/CNPJ:\s*([\d.\-/]+)");

            // [^\n]+ — para na primeira quebra, evita capturar Endereço
            dados.PrestadorNome = CapturarLinha(blocoPrestador,
                @"Nome/Razão Social:\s*([^\n]+)");
        }

        // ── Tomador ──────────────────────────────────────────────
        var blocoTomador = CapturarBloco(texto,
            @"TOMADOR DE SERVIÇOS\s*(.*?)INTERMEDIÁRIO DE SERVIÇOS");

        if (!string.IsNullOrEmpty(blocoTomador))
        {
            dados.TomadorNome = CapturarLinha(blocoTomador,
                @"Nome/Razão Social:\s*([^\n]+)");

            dados.TomadorCnpj = CapturarLinha(blocoTomador,
                @"CPF/CNPJ:\s*([\d.\-/]+)");
        }

        // ── Serviço ──────────────────────────────────────────────
        var mServico = Regex.Match(texto,
            @"Código do Serviço\s*\n(\d+)\s*-\s*([^\n]+)");

        if (mServico.Success)
        {
            dados.CodigoServico    = mServico.Groups[1].Value.Trim();
            dados.DescricaoServico = mServico.Groups[2].Value.Trim().TrimEnd('.');
        }

        var blocoDisc = CapturarBloco(texto,
            @"DISCRIMINAÇÃO DE SERVIÇOS\s*(.*?)VALOR TOTAL DO SERVIÇO");

        if (!string.IsNullOrEmpty(blocoDisc))
        {
            var linhas = blocoDisc.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            dados.Discriminacao = string.Join(" | ", linhas);
        }

        // ── Valores ──────────────────────────────────────────────
        dados.ValorTotal = CapturarLinha(texto,
            @"VALOR TOTAL DO SERVIÇO = R\$\s*([\d.,]+)");

        dados.Vencimento = CapturarLinha(texto,
            @"Vencimento:\s*(\d{2}/\d{2}/\d{4})");

        var mTabela = Regex.Match(texto,
            @"Alíquota \(%\) Valor do ISS \(R\$\)[^\n]+\n[\d.,]+\s+([\d.,]+)\s+([\d.,]+%)\s+([\d.,]+)");

        if (mTabela.Success)
        {
            dados.BaseCalculo = mTabela.Groups[1].Value.Trim();
            dados.Aliquota    = mTabela.Groups[2].Value.Trim();
            dados.ValorIss    = mTabela.Groups[3].Value.Trim();
        }

        return dados;
    }

    /// <summary>
    /// Captura grupo 1 com Multiline — ponto NÃO cruza quebras de linha.
    /// Usar para campos de linha única.
    /// </summary>
    private static string CapturarLinha(string texto, string padrao)
    {
        var m = Regex.Match(texto, padrao, RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// Captura grupo 1 com Singleline — ponto cruza quebras de linha.
    /// Usar para blocos multi-linha (prestador, tomador, discriminação).
    /// </summary>
    private static string CapturarBloco(string texto, string padrao)
    {
        var m = Regex.Match(texto, padrao, RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
    }
}
