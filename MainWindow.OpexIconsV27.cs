using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HubFinanceiro;

public partial class MainWindow
{
    private const string OpexActionIconHeaderV27Tag = "OpexActionIconHeaderV27";

    private static readonly Lazy<IReadOnlyList<BitmapSource>> OpexActionIconsV27 =
        new(CarregarIconesAcoesOpexV27);

    internal void AgendarIconesAcoesOpexV27()
    {
        Dispatcher.BeginInvoke(
            new Action(ConfigurarIconesAcoesOpexV27),
            DispatcherPriority.ContextIdle);
    }

    private void ConfigurarIconesAcoesOpexV27()
    {
        if (_menuAcoesOpex == null)
            return;

        _menuAcoesOpex.Opened -= MenuAcoesOpex_OpenedV27;
        _menuAcoesOpex.Opened += MenuAcoesOpex_OpenedV27;

        AplicarIconesAcoesOpexV27();
    }

    private void MenuAcoesOpex_OpenedV27(object sender, RoutedEventArgs e)
        => AplicarIconesAcoesOpexV27();

    private void AplicarIconesAcoesOpexV27()
    {
        if (_menuAcoesOpex == null)
            return;

        foreach (var item in _menuAcoesOpex.Items.OfType<MenuItem>())
        {
            if (item.Header is FrameworkElement elemento
                && string.Equals(elemento.Tag?.ToString(), OpexActionIconHeaderV27Tag, StringComparison.Ordinal))
            {
                continue;
            }

            // Se o projeto local já recebeu a V27 por pacote, preserva exatamente
            // o cabeçalho por imagem que o usuário já testou e aprovou.
            if (item.Header is Panel painelExistente
                && painelExistente.Children.OfType<Image>().Any()
                && painelExistente.Children.OfType<TextBlock>().Any())
            {
                continue;
            }

            string? texto = ExtrairTextoMenuOpexV27(item.Header);
            if (string.IsNullOrWhiteSpace(texto))
                continue;

            int indice = ObterIndiceIconeOpexV27(texto);
            if (indice < 0)
                continue;

            item.Header = CriarCabecalhoMenuOpexV27(texto, indice);
        }
    }

    private static string? ExtrairTextoMenuOpexV27(object? header)
    {
        if (header is string texto)
            return texto;

        if (header is Panel painel)
        {
            return painel.Children
                .OfType<TextBlock>()
                .Select(texto => texto.Text)
                .LastOrDefault(valor => !string.IsNullOrWhiteSpace(valor));
        }

        if (header is TextBlock textBlock)
            return textBlock.Text;

        return header?.ToString();
    }

    private static int ObterIndiceIconeOpexV27(string texto)
    {
        string chave = string.Concat(
                texto.Normalize(System.Text.NormalizationForm.FormD)
                    .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            .Normalize(System.Text.NormalizationForm.FormC)
            .Trim()
            .ToLowerInvariant();

        return chave switch
        {
            "provisionar pagamentos" => 0,
            "desprovisionar pagamento" => 1,
            "liquidar pagamentos" => 2,
            "baixar registro" => 3,
            "relatorio de pagamentos" => 4,
            "importar" => 5,
            "conferir pagamentos" => 6,
            _ => -1
        };
    }

    private static FrameworkElement CriarCabecalhoMenuOpexV27(string texto, int indice)
    {
        var cabecalho = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = OpexActionIconHeaderV27Tag
        };

        var icones = OpexActionIconsV27.Value;
        if (indice >= 0 && indice < icones.Count)
        {
            cabecalho.Children.Add(new Image
            {
                Source = icones[indice],
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        cabecalho.Children.Add(new TextBlock
        {
            Text = texto,
            VerticalAlignment = VerticalAlignment.Center
        });

        return cabecalho;
    }

    private static IReadOnlyList<BitmapSource> CarregarIconesAcoesOpexV27()
    {
        const int tamanho = 64;
        byte[] bytes = Convert.FromBase64String(OpexActionIconsSpriteV27);

        using var stream = new MemoryStream(bytes, writable: false);
        var sprite = new BitmapImage();
        sprite.BeginInit();
        sprite.CacheOption = BitmapCacheOption.OnLoad;
        sprite.StreamSource = stream;
        sprite.EndInit();
        sprite.Freeze();

        var icones = new List<BitmapSource>(7);
        for (int indice = 0; indice < 7; indice++)
        {
            var recorte = new CroppedBitmap(
                sprite,
                new Int32Rect(0, indice * tamanho, tamanho, tamanho));
            recorte.Freeze();
            icones.Add(recorte);
        }

        return icones;
    }

    private const string OpexActionIconsSpriteV27 =
        "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";
}
