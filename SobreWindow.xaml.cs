using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class SobreWindow : Window
{
    public SobreWindow()
    {
        InitializeComponent();
        HubVisualStyleHelper.AplicarScrollBarPadrao(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version == null
            ? "Versão 1.0"
            : $"Versão {version.Major}.{version.Minor}.{version.Build}";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
