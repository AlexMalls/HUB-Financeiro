using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace HubFinanceiro;

public partial class SelecionarDataWindow : Window
{
    public DateTime DataSelecionada { get; private set; }
    private int _cursorPosition = 0;
    private bool _isFormattingDate = false; // Flag para evitar recursão

    public SelecionarDataWindow()
    {
        InitializeComponent();
        
        // Define a data sugerida (próximo dia útil)
        DateTime dataSugerida = ObterProximoDiaUtil(DateTime.Now);
        DataTextBox.Text = dataSugerida.ToString("dd/MM/yyyy");
        DataSelecionada = dataSugerida;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    /// <summary>
    /// Calcula o próximo dia útil a partir de uma data
    /// </summary>
    private DateTime ObterProximoDiaUtil(DateTime data)
    {
        DateTime proximaData = data.AddDays(1);
        
        // Pula fins de semana
        while (proximaData.DayOfWeek == DayOfWeek.Saturday || 
               proximaData.DayOfWeek == DayOfWeek.Sunday)
        {
            proximaData = proximaData.AddDays(1);
        }
        
        // TODO: Adicionar verificação de feriados se necessário
        
        return proximaData;
    }

    /// <summary>
    /// Valida entrada apenas numérica
    /// </summary>
    private void DataTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Permite apenas números
        e.Handled = !IsNumeric(e.Text);
    }

    private bool IsNumeric(string text)
    {
        return Regex.IsMatch(text, "^[0-9]+$");
    }

    /// <summary>
    /// Formata o texto automaticamente enquanto digita (DD/MM/AAAA)
    /// </summary>
    private void DataTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isFormattingDate) return;

        try
        {
            _isFormattingDate = true;

            if (sender is not System.Windows.Controls.TextBox textBox)
                return;

            string texto = textBox.Text.Replace("/", ""); // Remove barras existentes
            int cursorPos = textBox.SelectionStart;

            if (string.IsNullOrWhiteSpace(texto))
            {
                textBox.Text = "";
                return;
            }

            // Remove caracteres não numéricos
            texto = new string(texto.Where(char.IsDigit).ToArray());
            
            // Limita a 8 dígitos (DDMMAAAA)
            if (texto.Length > 8)
                texto = texto.Substring(0, 8);

            // Formata conforme o usuário digita
            string textoFormatado = "";
            
            if (texto.Length >= 1)
            {
                // Primeiros 2 dígitos (DD)
                textoFormatado = texto.Substring(0, Math.Min(2, texto.Length));
                
                if (texto.Length >= 3)
                {
                    // Adiciona primeira barra após DD
                    textoFormatado += "/" + texto.Substring(2, Math.Min(2, texto.Length - 2));
                    
                    if (texto.Length >= 5)
                    {
                        // Adiciona segunda barra após MM
                        textoFormatado += "/" + texto.Substring(4);
                    }
                }
            }

            // Atualiza o texto apenas se mudou
            if (textBox.Text != textoFormatado)
            {
                int barrasAntes = textBox.Text.Substring(0, Math.Min(cursorPos, textBox.Text.Length)).Count(c => c == '/');
                
                textBox.Text = textoFormatado;
                
                int barrasDepois = textoFormatado.Substring(0, Math.Min(cursorPos, textoFormatado.Length)).Count(c => c == '/');
                int novoCursor = cursorPos + (barrasDepois - barrasAntes);
                
                // Ajusta posição do cursor
                textBox.SelectionStart = Math.Max(0, Math.Min(novoCursor, textoFormatado.Length));
            }
        }
        finally
        {
            _isFormattingDate = false;
        }
    }

    /// <summary>
    /// Trata o backspace para remover as barras também
    /// </summary>
    private void DataTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        if (e.Key == Key.Back)
        {
            int cursorPos = textBox.SelectionStart;
            
            // Se o cursor está logo após uma barra, remove a barra também
            if (cursorPos > 0 && textBox.Text.Length >= cursorPos)
            {
                if (cursorPos < textBox.Text.Length && textBox.Text[cursorPos - 1] == '/')
                {
                    string texto = textBox.Text.Remove(cursorPos - 1, 1);
                    textBox.Text = texto;
                    textBox.SelectionStart = cursorPos - 1;
                    e.Handled = true;
                }
            }
        }
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Provisionar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Valida e converte a data
            string textoData = DataTextBox.Text;
            
            if (string.IsNullOrWhiteSpace(textoData))
            {
                CustomMessageBox.ShowWarning("Por favor, informe uma data.");
                return;
            }

            // Tenta converter
            if (DateTime.TryParseExact(textoData, "dd/MM/yyyy", 
                System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, 
                out DateTime data))
            {
                // Verifica se é fim de semana
                if (data.DayOfWeek == DayOfWeek.Saturday || data.DayOfWeek == DayOfWeek.Sunday)
                {
                    var result = CustomMessageBox.ShowQuestion(
                        "A data selecionada é um fim de semana. Deseja prosseguir mesmo assim?",
                        "Atenção"
                    );
                    
                    if (result != HubFinanceiro.MessageBoxResult.Yes)
                        return;
                }

                // Verifica se a data é no passado
                if (data.Date < DateTime.Now.Date)
                {
                    var result = CustomMessageBox.ShowQuestion(
                        "A data selecionada é no passado. Deseja prosseguir mesmo assim?",
                        "Atenção"
                    );
                    
                    if (result != HubFinanceiro.MessageBoxResult.Yes)
                        return;
                }

                DataSelecionada = data;
                DialogResult = true;
                Close();
            }
            else
            {
                CustomMessageBox.ShowWarning("Data inválida. Use o formato DD/MM/AAAA.");
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao processar data: {ex.Message}");
        }
    }
}
