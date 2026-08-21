using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class LeituraFaturasDiagnosticoWindow : Window
{
    private readonly IReadOnlyList<FaturaBradescoArquivo> _arquivos;
    private List<LinhaDiagnostico> _linhasAtuais = new();

    public LeituraFaturasDiagnosticoWindow(
        string grupo,
        IReadOnlyList<FaturaBradescoArquivo> arquivos)
    {
        InitializeComponent();

        _arquivos = arquivos;
        GrupoText.Text = grupo;

        ArquivoComboBox.ItemsSource = _arquivos;
        ArquivoComboBox.DisplayMemberPath = nameof(FaturaBradescoArquivo.NomeArquivo);

        if (_arquivos.Count > 0)
            ArquivoComboBox.SelectedIndex = 0;
        else
            AtualizarArquivoSelecionado(null);
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ArquivoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AtualizarArquivoSelecionado(ArquivoComboBox.SelectedItem as FaturaBradescoArquivo);
    }

    private void PesquisaTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AplicarFiltro();
    }

    private void AtualizarArquivoSelecionado(FaturaBradescoArquivo? arquivo)
    {
        PesquisaTextBox.Text = string.Empty;

        if (arquivo == null)
        {
            _linhasAtuais = new List<LinhaDiagnostico>();
            LancamentosDataGrid.ItemsSource = _linhasAtuais;
            ResumoText.Text = "Nenhum arquivo disponível para diagnóstico.";
            DetalhesIgnoradosText.Text = string.Empty;
            AvisosText.Text = string.Empty;
            FiltroResultadoText.Text = "0 lançamentos";
            return;
        }

        _linhasAtuais = CriarLinhas(arquivo);

        string competencia = arquivo.Competencia?.ToString("MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) ?? "—";
        ResumoText.Text =
            $"✓ Competência {competencia}  •  Apólice {arquivo.Apolice}  •  " +
            $"{arquivo.Subfaturas.Count} subfatura(s)  •  {arquivo.TotalPaginasDetalhe} pág. de detalhe  •  " +
            $"{arquivo.TotalBeneficiarios} beneficiário(s)  •  {arquivo.TotalLancamentos} lançamento(s)";

        DetalhesIgnoradosText.Text =
            $"PDF: {arquivo.TotalPaginasPdf} página(s)  •  " +
            $"Subfatura 999 ignorada em {arquivo.PaginasSubfatura999Ignoradas.Count} página(s)  •  " +
            $"Linhas de contexto ignoradas: {arquivo.LinhasContextoIgnoradas.Count}  •  " +
            $"Linhas sem beneficiário: {arquivo.LinhasSemBeneficiario.Count}";

        if (arquivo.Avisos.Count == 0)
        {
            AvisosText.Text = "Sem avisos estruturais neste arquivo.";
        }
        else
        {
            AvisosText.Text = "Avisos: " + string.Join("  |  ", arquivo.Avisos.Take(3));
            if (arquivo.Avisos.Count > 3)
                AvisosText.Text += $"  |  ... e mais {arquivo.Avisos.Count - 3}.";
        }

        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        string pesquisa = (PesquisaTextBox.Text ?? string.Empty).Trim();

        IEnumerable<LinhaDiagnostico> consulta = _linhasAtuais;
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            string termo = NormalizarPesquisa(pesquisa);
            consulta = consulta.Where(x =>
                NormalizarPesquisa(x.Certificado).Contains(termo, StringComparison.Ordinal) ||
                NormalizarPesquisa(x.Beneficiario).Contains(termo, StringComparison.Ordinal));
        }

        List<LinhaDiagnostico> resultado = consulta.ToList();
        LancamentosDataGrid.ItemsSource = resultado;
        FiltroResultadoText.Text = resultado.Count == 1
            ? "1 lançamento"
            : $"{resultado.Count} lançamentos";
    }

    private static List<LinhaDiagnostico> CriarLinhas(FaturaBradescoArquivo arquivo)
    {
        var linhas = new List<LinhaDiagnostico>();
        CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");

        foreach (FaturaBradescoSubfatura subfatura in arquivo.Subfaturas)
        {
            foreach (FaturaBradescoBeneficiario beneficiario in subfatura.Beneficiarios)
            {
                foreach (FaturaBradescoLancamento lancamento in beneficiario.Lancamentos)
                {
                    linhas.Add(new LinhaDiagnostico
                    {
                        PaginaPdf = lancamento.PaginaPdf.ToString(CultureInfo.InvariantCulture),
                        PaginaFatura = lancamento.PaginaFatura?.ToString(CultureInfo.InvariantCulture) ?? "—",
                        Subfatura = subfatura.Numero.ToString(CultureInfo.InvariantCulture),
                        Entidade = subfatura.Entidade,
                        Certificado = beneficiario.Certificado,
                        Beneficiario = beneficiario.Nome,
                        Nascimento = beneficiario.DataNascimento?.ToString("dd/MM/yyyy", ptBr) ?? "—",
                        Plano = string.IsNullOrWhiteSpace(lancamento.Plano) ? (string.IsNullOrWhiteSpace(beneficiario.Plano) ? "—" : beneficiario.Plano) : lancamento.Plano,
                        Inicio = (lancamento.DataInicio ?? beneficiario.DataInicio)?.ToString("dd/MM/yyyy", ptBr) ?? "—",
                        Movimento = string.IsNullOrWhiteSpace(lancamento.Movimento) ? "—" : lancamento.Movimento,
                        Competencia = lancamento.Competencia.ToString("MM/yyyy", ptBr),
                        Valor = lancamento.Valor.ToString("N2", ptBr),
                        Participacao = lancamento.Participacao?.ToString("N2", ptBr) ?? "—"
                    });
                }
            }
        }

        return linhas
            .OrderBy(x => int.TryParse(x.PaginaPdf, out int p) ? p : int.MaxValue)
            .ThenBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Beneficiario, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string NormalizarPesquisa(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        string normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);

        foreach (char c in normalizado)
        {
            UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria != UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(c))
                sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed class LinhaDiagnostico
    {
        public string PaginaPdf { get; init; } = string.Empty;
        public string PaginaFatura { get; init; } = string.Empty;
        public string Subfatura { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public string Certificado { get; init; } = string.Empty;
        public string Beneficiario { get; init; } = string.Empty;
        public string Nascimento { get; init; } = string.Empty;
        public string Plano { get; init; } = string.Empty;
        public string Inicio { get; init; } = string.Empty;
        public string Movimento { get; init; } = string.Empty;
        public string Competencia { get; init; } = string.Empty;
        public string Valor { get; init; } = string.Empty;
        public string Participacao { get; init; } = string.Empty;
    }
}
