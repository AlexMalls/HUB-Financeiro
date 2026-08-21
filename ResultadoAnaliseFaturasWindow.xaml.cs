using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class ResultadoAnaliseFaturasWindow : Window
{
    private readonly AnaliseFinalDiagnostico _diagnostico;
    private readonly List<LinhaResultado> _linhas;
    private readonly AnaliseFaturasHistoricoContextoCriacao? _historicoContextoCriacao;
    private readonly AnaliseFaturasExplicacaoRecorrenteService? _explicacaoRecorrenteService;
    private AnaliseFaturasHistoricoSnapshot? _historicoSnapshot;
    private LinhaResultado? _linhaDestacada;
    private bool _edicoesManuaisAlteradas;

    public bool HistoricoSalvo { get; private set; }

    public ResultadoAnaliseFaturasWindow(AnaliseFinalDiagnostico diagnostico)
        : this(diagnostico, null, null)
    {
    }

    public ResultadoAnaliseFaturasWindow(
        AnaliseFinalDiagnostico diagnostico,
        AnaliseFaturasHistoricoContextoCriacao contextoHistorico)
        : this(diagnostico, contextoHistorico, null)
    {
    }

    public ResultadoAnaliseFaturasWindow(AnaliseFaturasHistoricoSnapshot snapshot)
        : this(snapshot?.Resultado ?? throw new ArgumentNullException(nameof(snapshot)), null, snapshot)
    {
    }

    private ResultadoAnaliseFaturasWindow(
        AnaliseFinalDiagnostico diagnostico,
        AnaliseFaturasHistoricoContextoCriacao? contextoHistorico,
        AnaliseFaturasHistoricoSnapshot? snapshotHistorico)
    {
        InitializeComponent();
        _diagnostico = diagnostico ?? throw new ArgumentNullException(nameof(diagnostico));
        _historicoContextoCriacao = contextoHistorico;
        _historicoSnapshot = snapshotHistorico;
        string? caminhoBaseData = contextoHistorico?.CaminhoBaseData ??
            ObterCaminhoBaseDataDoSnapshot(snapshotHistorico);
        if (!string.IsNullOrWhiteSpace(caminhoBaseData))
            _explicacaoRecorrenteService = new AnaliseFaturasExplicacaoRecorrenteService(caminhoBaseData);

        _linhas = diagnostico.Resultados.Select(x => new LinhaResultado(x, () => _edicoesManuaisAlteradas = true)).ToList();

        StatusComboBox.ItemsSource = new[] { "Todos", "Compatível", "Atenção", "Divergência" };

        TituloCompetenciaText.Text = $"Competência analisada: {_diagnostico.Competencia:MM/yyyy}";

        AnaliseFaturasHistoricoTotais? totaisSalvos = _historicoSnapshot?.Totais;
        TotalText.Text = (totaisSalvos?.Total ?? _diagnostico.Total).ToString("N0");
        CompativeisText.Text = (totaisSalvos?.Compativeis ?? _diagnostico.TotalCompativeis).ToString("N0");
        AtencaoText.Text = (totaisSalvos?.Atencoes ?? _diagnostico.TotalAtencao).ToString("N0");
        int totalDivergencias = totaisSalvos != null
            ? totaisSalvos.Pendentes + totaisSalvos.Ambiguas
            : _diagnostico.TotalPendentes + _diagnostico.TotalAmbiguos;
        DivergenciasText.Text = totalDivergencias.ToString("N0");

        AtualizarResumoTolerancia();

        // A tela abre diretamente no que precisa de ação.
        StatusComboBox.SelectedItem = "Divergência";
        AplicarFiltro();
    }

    private void AtualizarResumoTolerancia()
    {
        CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");
        AnaliseFaturasHistoricoTotais? totaisSalvos = _historicoSnapshot?.Totais;
        AnaliseFaturasHistoricoConfiguracao? configuracaoSalva = _historicoSnapshot?.Configuracoes;

        decimal tolerancia = configuracaoSalva?.ToleranciaFinanceira ?? _diagnostico.ToleranciaFinanceiraUtilizada;
        int quantidade = totaisSalvos?.CompativeisPorTolerancia ?? _diagnostico.TotalCompativeisPorTolerancia;
        decimal faturaMaior = totaisSalvos?.ToleranciaFaturaMaior ?? _diagnostico.SomaToleranciaFaturaMaior;
        decimal overMaior = totaisSalvos?.ToleranciaOverMaior ?? _diagnostico.SomaToleranciaOverMaior;
        decimal saldo = totaisSalvos?.SaldoToleranciaLiquido ?? _diagnostico.SaldoToleranciaLiquido;

        ToleranciaTituloText.Text = $"Diferenças ignoradas pela tolerância de ±R$ {tolerancia.ToString("N2", ptBr)}";
        ToleranciaQuantidadeText.Text = quantidade == 1
            ? "1 caso considerado compatível pela margem"
            : $"{quantidade:N0} casos considerados compatíveis pela margem";

        ToleranciaFaturaMaiorText.Text = $"R$ {faturaMaior.ToString("N2", ptBr)}";
        ToleranciaOverMaiorText.Text = $"R$ {overMaior.ToString("N2", ptBr)}";

        if (saldo == 0m)
        {
            ToleranciaSaldoText.Text = "R$ 0,00 • equilibrado";
        }
        else if (saldo > 0m)
        {
            ToleranciaSaldoText.Text = $"+R$ {saldo.ToString("N2", ptBr)} • Fatura";
        }
        else
        {
            ToleranciaSaldoText.Text = $"-R$ {Math.Abs(saldo).ToString("N2", ptBr)} • Over";
        }

        bool ignorandoCopart = configuracaoSalva?.IgnorarCoparticipacao ?? _diagnostico.IgnorandoCoparticipacao;
        bool ignorandoAnteriores = configuracaoSalva?.IgnorarCompetenciasAnteriores ?? _diagnostico.IgnorandoCompetenciasAnteriores;

        CopartModoText.Text = ignorandoCopart
            ? "Ignorada no NET comparável (padrão)"
            : "Incluída no NET comparável";

        CompetenciasAnterioresModoText.Text = ignorandoAnteriores
            ? "Ignoradas no valor comparável (padrão)"
            : "Incluídas no valor comparável";
    }

    private async void ExportarExcel_Click(object sender, RoutedEventArgs e)
    {
        List<AnaliseFinalResultado> divergencias = _diagnostico.Resultados
            .Where(x => x.Status == AnaliseFinalStatus.DivergenciaPendente || x.Status == AnaliseFinalStatus.Ambiguo)
            .ToList();

        if (divergencias.Count == 0)
        {
            CustomMessageBox.ShowInformation(
                "Não existem Divergências para extrair nesta análise.",
                "Extrair relatórios em Excel");
            return;
        }

        string conteudoAnterior = ExportarExcelButton.Content?.ToString() ?? "Extrair relatórios em Excel";
        ExportarExcelButton.IsEnabled = false;
        ExportarExcelButton.Content = "Extraindo...";
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            var exporter = new AnaliseFaturasExcelExporter();
            AnaliseFaturasExcelExportacaoResultado resultado = await Task.Run(() =>
                exporter.ExportarPendenciasPorTipo(_diagnostico));

            CustomMessageBox.ShowSuccess(
                $"{resultado.QuantidadeRelatorios:N0} relatório(s) em Excel criado(s) na Área de Trabalho.\n\n" +
                $"Pasta: {resultado.PastaDestino}",
                "Relatórios extraídos");
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError(
                "Não foi possível extrair os relatórios em Excel.\n\n" + ex.Message,
                "Erro na exportação");
        }
        finally
        {
            Mouse.OverrideCursor = null;
            ExportarExcelButton.IsEnabled = true;
            ExportarExcelButton.Content = conteudoAnterior;
        }
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string status = StatusComboBox.SelectedItem as string ?? "Todos";
        AtualizarCardsFiltro(status);
        AplicarFiltro();
    }

    private void PesquisaTextBox_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void FiltroCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao)
            return;

        string statusClicado = botao.Tag as string ?? "Todos";
        string statusAtual = StatusComboBox.SelectedItem as string ?? "Todos";

        if (statusClicado == "Todos")
        {
            if (statusAtual == "Todos")
            {
                AtualizarCardsFiltro("Todos");
                AplicarFiltro();
            }
            else
            {
                StatusComboBox.SelectedItem = "Todos";
            }
            return;
        }

        StatusComboBox.SelectedItem = string.Equals(statusAtual, statusClicado, StringComparison.OrdinalIgnoreCase)
            ? "Todos"
            : statusClicado;
    }

    private void AplicarFiltro()
    {
        if (!IsInitialized || ResultadosDataGrid == null || StatusComboBox == null || PesquisaTextBox == null)
            return;

        IEnumerable<LinhaResultado> consulta = _linhas;
        string status = StatusComboBox.SelectedItem as string ?? "Todos";
        if (status != "Todos")
            consulta = consulta.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));

        string termo = Compactar(PesquisaTextBox.Text);
        if (!string.IsNullOrWhiteSpace(termo))
        {
            consulta = consulta.Where(x =>
                Compactar(x.Beneficiario).Contains(termo, StringComparison.Ordinal) ||
                Compactar(x.Certificado).Contains(termo, StringComparison.Ordinal) ||
                Compactar(x.Entidade).Contains(termo, StringComparison.Ordinal) ||
                Compactar(x.ExplicacaoManual).Contains(termo, StringComparison.Ordinal) ||
                Compactar(x.RegraExplicativa).Contains(termo, StringComparison.Ordinal) ||
                Compactar(x.TipoDivergencia).Contains(termo, StringComparison.Ordinal) ||
                Compactar(x.Justificativa).Contains(termo, StringComparison.Ordinal));
        }

        List<LinhaResultado> resultado = consulta.ToList();

        LimparSelecaoAtual();
        ResultadosDataGrid.ItemsSource = resultado;
    }

    private void AbrirDetalhes_Click(object sender, RoutedEventArgs e) => AbrirSelecionado();

    private void ResultadosDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        LinhaResultado? linha = ObterLinhaSelecionada();

        if (!ReferenceEquals(_linhaDestacada, linha))
        {
            if (_linhaDestacada != null)
                _linhaDestacada.DestaqueLinha = false;

            _linhaDestacada = linha;

            if (_linhaDestacada != null)
                _linhaDestacada.DestaqueLinha = true;
        }

        AbrirDetalhesButton.IsEnabled = linha != null;
    }

    private void EditarExplicacao_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao ||
            botao.DataContext is not LinhaResultado linha ||
            !linha.PodeEditarExplicacao)
            return;

        AnaliseFaturasExplicacaoRecorrenteRegistro? recorrenciaAtual = null;
        try
        {
            recorrenciaAtual = _explicacaoRecorrenteService?.Obter(linha.Original);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowWarning(
                "A explicação pode ser editada, mas não foi possível consultar a recorrência deste cliente.\n\n" + ex.Message,
                "Explicação recorrente");
        }

        var janela = new EditarExplicacaoAnaliseWindow(
            linha.Beneficiario,
            linha.Certificado,
            linha.ExplicacaoManual,
            recorrenciaAtual != null)
        {
            Owner = this
        };

        if (janela.ShowDialog() != true)
            return;

        linha.ExplicacaoManual = janela.Explicacao;

        try
        {
            if (_explicacaoRecorrenteService != null)
            {
                if (janela.ExplicacaoRecorrente)
                    _explicacaoRecorrenteService.Salvar(linha.Original, janela.Explicacao);
                else if (recorrenciaAtual != null)
                    _explicacaoRecorrenteService.Remover(linha.Original);
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError(
                "A explicação desta análise será mantida, mas não foi possível atualizar a recorrência.\n\n" + ex.Message,
                "Erro ao salvar recorrência");
        }

        PersistirExplicacoesManuais();

        if (!string.IsNullOrWhiteSpace(PesquisaTextBox.Text))
            AplicarFiltro();
    }

    private void ResultadosDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        DataGridCell? celula = EncontrarAncestral<DataGridCell>(e.OriginalSource as DependencyObject);
        if (celula != null &&
            string.Equals(celula.Column.Header?.ToString(), "Explicação", StringComparison.Ordinal))
        {
            e.Handled = true;
            return;
        }

        AbrirSelecionado();
    }

    private void AbrirSelecionado()
    {
        LinhaResultado? linha = ObterLinhaSelecionada();
        if (linha == null)
            return;

        IReadOnlyList<AnaliseFaturasHistoricoArquivo> arquivosUtilizados =
            _historicoSnapshot?.ArquivosUtilizados ??
            _historicoContextoCriacao?.ArquivosUtilizados ??
            Array.Empty<AnaliseFaturasHistoricoArquivo>();

        var janela = new DetalheAnaliseFaturasWindow(linha.Original, arquivosUtilizados) { Owner = this };
        janela.ShowDialog();
    }

    private LinhaResultado? ObterLinhaSelecionada()
    {
        if (ResultadosDataGrid.CurrentItem is LinhaResultado atual)
            return atual;

        foreach (DataGridCellInfo celula in ResultadosDataGrid.SelectedCells)
        {
            if (celula.Item is LinhaResultado linha)
                return linha;
        }

        return ResultadosDataGrid.SelectedItem as LinhaResultado;
    }

    private void LimparSelecaoAtual()
    {
        if (_linhaDestacada != null)
        {
            _linhaDestacada.DestaqueLinha = false;
            _linhaDestacada = null;
        }

        if (ResultadosDataGrid != null)
        {
            ResultadosDataGrid.UnselectAllCells();
            ResultadosDataGrid.UnselectAll();
        }

        if (AbrirDetalhesButton != null)
            AbrirDetalhesButton.IsEnabled = false;
    }

    private void AtualizarCardsFiltro(string status)
    {
        if (CompativeisCardButton == null)
            return;

        AplicarEstadoCard(CompativeisCardButton, status == "Compatível", "#70D6A0");
        AplicarEstadoCard(AtencaoCardButton, status == "Atenção", "#74C7EC");
        AplicarEstadoCard(DivergenciasCardButton, status == "Divergência", "#F2C879");

        TotalCardButton.BorderBrush = Brushes.Transparent;
        TotalCardButton.BorderThickness = new Thickness(1);
        TotalCardButton.Opacity = 0.94;
    }

    private static void AplicarEstadoCard(Button botao, bool ativo, string corAtiva)
    {
        botao.BorderBrush = ativo
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(corAtiva))
            : Brushes.Transparent;
        botao.BorderThickness = ativo ? new Thickness(2) : new Thickness(1);
        botao.Opacity = ativo ? 1.0 : 0.94;
    }

    private void PersistirExplicacoesManuais()
    {
        if (!_edicoesManuaisAlteradas)
            return;

        try
        {
            if (_historicoContextoCriacao != null)
            {
                AnaliseFaturasHistoricoSnapshot snapshot = _historicoContextoCriacao.CriarSnapshot(_diagnostico);
                var service = new AnaliseFaturasHistoricoService(_historicoContextoCriacao.CaminhoBaseData);
                service.Salvar(snapshot);
                _historicoSnapshot = snapshot;
                HistoricoSalvo = true;
            }
            else if (_historicoSnapshot != null &&
                     !string.IsNullOrWhiteSpace(_historicoSnapshot.CaminhoArquivo))
            {
                AnaliseFaturasHistoricoService.AtualizarArquivoCarregado(_historicoSnapshot);
                HistoricoSalvo = true;
            }

            _edicoesManuaisAlteradas = false;
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError(
                "A explicação foi alterada na tela, mas não pôde ser gravada no histórico.\n\n" + ex.Message,
                "Erro ao salvar explicação");
        }
    }

    private static T? EncontrarAncestral<T>(DependencyObject? origem) where T : DependencyObject
    {
        DependencyObject? atual = origem;
        while (atual != null)
        {
            if (atual is T encontrado)
                return encontrado;

            atual = VisualTreeHelper.GetParent(atual);
        }

        return null;
    }

    private static string? ObterCaminhoBaseDataDoSnapshot(AnaliseFaturasHistoricoSnapshot? snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.CaminhoArquivo))
            return null;

        DirectoryInfo? pasta = Directory.GetParent(snapshot.CaminhoArquivo);
        while (pasta != null)
        {
            if (string.Equals(pasta.Name, "Relatórios de Analise", StringComparison.OrdinalIgnoreCase))
                return pasta.Parent?.FullName;

            pasta = pasta.Parent;
        }

        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        PersistirExplicacoesManuais();
        base.OnClosing(e);
    }

    private static string Compactar(string? texto)
    {
        string normalizado = AnaliseFaturasNormalizador.NormalizarNome(texto);
        return new string(normalizado.Where(char.IsLetterOrDigit).ToArray());
    }

    private sealed class LinhaResultado : INotifyPropertyChanged
    {
        private bool _destaqueLinha;
        private string _explicacaoManual;
        private readonly Action _aoAlterarExplicacao;

        public LinhaResultado(AnaliseFinalResultado original, Action aoAlterarExplicacao)
        {
            AnaliseFaturasVisaoFinanceira visaoFinanceira = AnaliseFaturasVisaoFinanceiraService.Calcular(original);

            Original = original;
            Beneficiario = Vazio(original.Beneficiario);
            Certificado = Vazio(original.Certificado);
            TipoDivergencia = original.TipoDivergencia;
            Diferenca = FormatarValor(visaoFinanceira.DiferencaResidual);
            ValorFatura = FormatarValor(visaoFinanceira.ValorFaturaLiquida);
            ValorOver = FormatarValor(original.ValorOver);
            Entidade = Vazio(original.Entidade);
            Competencia = original.Competencia.ToString("MM/yyyy");
            Status = original.Status == AnaliseFinalStatus.Ambiguo
                ? "Divergência"
                : AnaliseFinalService.TraduzirStatus(original.Status);
            RegraExplicativa = string.IsNullOrWhiteSpace(original.RegraExplicativa) ? "—" : original.RegraExplicativa;
            Justificativa = visaoFinanceira.ReconstruidaDeHistoricoLegado
                ? visaoFinanceira.CriarResumo(original.ValorFatura, original.ValorOver)
                : original.JustificativaFinal;
            _explicacaoManual = original.JustificativaManual ?? string.Empty;
            _aoAlterarExplicacao = aoAlterarExplicacao;
        }

        public AnaliseFinalResultado Original { get; }
        public string Beneficiario { get; }
        public string Certificado { get; }
        public string TipoDivergencia { get; }
        public string Diferenca { get; }
        public string ValorFatura { get; }
        public string ValorOver { get; }
        public string Entidade { get; }
        public string Competencia { get; }
        public string Status { get; }
        public string RegraExplicativa { get; }
        public string Justificativa { get; }
        public bool PodeEditarExplicacao => AnaliseFaturasExplicacaoRecorrenteService.PodeReceberExplicacaoManual(Original);

        public string ExplicacaoManual
        {
            get => _explicacaoManual;
            set
            {
                string novo = value ?? string.Empty;
                if (string.Equals(_explicacaoManual, novo, StringComparison.Ordinal))
                    return;

                _explicacaoManual = novo;
                Original.JustificativaManual = novo;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExplicacaoManual)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemExplicacaoManual)));
                _aoAlterarExplicacao();
            }
        }

        public bool TemExplicacaoManual => !string.IsNullOrWhiteSpace(_explicacaoManual);

        public bool DestaqueLinha
        {
            get => _destaqueLinha;
            set
            {
                if (_destaqueLinha == value)
                    return;

                _destaqueLinha = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DestaqueLinha)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private static string FormatarValor(decimal? valor) => valor.HasValue ? valor.Value.ToString("N2") : "—";
        private static string Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? "—" : texto;
    }
}
