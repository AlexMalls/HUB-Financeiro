using System;
using System.Windows;

namespace HubFinanceiro;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--validar-monitor-santander", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                SantanderMonitorServiceTestes.Executar();
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }

            return;
        }

        if (args.Any(arg => string.Equals(arg, "--validar-conferencia-pagamentos", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                OpexPaymentReconciliationServiceTestes.Executar();
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }

            return;
        }

        if (args.Any(arg => string.Equals(arg, "--validar-ajustes-ui", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                UiCorrecoesTestes.Executar();
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }

            return;
        }

        var app = new App();
        app.InitializeComponent();
        DebugService.Initialize();
        DebugSemanticService.Initialize();
        SantanderMonitorContinuoService.Initialize();
        SantanderCommitmentAnalyzerService.Initialize();
        SantanderCommitmentMemoryService.Initialize();
        OpexDebugInspectorService.Initialize();
        app.Run();
    }
}
