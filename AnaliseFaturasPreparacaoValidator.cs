using ClosedXML.Excel;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HubFinanceiro;

public sealed class AnaliseFaturasPreparacaoValidator
{
    private static readonly string[] CabecalhosOverObrigatorios =
    {
        "PERIODO",
        "OPERADORA",
        "ENTIDADE",
        "APOLICE",
        "MATRICULA",
        "BENEFICIARIO",
        "EVENTO",
        "DESCRICAO",
        "TITULO",
        "VALOR (PV)",
        "VALOR (NET)",
        "VALOR (OVER)",
        "INADIMPLENTE",
        "CARTAO"
    };

    public GrupoFaturasValidacao ValidarGrupoFaturas(IReadOnlyCollection<string> arquivos)
    {
        if (arquivos.Count == 0)
            return GrupoFaturasValidacao.Invalida("Nenhum PDF foi selecionado.");

        var resultados = new List<FaturaArquivoValidacao>();

        foreach (string arquivo in arquivos)
            resultados.Add(ValidarFatura(arquivo));

        var invalidos = resultados.Where(x => !x.Valido).ToList();
        if (invalidos.Count > 0)
        {
            string detalhes = string.Join(
                "\n",
                invalidos.Take(6).Select(x => $"• {Path.GetFileName(x.Arquivo)}: {x.Mensagem}"));

            if (invalidos.Count > 6)
                detalhes += $"\n• ... e mais {invalidos.Count - 6} arquivo(s).";

            return GrupoFaturasValidacao.Invalida(
                "Um ou mais PDFs não foram reconhecidos como faturas válidas para esta análise.\n\n" + detalhes,
                resultados);
        }

        var competencias = resultados
            .Where(x => x.Competencia.HasValue)
            .Select(x => PrimeiroDiaDoMes(x.Competencia!.Value))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (competencias.Count != 1)
        {
            string detalhes = string.Join(
                "\n",
                resultados.Select(x => $"• {Path.GetFileName(x.Arquivo)}: {FormatarCompetencia(x.Competencia)}"));

            return GrupoFaturasValidacao.Invalida(
                "Foram encontradas competências diferentes dentro do mesmo grupo de faturas.\n\n" + detalhes,
                resultados);
        }

        return GrupoFaturasValidacao.Valida(competencias[0], resultados);
    }

    public OverValidacao ValidarOver(string? arquivo)
    {
        if (string.IsNullOrWhiteSpace(arquivo) || !File.Exists(arquivo))
            return OverValidacao.Invalida("O arquivo Over selecionado não foi encontrado.");

        string extensao = Path.GetExtension(arquivo);
        if (!extensao.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !extensao.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return OverValidacao.Invalida(
                "O relatório Over deve estar no formato .xlsx ou .xlsm. Arquivos .xls antigos não são aceitos nesta etapa.");
        }

        try
        {
            using var workbook = new XLWorkbook(arquivo);

            IXLWorksheet? planilhaEncontrada = null;
            int linhaCabecalho = 0;
            Dictionary<string, int>? colunas = null;

            foreach (IXLWorksheet planilha in workbook.Worksheets)
            {
                int ultimaLinha = planilha.LastRowUsed()?.RowNumber() ?? 0;
                int ultimaColuna = planilha.LastColumnUsed()?.ColumnNumber() ?? 0;

                if (ultimaLinha == 0 || ultimaColuna == 0)
                    continue;

                int limiteLinha = Math.Min(12, ultimaLinha);

                for (int linha = 1; linha <= limiteLinha; linha++)
                {
                    var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    for (int coluna = 1; coluna <= ultimaColuna; coluna++)
                    {
                        string cabecalho = NormalizarCabecalho(planilha.Cell(linha, coluna).GetString());
                        if (!string.IsNullOrWhiteSpace(cabecalho) && !mapa.ContainsKey(cabecalho))
                            mapa[cabecalho] = coluna;
                    }

                    if (CabecalhosOverObrigatorios.All(mapa.ContainsKey))
                    {
                        planilhaEncontrada = planilha;
                        linhaCabecalho = linha;
                        colunas = mapa;
                        break;
                    }
                }

                if (planilhaEncontrada != null)
                    break;
            }

            if (planilhaEncontrada == null || colunas == null)
            {
                return OverValidacao.Invalida(
                    "A estrutura esperada do relatório Over não foi encontrada.\n\n" +
                    "O HUB procura as colunas: Periodo, Operadora, Entidade, Apolice, Matricula, Beneficiario, Evento, Descricao, Titulo, Valor (PV), Valor (NET), Valor (Over), Inadimplente e Cartao.");
            }

            int colunaPeriodo = colunas["PERIODO"];
            int ultimaLinhaDados = planilhaEncontrada.LastRowUsed()?.RowNumber() ?? linhaCabecalho;
            var competencias = new HashSet<DateTime>();
            int linhasComDados = 0;

            for (int linha = linhaCabecalho + 1; linha <= ultimaLinhaDados; linha++)
            {
                var celulaPeriodo = planilhaEncontrada.Cell(linha, colunaPeriodo);
                if (celulaPeriodo.IsEmpty())
                    continue;

                linhasComDados++;

                DateTime? competencia = LerCompetenciaOver(celulaPeriodo);
                if (competencia.HasValue)
                    competencias.Add(PrimeiroDiaDoMes(competencia.Value));
            }

            if (linhasComDados == 0)
                return OverValidacao.Invalida("O relatório Over possui os cabeçalhos esperados, mas não contém linhas de dados.");

            if (competencias.Count == 0)
                return OverValidacao.Invalida("Não foi possível identificar a competência na coluna Periodo do relatório Over.");

            if (competencias.Count > 1)
            {
                string lista = string.Join(", ", competencias.OrderBy(x => x).Select(x => FormatarCompetencia(x)));
                return OverValidacao.Invalida($"O relatório Over contém mais de uma competência na coluna Periodo: {lista}.");
            }

            return OverValidacao.Valida(
                competencias.Single(),
                planilhaEncontrada.Name,
                linhaCabecalho,
                linhasComDados);
        }
        catch (Exception ex)
        {
            return OverValidacao.Invalida(
                "Não foi possível abrir ou validar o relatório Over.\n\n" + ex.Message);
        }
    }

    public PreparacaoAnaliseValidacao ValidarSequencia(
        GrupoFaturasValidacao? mesPassado,
        GrupoFaturasValidacao? mesAtual,
        GrupoFaturasValidacao? mesSeguinte,
        OverValidacao? over)
    {
        if (mesPassado?.Valido != true ||
            mesAtual?.Valido != true ||
            mesSeguinte?.Valido != true ||
            over?.Valido != true ||
            !mesPassado.Competencia.HasValue ||
            !mesAtual.Competencia.HasValue ||
            !mesSeguinte.Competencia.HasValue ||
            !over.Competencia.HasValue)
        {
            return PreparacaoAnaliseValidacao.Invalida(
                "Selecione e valide os quatro grupos para conferir a sequência das competências.");
        }

        DateTime passado = PrimeiroDiaDoMes(mesPassado.Competencia.Value);
        DateTime atualEsperado = passado.AddMonths(1);
        DateTime seguinteEsperado = passado.AddMonths(2);
        DateTime atual = PrimeiroDiaDoMes(mesAtual.Competencia.Value);
        DateTime seguinte = PrimeiroDiaDoMes(mesSeguinte.Competencia.Value);
        DateTime competenciaOver = PrimeiroDiaDoMes(over.Competencia.Value);

        var erros = new List<string>();

        if (competenciaOver != passado)
        {
            erros.Add(
                $"Over: esperado {FormatarCompetencia(passado)}, encontrado {FormatarCompetencia(competenciaOver)}.");
        }

        if (atual != atualEsperado)
        {
            erros.Add(
                $"Faturas mês atual: esperado {FormatarCompetencia(atualEsperado)}, encontrado {FormatarCompetencia(atual)}.");
        }

        if (seguinte != seguinteEsperado)
        {
            erros.Add(
                $"Faturas mês que vem: esperado {FormatarCompetencia(seguinteEsperado)}, encontrado {FormatarCompetencia(seguinte)}.");
        }

        if (erros.Count > 0)
        {
            return PreparacaoAnaliseValidacao.Invalida(
                "As competências selecionadas não formam a sequência esperada.\n" + string.Join("\n", erros));
        }

        return PreparacaoAnaliseValidacao.Valida(
            passado,
            $"Preparação válida • análise de {FormatarCompetencia(passado)}");
    }

    private static FaturaArquivoValidacao ValidarFatura(string arquivo)
    {
        if (!File.Exists(arquivo))
            return FaturaArquivoValidacao.Invalida(arquivo, "Arquivo não encontrado.");

        if (!Path.GetExtension(arquivo).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return FaturaArquivoValidacao.Invalida(arquivo, "O arquivo não é PDF.");

        try
        {
            using var reader = new PdfReader(arquivo);
            using var documento = new PdfDocument(reader);

            if (documento.GetNumberOfPages() < 1)
                return FaturaArquivoValidacao.Invalida(arquivo, "PDF sem páginas.");

            string textoPrimeiraPagina = PdfTextExtractor.GetTextFromPage(documento.GetPage(1));
            if (string.IsNullOrWhiteSpace(textoPrimeiraPagina) || textoPrimeiraPagina.Trim().Length < 80)
            {
                return FaturaArquivoValidacao.Invalida(
                    arquivo,
                    "O PDF não possui texto suficiente para reconhecer a fatura.");
            }

            string textoNormalizado = NormalizarTextoParaBusca(textoPrimeiraPagina);

            string[] marcadoresObrigatorios =
            {
                "CIA",
                "SUC",
                "APOL",
                "FATURA",
                "CONTRATANTE",
                "SUBFATURA",
                "POSITIVA ADM DE BENEFICIOS LTDA"
            };

            var ausentes = marcadoresObrigatorios
                .Where(m => !textoNormalizado.Contains(m, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (ausentes.Count > 0)
            {
                return FaturaArquivoValidacao.Invalida(
                    arquivo,
                    "A estrutura esperada da fatura Bradesco não foi reconhecida.");
            }

            DateTime? competencia = ExtrairCompetenciaFatura(textoPrimeiraPagina);
            if (!competencia.HasValue)
            {
                return FaturaArquivoValidacao.Invalida(
                    arquivo,
                    "Não foi possível identificar a competência no cabeçalho da fatura.");
            }

            return FaturaArquivoValidacao.Valida(arquivo, PrimeiroDiaDoMes(competencia.Value));
        }
        catch (Exception ex)
        {
            return FaturaArquivoValidacao.Invalida(
                arquivo,
                "Não foi possível ler o PDF: " + ex.Message);
        }
    }

    private static DateTime? ExtrairCompetenciaFatura(string texto)
    {
        string semAcentos = RemoverAcentos(texto).ToUpperInvariant();

        Match matchMedica = Regex.Match(
            semAcentos,
            @"\bMEDICA\s+(?<mes>0[1-9]|1[0-2])/(?<ano>20\d{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (matchMedica.Success &&
            int.TryParse(matchMedica.Groups["mes"].Value, out int mes) &&
            int.TryParse(matchMedica.Groups["ano"].Value, out int ano))
        {
            return new DateTime(ano, mes, 1);
        }

        // Fallback: procura MM/AAAA que não faça parte de uma data dd/MM/AAAA.
        MatchCollection matches = Regex.Matches(
            semAcentos,
            @"(?<!/)(?<mes>0[1-9]|1[0-2])/(?<ano>20\d{2})\b",
            RegexOptions.CultureInvariant);

        var competencias = matches
            .Cast<Match>()
            .Select(match =>
            {
                bool mesOk = int.TryParse(match.Groups["mes"].Value, out int mesEncontrado);
                bool anoOk = int.TryParse(match.Groups["ano"].Value, out int anoEncontrado);
                return mesOk && anoOk
                    ? new DateTime?(new DateTime(anoEncontrado, mesEncontrado, 1))
                    : null;
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        return competencias.Count == 1 ? competencias[0] : null;
    }

    private static DateTime? LerCompetenciaOver(IXLCell celula)
    {
        if (celula.DataType == XLDataType.DateTime)
        {
            DateTime data = celula.GetDateTime();
            return new DateTime(data.Year, data.Month, 1);
        }

        if (celula.DataType == XLDataType.Number)
        {
            double numero = celula.GetDouble();
            int inteiro = (int)Math.Round(numero, MidpointRounding.AwayFromZero);
            return InterpretarPeriodoNumerico(inteiro.ToString(CultureInfo.InvariantCulture));
        }

        string texto = celula.GetString().Trim();
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        string digitos = Regex.Replace(texto, @"\D", string.Empty);
        return InterpretarPeriodoNumerico(digitos);
    }

    private static DateTime? InterpretarPeriodoNumerico(string texto)
    {
        if (texto.Length != 6)
            return null;

        if (!int.TryParse(texto[..4], out int ano) ||
            !int.TryParse(texto.Substring(4, 2), out int mes))
            return null;

        if (ano < 2000 || ano > 2200 || mes < 1 || mes > 12)
            return null;

        return new DateTime(ano, mes, 1);
    }

    private static string NormalizarCabecalho(string valor)
    {
        string texto = RemoverAcentos(valor).ToUpperInvariant().Trim();
        texto = Regex.Replace(texto, @"\s+", " ");
        return texto;
    }

    private static string NormalizarTextoParaBusca(string valor)
    {
        string texto = RemoverAcentos(valor).ToUpperInvariant();
        texto = Regex.Replace(texto, @"\s+", " ");
        return texto;
    }

    private static string RemoverAcentos(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        string normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);

        foreach (char c in normalizado)
        {
            UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static DateTime PrimeiroDiaDoMes(DateTime data)
        => new(data.Year, data.Month, 1);

    public static string FormatarCompetencia(DateTime? competencia)
        => competencia.HasValue ? competencia.Value.ToString("MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) : "não identificada";
}

public sealed record FaturaArquivoValidacao(
    string Arquivo,
    bool Valido,
    DateTime? Competencia,
    string Mensagem)
{
    public static FaturaArquivoValidacao Valida(string arquivo, DateTime competencia)
        => new(arquivo, true, competencia, "OK");

    public static FaturaArquivoValidacao Invalida(string arquivo, string mensagem)
        => new(arquivo, false, null, mensagem);
}

public sealed record GrupoFaturasValidacao(
    bool Valido,
    DateTime? Competencia,
    string Mensagem,
    IReadOnlyList<FaturaArquivoValidacao> Arquivos)
{
    public static GrupoFaturasValidacao Valida(DateTime competencia, IReadOnlyList<FaturaArquivoValidacao> arquivos)
        => new(true, competencia, "OK", arquivos);

    public static GrupoFaturasValidacao Invalida(string mensagem, IReadOnlyList<FaturaArquivoValidacao>? arquivos = null)
        => new(false, null, mensagem, arquivos ?? Array.Empty<FaturaArquivoValidacao>());
}

public sealed record OverValidacao(
    bool Valido,
    DateTime? Competencia,
    string Mensagem,
    string? Planilha,
    int LinhaCabecalho,
    int QuantidadeLinhas)
{
    public static OverValidacao Valida(DateTime competencia, string planilha, int linhaCabecalho, int quantidadeLinhas)
        => new(true, competencia, "OK", planilha, linhaCabecalho, quantidadeLinhas);

    public static OverValidacao Invalida(string mensagem)
        => new(false, null, mensagem, null, 0, 0);
}

public sealed record PreparacaoAnaliseValidacao(
    bool Valido,
    DateTime? CompetenciaAnalisada,
    string Mensagem)
{
    public static PreparacaoAnaliseValidacao Valida(DateTime competencia, string mensagem)
        => new(true, competencia, mensagem);

    public static PreparacaoAnaliseValidacao Invalida(string mensagem)
        => new(false, null, mensagem);
}
