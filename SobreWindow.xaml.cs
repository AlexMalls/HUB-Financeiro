using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HubFinanceiro;

public partial class SobreWindow : Window
{
    public SobreWindow()
    {
        InitializeComponent();
        HubVisualStyleHelper.AplicarScrollBarPadrao(this);
        Loaded += SobreWindow_Loaded;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version == null
            ? "Versão 1.0"
            : $"Versão {version.Major}.{version.Minor}.{version.Build}";
    }

    private void SobreWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SobreWindow_Loaded;
        CorrigirLogoEnnoia();
    }

    private void CorrigirLogoEnnoia()
    {
        try
        {
            var image = EncontrarFilhoVisual<Image>(this);
            if (image == null)
                return;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(
                "pack://application:,,,/HubFinanceiro;component/midia/ennoia-logo.png",
                UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.EndInit();
            bitmap.Freeze();

            // O arquivo da Ennoia é um PNG indexado. A conversão para BGRA32 evita
            // artefatos de paleta que podem aparecer em algumas instalações do WPF.
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Pbgra32;
            converted.EndInit();
            converted.Freeze();

            image.Source = converted;
        }
        catch
        {
            // Mantém o Source declarado no XAML como fallback caso o recurso não possa ser lido.
        }
    }

    private static T? EncontrarFilhoVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var nested = EncontrarFilhoVisual<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
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
