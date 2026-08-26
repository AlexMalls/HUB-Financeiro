using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class InfosPositivaWindow : Window
{
    public InfosPositivaWindow()
    {
        InitializeComponent();
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
