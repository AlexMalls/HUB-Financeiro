using System;
using System.Windows;

namespace HubFinanceiro;

public partial class MainWindow
{
    /// <summary>
    /// Confere os lançamentos do O.P.E.X. contra os compromissos Santander
    /// armazenados pelo monitor operacional do HUB.
    /// </summary>
    private void BtnConferirPagamentos_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hoje = DateTime.Today;
            var escolha = CustomMessageBox.ShowQuestion(
                $"Deseja conferir os pagamentos de hoje ({hoje:dd/MM/yyyy})?",
                "Conferir Pagamentos",
                "Escolha Hoje para analisar a data atual ou Outra data para selecionar um dia diferente.",
                "Hoje",
                "Outra data");

            DateTime dataAnalise;
            if (escolha == MessageBoxResult.Yes)
            {
                dataAnalise = hoje;
            }
            else if (escolha == MessageBoxResult.No)
            {
                var selecionarData = new SelecionarDataWindow(
                    hoje,
                    "Conferir Pagamentos",
                    "Qual data deseja conferir?",
                    "Conferir",
                    confirmarFimSemana: false,
                    confirmarPassado: false)
                {
                    Owner = this
                };

                if (selecionarData.ShowDialog() != true)
                    return;

                dataAnalise = selecionarData.DataSelecionada.Date;
            }
            else
            {
                return;
            }

            var pagamentosHub = CarregarPrevisoesPagamento();
            var memoriaBanco = SantanderCommitmentMemoryService.Snapshot();
            var resultado = OpexPaymentReconciliationService.Conferir(
                dataAnalise,
                pagamentosHub,
                memoriaBanco);

            DebugService.Record(
                "OPEX",
                $"Conferir Pagamentos | Data: {dataAnalise:dd/MM/yyyy} | HUB No Banco: {resultado.TotalHubNoBanco} | Santander: {resultado.TotalBanco} | Divergências: {resultado.Divergencias.Count}.",
                DebugEntryLevel.Action);

            var janela = new ConferirPagamentosWindow(resultado)
            {
                Owner = this
            };
            janela.ShowDialog();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao conferir pagamentos", ex);
        }
    }
}
