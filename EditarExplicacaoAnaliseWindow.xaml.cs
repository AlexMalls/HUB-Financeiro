using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class EditarExplicacaoAnaliseWindow : Window
{
    public string Explicacao { get; private set; } = string.Empty;
    public bool ExplicacaoRecorrente { get; private set; }

    public EditarExplicacaoAnaliseWindow(
        string? beneficiario,
        string? certificado,
        string? explicacaoAtual,
        bool explicacaoRecorrente)
    {
        InitializeComponent();

        string nome = string.IsNullOrWhiteSpace(beneficiario) ? "Cliente não identificado" : beneficiario.Trim();
        string codigo = string.IsNullOrWhiteSpace(certificado) ? "sem certificado" : certificado.Trim();
        ClienteText.Text = $"{nome}  •  {codigo}";
        ExplicacaoTextBox.Text = explicacaoAtual ?? string.Empty;
        ExplicacaoRecorrenteCheckBox.IsChecked = explicacaoRecorrente;

        Loaded += (_, _) =>
        {
            ExplicacaoTextBox.Focus();
            ExplicacaoTextBox.CaretIndex = ExplicacaoTextBox.Text.Length;
        };
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        string texto = ExplicacaoTextBox.Text.Trim();
        bool recorrente = ExplicacaoRecorrenteCheckBox.IsChecked == true;

        if (recorrente && string.IsNullOrWhiteSpace(texto))
        {
            CustomMessageBox.ShowWarning(
                "Escreva uma explicação antes de marcá-la como recorrente.",
                "Explicação recorrente");
            ExplicacaoTextBox.Focus();
            return;
        }

        Explicacao = texto;
        ExplicacaoRecorrente = recorrente;
        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
