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
    private bool _fornecedoresV18LayoutPendente;
    private MenuItem? _itemBaixarRegistroOpexV11;
    private Fornecedor? _fornecedorFantasmaOpexV11;
    private Style? _estiloBotaoExcluirFornecedorV18;

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
        Dispatcher.BeginInvoke(
            new Action(GarantirFornecedorFantasmaSelecionadoV11),
            DispatcherPriority.Background);
    }

    private void GarantirFornecedorFantasmaSelecionadoV11()
    {
        if (_pagamentoSelecionado == null)
            return;

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

        _fornecedoresV11ExclusaoVisualConfigurada = true;

        FornecedoresItemsControl.ItemContainerGenerator.StatusChanged -= FornecedoresItemsControl_StatusChangedV13;
        FornecedoresItemsControl.ItemContainerGenerator.StatusChanged += FornecedoresItemsControl_StatusChangedV13;

        FornecedoresItemsControl.PreviewMouseLeftButtonDown -= FornecedoresItemsControl_PreviewMouseLeftButtonDownV18;
        FornecedoresItemsControl.PreviewMouseLeftButtonDown += FornecedoresItemsControl_PreviewMouseLeftButtonDownV18;

        if (!AplicarExclusaoIntegradaFornecedoresV13())
            AgendarUmaPassagemDeLayoutFornecedoresV18();
    }

    private void FornecedoresItemsControl_StatusChangedV13(object? sender, EventArgs e)
    {
        if (FornecedoresItemsControl.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
            return;

        if (!AplicarExclusaoIntegradaFornecedoresV13())
            AgendarUmaPassagemDeLayoutFornecedoresV18();
    }

    private void AgendarUmaPassagemDeLayoutFornecedoresV18()
    {
        if (_fornecedoresV18LayoutPendente)
            return;

        _fornecedoresV18LayoutPendente = true;
        FornecedoresItemsControl.LayoutUpdated -= FornecedoresItemsControl_LayoutUpdatedUmaVezV18;
        FornecedoresItemsControl.LayoutUpdated += FornecedoresItemsControl_LayoutUpdatedUmaVezV18;
    }

    private void FornecedoresItemsControl_LayoutUpdatedUmaVezV18(object? sender, EventArgs e)
    {
        FornecedoresItemsControl.LayoutUpdated -= FornecedoresItemsControl_LayoutUpdatedUmaVezV18;
        _fornecedoresV18LayoutPendente = false;
        AplicarExclusaoIntegradaFornecedoresV13();
    }

    private void FornecedoresItemsControl_PreviewMouseLeftButtonDownV18(object sender, MouseButtonEventArgs e)
    {
        var selecaoAnterior = _fornecedorItemSelecionado;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                AtualizarEstadoVisualFornecedorV18(selecaoAnterior);
                if (!ReferenceEquals(selecaoAnterior, _fornecedorItemSelecionado))
                    AtualizarEstadoVisualFornecedorV18(_fornecedorItemSelecionado);
            }),
            DispatcherPriority.Background);
    }

    private bool AplicarExclusaoIntegradaFornecedoresV13()
    {
        bool todosIntegrados = true;

        for (int i = 0; i < FornecedoresItemsControl.Items.Count; i++)
        {
            if (FornecedoresItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not DependencyObject container)
            {
                todosIntegrados = false;
                continue;
            }

            if (!IntegrarBotaoExcluirFornecedorV13(container))
                todosIntegrados = false;
        }

        return todosIntegrados;
    }

    private bool IntegrarBotaoExcluirFornecedorV13(DependencyObject container)
    {
        var card = EncontrarDescendenteV13<Border>(
            container,
            border => string.Equals(border.Name, "FornecedorBorder", StringComparison.Ordinal));
        if (card == null)
            return false;

        var cornerRadius = new CornerRadius(10);
        if (card.CornerRadius != cornerRadius)
            card.CornerRadius = cornerRadius;

        var padding = new Thickness(12, 8, 12, 8);
        if (card.Padding != padding)
            card.Padding = padding;

        AtualizarEstadoVisualFornecedorV18(card);

        var corBordaNeutra = ((SolidColorBrush)FindResource("BorderColor")).Color;
        if (card.BorderBrush is not SolidColorBrush bordaAtual
            || bordaAtual.Color != corBordaNeutra
            || Math.Abs(bordaAtual.Opacity) > 0.0001)
        {
            card.BorderBrush = new SolidColorBrush(corBordaNeutra) { Opacity = 0 };
        }

        var espessuraBorda = new Thickness(0, 0, 0, 1);
        if (card.BorderThickness != espessuraBorda)
            card.BorderThickness = espessuraBorda;

        if (card.SnapsToDevicePixels)
            card.SnapsToDevicePixels = false;

        if (RenderOptions.GetEdgeMode(card) != EdgeMode.Unspecified)
            RenderOptions.SetEdgeMode(card, EdgeMode.Unspecified);

        var grid = card.Child as Grid;
        if (grid == null)
            return false;

        var existente = EncontrarDescendenteV13<Button>(
            card,
            botao => string.Equals(botao.ToolTip?.ToString(), "Excluir fornecedor", StringComparison.Ordinal));
        if (existente != null)
            return true;

        if (grid.ColumnDefinitions.Count < 3)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badgeStatus = grid.Children
            .OfType<Border>()
            .FirstOrDefault(item => Grid.GetColumn(item) == 1);
        var margemBadge = new Thickness(12, 0, 8, 0);
        if (badgeStatus != null && badgeStatus.Margin != margemBadge)
            badgeStatus.Margin = margemBadge;

        var botaoExcluir = new Button
        {
            ToolTip = "Excluir fornecedor",
            Style = ObterEstiloBotaoExcluirFornecedorV18(),
            Foreground = CriarBrushOpex("#D56A6A"),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0),
            Focusable = false,
            DataContext = card.DataContext,
            Content = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 17,
                Foreground = CriarBrushOpex("#D56A6A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        botaoExcluir.Click += BtnExcluirFornecedorLinhaV11_Click;
        Grid.SetColumn(botaoExcluir, 2);
        grid.Children.Add(botaoExcluir);

        return true;
    }

    private void AtualizarEstadoVisualFornecedorV18(Border? card)
    {
        if (card == null)
            return;

        bool selecionado = ReferenceEquals(card, _fornecedorItemSelecionado);
        if (selecionado)
        {
            var corSelecionada = Color.FromRgb(94, 23, 170);
            if (card.Background is not SolidColorBrush fundoSelecionado
                || fundoSelecionado.Color != corSelecionada
                || Math.Abs(fundoSelecionado.Opacity - 1d) > 0.0001)
            {
                card.Background = new SolidColorBrush(corSelecionada);
            }
        }
        else if (!ReferenceEquals(card.Background, Brushes.Transparent))
        {
            card.Background = Brushes.Transparent;
        }
    }

    private Style ObterEstiloBotaoExcluirFornecedorV18()
        => _estiloBotaoExcluirFornecedorV18 ??= CriarEstiloBotaoExcluirFornecedorV13();

    private static Style CriarEstiloBotaoExcluirFornecedorV13()
    {
        var template = new ControlTemplate(typeof(Button));
        var raiz = new FrameworkElementFactory(typeof(Border));
        raiz.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        raiz.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        raiz.SetValue(Border.BorderThicknessProperty, new Thickness(0));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        raiz.AppendChild(presenter);
        template.VisualTree = raiz;

        var estilo = new Style(typeof(Button));
        estilo.Setters.Add(new Setter(FrameworkElement.WidthProperty, 34d));
        estilo.Setters.Add(new Setter(FrameworkElement.HeightProperty, 32d));
        estilo.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        estilo.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        estilo.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        estilo.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        estilo.Setters.Add(new Setter(Control.TemplateProperty, template));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72d));
        estilo.Triggers.Add(hover);

        return estilo;
    }

    private static T? EncontrarDescendenteV13<T>(DependencyObject raiz, Func<T, bool> predicado)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T alvo && predicado(alvo))
                return alvo;

            var encontrado = EncontrarDescendenteV13<T>(filho, predicado);
            if (encontrado != null)
                return encontrado;
        }

        return null;
    }

    private void BtnExcluirFornecedorLinhaV11_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Fornecedor fornecedor })
            return;

        e.Handled = true;
        ExcluirFornecedorV10(fornecedor);
    }
}
