using System.Windows;

namespace HubFinanceiro;

public partial class MainWindow
{
    private void InfosPositivaMenu_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;

        var window = new InfosPositivaWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void SobreMenu_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;

        var window = new SobreWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }
}
