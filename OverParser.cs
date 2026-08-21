using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HubFinanceiro;

public sealed class OverArquivo
{
    public string CaminhoArquivo { get; init; } = string.Empty;
    public string NomeArquivo { get; init; } = string.Empty;
    public string Planilha { get; init; } = string.Empty;
    public int LinhaCabecalho { get; init; }
    public DateTime? Competencia { get; init; }
    public IReadOnlyList<DateTime> CompetenciasEncontradas { get; init; } = Array.Empty<DateTime>();
    public int TotalLinhasPlanilha { get; init; }
    public int TotalLancamentos => Lancamentos.Count;
    public IReadOnlyList<OverLancamento> Lancamentos { get; init; } = Array.Empty<OverLancamento>();
    public IReadOnlyList<int> LinhasVaziasIgnoradas { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Avisos { get; init; } = Array.Empty<string>();
}

public sealed class OverLancamento
{
    public int NumeroLinha { get; init; }
    public string PeriodoOriginal { get; init; } = string.Empty;
    public DateTime? Competencia { get; init; }
    public string Operadora { get; init; } = string.Empty;
    public string Entidade { get; init; } = string.Empty;
    public string Apolice { get; init; } = string.Empty;
    public string Matricula { get; init; } = string.Empty;
    public string Beneficiario { get; init; } = string.Empty;
    public string Evento { get; init; } = string.Empty;
    public string Descricao { get; init; } = string.Empty;
    public string Titulo { get; init; } = string.Empty;
    public decimal? ValorPV { get; init; }
    public decimal? ValorNET { get; init; }
    public decimal? ValorOver { get; init; }
    public string Inadimplente { get; init; } = string.Empty;
    public string Cartao { get; init; } = string.Empty;
}

public sealed class OverParser
{
    private static readonly string[] CabecalhosObrigatorios =
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
        "VALORPV",
        "VALORNET",
        "VALOROVER",
        "INADIMPLENTE",
        "CARTAO"
    };

    public OverArquivo Ler(string arquivo)
    {
        if (string.IsNullOrWhiteSpace(arquivo))
            throw new ArgumentException("O caminho do relatório Over não foi informado.", nameof(arquivo));

        if (!File.Exists(arquivo))
            throw new FileNotFoundException("O relatório Over não foi encontrado.", arquivo);

        string extensao = Path.GetExtension(arquivo);
        if (!extensao.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !extensao.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O leitor estruturado do Over aceita arquivos .xlsx ou .xlsm.");
        }

        using var workbook = new XLWorkbook(arquivo);

        (IXLWorksheet planilha, int linhaCabecalho, Dictionary<string, int> colunas) =
            LocalizarEstrutura(workbook);

        int ultimaLinha = planilha.LastRowUsed()?.RowNumber() ?? linhaCabecalho;
        var lancamentos = new List<OverLancamento>();
        var linhasVazias = new List<int>();
        var avisos = new List<string>();
        var competencias = new HashSet<DateTime>();

        for (int linha = linhaCabecalho + 1; linha <= ultimaLinha; linha++)
        {
            if (LinhaSemDados(planilha, linha, colunas.Values))
            {
                linhasVazias.Add(linha);
                continue;
            }

            string periodoOriginal = LerTextoPreservando(planilha.Cell(linha, colunas["PERIODO"]));
            DateTime? competencia = LerCompetencia(periodoOriginal, planilha.Cell(linha, colunas["PERIODO"]));
            if (competencia.HasValue)
                competencias.Add(PrimeiroDiaDoMes(competencia.Value));

            var item = new OverLancamento
            {
                NumeroLinha = linha,
                PeriodoOriginal = periodoOriginal,
                Competencia = competencia.HasValue ? PrimeiroDiaDoMes(competencia.Value) : null,
                Operadora = LerTextoPreservando(planilha.Cell(linha, colunas["OPERADORA"])),
                Entidade = LerTextoPreservando(planilha.Cell(linha, colunas["ENTIDADE"])),
                Apolice = LerTextoPreservando(planilha.Cell(linha, colunas["APOLICE"])),
                Matricula = LerTextoPreservando(planilha.Cell(linha, colunas["MATRICULA"])),
                Beneficiario = LerTextoPreservando(planilha.Cell(linha, colunas["BENEFICIARIO"])),
                Evento = LerTextoPreservando(planilha.Cell(linha, colunas["EVENTO"])),
                Descricao = LerTextoPreservando(planilha.Cell(linha, colunas["DESCRICAO"])),
                Titulo = LerTextoPreservando(planilha.Cell(linha, colunas["TITULO"])),
                ValorPV = LerDecimalOpcional(planilha.Cell(linha, colunas["VALORPV"])),
                ValorNET = LerDecimalOpcional(planilha.Cell(linha, colunas["VALORNET"])),
                ValorOver = LerDecimalOpcional(planilha.Cell(linha, colunas["VALOROVER"])),
                Inadimplente = LerTextoPreservando(planilha.Cell(linha, colunas["INADIMPLENTE"])),
                Cartao = LerTextoPreservando(planilha.Cell(linha, colunas["CARTAO"]))
            };

            // Campos vazios são preservados como vazios; nesta etapa de leitura
            // eles não são tratados como divergência. Apenas problemas estruturais
            // de período são registrados como aviso.
            if (!item.Competencia.HasValue)
                avisos.Add($"Linha {linha}: período não reconhecido ({item.PeriodoOriginal}).");

            lancamentos.Add(item);
        }

        List<DateTime> competenciasOrdenadas = competencias.OrderBy(x => x).ToList();
        DateTime? competenciaUnica = competenciasOrdenadas.Count == 1
            ? competenciasOrdenadas[0]
            : null;

        if (competenciasOrdenadas.Count > 1)
        {
            avisos.Insert(
                0,
                "Foram encontradas várias competências no arquivo: " +
                string.Join(", ", competenciasOrdenadas.Select(x => x.ToString("MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")))) + ".");
        }

        return new OverArquivo
        {
            CaminhoArquivo = Path.GetFullPath(arquivo),
            NomeArquivo = Path.GetFileName(arquivo),
            Planilha = planilha.Name,
            LinhaCabecalho = linhaCabecalho,
            Competencia = competenciaUnica,
            CompetenciasEncontradas = competenciasOrdenadas,
            TotalLinhasPlanilha = ultimaLinha,
            Lancamentos = lancamentos,
            LinhasVaziasIgnoradas = linhasVazias,
            Avisos = avisos.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static (IXLWorksheet planilha, int linhaCabecalho, Dictionary<string, int> colunas)
        LocalizarEstrutura(XLWorkbook workbook)
    {
        foreach (IXLWorksheet planilha in workbook.Worksheets)
        {
            int ultimaLinha = planilha.LastRowUsed()?.RowNumber() ?? 0;
            int ultimaColuna = planilha.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (ultimaLinha == 0 || ultimaColuna == 0)
                continue;

            int limiteLinha = Math.Min(20, ultimaLinha);

            for (int linha = 1; linha <= limiteLinha; linha++)
            {
                var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                for (int coluna = 1; coluna <= ultimaColuna; coluna++)
                {
                    string cabecalho = NormalizarCabecalho(planilha.Cell(linha, coluna).GetString());
                    if (!string.IsNullOrWhiteSpace(cabecalho) && !mapa.ContainsKey(cabecalho))
                        mapa[cabecalho] = coluna;
                }

                if (CabecalhosObrigatorios.All(mapa.ContainsKey))
                    return (planilha, linha, mapa);
            }
        }

        throw new InvalidOperationException(
            "A estrutura do relatório Over não foi localizada. O HUB procura as colunas " +
            "Periodo, Operadora, Entidade, Apolice, Matricula, Beneficiario, Evento, Descricao, Titulo, " +
            "Valor (PV), Valor (NET), Valor (Over), Inadimplente e Cartao.");
    }

    private static bool LinhaSemDados(IXLWorksheet planilha, int linha, IEnumerable<int> colunas)
    {
        foreach (int coluna in colunas)
        {
            if (!planilha.Cell(linha, coluna).IsEmpty())
                return false;
        }

        return true;
    }

    private static string LerTextoPreservando(IXLCell celula)
    {
        if (celula.IsEmpty())
            return string.Empty;

        if (celula.DataType == XLDataType.Text)
            return celula.GetString().Trim();

        if (celula.DataType == XLDataType.Number)
        {
            double numero = celula.GetDouble();
            string formato = celula.Style.NumberFormat.Format ?? string.Empty;

            // Caso o Excel use uma máscara composta apenas por zeros, preservamos
            // os zeros à esquerda (útil para códigos, evento, matrícula e cartão).
            string mascaraInteira = formato.Split(';')[0].Trim();
            if (mascaraInteira.Length > 0 && mascaraInteira.All(c => c == '0'))
            {
                long inteiro = Convert.ToInt64(Math.Round(numero, MidpointRounding.AwayFromZero));
                return inteiro.ToString(new string('0', mascaraInteira.Length), CultureInfo.InvariantCulture);
            }

            return numero.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        if (celula.DataType == XLDataType.DateTime)
            return celula.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));

        return celula.GetString().Trim();
    }

    private static decimal? LerDecimalOpcional(IXLCell celula)
    {
        if (celula.IsEmpty())
            return null;

        if (celula.DataType == XLDataType.Number)
            return (decimal)celula.GetDouble();

        string texto = celula.GetString().Trim();
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        if (decimal.TryParse(
                texto,
                NumberStyles.Any,
                CultureInfo.GetCultureInfo("pt-BR"),
                out decimal valorPtBr))
        {
            return valorPtBr;
        }

        if (decimal.TryParse(
                texto,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimal valorInvariant))
        {
            return valorInvariant;
        }

        return null;
    }

    private static DateTime? LerCompetencia(string texto, IXLCell celula)
    {
        if (celula.DataType == XLDataType.DateTime)
        {
            DateTime data = celula.GetDateTime();
            return PrimeiroDiaDoMes(data);
        }

        texto = (texto ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        string somenteDigitos = new(texto.Where(char.IsDigit).ToArray());

        if (somenteDigitos.Length == 6 &&
            int.TryParse(somenteDigitos[..4], out int anoYyyyMm) &&
            int.TryParse(somenteDigitos.Substring(4, 2), out int mesYyyyMm) &&
            AnoMesValido(anoYyyyMm, mesYyyyMm))
        {
            return new DateTime(anoYyyyMm, mesYyyyMm, 1);
        }

        if (somenteDigitos.Length == 6 &&
            int.TryParse(somenteDigitos[..2], out int mesMmYyyy) &&
            int.TryParse(somenteDigitos.Substring(2, 4), out int anoMmYyyy) &&
            AnoMesValido(anoMmYyyy, mesMmYyyy))
        {
            return new DateTime(anoMmYyyy, mesMmYyyy, 1);
        }

        string[] formatos =
        {
            "MM/yyyy", "M/yyyy", "yyyy/MM", "yyyy-MM", "MM-yyyy",
            "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"
        };

        if (DateTime.TryParseExact(
                texto,
                formatos,
                CultureInfo.GetCultureInfo("pt-BR"),
                DateTimeStyles.None,
                out DateTime dataConvertida))
        {
            return PrimeiroDiaDoMes(dataConvertida);
        }

        return null;
    }

    private static bool AnoMesValido(int ano, int mes)
        => ano >= 2000 && ano <= 2200 && mes >= 1 && mes <= 12;

    private static DateTime PrimeiroDiaDoMes(DateTime data)
        => new(data.Year, data.Month, 1);

    private static string NormalizarCabecalho(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        string decomposed = texto.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
