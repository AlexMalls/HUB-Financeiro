using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class BaixarRegistroWindow : Window
{
    public DateTime DataSelecionada { get; private set; }

    public BaixarRegistroWindow(DateTime dataInicial)
    {
        InitializeComponent();
        DataSelecionada = dataInicial.Date;
        DataBaixaDatePicker.SelectedDate = DataSelecionada;
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!DataBaixaDatePicker.SelectedDate.HasValue)
        {
            CustomMessageBox.ShowWarning("Selecione uma data válida.");
            return;
        }

        DataSelecionada = DataBaixaDatePicker.SelectedDate.Value.Date;
        DialogResult = true;
        Close();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
