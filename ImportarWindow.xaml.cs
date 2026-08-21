using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

/// <summary>
/// Tipos de importação disponíveis
/// </summary>
public enum TipoImportacao
{
    Nenhum,
    Lote,
    Nota
}

/// <summary>
/// Janela de seleção do tipo de importação da O.P.E.X
/// </summary>
public partial class ImportarWindow : Window
{
    /// <summary>
    /// Tipo de importação escolhido pelo usuário.
    /// Preenchido antes de DialogResult = true.
    /// </summary>
    public TipoImportacao TipoImportacao { get; private set; } = TipoImportacao.Nenhum;

    /// <summary>
    /// Caminho completo do arquivo selecionado pelo usuário.
    /// </summary>
    public string CaminhoArquivo { get; private set; } = string.Empty;

    public ImportarWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Fecha a janela sem selecionar nada
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Permite arrastar a janela
    /// </summary>
    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    /// <summary>
    /// Opção: Importar Lote — abre seleção de arquivo Excel
    /// </summary>
    private void ImportarLote_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title       = "Selecionar lote de pagamentos (.xlsx)",
            Filter      = "Arquivos Excel (*.xlsx)|*.xlsx",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        TipoImportacao  = TipoImportacao.Lote;
        CaminhoArquivo  = dialog.FileName;
        DialogResult    = true;
        Close();
    }

    /// <summary>
    /// Opção: Importar Nota — abre seleção de arquivo PDF
    /// </summary>
    private void ImportarNota_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title       = "Selecionar nota fiscal (.pdf)",
            Filter      = "Arquivos PDF (*.pdf)|*.pdf",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        TipoImportacao  = TipoImportacao.Nota;
        CaminhoArquivo  = dialog.FileName;
        DialogResult    = true;
        Close();
    }
}
