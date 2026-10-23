using System.Windows;

namespace HubFinanceiro;

public partial class App : Application
{
    public App()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is MainWindow window)
                    window.AgendarIconesAcoesOpexV27();
            }));
    }
}
