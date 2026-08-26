using System.Windows;
using System.Windows.Media;
using System.Linq;

namespace HubFinanceiro;

/// <summary>
/// Tipos de mensagem para o CustomMessageBox
/// </summary>
public enum MessageBoxType
{
    Information,
    Warning,
    Error,
    Question,
    Success
}

/// <summary>
/// Resultado do CustomMessageBox
/// </summary>
public enum MessageBoxResult
{
    OK,
    Yes,
    No
}

/// <summary>
/// MessageBox customizado com o visual do programa
/// </summary>
public partial class CustomMessageBox : Window
{
    public MessageBoxResult Result { get; private set; }

    private CustomMessageBox(
        string message,
        string title,
        MessageBoxType type,
        bool showYesNo,
        string? detail = null,
        string yesText = "Sim",
        string noText = "Não")
    {
        InitializeComponent();
        
        // Configura o título
        TitleTextBlock.Text = title;
        
        // Configura a mensagem principal
        MessageTextBlock.Text = message;
        
        // Se tiver detalhe (como nome do fornecedor), mostra em destaque
        if (!string.IsNullOrEmpty(detail))
        {
            MessageDetailTextBlock.Text = detail;
            MessageDetailTextBlock.Visibility = Visibility.Visible;
        }
        
        // Configura o ícone baseado no tipo
        ConfigurarIcone(type);
        
        // Configura os botões
        if (showYesNo)
        {
            YesButton.Content = yesText;
            NoButton.Content = noText;
            YesButton.Visibility = Visibility.Visible;
            NoButton.Visibility = Visibility.Visible;
            OkButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;
            OkButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Configura o ícone baseado no tipo de mensagem
    /// </summary>
    private void ConfigurarIcone(MessageBoxType type)
    {
        switch (type)
        {
            case MessageBoxType.Information:
                IconTextBlock.Text = "ℹ";
                IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(121, 32, 220)); // Roxo
                break;
            case MessageBoxType.Warning:
                IconTextBlock.Text = "⚠";
                IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Laranja
                break;
            case MessageBoxType.Error:
                IconTextBlock.Text = "✕";
                IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28)); // Vermelho
                break;
            case MessageBoxType.Question:
                IconTextBlock.Text = "?";
                IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(121, 32, 220)); // Roxo
                break;
            case MessageBoxType.Success:
                IconTextBlock.Text = "✓";
                IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Verde
                break;
        }
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        DialogResult = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = false;
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        DialogResult = true;
        Close();
    }

    #region Métodos Estáticos

    /// <summary>
    /// Mostra uma mensagem de informação
    /// </summary>
    public static void ShowInformation(string message, string title = "Informação")
    {
        var msgBox = new CustomMessageBox(message, title, MessageBoxType.Information, false);
        
        // Define o Owner como a janela ativa ou MainWindow
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (activeWindow != null)
            msgBox.Owner = activeWindow;
        else if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            msgBox.Owner = Application.Current.MainWindow;
        
        msgBox.ShowDialog();
    }

    /// <summary>
    /// Mostra uma mensagem de aviso
    /// </summary>
    public static void ShowWarning(string message, string title = "Aviso")
    {
        var msgBox = new CustomMessageBox(message, title, MessageBoxType.Warning, false);
        
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (activeWindow != null)
            msgBox.Owner = activeWindow;
        else if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            msgBox.Owner = Application.Current.MainWindow;
        
        msgBox.ShowDialog();
    }

    /// <summary>
    /// Mostra uma mensagem de erro
    /// </summary>
    public static void ShowError(string message, string title = "Erro")
    {
        var msgBox = new CustomMessageBox(message, title, MessageBoxType.Error, false);
        
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (activeWindow != null)
            msgBox.Owner = activeWindow;
        else if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            msgBox.Owner = Application.Current.MainWindow;
        
        msgBox.ShowDialog();
    }

    /// <summary>
    /// Mostra uma pergunta com Sim/Não
    /// </summary>
    public static MessageBoxResult ShowQuestion(
        string message,
        string title = "Confirmação",
        string? detail = null,
        string yesText = "Sim",
        string noText = "Não")
    {
        var msgBox = new CustomMessageBox(message, title, MessageBoxType.Question, true, detail, yesText, noText);
        
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (activeWindow != null)
            msgBox.Owner = activeWindow;
        else if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            msgBox.Owner = Application.Current.MainWindow;
        
        msgBox.ShowDialog();
        return msgBox.Result;
    }

    /// <summary>
    /// Mostra uma mensagem de sucesso
    /// </summary>
    public static void ShowSuccess(string message, string title = "Sucesso")
    {
        var msgBox = new CustomMessageBox(message, title, MessageBoxType.Success, false);
        
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (activeWindow != null)
            msgBox.Owner = activeWindow;
        else if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            msgBox.Owner = Application.Current.MainWindow;
        
        msgBox.ShowDialog();
    }

    #endregion
}
