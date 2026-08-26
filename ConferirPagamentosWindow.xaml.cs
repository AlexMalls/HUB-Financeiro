using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class ConferirPagamentosWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public ConferirPagamentosWindow(OpexPaymentReconciliationResult result)
    {
        InitializeComponent();
        Preencher(result);
    }

    private void Preencher(OpexPaymentReconciliationResult result)
    {
        HeaderDateTextBlock.Text = $"Conferência de {result.Data:dd/MM/yyyy}";
        HubNoBancoCountTextBlock.Text = result.TotalHubNoBanco.ToString(CultureInfo.InvariantCulture);
        BankCountTextBlock.Text = result.TotalBanco.ToString(CultureInfo.InvariantCulture);
        IssueCountTextBlock.Text = result.Divergencias.Count.ToString(CultureInfo.InvariantCulture);

        IssueSummaryTextBlock.Text = result.SemDivergencias
            ? "nenhuma divergência encontrada"
            : result.CoberturaBancoCompleta
                ? "itens que precisam de atenção"
                : "inclui contexto Santander ainda não consultado";

        CoverageTextBox.Text = BuildCoverageText(result);
        FooterHintTextBlock.Text =
            "A conferência não altera lançamentos automaticamente. Quando um pagamento do Santander estiver no HUB com status diferente de “No Banco”, ajuste o status manualmente após validar o item.";

        var views = new ObservableCollection<IssueViewModel>(
            result.Divergencias.Select(issue => new IssueViewModel(issue)));
        IssuesItemsControl.ItemsSource = views;

        if (result.SemDivergencias)
        {
            IssuesPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Visible;
            SuccessTextBlock.Text =
                $"Os {result.TotalHubNoBanco} lançamento(s) do HUB marcados como “No Banco” foram encontrados no Santander e nenhum pagamento bancário ficou sem correspondência no HUB.";
        }
        else
        {
            IssuesPanel.Visibility = Visibility.Visible;
            SuccessPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string BuildCoverageText(OpexPaymentReconciliationResult result)
    {
        string Empresa(OpexPaymentReconciliationCompanyResult company)
        {
            if (!company.DadosBancoDisponiveis)
                return $"{company.Empresa}: Santander não consultado para o dia";

            var updated = company.SnapshotSelecionado?.AtualizadoEm.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) ?? "?";
            return $"{company.Empresa}: HUB {company.QuantidadeHubDia} • No Banco {company.QuantidadeHubNoBanco} • Santander {company.QuantidadeBancoDia} • memória {updated}";
        }

        return $"{Empresa(result.Administradora)}    |    {Empresa(result.Corretora)}";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed class IssueViewModel
    {
        public string Empresa { get; }
        public string DirectionLabel { get; }
        public string Titulo { get; }
        public string Descricao { get; }
        public string ValorTexto { get; }
        public Brush AccentBrush { get; }
        public Brush BadgeBackground { get; }

        public IssueViewModel(OpexPaymentReconciliationIssue issue)
        {
            Empresa = issue.Empresa;
            Titulo = issue.Titulo;
            Descricao = issue.Descricao;
            ValorTexto = issue.Valor.HasValue ? issue.Valor.Value.ToString("C2", PtBr) : string.Empty;

            switch (issue.Tipo)
            {
                case OpexPaymentReconciliationIssueKind.HubNoBancoAusenteNoSantander:
                    DirectionLabel = "HUB → BANCO";
                    AccentBrush = new SolidColorBrush(Color.FromRgb(255, 109, 109));
                    BadgeBackground = new SolidColorBrush(Color.FromRgb(62, 35, 38));
                    break;

                case OpexPaymentReconciliationIssueKind.SantanderAusenteNoHub:
                    DirectionLabel = "BANCO → HUB";
                    AccentBrush = new SolidColorBrush(Color.FromRgb(255, 109, 109));
                    BadgeBackground = new SolidColorBrush(Color.FromRgb(62, 35, 38));
                    break;

                case OpexPaymentReconciliationIssueKind.StatusHubDivergente:
                    DirectionLabel = "STATUS";
                    AccentBrush = new SolidColorBrush(Color.FromRgb(242, 184, 75));
                    BadgeBackground = new SolidColorBrush(Color.FromRgb(55, 46, 30));
                    break;

                default:
                    DirectionLabel = "DADOS";
                    AccentBrush = new SolidColorBrush(Color.FromRgb(118, 183, 255));
                    BadgeBackground = new SolidColorBrush(Color.FromRgb(30, 45, 61));
                    break;
            }
        }
    }
}
