using System.Windows;

namespace HubFinanceiro;

public partial class IncluirAnaliseFaturaWindow
{
    private bool _defaultsUiAplicados;

    static IncluirAnaliseFaturaWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(IncluirAnaliseFaturaWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(IncluirAnaliseFaturaWindow_CorrecoesUi_Loaded),
            true);
    }

    private static void IncluirAnaliseFaturaWindow_CorrecoesUi_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IncluirAnaliseFaturaWindow window || window._defaultsUiAplicados)
            return;

        window._defaultsUiAplicados = true;
        window.IgnorarClientesCanceladosCheckBox.IsChecked = UiCorrecoesPolicy.IgnorarClientesCanceladosPorPadrao;
    }
}
