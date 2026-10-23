using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HubFinanceiro;

/// <summary>
/// Exportacao de divergencias compacta para o setor responsavel.
/// A aba Divergencias e o indice de navegacao; Fichas preserva apenas
/// os dados autorizados pelo usuario, sem depender dos PDFs originais.
/// Nao altera calculos, classificacoes ou regras de identidade.
/// </summary>
internal static class AnaliseFaturasExcelFichasV31
{
    private static readonly XLColor Escuro = XLColor.FromHtml("#202024");
    private static readonly XLColor Roxo = XLColor.FromHtml("#7920DC");
    private static readonly XLColor Texto = XLColor.FromHtml("#252526");
    private static readonly XLColor Borda = XLColor.FromHtml("#D9D9E0");
    private static readonly XLColor Claro = XLColor.FromHtml("#E9EEF5");
    private static readonly XLColor Alternado = XLColor.FromHtml("#F7F5FA");
    private static readonly XLColor Justificado = XLColor.FromHtml("#EAF5EE");
    private static readonly XLColor Vermelho = XLColor.FromHtml("#C62839");
    private static readonly XLColor Link = XLColor.FromHtml("#354A91");
    private const string Moeda = "R$ #,##0.00;[Red]-R$ #,##0.00";

    internal static void CriarWorkbook(string caminho, AnaliseFinalDiagnostico diagnostico,
        string tipoDivergencia, IReadOnlyList<AnaliseFinalResultado> itens)
    {
        using var livro = new XLWorkbook();
        IXLWorksheet resumo = livro.Worksheets.Add("Divergências");
        IXLWorksheet fichas = livro.Worksheets.Add("Fichas");
        ConfigurarResumo(resumo, tipoDivergencia);
        ConfigurarFichas(fichas);

        int fichaLinha = 1;
        for (int i = 0; i < itens.Count; i++)
        {
            AnaliseFinalResultado item = itens[i];
            AnaliseFaturasVisaoFinanceira visao = AnaliseFaturasVisaoFinanceiraService.Calcular(item);
            int linhaResumo = i + 4;
            int inicioFicha = fichaLinha;
            fichaLinha = EscreverFicha(fichas, inicioFicha, i + 1, linhaResumo, item, visao);
            EscreverResumo(resumo, linhaResumo, item, visao, inicioFicha, i);
        }

        if (itens.Count > 0)
        {
            int ultimaLinha = itens.Count + 3;
            resumo.Range(3, 1, ultimaLinha, 7).SetAutoFilter();
            // Alterar a selecao no resumo nao interfere nas fichas vinculadas.
            resumo.SheetView.FreezeRows(3);
        }

        livro.SaveAs(caminho);
    }

    private static void ConfigurarResumo(IXLWorksheet ws, string tipo)
    {
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Segoe UI";
        ws.Style.Font.FontSize = 10;
        ws.Column(1).Width = 49.29; // Beneficiario
        ws.Column(2).Width = 16;    // Certificado
        ws.Column(3).Width = 16;    // Diferenca
        ws.Column(4).Width = 19;    // Fatura liquida
        ws.Column(5).Width = 22;    // Protheus / Over
        ws.Column(6).Width = 64;    // Explicacao (mantida da exportacao atual)
        ws.Column(7).Width = 12;    // Icone de documento

        Faixa(ws, 1, 1, 7, "HUB Financeiro  |  Análise de Faturas", Escuro, 16, 30);
        Faixa(ws, 2, 1, 7, $"Divergências — {tipo}", Roxo, 12, 24);
        string[] cabecalho = { "Beneficiário", "Certificado", "Diferença",
            "Fatura líquida", "Valor no Protheus", "Explicação", "Detalhes" };
        for (int c = 1; c <= 7; c++) ws.Cell(3, c).Value = cabecalho[c - 1];
        var header = ws.Range(3, 1, 3, 7);
        header.Style.Fill.BackgroundColor = Escuro;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        header.Style.Border.BottomBorderColor = Roxo;
        ws.Row(3).Height = 26;
    }

    private static void EscreverResumo(IXLWorksheet ws, int linha,
        AnaliseFinalResultado item, AnaliseFaturasVisaoFinanceira visao,
        int primeiraLinhaFicha, int indice)
    {
        ws.Cell(linha, 1).Value = item.Beneficiario;
        ws.Cell(linha, 2).Value = item.Certificado;
        if (visao.DiferencaResidual.HasValue)
            ws.Cell(linha, 3).Value = visao.DiferencaResidual.Value;
        if (visao.ValorFaturaLiquida.HasValue)
            ws.Cell(linha, 4).Value = visao.ValorFaturaLiquida.Value;
        if (item.ValorOver.HasValue)
            ws.Cell(linha, 5).Value = item.ValorOver.Value;
        ws.Cell(linha, 6).Value = AnaliseFaturasEmailService.ExibirOverComoProtheus(item.JustificativaManual);

        // O simbolo e um caractere de documento clicavel, sem macros/VBA,
        // sem figuras externas e sem formulas que precisem de recalculo.
        IXLCell icone = ws.Cell(linha, 7);
        icone.Value = "\uE8A5";
        icone.SetHyperlink(new XLHyperlink($"'Fichas'!A{primeiraLinhaFicha}")
        {
            Tooltip = $"Abrir ficha de {item.Beneficiario}"
        });

        IXLRange range = ws.Range(linha, 1, linha, 7);
        range.Style.Fill.BackgroundColor = !string.IsNullOrWhiteSpace(item.JustificativaManual)
            ? Justificado : indice % 2 == 0 ? XLColor.White : Alternado;
        range.Style.Font.FontColor = Texto;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = Borda;
        ws.Range(linha, 3, linha, 5).Style.NumberFormat.Format = Moeda;
        ws.Cell(linha, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(linha, 6).Style.Alignment.WrapText = true;
        icone.Style.Font.FontName = "Segoe MDL2 Assets";
        icone.Style.Font.FontSize = 18;
        icone.Style.Font.Underline = XLFontUnderlineValues.None;
        icone.Style.Font.FontColor = Texto;
        ws.Row(linha).Height = 28;
    }

    private static void ConfigurarFichas(IXLWorksheet ws)
    {
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Segoe UI";
        ws.Style.Font.FontSize = 10;
        ws.Style.Font.FontColor = Texto;
        ws.Column(1).Width = 25;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 15;
        ws.Column(4).Width = 17;
        ws.Column(5).Width = 18;
        ws.Column(6).Width = 20;
        ws.Column(7).Width = 17;
    }

    private static int EscreverFicha(IXLWorksheet ws, int inicio, int numero,
        int linhaResumo, AnaliseFinalResultado item, AnaliseFaturasVisaoFinanceira visao)
    {
        int row = inicio;
        Faixa(ws, row++, 1, 7, $"{numero:00}   {item.Beneficiario}", Escuro, 15, 30);
        Faixa(ws, row++, 1, 7,
            $"Certificado: {NaoInformado(item.Certificado)}    |    Entidade: {NaoInformado(item.Entidade)}",
            Roxo, 10, 22, centralizar: false);
        Faixa(ws, row++, 1, 7,
            $"Plano na fatura Bradesco: {NaoInformado(item.DadosFatura?.Plano)}    |    Plano no Over: Não informado",
            Roxo, 10, 22, centralizar: false);

        // Um unico indicador de diferenca: a residual efetiva, sem card separado.
        int topo = row;
        PainelFinanceiro(ws, topo, 1, 2, "FATURA LÍQUIDA", visao.ValorFaturaLiquida);
        PainelFinanceiro(ws, topo, 3, 4, "OVER NET", item.ValorOver);
        PainelFinanceiro(ws, topo, 5, 7, "DIFERENÇA", visao.DiferencaResidual, destaque: true);
        ws.Row(topo).Height = 27;
        ws.Row(topo + 1).Height = 27;
        row += 2;
        ws.Row(row++).Height = 9;

        Faixa(ws, row++, 1, 7, "LINHA DO TEMPO  ·  FATURAS E CONTEXTO", Escuro, 12, 25);
        int linhaTitulos = row++;
        CabecalhoLinhaDoTempo(ws, linhaTitulos);
        int mesBase = row++;
        EscreverPeriodo(ws, mesBase, item.Competencia,
            visao.ValorFaturaLiquida, "Fatura analisada", corAlternada: true);

        // Somente faturas posteriores; nao inclui competencias anteriores nem arquivos/paginas.
        var futuros = (item.ContextoTemporal?.Evidencias ?? Array.Empty<ContextoTemporalEvidencia>())
            .Where(x => x.CompetenciaFatura.Year > item.Competencia.Year ||
                        (x.CompetenciaFatura.Year == item.Competencia.Year &&
                         x.CompetenciaFatura.Month > item.Competencia.Month))
            .GroupBy(x => new DateTime(x.CompetenciaFatura.Year, x.CompetenciaFatura.Month, 1))
            .OrderBy(g => g.Key)
            .ToList();
        foreach (var grupo in futuros)
            EscreverPeriodo(ws, row++, grupo.Key,
                grupo.Sum(x => x.Valor), "Contexto posterior", corAlternada: (row % 2 == 0));

        if (futuros.Count == 0)
        {
            TextoFaixa(ws, row, 1, 7, "Nenhuma fatura posterior localizada para esta investigação.",
                23, Alternado);
            row++;
        }
        TextoFaixa(ws, row++, 1, 7,
            "Contexto temporal: " + NaoInformado(item.ContextoTemporal?.Observacao),
            AlturaTexto(item.ContextoTemporal?.Observacao, 35, 85), Claro);
        ws.Row(row++).Height = 9;

        Faixa(ws, row++, 1, 7, "INVESTIGAÇÃO E CONCLUSÃO", Escuro, 12, 25);
        string conclusao = ConstruirConclusao(item);
        TextoFaixa(ws, row++, 1, 7, "Conclusão automática: " + conclusao,
            AlturaTexto(conclusao, 31, 90), Alternado);
        string justificativa = NaoInformado(
            AnaliseFaturasEmailService.ExibirOverComoProtheus(item.JustificativaFinal));
        TextoFaixa(ws, row++, 1, 7, "Justificativa: " + justificativa,
            AlturaTexto(justificativa, 31, 120), Claro);

        IXLRange voltar = ws.Range(row, 1, row, 7);
        voltar.Merge();
        IXLCell celulaVoltar = ws.Cell(row, 1);
        celulaVoltar.Value = "◀  VOLTAR AO RESUMO";
        celulaVoltar.SetHyperlink(new XLHyperlink($"'Divergências'!A{linhaResumo}")
        {
            Tooltip = "Voltar à linha deste beneficiário na aba Divergências"
        });
        voltar.Style.Fill.BackgroundColor = Alternado;
        voltar.Style.Font.FontColor = Link;
        voltar.Style.Font.Bold = true;
        voltar.Style.Font.Underline = XLFontUnderlineValues.None;
        voltar.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        voltar.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(row++).Height = 24;
        ws.Row(row++).Height = 15;
        return row;
    }

    private static void CabecalhoLinhaDoTempo(IXLWorksheet ws, int row)
    {
        ws.Cell(row, 1).Value = "Competência";
        ws.Range(row, 2, row, 3).Merge();
        ws.Cell(row, 2).Value = "Valor da fatura";
        ws.Range(row, 4, row, 7).Merge();
        ws.Cell(row, 4).Value = "Referência";
        IXLRange area = ws.Range(row, 1, row, 7);
        area.Style.Fill.BackgroundColor = Claro;
        area.Style.Font.Bold = true;
        area.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(row).Height = 23;
    }

    private static void EscreverPeriodo(IXLWorksheet ws, int row, DateTime competencia,
        decimal? valor, string referencia, bool corAlternada)
    {
        ws.Cell(row, 1).Value = competencia.ToString("MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        ws.Range(row, 2, row, 3).Merge();
        if (valor.HasValue) ws.Cell(row, 2).Value = valor.Value;
        else ws.Cell(row, 2).Value = "—";
        ws.Cell(row, 2).Style.NumberFormat.Format = Moeda;
        ws.Range(row, 4, row, 7).Merge();
        ws.Cell(row, 4).Value = referencia;
        ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = corAlternada ? Alternado : XLColor.White;
        ws.Range(row, 1, row, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(row).Height = 23;
    }

    private static void PainelFinanceiro(IXLWorksheet ws, int row, int colunaInicio,
        int colunaFim, string legenda, decimal? valor, bool destaque = false)
    {
        IXLRange titulo = ws.Range(row, colunaInicio, row, colunaFim);
        titulo.Merge();
        ws.Cell(row, colunaInicio).Value = legenda;
        titulo.Style.Fill.BackgroundColor = Escuro;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontSize = 11;
        titulo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        IXLRange numero = ws.Range(row + 1, colunaInicio, row + 1, colunaFim);
        numero.Merge();
        if (valor.HasValue) ws.Cell(row + 1, colunaInicio).Value = valor.Value;
        else ws.Cell(row + 1, colunaInicio).Value = "—";
        ws.Cell(row + 1, colunaInicio).Style.NumberFormat.Format = Moeda;
        numero.Style.Fill.BackgroundColor = Claro;
        numero.Style.Font.FontSize = 12;
        numero.Style.Font.FontColor = destaque && valor < 0 ? Vermelho : Texto;
        numero.Style.Font.Bold = destaque;
        numero.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        numero.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void Faixa(IXLWorksheet ws, int linha, int inicio, int fim,
        string texto, XLColor fundo, int fonte, int altura, bool centralizar = true)
    {
        IXLRange r = ws.Range(linha, inicio, linha, fim);
        r.Merge();
        ws.Cell(linha, inicio).Value = texto;
        r.Style.Fill.BackgroundColor = fundo;
        r.Style.Font.FontColor = XLColor.White;
        r.Style.Font.FontSize = fonte;
        r.Style.Font.Bold = true;
        r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        r.Style.Alignment.Horizontal = centralizar
            ? XLAlignmentHorizontalValues.Center : XLAlignmentHorizontalValues.Left;
        ws.Row(linha).Height = altura;
    }

    private static void TextoFaixa(IXLWorksheet ws, int linha, int inicio,
        int fim, string texto, int altura, XLColor fundo)
    {
        IXLRange range = ws.Range(linha, inicio, linha, fim);
        range.Merge();
        ws.Cell(linha, inicio).Value = texto;
        range.Style.Fill.BackgroundColor = fundo;
        range.Style.Font.FontColor = Texto;
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Row(linha).Height = altura;
    }

    private static string ConstruirConclusao(AnaliseFinalResultado item)
    {
        if (item.Status == AnaliseFinalStatus.Ambiguo)
            return "Identificação inconclusiva; vínculo com o beneficiário requer conferência.";
        if (!string.IsNullOrWhiteSpace(item.RegraExplicativa))
            return AnaliseFaturasEmailService.ExibirOverComoProtheus(item.RegraExplicativa);
        if (item.ContextoTemporal?.Explicada == true)
            return "O contexto temporal explica a divergência identificada.";
        return "As verificações automáticas não justificaram integralmente a diferença financeira.";
    }

    private static string NaoInformado(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? "Não informado" : valor.Trim();

    private static int AlturaTexto(string? texto, int minimo, int maximo)
    {
        // A altura depende do comprimento, mas fica limitada para manter a ficha enxuta.
        int linhasEstimadas = (int)Math.Ceiling((texto?.Length ?? 0) / 135.0);
        return Math.Min(maximo, Math.Max(minimo, linhasEstimadas * 17 + 14));
    }
}