using System.Windows;
using System.Windows.Controls;

namespace HubFinanceiro;

internal static class HubVisualStyleHelper
{
    public static void AplicarScrollBarPadrao(Window target)
    {
        var origem = Application.Current?.MainWindow;
        if (origem?.TryFindResource(typeof(ScrollBar)) is Style style)
            target.Resources[typeof(ScrollBar)] = style;
    }

    public static void AplicarCheckBoxPadrao(CheckBox checkBox)
    {
        var origem = Application.Current?.MainWindow;
        if (origem?.TryFindResource("CustomCheckBoxStyle") is Style style)
        {
            checkBox.Style = style;
            checkBox.ClearValue(FrameworkElement.WidthProperty);
            checkBox.ClearValue(FrameworkElement.HeightProperty);
        }
    }
}
