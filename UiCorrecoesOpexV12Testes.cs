using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HubFinanceiro;

public static class UiCorrecoesOpexV12Testes
{
    public static void Executar()
    {
        GarantirAplicacaoWpf();
        DeveUsarSeletorDeDataPadraoHubNaBaixa();
        DeveManterDataInicialEFormatacaoDoHub();
        DeveUsarUmaUnicaSuperficieNaLinhaDeFornecedor();
        DeveRemoverBordaEReaplicarEstadoNeutroDoCard();
        DevePreservarRoxoNoFornecedorSelecionado();
    }

    private static void GarantirAplicacaoWpf()
    {
        if (Application.Current != null)
            return;

        var app = new App();
        app.InitializeComponent();
    }

    private static void DeveUsarSeletorDeDataPadraoHubNaBaixa()
    {
        var janela = new BaixarRegistroWindow(new DateTime(2026, 9, 15));
        try
        {
            Assert(janela.FindName("DataBaixaDatePicker") is HubDatePicker,
                "a janela Baixar Registro deve usar o seletor de data reutilizável do HUB");
        }
        finally
        {
            janela.Close();
        }
    }

    private static void DeveManterDataInicialEFormatacaoDoHub()
    {
        var seletor = new HubDatePicker
        {
            SelectedDate = new DateTime(2026, 9, 15)
        };

        seletor.Measure(new Size(360, 80));
        seletor.Arrange(new Rect(0, 0, 360, 80));
        seletor.UpdateLayout();

        Assert(seletor.SelectedDate == new DateTime(2026, 9, 15),
            "o seletor compartilhado deve preservar a data selecionada");
        Assert(seletor.TextoData == "15/09/2026",
            "o seletor compartilhado deve exibir datas no padrão dd/MM/yyyy");
    }

    private static void DeveUsarUmaUnicaSuperficieNaLinhaDeFornecedor()
    {
        var window = new MainWindow();
        try
        {
            var fornecedor = new Fornecedor
            {
                Nome = "Fornecedor visual V13",
                Codigo = 130013,
                Ativo = true,
                Administradora = true
            };

            window.FornecedoresLayoutGrid.Visibility = Visibility.Visible;
            window.FornecedoresItemsControl.ItemsSource = new[] { fornecedor };
            window.FornecedoresLayoutGrid.Measure(new Size(1000, 650));
            window.FornecedoresLayoutGrid.Arrange(new Rect(0, 0, 1000, 650));
            window.FornecedoresLayoutGrid.UpdateLayout();

            var container = window.FornecedoresItemsControl.ItemContainerGenerator.ContainerFromIndex(0);
            Assert(container != null, "a lista de fornecedores deve gerar uma linha visual");

            var card = EncontrarDescendente<Border>(container!,
                border => string.Equals(border.Name, "FornecedorBorder", StringComparison.Ordinal));
            Assert(card != null, "cada fornecedor deve manter o card FornecedorBorder como superfície única");
            Assert(card!.CornerRadius == new CornerRadius(10),
                "o card do fornecedor deve ter os cantos arredondados do mock aprovado");
            Assert(card.Background is SolidColorBrush fundoCard
                && fundoCard.Color.A == 0,
                "o card do fornecedor não deve desenhar um fundo próprio diferente do fundo da lista");

            var lixeiraDentroDoCard = EncontrarDescendente<Button>(card,
                botao => string.Equals(botao.ToolTip?.ToString(), "Excluir fornecedor", StringComparison.Ordinal));
            Assert(lixeiraDentroDoCard != null,
                "a lixeira deve ficar dentro do mesmo card do fornecedor, e não em uma área lateral separada");

            Assert(lixeiraDentroDoCard!.Background is SolidColorBrush fundoLixeira
                && fundoLixeira.Color.A == 0,
                "a lixeira não deve desenhar uma segunda superfície sobre o card do fornecedor");

            var superficieLegada = EncontrarDescendentePorTag<Panel>(container!, "FornecedorLinhaV12Surface");
            Assert(superficieLegada == null,
                "a linha não deve manter a superfície lateral legada da V12");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveRemoverBordaEReaplicarEstadoNeutroDoCard()
    {
        var window = new MainWindow();
        try
        {
            var fornecedor = new Fornecedor
            {
                Nome = "Fornecedor visual V15",
                Codigo = 150015,
                Ativo = true,
                Administradora = true
            };

            window.FornecedoresLayoutGrid.Visibility = Visibility.Visible;
            window.FornecedoresItemsControl.ItemsSource = new[] { fornecedor };
            window.FornecedoresLayoutGrid.Measure(new Size(1000, 650));
            window.FornecedoresLayoutGrid.Arrange(new Rect(0, 0, 1000, 650));
            window.FornecedoresLayoutGrid.UpdateLayout();

            var container = window.FornecedoresItemsControl.ItemContainerGenerator.ContainerFromIndex(0);
            Assert(container != null, "a lista precisa manter o container do fornecedor após atualização de layout");

            var card = EncontrarDescendente<Border>(container!,
                border => string.Equals(border.Name, "FornecedorBorder", StringComparison.Ordinal));
            Assert(card != null, "o card do fornecedor deve continuar localizado após nova atualização de layout");
            Assert(card!.BorderThickness == new Thickness(0, 0, 0, 1),
                "a estrutura legada pode manter 1 px inferior sem produzir separador visual");
            Assert(card.BorderBrush is SolidColorBrush borda && borda.Opacity == 0,
                "a borda inferior deve ter opacidade zero para não existir linha visível entre fornecedores");
            Assert(card.Background is SolidColorBrush fundo && fundo.Color.A == 0,
                "ao deselecionar o fornecedor o card deve voltar ao estado neutro transparente");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DevePreservarRoxoNoFornecedorSelecionado()
    {
        var window = new MainWindow();
        try
        {
            var fornecedor = new Fornecedor
            {
                Nome = "Fornecedor visual V17",
                Codigo = 170017,
                Ativo = true,
                Administradora = true
            };

            window.FornecedoresLayoutGrid.Visibility = Visibility.Visible;
            window.FornecedoresItemsControl.ItemsSource = new[] { fornecedor };
            window.FornecedoresLayoutGrid.Measure(new Size(1000, 650));
            window.FornecedoresLayoutGrid.Arrange(new Rect(0, 0, 1000, 650));
            window.FornecedoresLayoutGrid.UpdateLayout();

            var container = window.FornecedoresItemsControl.ItemContainerGenerator.ContainerFromIndex(0);
            Assert(container != null, "a lista deve gerar o fornecedor usado no teste de seleção");

            var card = EncontrarDescendente<Border>(container!,
                border => string.Equals(border.Name, "FornecedorBorder", StringComparison.Ordinal));
            Assert(card != null, "o card deve existir para validar a seleção roxa");

            var campoSelecao = typeof(MainWindow).GetField(
                "_fornecedorItemSelecionado",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var metodoAplicar = typeof(MainWindow).GetMethod(
                "AplicarExclusaoIntegradaFornecedoresV13",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert(campoSelecao != null && metodoAplicar != null,
                "a V17 deve sincronizar o estado visual com a seleção atual");

            campoSelecao!.SetValue(window, card);
            metodoAplicar!.Invoke(window, null);

            Assert(card!.Background is SolidColorBrush selecionado
                && selecionado.Color == (Color)ColorConverter.ConvertFromString("#5E17AA"),
                "o fornecedor selecionado deve permanecer com o card inteiro roxo");

            campoSelecao.SetValue(window, null);
            metodoAplicar.Invoke(window, null);

            Assert(card.Background is SolidColorBrush neutro && neutro.Color.A == 0,
                "ao deselecionar o fornecedor o card deve voltar ao fundo transparente");
        }
        finally
        {
            window.Close();
        }
    }

    private static T? EncontrarDescendentePorTag<T>(DependencyObject raiz, object tag)
        where T : FrameworkElement
        => EncontrarDescendente<T>(raiz, elemento => Equals(elemento.Tag, tag));

    private static T? EncontrarDescendente<T>(DependencyObject raiz, Func<T, bool> predicado)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T elemento && predicado(elemento))
                return elemento;

            var encontrado = EncontrarDescendente<T>(filho, predicado);
            if (encontrado != null)
                return encontrado;
        }

        return null;
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste O.P.E.X. V17: {scenario}.");
    }
}
