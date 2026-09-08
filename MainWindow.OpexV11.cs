using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HubFinanceiro;

public partial class MainWindow
{
    private const string FornecedorFantasmaOpexV11Tag = "FornecedorFantasma";

    private bool _opexV11Configurado;
    private bool _fornecedoresV11ExclusaoVisualConfigurada;
    private MenuItem? _itemBaixarRegistroOpexV11;
    private Fornecedor? _fornecedorFantasmaOpexV11;

    private void ConfigurarOpexV11()
    {
        if (_opexV11Configurado)
            return;

        _opexV11Configurado = true;

        ConfigurarBaixaDiretaOpexV11();
        ConfigurarFornecedorFantasmaOpexV11();
        ConfigurarDesselecaoExternaOpexV11();
        ConfigurarExclusaoVisualFornecedoresV11();
    }

    private void ConfigurarBaixaDiretaOpexV11()
    {
        if (_menuAcoesOpex == null)
            return;

        var estilo = _menuAcoesOpex.Items
            .OfType<MenuItem>()
            .Select(item => item.Style)
            .FirstOrDefault(style => style != null);

        if (estilo == null)
            return;

        _itemBaixarRegistroOpexV11 = CriarOpcaoMenuOpex(
            "Baixar Registro",
            ExecutarBaixarRegistroOpexV11,
            estilo);
        _itemBaixarRegistroOpexV11.IsEnabled = false;

        // Mantém a baixa individual junto das movimentações financeiras,
        // logo após Liquidar e antes do Relatório.
        int indice = Math.Min(3, _menuAcoesOpex.Items.Count);
        _menuAcoesOpex.Items.Insert(indice, _itemBaixarRegistroOpexV11);

        _menuAcoesOpex.Opened -= MenuAcoesOpexV11_Opened;
        _menuAcoesOpex.Opened += MenuAcoesOpexV11_Opened;
    }

    private void MenuAcoesOpexV11_Opened(object sender, RoutedEventArgs e)
        => AtualizarEstadoBaixarRegistroOpexV11();

    private void AtualizarEstadoBaixarRegistroOpexV11()
    {
        if (_itemBaixarRegistroOpexV11 == null)
            return;

        bool habilitado = _pagamentoSelecionado != null;
        _itemBaixarRegistroOpexV11.IsEnabled = habilitado;
        _itemBaixarRegistroOpexV11.Opacity = habilitado ? 1.0 : 0.45;
    }

    private void ExecutarBaixarRegistroOpexV11()
    {
        if (_pagamentoSelecionado == null)
            return;

        try
        {
            int pagamentoId = _pagamentoSelecionado.Id;
            var janela = new BaixarRegistroWindow(_pagamentoSelecionado.DataPagamento)
            {
                Owner = this
            };

            if (janela.ShowDialog() != true)
                return;

            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            var pagamentos = CarregarPrevisoesPagamento();
            var pagamento = pagamentos.FirstOrDefault(p => p.Id == pagamentoId);

            if (pagamento == null)
            {
                MostrarErro("Pagamento não encontrado.");
                return;
            }

            AplicarBaixaDiretaOpexV11(pagamento, janela.DataSelecionada);

            SalvarPrevisoes(pagamentos, caminhoArquivo);
            DesselecionarPagamento();
            RecarregarPagamentos();

            MostrarSucesso($"Registro baixado como pago em {janela.DataSelecionada:dd/MM/yyyy}.");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao baixar registro", ex);
        }
    }

    internal static void AplicarBaixaDiretaOpexV11(PrevisaoPagamento pagamento, DateTime dataBaixa)
    {
        ArgumentNullException.ThrowIfNull(pagamento);
        pagamento.Status = "Pago";
        pagamento.DataProvisionamento = dataBaixa.Date;
    }

    private void ConfigurarFornecedorFantasmaOpexV11()
    {
        PagamentosItemsControl.PreviewMouseLeftButtonDown -= PagamentosItemsControl_PreviewMouseLeftButtonDownV11;
        PagamentosItemsControl.PreviewMouseLeftButtonDown += PagamentosItemsControl_PreviewMouseLeftButtonDownV11;
    }

    private void PagamentosItemsControl_PreviewMouseLeftButtonDownV11(object sender, MouseButtonEventArgs e)
    {
        // A seleção original acontece no MouseLeftButtonDown da linha.
        // Executamos depois do roteamento atual para complementar apenas os registros
        // cujo fornecedor não existe na base permanente.
        Dispatcher.BeginInvoke(
            new Action(GarantirFornecedorFantasmaSelecionadoV11),
            DispatcherPriority.Background);
    }

    private void GarantirFornecedorFantasmaSelecionadoV11()
    {
        if (_pagamentoSelecionado == null)
            return;

        // Se a rotina original encontrou um fornecedor real, não interferimos.
        if (OpexFornecedorComboBox.SelectedItem is Fornecedor)
        {
            _fornecedorFantasmaOpexV11 = null;
            return;
        }

        _fornecedorFantasmaOpexV11 = CriarFornecedorFantasma(_pagamentoSelecionado);

        var itensEdicao = new List<Fornecedor> { _fornecedorFantasmaOpexV11 };
        itensEdicao.AddRange(_fornecedoresOpex);

        _isAtualizandoAutocompleteOpex = true;
        try
        {
            OpexFornecedorComboBox.ItemsSource = itensEdicao;
            OpexFornecedorComboBox.SelectedItem = _fornecedorFantasmaOpexV11;
            OpexFornecedorComboBox.Text = _fornecedorFantasmaOpexV11.Nome;
            OpexFornecedorComboBox.Tag = FornecedorFantasmaOpexV11Tag;
        }
        finally
        {
            _isAtualizandoAutocompleteOpex = false;
        }

        if (OpexPlaceholder != null)
            OpexPlaceholder.Visibility = Visibility.Collapsed;

        AtualizarEstadoBotoes();
    }

    internal static Fornecedor CriarFornecedorFantasma(PrevisaoPagamento pagamento)
    {
        ArgumentNullException.ThrowIfNull(pagamento);

        return new Fornecedor
        {
            Nome = pagamento.NomeFornecedor,
            Codigo = pagamento.CodigoFornecedor,
            Natureza = pagamento.Natureza,
            TipoPagamento = pagamento.TipoPagamento,
            DiaPagamento = pagamento.DataPagamento.Day,
            Ativo = true,
            Administradora = string.Equals(pagamento.Empresa, "ADM", StringComparison.OrdinalIgnoreCase),
            Corretora = string.Equals(pagamento.Empresa, "COR", StringComparison.OrdinalIgnoreCase)
        };
    }

    private void ConfigurarDesselecaoExternaOpexV11()
    {
        PreviewMouseDown -= OpexLayoutGrid_PreviewMouseDownV11;
        PreviewMouseDown += OpexLayoutGrid_PreviewMouseDownV11;
    }

    private void OpexLayoutGrid_PreviewMouseDownV11(object sender, MouseButtonEventArgs e)
    {
        if (_pagamentoSelecionado == null || OpexLayoutGrid.Visibility != Visibility.Visible)
            return;

        if (e.OriginalSource is not DependencyObject origem)
            return;

        if (CliqueMantemSelecaoOpexV11(origem))
            return;

        DesselecionarPagamento();
        OpexFornecedorComboBox.Tag = null;
        _fornecedorFantasmaOpexV11 = null;
        AtualizarEstadoBaixarRegistroOpexV11();
    }

    private bool CliqueMantemSelecaoOpexV11(DependencyObject origem)
    {
        DependencyObject? atual = origem;

        while (atual != null)
        {
            if (ReferenceEquals(atual, _pagamentoBorderSelecionado))
                return true;

            if (atual is FrameworkElement elemento && elemento.Tag is PrevisaoPagamento)
                return true;

            if (atual is ButtonBase
                || atual is TextBoxBase
                || atual is ComboBox
                || atual is DatePicker
                || atual is MenuItem)
            {
                return true;
            }

            DependencyObject? pai = null;
            try
            {
                pai = VisualTreeHelper.GetParent(atual);
            }
            catch
            {
                // Alguns elementos de conteúdo não pertencem à VisualTree.
            }

            if (pai == null)
                pai = LogicalTreeHelper.GetParent(atual);

            atual = pai;
        }

        return false;
    }

    private void ConfigurarExclusaoVisualFornecedoresV11()
    {
        if (_fornecedoresV11ExclusaoVisualConfigurada)
            return;

        var templateOriginal = FornecedoresItemsControl.ItemTemplate;
        if (templateOriginal == null)
            return;

        _fornecedoresV11ExclusaoVisualConfigurada = true;

        var raiz = new FrameworkElementFactory(typeof(DockPanel));
        raiz.SetValue(DockPanel.LastChildFillProperty, true);

        var botaoExcluir = new FrameworkElementFactory(typeof(Button));
        botaoExcluir.SetValue(FrameworkElement.ToolTipProperty, "Excluir fornecedor");
        botaoExcluir.SetValue(FrameworkElement.StyleProperty, (Style)FindResource("CnabActionIconButtonStyle"));
        botaoExcluir.SetValue(Control.ForegroundProperty, CriarBrushOpex("#D56A6A"));
        botaoExcluir.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        botaoExcluir.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 4, 0));
        botaoExcluir.SetValue(UIElement.FocusableProperty, false);
        botaoExcluir.SetValue(DockPanel.DockProperty, Dock.Right);
        botaoExcluir.AddHandler(Button.ClickEvent, new RoutedEventHandler(BtnExcluirFornecedorLinhaV11_Click));

        var iconeExcluir = new FrameworkElementFactory(typeof(TextBlock));
        iconeExcluir.SetValue(TextBlock.TextProperty, "\uE74D");
        iconeExcluir.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        iconeExcluir.SetValue(TextBlock.FontSizeProperty, 17d);
        iconeExcluir.SetValue(TextBlock.ForegroundProperty, CriarBrushOpex("#D56A6A"));
        iconeExcluir.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        iconeExcluir.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        botaoExcluir.AppendChild(iconeExcluir);
        raiz.AppendChild(botaoExcluir);

        var conteudoOriginal = new FrameworkElementFactory(typeof(ContentPresenter));
        conteudoOriginal.SetBinding(ContentPresenter.ContentProperty, new Binding());
        conteudoOriginal.SetValue(ContentPresenter.ContentTemplateProperty, templateOriginal);
        conteudoOriginal.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        raiz.AppendChild(conteudoOriginal);

        FornecedoresItemsControl.ItemTemplate = new DataTemplate(typeof(Fornecedor))
        {
            VisualTree = raiz
        };
    }

    private void BtnExcluirFornecedorLinhaV11_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Fornecedor fornecedor })
            return;

        e.Handled = true;
        ExcluirFornecedorV10(fornecedor);
    }
}
