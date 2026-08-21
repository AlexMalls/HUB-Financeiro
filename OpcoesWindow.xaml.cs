using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

/// <summary>
/// Janela de Opções do sistema
/// </summary>
public partial class OpcoesWindow : Window
{
    public OpcoesWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Fecha a janela
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
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
    /// Click no botão Geral (por enquanto não faz nada pois já está selecionado)
    /// </summary>
    private void GeralButton_Click(object sender, RoutedEventArgs e)
    {
        // Por enquanto só tem Geral, então não faz nada
        GeralContent.Visibility = Visibility.Visible;
    }
}
