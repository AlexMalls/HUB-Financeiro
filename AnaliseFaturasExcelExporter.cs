using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HubFinanceiro;

public sealed class AnaliseFaturasExcelExportacaoResultado
{
    public string PastaDestino { get; init; } = string.Empty;
    public int QuantidadeRelatorios { get; init; }
    public int QuantidadePendencias { get; init; }
    public IReadOnlyList<string> ArquivosGerados { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Exporta apenas os resultados finais com status de Divergência.
/// Cada tipo de divergência gera um arquivo .xlsx independente na pasta financeira de ANALISE DE FATURA,
/// organizado em uma subpasta própria por competência.
/// </summary>
public sealed class AnaliseFaturasExcelExporter
{
    private static readonly XLColor CorFundoEscuro = XLColor.FromHtml("#202024");
    private static readonly XLColor CorFundoMedio = XLColor.FromHtml("#29292E");
    private static readonly XLColor CorRoxo = XLColor.FromHtml("#7920DC");
    private static readonly XLColor CorTextoEscuro = XLColor.FromHtml("#252526");
    private static readonly XLColor CorTextoSuave = XLColor.FromHtml("#6F6F78");
    private static readonly XLColor CorBorda = XLColor.FromHtml("#D9D9E0");
    private static readonly XLColor CorPendente = XLColor.FromHtml("#C78A12");
    private static readonly XLColor CorLinhaAlternada = XLColor.FromHtml("#F7F5FA");
    private static readonly XLColor CorLinhaJustificada = XLColor.FromHtml("#EAF5EE");

    public AnaliseFaturasExcelExportacaoResultado ExportarPendenciasPorTipo(AnaliseFinalDiagnostico diagnostico)
    {
        if (diagnostico == null)
            throw new ArgumentNullException(nameof(diagnostico));

        List<AnaliseFinalResultado> pendentes = diagnostico.Resultados
            .Where(x => x.Status == AnaliseFinalStatus.DivergenciaPendente || x.Status == AnaliseFinalStatus.Ambiguo)
            .ToList();

        if (pendentes.Count == 0)
            throw new InvalidOperationException("Não existem Divergências para exportar.");

        string perfilUsuario = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(perfilUsuario))
            throw new DirectoryNotFoundException("Não foi possível localizar a pasta do usuário.");

        string pastaBase = Path.Combine(
            perfilUsuario,
            "OneDrive - Positiva Administradora de Benefícios Ltda",
            "Documentos",
            "Financeiro",
            "ANALISE DE FATURA");

        if (!Directory.Exists(pastaBase))
            throw new DirectoryNotFoundException($"A pasta de destino dos relatórios não foi encontrada: {pastaBase}");

        // Mantém uma subpasta por competência para não misturar relatórios e, principalmente,
        // para que a limpeza dos .xlsx gerados anteriormente nunca alcance outros arquivos
        // existentes na pasta compartilhada ANALISE DE FATURA.
        string pastaDestino = Path.Combine(
            pastaBase,
            $"HUB Financeiro - Analise de Faturas - {diagnostico.Competencia:MM-yyyy}");

        Directory.CreateDirectory(pastaDestino);

        // A pasta representa uma exportação atual da competência. Remove somente .xlsx
        // gerados anteriormente por este recurso para não manter relatórios obsoletos.
        foreach (string antigo in Directory.EnumerateFiles(pastaDestino, "*.xlsx", SearchOption.TopDirectoryOnly))
        {
            try { File.Delete(antigo); }
            catch { /* Se estiver aberto, o SaveAs abaixo dará uma mensagem mais específica. */ }
        }

        var grupos = pendentes
            .GroupBy(x => string.IsNullOrWhiteSpace(x.TipoDivergencia) ? "Divergência não classificada" : x.TipoDivergencia.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var arquivos = new List<string>();
        int indice = 1;

        foreach (IGrouping<string, AnaliseFinalResultado> grupo in grupos)
        {
            List<AnaliseFinalResultado> itens = grupo
                .OrderBy(x => x.Entidade, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Beneficiario, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Certificado, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            string nomeTipo = LimparNomeArquivo(grupo.Key);
            string caminho = Path.Combine(pastaDestino, $"{indice:00} - {nomeTipo}.xlsx");

            CriarWorkbook(caminho, diagnostico, grupo.Key, itens);
            arquivos.Add(caminho);
            indice++;
        }

        return new AnaliseFaturasExcelExportacaoResultado
        {
            PastaDestino = pastaDestino,
            QuantidadeRelatorios = arquivos.Count,
            QuantidadePendencias = pendentes.Count,
            ArquivosGerados = arquivos
        };
    }

    private static void CriarWorkbook(
        string caminho,
        AnaliseFinalDiagnostico diagnostico,
        string tipoDivergencia,
        IReadOnlyList<AnaliseFinalResultado> itens)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet ws = workbook.Worksheets.Add("Divergências");

        const int totalColunas = 9;
        const int linhaCabecalho = 3;
        int primeiraLinhaDados = linhaCabecalho + 1;
        int ultimaLinhaDados = primeiraLinhaDados + itens.Count - 1;

        // Linha 1: título principal.
        ws.Range(1, 1, 1, totalColunas).Merge();
        ws.Cell(1, 1).Value = "HUB Financeiro  |  Análise de Faturas";
        ws.Row(1).Height = 30;
        IXLStyle tituloStyle = ws.Range(1, 1, 1, totalColunas).Style;
        tituloStyle.Fill.BackgroundColor = CorFundoEscuro;
        tituloStyle.Font.FontColor = XLColor.White;
        tituloStyle.Font.Bold = true;
        tituloStyle.Font.FontSize = 16;
        tituloStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        tituloStyle.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // Linha 2: tipo da pendência. Não há mais bloco-resumo entre o título e a tabela.
        ws.Range(2, 1, 2, totalColunas).Merge();
        ws.Cell(2, 1).Value = $"Divergências — {tipoDivergencia}";
        ws.Row(2).Height = 24;
        IXLStyle subtituloStyle = ws.Range(2, 1, 2, totalColunas).Style;
        subtituloStyle.Fill.BackgroundColor = CorRoxo;
        subtituloStyle.Font.FontColor = XLColor.White;
        subtituloStyle.Font.Bold = true;
        subtituloStyle.Font.FontSize = 12;
        subtituloStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        subtituloStyle.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        string[] cabecalhos =
        {
            "Beneficiário",
            "Certificado",
            "Entidade",
            "Competência",
            "Diferença",
            "Fatura líquida",
            "Valor no Over",
            "Explicação",
            "Justificativa"
        };

        for (int c = 1; c <= cabecalhos.Length; c++)
            ws.Cell(linhaCabecalho, c).Value = cabecalhos[c - 1];

        IXLRange faixaCabecalho = ws.Range(linhaCabecalho, 1, linhaCabecalho, totalColunas);
        faixaCabecalho.Style.Fill.BackgroundColor = CorFundoMedio;
        faixaCabecalho.Style.Font.FontColor = XLColor.White;
        faixaCabecalho.Style.Font.Bold = true;
        faixaCabecalho.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        faixaCabecalho.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        faixaCabecalho.Style.Alignment.WrapText = true;
        faixaCabecalho.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        faixaCabecalho.Style.Border.BottomBorderColor = CorRoxo;
        ws.Row(linhaCabecalho).Height = 24;

        int linha = primeiraLinhaDados;
        foreach (AnaliseFinalResultado item in itens)
        {
            AnaliseFaturasVisaoFinanceira visaoFinanceira = AnaliseFaturasVisaoFinanceiraService.Calcular(item);

            ws.Cell(linha, 1).Value = item.Beneficiario;
            ws.Cell(linha, 2).Value = item.Certificado;
            ws.Cell(linha, 3).Value = item.Entidade;
            ws.Cell(linha, 4).Value = item.Competencia;
            if (visaoFinanceira.DiferencaResidual.HasValue) ws.Cell(linha, 5).Value = visaoFinanceira.DiferencaResidual.Value;
            if (visaoFinanceira.ValorFaturaLiquida.HasValue) ws.Cell(linha, 6).Value = visaoFinanceira.ValorFaturaLiquida.Value;
            if (item.ValorOver.HasValue) ws.Cell(linha, 7).Value = item.ValorOver.Value;
            ws.Cell(linha, 8).Value = item.JustificativaManual;
            ws.Cell(linha, 9).Value = visaoFinanceira.ReconstruidaDeHistoricoLegado
                ? visaoFinanceira.CriarResumo(item.ValorFatura, item.ValorOver)
                : item.JustificativaFinal;

            if (!string.IsNullOrWhiteSpace(item.JustificativaManual))
                ws.Range(linha, 1, linha, totalColunas).Style.Fill.BackgroundColor = CorLinhaJustificada;
            else if ((linha - primeiraLinhaDados) % 2 == 1)
                ws.Range(linha, 1, linha, totalColunas).Style.Fill.BackgroundColor = CorLinhaAlternada;

            linha++;
        }

        if (itens.Count > 0)
        {
            IXLRange dados = ws.Range(primeiraLinhaDados, 1, ultimaLinhaDados, totalColunas);
            dados.Style.Font.FontColor = CorTextoEscuro;
            dados.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dados.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            dados.Style.Alignment.WrapText = true;
            dados.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            dados.Style.Border.BottomBorderColor = CorBorda;

            ws.Range(primeiraLinhaDados, 4, ultimaLinhaDados, 4).Style.DateFormat.Format = "MM/yyyy";
            ws.Range(primeiraLinhaDados, 5, ultimaLinhaDados, 7).Style.NumberFormat.Format = "R$ #,##0.00;[Red]-R$ #,##0.00";

            ws.Range(linhaCabecalho, 1, ultimaLinhaDados, totalColunas).SetAutoFilter();
        }

        ws.SheetView.FreezeRows(linhaCabecalho);
        ws.ShowGridLines = false;

        // Larguras calibradas a partir do layout aprovado no Excel.
        // Os valores abaixo são unidades de largura do Excel; os comentários registram
        // os pixels exibidos pelo próprio Excel para facilitar manutenção futura.
        ws.Column(1).Width = 49.29; // A - Beneficiário: 350 px
        ws.Column(2).Width = 16;    // B - Certificado
        ws.Column(3).Width = 27.86; // C - Entidade: 200 px
        ws.Column(4).Width = 19.29; // D - Competência: 140 px
        ws.Column(5).Width = 15;    // E - Diferença
        ws.Column(6).Width = 15;    // F - Fatura líquida
        ws.Column(7).Width = 15;    // G - Valor no Over
        ws.Column(8).Width = 34;    // H - Explicação manual
        ws.Column(9).Width = 77.86; // I - Justificativa: 550 px

        int ultimaLinhaUsada = Math.Max(ultimaLinhaDados, linhaCabecalho);
        ws.Range(1, 1, ultimaLinhaUsada, totalColunas).Style.Font.FontName = "Segoe UI";

        // Reproduz o efeito do Excel "Ajustar automaticamente a altura da linha".
        // Como WrapText já está ativo, justificativas/origens crescem em altura sem cortar conteúdo.
        if (itens.Count > 0)
        {
            ws.Rows(primeiraLinhaDados, ultimaLinhaDados).AdjustToContents();

            // Evita linhas muito baixas em registros curtos e mantém a aparência moderna da grade.
            foreach (IXLRow row in ws.Rows(primeiraLinhaDados, ultimaLinhaDados))
            {
                if (row.Height < 24)
                    row.Height = 24;
            }
        }

        workbook.SaveAs(caminho);
    }

    private static string LimparNomeArquivo(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "Divergencia";

        var sb = new StringBuilder(texto.Trim());
        foreach (char invalido in Path.GetInvalidFileNameChars())
            sb.Replace(invalido, '_');

        string resultado = sb.ToString();
        while (resultado.Contains("  ", StringComparison.Ordinal))
            resultado = resultado.Replace("  ", " ", StringComparison.Ordinal);

        resultado = resultado.Trim().TrimEnd('.');
        if (resultado.Length > 85)
            resultado = resultado[..85].TrimEnd();

        return string.IsNullOrWhiteSpace(resultado) ? "Divergencia" : resultado;
    }
}
