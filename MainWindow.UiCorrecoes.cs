using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class MainWindow
{
    private bool _correcoesUiGeraisAplicadas;
    private bool _aplicandoFiltroFornecedorEmail;
    private TextBox? _pesquisaFornecedorEmailTextBox;
    private TextBlock? _pesquisaFornecedorEmailPlaceholder;
    private Style? _fornecedorEmailOpexStyle;

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
        if (FornecedorItemsControl == null)
            return;

        _fornecedorEmailOpexStyle ??= CriarEstiloFornecedorEmailOpex();
        FornecedorItemsControl.ItemContainerGenerator.StatusChanged -= FornecedorEmailContainers_StatusChanged;
        FornecedorItemsControl.ItemContainerGenerator.StatusChanged += FornecedorEmailContainers_StatusChanged;

        InserirPesquisaFornecedorEmail();
        AplicarEstadoVisualFornecedoresEmail();
    }

    private void InserirPesquisaFornecedorEmail()
    {
        if (_pesquisaFornecedorEmailTextBox != null)
            return;

        var scrollViewer = EncontrarAncestral<ScrollViewer>(FornecedorItemsControl);
        if (scrollViewer?.Content is not UIElement conteudoOriginal)
            return;

        scrollViewer.Content = null;
        scrollViewer.Padding = new Thickness(0);
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        var raiz = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0)
        };

        var caixaPesquisa = new Border
        {
            Background = NovaCor("#2A2A2D"),
            BorderBrush = NovaCor("#45454D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var gridPesquisa = new Grid();
        _pesquisaFornecedorEmailTextBox = new TextBox
        {
            Height = 36,
            Background = Brushes.Transparent,
            Foreground = NovaCor("#F1F1F3"),
            CaretBrush = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(11, 0, 11, 0),
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
            Margin = new Thickness(11, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        gridPesquisa.Children.Add(_pesquisaFornecedorEmailTextBox);
        gridPesquisa.Children.Add(_pesquisaFornecedorEmailPlaceholder);
        caixaPesquisa.Child = gridPesquisa;
        raiz.Children.Add(caixaPesquisa);

        var cabecalho = new Grid
        {
            Height = UiCorrecoesPolicy.AlturaCabecalhoFornecedorEmail,
            Background = NovaCor("#992A2A2D")
        };
        cabecalho.Children.Add(new TextBlock
        {
            Text = UiCorrecoesPolicy.CabecalhoFornecedorEmail,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 10, 0)
        });
        raiz.Children.Add(cabecalho);

        FornecedorItemsControl.Margin = new Thickness(0);
        raiz.Children.Add(conteudoOriginal);
        scrollViewer.Content = raiz;
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
            FornecedorItemsControl.ItemsSource = UiCorrecoesPolicy.FiltrarFornecedoresEmail(
                _todosFornecedores,
                _pesquisaFornecedorEmailTextBox.Text);
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
        if (FornecedorItemsControl == null)
            return;

        _fornecedorEmailOpexStyle ??= CriarEstiloFornecedorEmailOpex();

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

            // O XAML antigo define margem e estilo localmente. Limpamos esses valores
            // para a linha assumir exatamente a geometria usada na tabela da O.P.E.X.
            toggle.ClearValue(FrameworkElement.MarginProperty);
            toggle.ClearValue(FrameworkElement.HeightProperty);
            toggle.ClearValue(Control.PaddingProperty);
            toggle.ClearValue(Control.BackgroundProperty);
            toggle.ClearValue(Control.BorderBrushProperty);
            toggle.ClearValue(Control.BorderThicknessProperty);
            toggle.ClearValue(Control.FontSizeProperty);
            toggle.ClearValue(Control.HorizontalContentAlignmentProperty);
            toggle.ClearValue(Control.VerticalContentAlignmentProperty);
            toggle.Style = _fornecedorEmailOpexStyle;

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

    private static Style CriarEstiloFornecedorEmailOpex()
    {
        var template = new ControlTemplate(typeof(ToggleButton));

        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        borda.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        borda.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        borda.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        borda.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
        conteudo.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        conteudo.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
        conteudo.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        conteudo.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        borda.AppendChild(conteudo);
        template.VisualTree = borda;

        var estilo = new Style(typeof(ToggleButton));
        estilo.Setters.Add(new Setter(FrameworkElement.HeightProperty, UiCorrecoesPolicy.AlturaLinhaFornecedorEmail));
        estilo.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        estilo.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        estilo.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(77, 45, 45, 48))));
        estilo.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        estilo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(20, 0, 10, 0)));
        estilo.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        estilo.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        estilo.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        estilo.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        estilo.Setters.Add(new Setter(Control.TemplateProperty, template));

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, NovaCor("#2A2A2D")));
        estilo.Triggers.Add(hover);

        var selecionado = new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true
        };
        selecionado.Setters.Add(new Setter(Control.BackgroundProperty, NovaCor("#3D2354")));
        selecionado.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        estilo.Triggers.Add(selecionado);

        return estilo;
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
