using System;
using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

/// <summary>
/// Janela de confirmação da importação de nota fiscal.
/// Exibe os dados interpretados e permite ao usuário editar
/// a data de pagamento antes de confirmar.
/// </summary>
public partial class ConfirmarNotaWindow : Window
{
    /// <summary>
    /// Data de pagamento final (pode ter sido editada pelo usuário).
    /// </summary>
    public DateTime DataPagamentoFinal { get; private set; }

    /// <summary>
    /// True se o usuário confirmou a importação.
    /// </summary>
    public bool Confirmado { get; private set; }

    private bool _editandoData = false;
    private bool _formatandoData = false;

    public ConfirmarNotaWindow(
        string numeroNota,
        string dataEmissao,
        string nomeFornecedor,
        int    codigoFornecedor,
        decimal valor,
        DateTime dataPagamento,
        string empresa)
    {
        InitializeComponent();

        DataPagamentoFinal = dataPagamento;

        TitleTextBlock.Text         = $"Importar Nota — Nº {numeroNota}";
        NumeroNotaTextBlock.Text    = numeroNota;
        DataEmissaoTextBlock.Text   = dataEmissao.Length >= 10 ? dataEmissao[..10] : dataEmissao;
        FornecedorTextBlock.Text    = $"{nomeFornecedor}  (cód. {codigoFornecedor})";
        ValorTextBlock.Text         = $"R$ {valor:N2}";
        EmpresaTextBlock.Text       = empresa;
        DataPagamentoTextBlock.Text = dataPagamento.ToString("dd/MM/yyyy");
    }

    // ── Lógica de edição da data ─────────────────────────────────

    private void DataLabel_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        EntrarModoEdicaoData();
    }

    private void EntrarModoEdicaoData()
    {
        _editandoData = true;
        DataLabelPanel.Visibility  = Visibility.Collapsed;
        DataEditBorder.Visibility  = Visibility.Visible;
        DataPagamentoTextBox.Text  = DataPagamentoFinal.ToString("dd/MM/yyyy");
        DataPagamentoTextBox.SelectAll();
        DataPagamentoTextBox.Focus();
    }

    private void SairModoEdicaoData(bool aplicar)
    {
        if (!_editandoData) return;
        _editandoData = false;

        if (aplicar && TentarParsearData(DataPagamentoTextBox.Text, out DateTime nova))
        {
            DataPagamentoFinal          = nova;
            DataPagamentoTextBlock.Text = nova.ToString("dd/MM/yyyy");
        }
        else if (aplicar)
        {
            // Data inválida — restaura o valor anterior sem travar
            DataPagamentoTextBox.Text = DataPagamentoFinal.ToString("dd/MM/yyyy");
        }

        DataEditBorder.Visibility  = Visibility.Collapsed;
        DataLabelPanel.Visibility  = Visibility.Visible;
    }

    private static bool TentarParsearData(string texto, out DateTime resultado)
        => DateTime.TryParseExact(
                texto.Trim(),
                new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy" },
                System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                System.Globalization.DateTimeStyles.None,
                out resultado);

    private void DataTextBox_LostFocus(object sender, RoutedEventArgs e)
        => SairModoEdicaoData(aplicar: true);

    private void DataTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { SairModoEdicaoData(aplicar: true);  e.Handled = true; }
        if (e.Key == Key.Escape) { SairModoEdicaoData(aplicar: false); e.Handled = true; }
    }

    private void DataTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_formatandoData) return;

        var box  = DataPagamentoTextBox;
        string t = box.Text.Replace("/", "");

        if (!System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d*$"))
        {
            // Remove caracteres não numéricos
            _formatandoData = true;
            box.Text = new string(System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Where(box.Text, char.IsDigit)));
            box.CaretIndex = box.Text.Length;
            _formatandoData = false;
            return;
        }

        _formatandoData = true;
        string formatado = t.Length switch
        {
            >= 5 => t[..2] + "/" + t[2..4] + "/" + t[4..],
            >= 3 => t[..2] + "/" + t[2..],
            _    => t
        };
        box.Text = formatado;
        box.CaretIndex = formatado.Length;
        _formatandoData = false;
    }

    // ── Botões ───────────────────────────────────────────────────

    private void Sim_Click(object sender, RoutedEventArgs e)
    {
        // Se estiver editando, aplica antes de confirmar
        if (_editandoData)
            SairModoEdicaoData(aplicar: true);

        Confirmado   = true;
        DialogResult = true;
        Close();
    }

    private void Nao_Click(object sender, RoutedEventArgs e)
    {
        Confirmado   = false;
        DialogResult = false;
        Close();
    }
}
