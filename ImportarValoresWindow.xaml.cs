using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace HubFinanceiro;

public partial class ImportarValoresWindow : Window
{
    public List<decimal> ValoresImportados { get; private set; } = new List<decimal>();
    public bool Importado { get; private set; } = false;
    
    public ImportarValoresWindow()
    {
        InitializeComponent();
        
        // Foca no TextBox quando abrir
        Loaded += (s, e) => ValoresTextBox.Focus();
    }
    
    /// <summary>
    /// Processa o texto colado e extrai os valores numéricos
    /// </summary>
    private void ImportarValores_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string texto = ValoresTextBox.Text;
            
            if (string.IsNullOrWhiteSpace(texto))
            {
                CustomMessageBox.ShowWarning("Por favor, cole os valores antes de importar.");
                return;
            }
            
            // Processa o texto e extrai valores
            ValoresImportados = ProcessarTextoExcel(texto);
            
            if (ValoresImportados.Count == 0)
            {
                CustomMessageBox.ShowWarning("Nenhum valor numérico foi encontrado no texto colado.");
                return;
            }
            
            Importado = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao processar valores: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Processa o texto colado do Excel e extrai todos os valores numéricos.
    /// Usa cultura pt-BR (vírgula = decimal, ponto = milhares).
    /// Aceita: "R$ 4.283,35" → 4283.35 | "65,22" → 65.22 | "111.00" → 111.00
    /// </summary>
    private List<decimal> ProcessarTextoExcel(string texto)
    {
        var valores = new List<decimal>();
        var culturaBR = CultureInfo.GetCultureInfo("pt-BR");
        
        // Remove símbolos de moeda
        texto = texto.Replace("R$", "")
                    .Replace("$", "")
                    .Replace("BRL", "")
                    .Trim();
        
        // Regex: captura sequências que parecem números (com pontos e vírgulas)
        // Ex: "4.283,35" | "65,22" | "111,00" | "123.45"
        var pattern = @"[\d]+(?:[.,][\d]+)*";
        var matches = Regex.Matches(texto, pattern);
        
        foreach (Match match in matches)
        {
            string raw = match.Value;
            decimal valor = 0;
            bool parseOk = false;

            // Caso 1: tem PONTO e VÍRGULA (ex: "4.283,35") → formato BR
            if (raw.Contains(".") && raw.Contains(","))
            {
                // Remove os pontos de milhar e usa a vírgula como decimal
                string normalizado = raw.Replace(".", "");
                parseOk = decimal.TryParse(normalizado, NumberStyles.Number, culturaBR, out valor);
            }
            // Caso 2: só VÍRGULA (ex: "65,22") → decimal BR
            else if (raw.Contains(",") && !raw.Contains("."))
            {
                parseOk = decimal.TryParse(raw, NumberStyles.Number, culturaBR, out valor);
            }
            // Caso 3: só PONTO (ex: "111.00" ou "4283.35") → decimal americano
            else if (raw.Contains(".") && !raw.Contains(","))
            {
                parseOk = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
            }
            // Caso 4: número inteiro (ex: "123")
            else
            {
                parseOk = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
            }

            if (parseOk && valor > 0)
            {
                System.Diagnostics.Debug.WriteLine($"✅ Importado: {raw} → {valor:N2}");
                valores.Add(valor);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Não parseou: {raw}");
            }
        }
        
        return valores;
    }
    
    private void Fechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
