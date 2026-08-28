using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class MainWindow
{
    private bool _aplicandoFiltroFornecedorEmail;

    private void AplicarCorrecoesUiGerais()
    {
        ConfigurarListaFornecedoresEmail();
        AjustarBotoesOpex();
    }

    private void ConfigurarListaFornecedoresEmail()
    {
        FornecedorItemsControl.ItemContainerGenerator.StatusChanged -= FornecedorEmailContainers_StatusChanged;
        FornecedorItemsControl.ItemContainerGenerator.StatusChanged += FornecedorEmailContainers_StatusChanged;
        AplicarEstadoVisualFornecedoresEmail();
    }

    private void PesquisaFornecedorEmail_TextChanged(object sender, TextChangedEventArgs e)
    {
        FornecedorEmailPesquisaPlaceholder.Visibility =
            string.IsNullOrEmpty(FornecedorEmailPesquisaTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

        AplicarFiltroFornecedorEmail();
    }

    private void AplicarFiltroFornecedorEmail()
    {
        if (_aplicandoFiltroFornecedorEmail)
            return;

        try
        {
            _aplicandoFiltroFornecedorEmail = true;
            var resultado = UiCorrecoesPolicy.FiltrarFornecedoresEmail(
                _todosFornecedores,
                FornecedorEmailPesquisaTextBox.Text);

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

        if (!_aplicandoFiltroFornecedorEmail
            && !string.IsNullOrWhiteSpace(FornecedorEmailPesquisaTextBox.Text))
        {
            var termo = FornecedorEmailPesquisaTextBox.Text.Trim();
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
}
