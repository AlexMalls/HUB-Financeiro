using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class LeituraOverDiagnosticoWindow : Window
{
    private readonly OverArquivo _arquivo;
    private readonly List<LinhaOverDiagnostico> _linhas;

    public LeituraOverDiagnosticoWindow(OverArquivo arquivo)
    {
        InitializeComponent();

        _arquivo = arquivo;
        _linhas = CriarLinhas(arquivo);

        ArquivoText.Text = arquivo.NomeArquivo;
        ArquivoText.ToolTip = arquivo.CaminhoArquivo;

        AtualizarResumo();
        AplicarFiltro();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void PesquisaTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => AplicarFiltro();

    private void AtualizarResumo()
    {
        CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");

        string competencia = _arquivo.Competencia?.ToString("MM/yyyy", ptBr)
            ?? (_arquivo.CompetenciasEncontradas.Count > 0
                ? string.Join(", ", _arquivo.CompetenciasEncontradas.Select(x => x.ToString("MM/yyyy", ptBr)))
                : "—");

        int eventos = _arquivo.Lancamentos
            .Select(x => x.Evento)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int negativos = _arquivo.Lancamentos.Count(x =>
            x.ValorPV < 0m || x.ValorNET < 0m || x.ValorOver < 0m);

        int beneficiarios = _arquivo.Lancamentos
            .Select(x => x.Beneficiario)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Count();

        int cartoesVazios = _arquivo.Lancamentos.Count(x => string.IsNullOrWhiteSpace(x.Cartao));

        int cartoesNormalizados = _arquivo.Lancamentos.Count(x =>
            !string.IsNullOrWhiteSpace(AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(x.Cartao)));

        int cartoesPreenchidos = _arquivo.Lancamentos.Count(x => !string.IsNullOrWhiteSpace(x.Cartao));
        int cartoesNaoNormalizados = Math.Max(0, cartoesPreenchidos - cartoesNormalizados);

        IReadOnlyList<NormalizacaoTesteResultado> testesNormalizacao = AnaliseFaturasNormalizadorTestes.Executar();
        int testesOk = testesNormalizacao.Count(x => x.Sucesso);

        ResumoText.Text =
            $"✓ Competência {competencia}  •  Planilha {_arquivo.Planilha}  •  " +
            $"cabeçalho linha {_arquivo.LinhaCabecalho}  •  {_arquivo.TotalLancamentos:N0} lançamento(s)";

        DetalhesText.Text =
            $"{beneficiarios:N0} beneficiário(s)  •  {eventos:N0} evento(s) diferente(s)  •  " +
            $"{negativos:N0} linha(s) com valor negativo  •  {cartoesNormalizados:N0} cartão(ões) normalizado(s)  •  " +
            $"{cartoesNaoNormalizados:N0} cartão(ões) preenchido(s) não normalizado(s)  •  {cartoesVazios:N0} vazio(s) preservado(s)  •  " +
            $"normalizador {testesOk}/{testesNormalizacao.Count} teste(s) interno(s)  •  " +
            $"{_arquivo.LinhasVaziasIgnoradas.Count:N0} linha(s) totalmente vazia(s) ignorada(s)  •  última linha usada: {_arquivo.TotalLinhasPlanilha:N0}";

        List<string> avisosCombinados = _arquivo.Avisos.ToList();
        avisosCombinados.AddRange(
            testesNormalizacao
                .Where(x => !x.Sucesso)
                .Select(x => $"Normalização '{x.Nome}': esperado {x.Esperado}, obtido {x.Obtido}"));

        if (avisosCombinados.Count == 0)
        {
            AvisosText.Text = "Sem avisos estruturais neste arquivo.";
        }
        else
        {
            AvisosText.Text = "Avisos: " + string.Join("  |  ", avisosCombinados.Take(3));
            if (avisosCombinados.Count > 3)
                AvisosText.Text += $"  |  ... e mais {avisosCombinados.Count - 3}.";
        }
    }

    private void AplicarFiltro()
    {
        string pesquisa = (PesquisaTextBox.Text ?? string.Empty).Trim();
        IEnumerable<LinhaOverDiagnostico> consulta = _linhas;

        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            string termo = NormalizarPesquisa(pesquisa);
            consulta = consulta.Where(x =>
                NormalizarPesquisa(x.Beneficiario).Contains(termo, StringComparison.Ordinal) ||
                NormalizarPesquisa(x.CertificadoNormalizado).Contains(termo, StringComparison.Ordinal) ||
                NormalizarPesquisa(x.Cartao).Contains(termo, StringComparison.Ordinal) ||
                NormalizarPesquisa(x.Matricula).Contains(termo, StringComparison.Ordinal) ||
                NormalizarPesquisa(x.Evento).Contains(termo, StringComparison.Ordinal) ||
                NormalizarPesquisa(x.Entidade).Contains(termo, StringComparison.Ordinal));
        }

        List<LinhaOverDiagnostico> resultado = consulta.ToList();
        LancamentosDataGrid.ItemsSource = resultado;
        FiltroResultadoText.Text = resultado.Count == 1
            ? "1 lançamento"
            : $"{resultado.Count:N0} lançamentos";
    }

    private static List<LinhaOverDiagnostico> CriarLinhas(OverArquivo arquivo)
    {
        CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");

        return arquivo.Lancamentos
            .Select(x => new LinhaOverDiagnostico
            {
                Linha = x.NumeroLinha.ToString(CultureInfo.InvariantCulture),
                Periodo = x.Competencia?.ToString("MM/yyyy", ptBr)
                    ?? (string.IsNullOrWhiteSpace(x.PeriodoOriginal) ? "—" : x.PeriodoOriginal),
                Operadora = VazioComoTraco(x.Operadora),
                Entidade = VazioComoTraco(x.Entidade),
                Apolice = VazioComoTraco(x.Apolice),
                Matricula = VazioComoTraco(x.Matricula),
                Beneficiario = VazioComoTraco(x.Beneficiario),
                Evento = VazioComoTraco(x.Evento),
                Descricao = VazioComoTraco(x.Descricao),
                Titulo = VazioComoTraco(x.Titulo),
                ValorPV = FormatarValor(x.ValorPV, ptBr),
                ValorNET = FormatarValor(x.ValorNET, ptBr),
                ValorOver = FormatarValor(x.ValorOver, ptBr),
                Inadimplente = VazioComoTraco(x.Inadimplente),
                Cartao = VazioComoTraco(x.Cartao),
                CertificadoNormalizado = VazioComoTraco(AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(x.Cartao) ?? string.Empty)
            })
            .OrderBy(x => int.TryParse(x.Linha, out int linha) ? linha : int.MaxValue)
            .ToList();
    }

    private static string FormatarValor(decimal? valor, CultureInfo cultura)
        => valor.HasValue ? valor.Value.ToString("N2", cultura) : "—";

    private static string VazioComoTraco(string texto)
        => string.IsNullOrWhiteSpace(texto) ? "—" : texto;

    private static string NormalizarPesquisa(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        string nomeNormalizado = AnaliseFaturasNormalizador.NormalizarNome(texto);
        return new string(nomeNormalizado.Where(char.IsLetterOrDigit).ToArray());
    }

    private sealed class LinhaOverDiagnostico
    {
        public string Linha { get; init; } = string.Empty;
        public string Periodo { get; init; } = string.Empty;
        public string Operadora { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public string Apolice { get; init; } = string.Empty;
        public string Matricula { get; init; } = string.Empty;
        public string Beneficiario { get; init; } = string.Empty;
        public string Evento { get; init; } = string.Empty;
        public string Descricao { get; init; } = string.Empty;
        public string Titulo { get; init; } = string.Empty;
        public string ValorPV { get; init; } = string.Empty;
        public string ValorNET { get; init; } = string.Empty;
        public string ValorOver { get; init; } = string.Empty;
        public string Inadimplente { get; init; } = string.Empty;
        public string Cartao { get; init; } = string.Empty;
        public string CertificadoNormalizado { get; init; } = string.Empty;
    }
}
