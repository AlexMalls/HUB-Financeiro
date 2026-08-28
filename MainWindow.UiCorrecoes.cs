using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class MainWindow
{
    private bool _correcoesUiGeraisAplicadas;
    private bool _painelFornecedorEmailReconstruido;
    private bool _aplicandoFiltroFornecedorEmail;
    private TextBox? _pesquisaFornecedorEmailTextBox;
    private TextBlock? _pesquisaFornecedorEmailPlaceholder;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_correcoesUiGeraisAplicadas)
            return;

        _correcoesUiGeraisAplicadas = true;
        AplicarCorrecoesUiGerais();
    }

    private void AplicarCorrecoesUiGerais()
    {
        ConfigurarListaFornecedoresEmail();
        AjustarBotoesOpex();
    }

    private void ConfigurarListaFornecedoresEmail()
    {
        if (FornecedorItemsControl == null || _painelFornecedorEmailReconstruido)
            return;

        var scrollViewerAntigo = EncontrarAncestral<ScrollViewer>(FornecedorItemsControl);
        if (scrollViewerAntigo?.Parent is not Grid gridAntigo
            || gridAntigo.Parent is not Border painelAntigo)
            return;

        _painelFornecedorEmailReconstruido = true;

        // O XAML antigo continua existindo como âncora para preservar os nomes e o fluxo
        // já usado pelo restante do sistema, mas o visual é descartado por completo aqui.
        scrollViewerAntigo.Content = null;
        painelAntigo.Child = null;
        painelAntigo.Background = Brushes.Transparent;
        painelAntigo.BorderThickness = new Thickness(0);
        painelAntigo.Padding = new Thickness(0);
        painelAntigo.CornerRadius = new CornerRadius(0);

        FornecedorItemsControl.Background = Brushes.Transparent;
        FornecedorItemsControl.Margin = new Thickness(0);
        FornecedorItemsControl.ItemTemplate = CriarTemplateLinhaFornecedorEmail();
        FornecedorItemsControl.ItemContainerGenerator.StatusChanged -= FornecedorEmailContainers_StatusChanged;
        FornecedorItemsControl.ItemContainerGenerator.StatusChanged += FornecedorEmailContainers_StatusChanged;

        painelAntigo.Child = CriarPainelFornecedorEmailOpex();
        AplicarEstadoVisualFornecedoresEmail();
    }

    private Grid CriarPainelFornecedorEmailOpex()
    {
        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titulo = new TextBlock
        {
            Text = "Fornecedores",
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(10, 5, 10, 10)
        };
        Grid.SetRow(titulo, 0);
        raiz.Children.Add(titulo);

        var pesquisa = CriarPesquisaFornecedorEmail();
        Grid.SetRow(pesquisa, 1);
        raiz.Children.Add(pesquisa);

        var tabela = new Border
        {
            Background = NovaCor(UiCorrecoesPolicy.FundoTabelaFornecedorEmail),
            CornerRadius = new CornerRadius(8),
            BorderBrush = NovaCor(UiCorrecoesPolicy.CorSeparadorFornecedorEmail),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        Grid.SetRow(tabela, 2);

        var tabelaGrid = new Grid { Margin = new Thickness(0) };
        tabelaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tabelaGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var cabecalho = CriarCabecalhoFornecedorEmail();
        Grid.SetRow(cabecalho, 0);
        tabelaGrid.Children.Add(cabecalho);

        var listaScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Content = FornecedorItemsControl
        };
        Grid.SetRow(listaScroll, 1);
        tabelaGrid.Children.Add(listaScroll);

        tabela.Child = tabelaGrid;
        raiz.Children.Add(tabela);
        return raiz;
    }

    private Border CriarPesquisaFornecedorEmail()
    {
        var caixaPesquisa = new Border
        {
            Height = 38,
            Background = NovaCor("#992A2A2D"),
            BorderBrush = NovaCor(UiCorrecoesPolicy.CorSeparadorFornecedorEmail),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var gridPesquisa = new Grid();
        _pesquisaFornecedorEmailTextBox = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 0, 12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            ToolTip = "Pesquisar fornecedor"
        };
        _pesquisaFornecedorEmailTextBox.TextChanged += PesquisaFornecedorEmail_TextChanged;

        _pesquisaFornecedorEmailPlaceholder = new TextBlock
        {
            Text = "Pesquisar fornecedor...",
            Foreground = NovaCor("#85858E"),
            FontSize = 12,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        gridPesquisa.Children.Add(_pesquisaFornecedorEmailTextBox);
        gridPesquisa.Children.Add(_pesquisaFornecedorEmailPlaceholder);
        caixaPesquisa.Child = gridPesquisa;
        return caixaPesquisa;
    }

    private static UniformGrid CriarCabecalhoFornecedorEmail()
    {
        var cabecalho = new UniformGrid
        {
            Columns = 2,
            Rows = 1,
            Height = UiCorrecoesPolicy.AlturaCabecalhoFornecedorEmail,
            Background = NovaCor(UiCorrecoesPolicy.FundoCabecalhoFornecedorEmail)
        };

        var fornecedor = new TextBlock
        {
            Text = UiCorrecoesPolicy.CabecalhoFornecedorEmail,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 10, 0)
        };
        cabecalho.Children.Add(fornecedor);

        var email = new TextBlock
        {
            Text = UiCorrecoesPolicy.CabecalhoEmailFornecedor,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 20, 0)
        };
        cabecalho.Children.Add(email);

        return cabecalho;
    }

    private DataTemplate CriarTemplateLinhaFornecedorEmail()
    {
        var dataTemplate = new DataTemplate(typeof(Fornecedor));

        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(FrameworkElement.HeightProperty, UiCorrecoesPolicy.AlturaLinhaFornecedorEmail);
        toggle.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        toggle.SetValue(Control.PaddingProperty, new Thickness(0));
        toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        toggle.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        toggle.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch);
        toggle.SetValue(Control.FocusVisualStyleProperty, null);
        toggle.SetValue(Control.TemplateProperty, CriarTemplateToggleLinhaFornecedorEmail());
        toggle.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(FornecedorToggle_Click));

        dataTemplate.VisualTree = toggle;
        return dataTemplate;
    }

    private static ControlTemplate CriarTemplateToggleLinhaFornecedorEmail()
    {
        var template = new ControlTemplate(typeof(ToggleButton));

        var linha = new FrameworkElementFactory(typeof(Border), "Linha");
        linha.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        linha.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        linha.SetValue(Border.PaddingProperty, new Thickness(0));
        linha.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var grid = new FrameworkElementFactory(typeof(Grid));

        var colunas = new FrameworkElementFactory(typeof(UniformGrid));
        colunas.SetValue(UniformGrid.ColumnsProperty, 2);
        colunas.SetValue(FrameworkElement.HeightProperty, UiCorrecoesPolicy.AlturaLinhaFornecedorEmail);

        var nome = new FrameworkElementFactory(typeof(TextBlock));
        nome.SetBinding(TextBlock.TextProperty, new Binding(nameof(Fornecedor.Nome)));
        nome.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        nome.SetValue(TextBlock.FontSizeProperty, 12d);
        nome.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        nome.SetValue(TextBlock.MarginProperty, new Thickness(20, 0, 10, 0));
        nome.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        colunas.AppendChild(nome);

        var email = new FrameworkElementFactory(typeof(TextBlock));
        email.SetBinding(TextBlock.TextProperty, new Binding(nameof(Fornecedor.Email)));
        email.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        email.SetValue(TextBlock.FontSizeProperty, 12d);
        email.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        email.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 20, 0));
        email.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        colunas.AppendChild(email);
        grid.AppendChild(colunas);

        var separador = new FrameworkElementFactory(typeof(Border));
        separador.SetValue(FrameworkElement.HeightProperty, 1d);
        separador.SetValue(Border.BackgroundProperty, NovaCor(UiCorrecoesPolicy.CorSeparadorFornecedorEmail));
        separador.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        separador.SetValue(UIElement.OpacityProperty, UiCorrecoesPolicy.OpacidadeSeparadorFornecedorEmail);
        grid.AppendChild(separador);

        linha.AppendChild(grid);
        template.VisualTree = linha;

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, NovaCor("#2A2A2D"), "Linha"));
        template.Triggers.Add(hover);

        var selecionado = new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true
        };
        selecionado.Setters.Add(new Setter(Border.BackgroundProperty, NovaCor("#3D2354"), "Linha"));
        template.Triggers.Add(selecionado);

        var desabilitado = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        desabilitado.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent, "Linha"));
        template.Triggers.Add(desabilitado);

        return template;
    }

    private void PesquisaFornecedorEmail_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_pesquisaFornecedorEmailTextBox == null)
            return;

        if (_pesquisaFornecedorEmailPlaceholder != null)
            _pesquisaFornecedorEmailPlaceholder.Visibility =
                string.IsNullOrEmpty(_pesquisaFornecedorEmailTextBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

        AplicarFiltroFornecedorEmail();
    }

    private void AplicarFiltroFornecedorEmail()
    {
        if (_aplicandoFiltroFornecedorEmail || _pesquisaFornecedorEmailTextBox == null)
            return;

        try
        {
            _aplicandoFiltroFornecedorEmail = true;
            var resultado = UiCorrecoesPolicy.FiltrarFornecedoresEmail(
                _todosFornecedores,
                _pesquisaFornecedorEmailTextBox.Text);

            if (_fornecedorSelecionado?.DataContext is Fornecedor selecionado
                && !resultado.Contains(selecionado))
            {
                _fornecedorSelecionado.IsChecked = false;
                _fornecedorSelecionado = null;
            }

            FornecedorItemsControl.ItemsSource = resultado;
        }
        finally
        {
            _aplicandoFiltroFornecedorEmail = false;
        }
    }

    private void FornecedorEmailContainers_StatusChanged(object? sender, EventArgs e)
    {
        if (FornecedorItemsControl.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
            return;

        // Se o watcher recarregar o ItemsSource durante uma pesquisa, reaplica o termo.
        if (!_aplicandoFiltroFornecedorEmail
            && _pesquisaFornecedorEmailTextBox != null
            && !string.IsNullOrWhiteSpace(_pesquisaFornecedorEmailTextBox.Text))
        {
            var termo = _pesquisaFornecedorEmailTextBox.Text.Trim();
            var contemItemForaDoFiltro = FornecedorItemsControl.Items
                .OfType<Fornecedor>()
                .Any(f => !f.Nome.Contains(termo, StringComparison.CurrentCultureIgnoreCase));

            if (contemItemForaDoFiltro)
            {
                AplicarFiltroFornecedorEmail();
                return;
            }
        }

        AplicarEstadoVisualFornecedoresEmail();
    }

    private void AplicarEstadoVisualFornecedoresEmail()
    {
        for (var i = 0; i < FornecedorItemsControl.Items.Count; i++)
        {
            if (FornecedorItemsControl.Items[i] is not Fornecedor fornecedor)
                continue;

            var container = FornecedorItemsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (container == null)
                continue;

            var toggle = EncontrarFilhoVisual<ToggleButton>(container);
            if (toggle == null)
                continue;

            var habilitado = UiCorrecoesPolicy.FornecedorPodeReceberEmail(fornecedor);
            toggle.IsEnabled = habilitado;
            toggle.Opacity = habilitado ? 1.0 : 0.34;
            toggle.Cursor = habilitado
                ? System.Windows.Input.Cursors.Hand
                : System.Windows.Input.Cursors.Arrow;
            toggle.ToolTip = habilitado ? null : "Fornecedor sem e-mail cadastrado";

            if (!habilitado && toggle.IsChecked == true)
            {
                toggle.IsChecked = false;
                if (ReferenceEquals(_fornecedorSelecionado, toggle))
                    _fornecedorSelecionado = null;
            }
        }
    }

    private void AjustarBotoesOpex()
    {
        if (BtnRegistrarPagamento != null)
        {
            BtnRegistrarPagamento.MinWidth = UiCorrecoesPolicy.LarguraMinimaRegistrarPagamento;
            BtnRegistrarPagamento.Padding = new Thickness(14, 0, 14, 0);
            AjustarColunaDoBotao(
                BtnRegistrarPagamento,
                UiCorrecoesPolicy.LarguraMinimaRegistrarPagamento,
                manterEstrela: true);
        }

        if (BtnConferirPagamentos != null)
        {
            BtnConferirPagamentos.MinWidth = UiCorrecoesPolicy.LarguraConferirPagamentos;
            BtnConferirPagamentos.Padding = new Thickness(12, 0, 12, 0);
            AjustarColunaDoBotao(
                BtnConferirPagamentos,
                UiCorrecoesPolicy.LarguraConferirPagamentos,
                manterEstrela: false);
        }
    }

    private static void AjustarColunaDoBotao(Button botao, double largura, bool manterEstrela)
    {
        if (botao.Parent is not Grid grid)
            return;

        var coluna = Grid.GetColumn(botao);
        if (coluna < 0 || coluna >= grid.ColumnDefinitions.Count)
            return;

        var definition = grid.ColumnDefinitions[coluna];
        definition.MinWidth = Math.Max(definition.MinWidth, largura);

        if (!manterEstrela)
            definition.Width = new GridLength(largura, GridUnitType.Pixel);
    }

    private static T? EncontrarAncestral<T>(DependencyObject child) where T : DependencyObject
    {
        var atual = child;
        while (atual != null)
        {
            atual = VisualTreeHelper.GetParent(atual);
            if (atual is T typed)
                return typed;
        }

        return null;
    }

    private static T? EncontrarFilhoVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var nested = EncontrarFilhoVisual<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static SolidColorBrush NovaCor(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
