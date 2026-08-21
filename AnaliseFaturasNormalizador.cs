using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HubFinanceiro;

/// <summary>
/// Camada pura de normalização utilizada pela Análise de Faturas.
/// Não acessa arquivos, janelas, banco de dados ou estado global.
/// </summary>
public static class AnaliseFaturasNormalizador
{
    private static readonly Regex RegexCertificadoComBarra = new(
        @"^(?<base>\d{1,7})\s*/\s*(?<dependente>\d{1,2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RegexMesAno = new(
        @"^(?<mes>0?[1-9]|1[0-2])\s*[/\-]\s*(?<ano>20\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RegexAnoMes = new(
        @"^(?<ano>20\d{2})\s*[/\-]\s*(?<mes>0?[1-9]|1[0-2])$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Normaliza o certificado para o formato 0000000/00.
    /// Aceita, por exemplo: 0000004/00, 4/0, 000000400 e 0000004.
    /// Quando recebe somente a base de sete dígitos, considera titular /00.
    /// </summary>
    public static string? NormalizarCertificadoFatura(string? certificado)
    {
        if (string.IsNullOrWhiteSpace(certificado))
            return null;

        string texto = certificado.Trim();
        Match match = RegexCertificadoComBarra.Match(texto);
        if (match.Success)
        {
            string baseCertificado = match.Groups["base"].Value.PadLeft(7, '0');
            string dependente = match.Groups["dependente"].Value.PadLeft(2, '0');
            return $"{baseCertificado}/{dependente}";
        }

        string digitos = SomenteDigitos(texto);
        if (digitos.Length == 7)
            return $"{digitos}/00";

        if (digitos.Length == 9)
            return $"{digitos[..7]}/{digitos.Substring(7, 2)}";

        return null;
    }

    /// <summary>
    /// Extrai o Certif. da fatura a partir do cartão longo do Over.
    /// Layout empírico validado nos arquivos Bradesco usados pelo processo:
    /// 5 dígitos iniciais + 7 dígitos do certificado + 2 dígitos do dependente + 1 DV.
    /// Ex.: 952490000004001 -> 0000004/00.
    /// </summary>
    public static string? ExtrairCertificadoDoCartaoOver(string? cartao)
    {
        if (string.IsNullOrWhiteSpace(cartao))
            return null;

        string digitos = SomenteDigitos(cartao);
        if (digitos.Length != 15)
            return null;

        string baseCertificado = digitos.Substring(5, 7);
        string dependente = digitos.Substring(12, 2);
        return NormalizarCertificadoFatura($"{baseCertificado}/{dependente}");
    }

    /// <summary>
    /// Normaliza nomes para comparação: maiúsculas, sem acentos, pontuação transformada
    /// em espaço e espaços duplicados removidos. Não remove partículas como DE/DA/DO.
    /// </summary>
    public static string NormalizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return string.Empty;

        string decomposed = nome.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool ultimoFoiEspaco = true;

        foreach (char c in decomposed)
        {
            UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
                ultimoFoiEspaco = false;
            }
            else if (!ultimoFoiEspaco)
            {
                sb.Append(' ');
                ultimoFoiEspaco = true;
            }
        }

        return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normaliza valores textuais para decimal. Suporta formato brasileiro,
    /// sinal antes/depois do número, R$ e parênteses para negativos.
    /// Texto vazio retorna null; texto inválido gera FormatException.
    /// </summary>
    public static decimal? NormalizarValor(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        string texto = valor
            .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        bool negativo = false;

        if (texto.StartsWith('(') && texto.EndsWith(')') && texto.Length > 2)
        {
            negativo = true;
            texto = texto[1..^1];
        }

        if (texto.EndsWith('-'))
        {
            negativo = true;
            texto = texto[..^1];
        }

        if (texto.StartsWith('-'))
        {
            negativo = true;
            texto = texto[1..];
        }
        else if (texto.StartsWith('+'))
        {
            texto = texto[1..];
        }

        if (string.IsNullOrWhiteSpace(texto))
            throw new FormatException($"Valor não reconhecido: '{valor}'.");

        string canonico;
        if (texto.Contains(','))
        {
            // Formato brasileiro: ponto como milhar e vírgula como decimal.
            canonico = texto.Replace(".", string.Empty, StringComparison.Ordinal)
                            .Replace(',', '.');
        }
        else if (texto.Contains('.'))
        {
            string[] partes = texto.Split('.');

            // Um único ponto seguido de 1 ou 2 dígitos é tratado como decimal.
            // Demais casos compostos por grupos de 3 dígitos são tratados como milhar.
            if (partes.Length == 2 && partes[1].Length is 1 or 2)
            {
                canonico = texto;
            }
            else if (partes.Length > 1 && partes.Skip(1).All(x => x.Length == 3 && x.All(char.IsDigit)))
            {
                canonico = string.Concat(partes);
            }
            else
            {
                canonico = texto;
            }
        }
        else
        {
            canonico = texto;
        }

        if (!decimal.TryParse(
                canonico,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal resultado))
        {
            throw new FormatException($"Valor não reconhecido: '{valor}'.");
        }

        return negativo ? -Math.Abs(resultado) : resultado;
    }

    public static decimal? NormalizarValor(decimal? valor) => valor;

    /// <summary>
    /// Normaliza competência para o primeiro dia do mês.
    /// Suporta MM/yyyy, yyyyMM, yyyy-MM, yyyy/MM e datas completas usuais.
    /// Texto vazio retorna null; formato inválido gera FormatException.
    /// </summary>
    public static DateTime? NormalizarCompetencia(string? competencia)
    {
        if (string.IsNullOrWhiteSpace(competencia))
            return null;

        string texto = competencia.Trim();
        string digitos = SomenteDigitos(texto);

        if (digitos.Length == 6 && int.TryParse(digitos[..4], out int anoCompacto) &&
            int.TryParse(digitos.Substring(4, 2), out int mesCompacto) &&
            anoCompacto is >= 2000 and <= 2999 && mesCompacto is >= 1 and <= 12)
        {
            return new DateTime(anoCompacto, mesCompacto, 1);
        }

        Match mesAno = RegexMesAno.Match(texto);
        if (mesAno.Success)
        {
            return new DateTime(
                int.Parse(mesAno.Groups["ano"].Value, CultureInfo.InvariantCulture),
                int.Parse(mesAno.Groups["mes"].Value, CultureInfo.InvariantCulture),
                1);
        }

        Match anoMes = RegexAnoMes.Match(texto);
        if (anoMes.Success)
        {
            return new DateTime(
                int.Parse(anoMes.Groups["ano"].Value, CultureInfo.InvariantCulture),
                int.Parse(anoMes.Groups["mes"].Value, CultureInfo.InvariantCulture),
                1);
        }

        string[] formatos =
        {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
            "yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd"
        };

        if (DateTime.TryParseExact(
                texto,
                formatos,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime data))
        {
            return new DateTime(data.Year, data.Month, 1);
        }

        throw new FormatException($"Competência não reconhecida: '{competencia}'.");
    }

    public static DateTime NormalizarCompetencia(DateTime competencia)
        => new(competencia.Year, competencia.Month, 1);

    private static string SomenteDigitos(string texto)
        => new string(texto.Where(char.IsDigit).ToArray());
}
