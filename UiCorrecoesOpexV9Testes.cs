using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HubFinanceiro;

public static class UiCorrecoesOpexV9Testes
{
    public static void Executar()
    {
        DeveManterLixeiraNoProprioTemplateOpex();
    }

    private static void DeveManterLixeiraNoProprioTemplateOpex()
    {
        if (Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            var conteudoTemplate = window.PagamentosItemsControl.ItemTemplate?.LoadContent() as DependencyObject;
            Assert(conteudoTemplate != null,
                "o template dos registros da O.P.E.X. deve existir");

            var lixeira = EncontrarDescendentePorTag<Button>(
                conteudoTemplate!,
                "OpexExcluirRegistro");

            Assert(lixeira != null,
                "a lixeira precisa fazer parte do próprio DataTemplate da linha, sem depender de injeção assíncrona após recarregamentos");
        }
        finally
        {
            window.Close();
        }
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
