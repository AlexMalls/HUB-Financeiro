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
                Nome = "Fornecedor visual V12",
                Codigo = 120012,
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

            var superficie = EncontrarDescendentePorTag<Panel>(container!, "FornecedorLinhaV12Surface");
            Assert(superficie != null,
                "a linha inteira do fornecedor deve usar uma única superfície por trás do conteúdo e da lixeira");

            Assert(superficie!.Background is SolidColorBrush brush
                && brush.Color == (Color)ColorConverter.ConvertFromString("#992A2A2D"),
                "a área do fornecedor e a área da lixeira devem compartilhar exatamente o mesmo fundo");

            var lixeira = EncontrarDescendente<Button>(container!,
                botao => string.Equals(botao.ToolTip?.ToString(), "Excluir fornecedor", StringComparison.Ordinal));
            Assert(lixeira != null, "a lixeira do fornecedor deve continuar visível");
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
            throw new InvalidOperationException($"Falha no teste O.P.E.X. V12: {scenario}.");
    }
}
