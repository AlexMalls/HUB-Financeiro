using System.Windows;
using System.Windows.Media;

namespace HubFinanceiro;

/// <summary>
/// Gerenciador de temas da aplicação
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// Aplica o tema Dark Mode
    /// </summary>
    public static void ApplyDarkTheme()
    {
        var resources = Application.Current.Resources;

        // Cores de fundo
        resources["BackgroundDark"] = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        resources["BackgroundMedium"] = new SolidColorBrush(Color.FromRgb(37, 37, 38));
        resources["BackgroundLight"] = new SolidColorBrush(Color.FromRgb(42, 42, 45));
        
        // Cores de texto
        resources["TextColor"] = new SolidColorBrush(Colors.White);
        
        // Bordas
        resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(45, 45, 48));
        
        // Cores de acento permanecem as mesmas (roxo)
        // PrimaryColor e AccentColor não mudam
        
        System.Diagnostics.Debug.WriteLine("Dark Theme aplicado");
    }

    /// <summary>
    /// Aplica o tema White Mode
    /// </summary>
    public static void ApplyWhiteTheme()
    {
        var resources = Application.Current.Resources;

        // Cores de fundo
        resources["BackgroundDark"] = new SolidColorBrush(Color.FromRgb(245, 245, 245));      // Branco gelo
        resources["BackgroundMedium"] = new SolidColorBrush(Color.FromRgb(230, 230, 230));    // Cinza claro
        resources["BackgroundLight"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));     // Branco puro
        
        // Cores de texto
        resources["TextColor"] = new SolidColorBrush(Color.FromRgb(30, 30, 30));              // Preto
        
        // Bordas
        resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(200, 200, 200));         // Cinza médio
        
        // Cores de acento permanecem as mesmas (roxo)
        // PrimaryColor e AccentColor não mudam
        
        System.Diagnostics.Debug.WriteLine("White Theme aplicado");
    }
}
