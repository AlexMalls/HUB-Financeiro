using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HubFinanceiro;

public partial class MainWindow
{
    private bool _correcoesUiGeraisAplicadas;
    private bool _aplicandoFiltroFornecedorEmail;
    private TextBox? _pesquisaFornecedorEmailTextBox;
    private TextBlock? _pesquisaFornecedorEmailPlaceholder;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindow_CorrecoesUi_Loaded),
            true);
    }

    private static void MainWindow_CorrecoesUi_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._correcoesUiGeraisAplicadas)
            return;

        window._correcoesUiGeraisAplicadas = true;
        window.Dispatcher.BeginInvoke(
            new Action(window.AplicarCorrecoesUiGerais),
            DispatcherPriority.Loaded);
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
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        var raiz = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 2, 0)
        };

        var caixaPesquisa = new Border
        {
            Background = NovaCor("#2A2A2D"),
            BorderBrush = NovaCor("#45454D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 6, 10)
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

        var cabecalho = new Border
        {
            Background = NovaCor("#252526"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6, 12, 7),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = "Nome do Fornecedor",
                Foreground = NovaCor("#A8A8B0"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            }
        };
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

        // Caso o watcher de fornecedores recarregue a lista enquanto existe uma pesquisa,
        // reaplica o termo para não perder o filtro visual.
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

            toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
            toggle.HorizontalContentAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(0);
            toggle.Padding = new Thickness(12, 10, 12, 10);
            toggle.BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            toggle.BorderThickness = new Thickness(0, 0, 0, 1);
            toggle.IsEnabled = habilitado;
            toggle.Opacity = habilitado ? 1.0 : 0.34;
            toggle.Cursor = habilitado ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
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
