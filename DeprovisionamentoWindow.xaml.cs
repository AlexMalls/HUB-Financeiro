using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;

namespace HubFinanceiro;

public partial class DeprovisionamentoWindow : Window
{
    private ObservableCollection<PrevisaoPagamento> _pagamentosAdm = new ObservableCollection<PrevisaoPagamento>();
    private ObservableCollection<PrevisaoPagamento> _pagamentosCorrectly = new ObservableCollection<PrevisaoPagamento>();
    private List<PrevisaoPagamento> _todosPagamentos = new List<PrevisaoPagamento>();
    private DateTime? _ultimoClique = null;
    private PrevisaoPagamento? _ultimoItemClicado = null;

    public DeprovisionamentoWindow()
    {
        InitializeComponent();
        CarregarPagamentos();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CarregarPagamentos()
    {
        try
        {
            // Carrega TODOS os pagamentos e guarda
            _todosPagamentos = CarregarPrevisoesPagamento();

            // Separa por empresa, filtrando apenas os que estão "No Banco"
            _pagamentosAdm = new ObservableCollection<PrevisaoPagamento>(
                _todosPagamentos
                    .Where(p => p.Empresa == "ADM" && p.Status == "No Banco")
                    .OrderBy(p => p.DataPagamento)
                    .ToList()
            );

            _pagamentosCorrectly = new ObservableCollection<PrevisaoPagamento>(
                _todosPagamentos
                    .Where(p => p.Empresa == "COR" && p.Status == "No Banco")
                    .OrderBy(p => p.DataPagamento)
                    .ToList()
            );

            // Atualiza as listas
            AdministradoraItemsControl.ItemsSource = _pagamentosAdm;
            CorretoraItemsControl.ItemsSource = _pagamentosCorrectly;

            // Atualiza totais
            AtualizarTotais();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao carregar pagamentos: {ex.Message}");
        }
    }

    private void AtualizarTotais()
    {
        // Administradora
        var totalAdm = _pagamentosAdm.Sum(p => p.Valor);
        var quantAdm = _pagamentosAdm.Count;
        TotalAdmTextBlock.Text = $"R$ {totalAdm:N2}";
        QuantAdmTextBlock.Text = $"{quantAdm}";

        // Corretora
        var totalCor = _pagamentosCorrectly.Sum(p => p.Valor);
        var quantCor = _pagamentosCorrectly.Count;
        TotalCorTextBlock.Text = $"R$ {totalCor:N2}";
        QuantCorTextBlock.Text = $"{quantCor}";
    }

    private List<PrevisaoPagamento> CarregarPrevisoesPagamento()
    {
        try
        {
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();

            if (!System.IO.File.Exists(caminhoArquivo))
                return new List<PrevisaoPagamento>();

            string json = System.IO.File.ReadAllText(caminhoArquivo);
            return System.Text.Json.JsonSerializer.Deserialize<List<PrevisaoPagamento>>(json) ?? new List<PrevisaoPagamento>();
        }
        catch
        {
            return new List<PrevisaoPagamento>();
        }
    }

    private string ObterCaminhoArquivoPrevisoes()
    {
        if (System.IO.Directory.Exists(@"C:/Users/Alexandre Mallorca"))
            return @"C:/Users/Alexandre Mallorca/OneDrive - Positiva Administradora de Benefícios Ltda/Documentos/Financeiro/HUB Financeiro/data/previsoes_pagamento.json";

        if (System.IO.Directory.Exists(@"C:/Users/Vinícius Oliveira"))
            return @"C:/Users/Vinícius Oliveira/Positiva Administradora de Benefícios Ltda/Alexandre Mallorca Silveira - data/previsoes_pagamento.json";

        throw new System.IO.DirectoryNotFoundException(
            "Usuário não reconhecido. Usuários suportados: Alexandre Mallorca, Vinícius Oliveira. " +
            "Verifique se o programa está sendo executado em uma máquina cadastrada.");
    }

    // Evento de duplo clique nos itens da ADM
    private void ItemAdm_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border)
            return;

        if (border.DataContext is not PrevisaoPagamento pagamento)
            return;

        bool isDuploClique = false;
        if (_ultimoItemClicado == pagamento && _ultimoClique.HasValue)
        {
            var diff = DateTime.Now - _ultimoClique.Value;
            if (diff.TotalMilliseconds < 500)
                isDuploClique = true;
        }

        _ultimoClique = DateTime.Now;
        _ultimoItemClicado = pagamento;

        if (isDuploClique)
        {
            _pagamentosAdm.Remove(pagamento);
            AtualizarTotais();
        }
    }

    // Evento de duplo clique nos itens da Corretora
    private void ItemCor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border)
            return;

        if (border.DataContext is not PrevisaoPagamento pagamento)
            return;

        bool isDuploClique = false;
        if (_ultimoItemClicado == pagamento && _ultimoClique.HasValue)
        {
            var diff = DateTime.Now - _ultimoClique.Value;
            if (diff.TotalMilliseconds < 500)
                isDuploClique = true;
        }

        _ultimoClique = DateTime.Now;
        _ultimoItemClicado = pagamento;

        if (isDuploClique)
        {
            _pagamentosCorrectly.Remove(pagamento);
            AtualizarTotais();
        }
    }

    private void Desprovisionar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Confirmação antes de desprovisionar
            var result = CustomMessageBox.ShowQuestion(
                "Deseja reverter os pagamentos listados para o status \"Pendente\"?",
                "Confirmar Desprovisionamento"
            );

            if (result != HubFinanceiro.MessageBoxResult.Yes)
                return;

            // Reverte o status de todos os pagamentos exibidos para "Pendente"
            // e limpa a data de provisionamento
            foreach (var pagamento in _pagamentosAdm)
            {
                pagamento.Status = "Pendente";
                pagamento.DataProvisionamento = null;
            }

            foreach (var pagamento in _pagamentosCorrectly)
            {
                pagamento.Status = "Pendente";
                pagamento.DataProvisionamento = null;
            }

            // Salva as alterações
            SalvarPrevisoes();

            // Atualiza a lista na MainWindow
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.RecarregarPagamentos();
            }

            // Mostra mensagem de sucesso
            CustomMessageBox.ShowInformation(
                "Desprovisionamento realizado com sucesso! Os pagamentos voltaram para o status \"Pendente\".",
                "Sucesso"
            );

            // Fecha a janela
            Close();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao desprovisionar pagamentos: {ex.Message}");
        }
    }

    private void SalvarPrevisoes()
    {
        try
        {
            // Atualiza a lista completa com as mudanças
            _todosPagamentos.RemoveAll(p =>
                (p.Empresa == "ADM" && _pagamentosAdm.Any(pa => pa.NomeFornecedor == p.NomeFornecedor)) ||
                (p.Empresa == "COR" && _pagamentosCorrectly.Any(pc => pc.NomeFornecedor == p.NomeFornecedor))
            );

            // Adiciona todos os pagamentos atualizados
            _todosPagamentos.AddRange(_pagamentosAdm);
            _todosPagamentos.AddRange(_pagamentosCorrectly);

            // Salva TUDO
            string json = System.Text.Json.JsonSerializer.Serialize(_todosPagamentos, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            System.IO.File.WriteAllText(caminhoArquivo, json);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao salvar previsões: {ex.Message}");
        }
    }
}
