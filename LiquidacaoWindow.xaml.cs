using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections.ObjectModel;

namespace HubFinanceiro;

public partial class LiquidacaoWindow : Window
{
    private ObservableCollection<PrevisaoPagamento> _pagamentosAdm = new ObservableCollection<PrevisaoPagamento>();
    private ObservableCollection<PrevisaoPagamento> _pagamentosCorretora = new ObservableCollection<PrevisaoPagamento>();
    private List<PrevisaoPagamento> _todosPagamentos = new List<PrevisaoPagamento>();
    private DateTime? _ultimoClique = null;
    private PrevisaoPagamento? _ultimoItemClicado = null;
    private DateTime _dataProvisionamento; // Data do provisionamento sendo liquidado

    public LiquidacaoWindow(DateTime dataProvisionamento)
    {
        InitializeComponent();
        _dataProvisionamento = dataProvisionamento;
        
        // Atualiza o título com a data
        DataProvisionamentoTextBlock.Text = $"(Provisionado para: {dataProvisionamento:dd/MM/yyyy})";
        
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
            // Carrega TODOS os pagamentos
            _todosPagamentos = CarregarPrevisoesPagamento();

            // Filtra apenas os que estão "No Banco" E foram provisionados nesta data
            var pagamentosParaLiquidar = _todosPagamentos
                .Where(p => p.Status == "No Banco" 
                         && p.DataProvisionamento.HasValue 
                         && p.DataProvisionamento.Value.Date == _dataProvisionamento.Date)
                .ToList();

            // Separa por empresa
            _pagamentosAdm = new ObservableCollection<PrevisaoPagamento>(
                pagamentosParaLiquidar.Where(p => p.Empresa == "ADM").ToList()
            );
            
            _pagamentosCorretora = new ObservableCollection<PrevisaoPagamento>(
                pagamentosParaLiquidar.Where(p => p.Empresa == "COR").ToList()
            );

            // Atualiza as listas
            AdministradoraItemsControl.ItemsSource = _pagamentosAdm;
            CorretoraItemsControl.ItemsSource = _pagamentosCorretora;

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
        var totalCor = _pagamentosCorretora.Sum(p => p.Valor);
        var quantCor = _pagamentosCorretora.Count;
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
        // Verifica qual usuário está ativo pelo sistema de arquivos (sem API do Windows)
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

        // Detecta duplo clique
        bool isDuploClique = false;
        if (_ultimoItemClicado == pagamento && _ultimoClique.HasValue)
        {
            var diff = DateTime.Now - _ultimoClique.Value;
            if (diff.TotalMilliseconds < 500) // 500ms para duplo clique
            {
                isDuploClique = true;
            }
        }

        _ultimoClique = DateTime.Now;
        _ultimoItemClicado = pagamento;

        if (isDuploClique)
        {
            // Remove o item da lista
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

        // Detecta duplo clique
        bool isDuploClique = false;
        if (_ultimoItemClicado == pagamento && _ultimoClique.HasValue)
        {
            var diff = DateTime.Now - _ultimoClique.Value;
            if (diff.TotalMilliseconds < 500) // 500ms para duplo clique
            {
                isDuploClique = true;
            }
        }

        _ultimoClique = DateTime.Now;
        _ultimoItemClicado = pagamento;

        if (isDuploClique)
        {
            // Remove o item da lista
            _pagamentosCorretora.Remove(pagamento);
            AtualizarTotais();
        }
    }

    private void Liquidar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Verifica se há pagamentos para liquidar
            if (!_pagamentosAdm.Any() && !_pagamentosCorretora.Any())
            {
                CustomMessageBox.ShowWarning("Não há pagamentos para liquidar.", "Aviso");
                return;
            }

            // Pergunta confirmação
            int totalPagamentos = _pagamentosAdm.Count + _pagamentosCorretora.Count;
            decimal valorTotal = _pagamentosAdm.Sum(p => p.Valor) + _pagamentosCorretora.Sum(p => p.Valor);
            
            var resultado = CustomMessageBox.ShowQuestion(
                $"Deseja liquidar {totalPagamentos} pagamento(s) no valor total de R$ {valorTotal:N2}?",
                "Confirmar Liquidação"
            );

            if (resultado != HubFinanceiro.MessageBoxResult.Yes)
                return;

            // Muda o status de todos os pagamentos nas listas para "Pago"
            foreach (var pagamento in _pagamentosAdm)
            {
                pagamento.Status = "Pago";
            }
            
            foreach (var pagamento in _pagamentosCorretora)
            {
                pagamento.Status = "Pago";
            }

            // Atualiza no arquivo (pega TODOS os pagamentos e atualiza os que foram liquidados)
            var todosPagamentos = _todosPagamentos;
            
            // Atualiza o status dos pagamentos liquidados
            foreach (var pag in todosPagamentos)
            {
                var pagAdm = _pagamentosAdm.FirstOrDefault(p => p.Id == pag.Id);
                var pagCor = _pagamentosCorretora.FirstOrDefault(p => p.Id == pag.Id);
                
                if (pagAdm != null)
                    pag.Status = "Pago";
                if (pagCor != null)
                    pag.Status = "Pago";
            }

            // Salva no arquivo
            SalvarPrevisoes(todosPagamentos);

            // Atualiza a MainWindow se for o Owner
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.RecarregarPagamentos();
            }

            // Mostra mensagem de sucesso
            CustomMessageBox.ShowInformation(
                $"Liquidação realizada com sucesso!\n{totalPagamentos} pagamento(s) marcado(s) como PAGO.",
                "Sucesso"
            );

            // Fecha a janela
            Close();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao liquidar pagamentos: {ex.Message}");
        }
    }

    /// <summary>
    /// Salva a lista de previsões no arquivo JSON
    /// </summary>
    private void SalvarPrevisoes(List<PrevisaoPagamento> previsoes)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(previsoes, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            System.IO.File.WriteAllText(caminhoArquivo, json);
        }
        catch (Exception ex)
        {
            throw new Exception($"Erro ao salvar previsões: {ex.Message}");
        }
    }
}
