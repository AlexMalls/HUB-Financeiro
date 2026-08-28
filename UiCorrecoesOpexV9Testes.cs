using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HubFinanceiro;

public static class UiCorrecoesOpexV9Testes
{
    public static void Executar()
    {
        DeveManterLixeiraEstavelAoRecarregarRegistrosOpex();
        DeveUsarTemplateProprioNoMenuAcoesOpex();
    }

    private static void DeveManterLixeiraEstavelAoRecarregarRegistrosOpex()
    {
        if (Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            var primeiro = CriarPagamentoTeste(991, "Fornecedor teste V9 A", 250.75m);
            var segundo = CriarPagamentoTeste(992, "Fornecedor teste V9 B", 410.20m);

            window.OpexLayoutGrid.Visibility = Visibility.Visible;
            window.PagamentosItemsControl.ItemsSource = new[] { primeiro };
            PrepararLayout(window);

            var primeiraLixeira = EncontrarLixeiraDaPrimeiraLinha(window);
            Assert(primeiraLixeira != null,
                "a lixeira deve existir assim que a primeira linha for gerada, sem depender de Dispatcher assíncrono");
            Assert(ReferenceEquals(primeiraLixeira!.DataContext, primeiro),
                "a primeira lixeira deve apontar para o registro correto");

            window.PagamentosItemsControl.ItemsSource = new[] { segundo };
            PrepararLayout(window);

            var segundaLixeira = EncontrarLixeiraDaPrimeiraLinha(window);
            Assert(segundaLixeira != null,
                "a lixeira deve continuar existindo imediatamente após recarregar a lista");
            Assert(ReferenceEquals(segundaLixeira!.DataContext, segundo),
                "a lixeira recriada deve apontar para o novo registro sem depender de reinjeção posterior");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveUsarTemplateProprioNoMenuAcoesOpex()
    {
        var window = new MainWindow();
        try
        {
            var grupo = EncontrarDescendentePorTag<StackPanel>(window.OpexInputsGrid, "OpexV8Actions");
            Assert(grupo != null && grupo.Children.Count == 2,
                "o grupo compacto de ações O.P.E.X. deve existir");

            var botaoAcoes = grupo!.Children[1] as Button;
            Assert(botaoAcoes?.ContextMenu != null,
                "o botão Ações O.P.E.X. deve possuir menu flutuante");

            var menu = botaoAcoes!.ContextMenu!;
            Assert(menu.ReadLocalValue(Control.TemplateProperty) != DependencyProperty.UnsetValue,
                "o ContextMenu deve usar template próprio do HUB em vez do chrome padrão do Windows");

            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                var temTemplateProprio = item.Style?.Setters
                    .OfType<Setter>()
                    .Any(setter => setter.Property == Control.TemplateProperty) == true;

                Assert(temTemplateProprio,
                    $"a opção '{item.Header}' deve usar template próprio, sem faixa de ícones/check padrão do Windows");
            }

            var accent = ((SolidColorBrush)window.FindResource("AccentColor")).Color;
            Assert(menu.BorderBrush is SolidColorBrush borda && borda.Color == accent,
                "o menu deve usar o contorno de destaque roxo do HUB");
        }
        finally
        {
            window.Close();
        }
    }

    private static PrevisaoPagamento CriarPagamentoTeste(int id, string fornecedor, decimal valor)
    {
        return new PrevisaoPagamento
        {
            Id = id,
            CodigoFornecedor = id,
            NomeFornecedor = fornecedor,
            Natureza = 20805,
            TipoPagamento = 3,
            Valor = valor,
            DataPagamento = new DateTime(2026, 8, 28),
            Empresa = "ADM",
            Status = "Pendente"
        };
    }

    private static void PrepararLayout(MainWindow window)
    {
        window.OpexLayoutGrid.Measure(new Size(1100, 650));
        window.OpexLayoutGrid.Arrange(new Rect(0, 0, 1100, 650));
        window.OpexLayoutGrid.UpdateLayout();
    }

    private static Button? EncontrarLixeiraDaPrimeiraLinha(MainWindow window)
    {
        var container = window.PagamentosItemsControl.ItemContainerGenerator.ContainerFromIndex(0);
        if (container == null)
            return null;

        return EncontrarDescendentePorTag<Button>(container, "OpexExcluirRegistro");
    }

    private static T? EncontrarDescendentePorTag<T>(DependencyObject raiz, object tag)
        where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T elemento && Equals(elemento.Tag, tag))
                return elemento;

            var encontrado = EncontrarDescendentePorTag<T>(filho, tag);
            if (encontrado != null)
                return encontrado;
        }

        return null;
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste O.P.E.X. V9: {scenario}.");
    }
}
