using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections.ObjectModel;

namespace HubFinanceiro;

public partial class ProvisionamentoWindow : Window
{
    private ObservableCollection<PrevisaoPagamento> _pagamentosAdm = new ObservableCollection<PrevisaoPagamento>();
    private ObservableCollection<PrevisaoPagamento> _pagamentosCorrectly = new ObservableCollection<PrevisaoPagamento>();
    private List<PrevisaoPagamento> _todosPagamentos = new List<PrevisaoPagamento>(); // MANTÉM TODOS
    private DateTime? _ultimoClique = null;
    private PrevisaoPagamento? _ultimoItemClicado = null;

    public ProvisionamentoWindow()
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

            // Calcula o próximo escopo de pagamento (janela de 5 dias)
            var (dataInicio, dataFim) = CalcularProximoEscopo(DateTime.Now);
            
            System.Diagnostics.Debug.WriteLine($"📅 Escopo de pagamento: {dataInicio:dd/MM/yyyy} até {dataFim:dd/MM/yyyy}");

            // Separa por empresa E filtra apenas os pendentes DENTRO DO ESCOPO
            _pagamentosAdm = new ObservableCollection<PrevisaoPagamento>(
                _todosPagamentos
                    .Where(p => p.Empresa == "ADM" 
                             && p.Status == "Pendente"
                             && p.DataPagamento.Date >= dataInicio.Date 
                             && p.DataPagamento.Date < dataFim.Date)
                    .OrderBy(p => p.DataPagamento)
                    .ToList()
            );
            
            _pagamentosCorrectly = new ObservableCollection<PrevisaoPagamento>(
                _todosPagamentos
                    .Where(p => p.Empresa == "COR" 
                             && p.Status == "Pendente"
                             && p.DataPagamento.Date >= dataInicio.Date 
                             && p.DataPagamento.Date < dataFim.Date)
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
    
    /// <summary>
    /// Calcula o próximo escopo de pagamento (janela de 5 dias)
    /// Marcos: 1, 5, 10, 15, 20, 25, 30 (fim do mês)
    /// Retorna: (início do próximo escopo, início do escopo seguinte)
    /// </summary>
    private (DateTime inicio, DateTime fim) CalcularProximoEscopo(DateTime dataAtual)
    {
        int diaAtual = dataAtual.Day;
        int mes = dataAtual.Month;
        int ano = dataAtual.Year;
        int ultimoDiaMes = DateTime.DaysInMonth(ano, mes);
        
        // Marcos de pagamento (1, 5, 10, 15, 20, 25, fim do mês)
        int[] marcos = { 1, 5, 10, 15, 20, 25, ultimoDiaMes };
        
        // Encontra o próximo marco (ou o atual se hoje for um marco)
        int proximoMarco = -1;
        int marcoSeguinte = -1;
        
        for (int i = 0; i < marcos.Length; i++)
        {
            // MUDANÇA: >= em vez de > para incluir quando hoje É um marco
            if (marcos[i] >= diaAtual)
            {
                proximoMarco = marcos[i];
                
                // Pega o marco seguinte
                if (i + 1 < marcos.Length)
                {
                    marcoSeguinte = marcos[i + 1];
                }
                else
                {
                    // Se for o último marco do mês, próximo é dia 5 do mês seguinte
                    var proximoMes = new DateTime(ano, mes, 1).AddMonths(1);
                    return (
                        dataAtual.Date,
                        new DateTime(proximoMes.Year, proximoMes.Month, 5)
                    );
                }
                break;
            }
        }
        
        // Se não achou (passou do último marco do mês)
        if (proximoMarco == -1)
        {
            var proximoMes = new DateTime(ano, mes, 1).AddMonths(1);
            return (
                new DateTime(proximoMes.Year, proximoMes.Month, 1),
                new DateTime(proximoMes.Year, proximoMes.Month, 5)
            );
        }
        
        return (
            dataAtual.Date,
            new DateTime(ano, mes, marcoSeguinte)
        );
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
            _pagamentosCorrectly.Remove(pagamento);
            AtualizarTotais();
        }
    }

    private void Provisionar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Abre janela de seleção de data
            var dataWindow = new SelecionarDataWindow
            {
                Owner = this
            };
            
            if (dataWindow.ShowDialog() == true)
            {
                DateTime dataSelecionada = dataWindow.DataSelecionada;
                
                // Muda o status de todos os pagamentos para "No Banco" E salva a data de provisionamento
                foreach (var pagamento in _pagamentosAdm)
                {
                    pagamento.Status = "No Banco";
                    pagamento.DataProvisionamento = dataSelecionada; // SALVA A DATA!
                }
                
                foreach (var pagamento in _pagamentosCorrectly)
                {
                    pagamento.Status = "No Banco";
                    pagamento.DataProvisionamento = dataSelecionada; // SALVA A DATA!
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
                    $"Provisionamento realizado com sucesso para {dataSelecionada:dd/MM/yyyy}!",
                    "Sucesso"
                );
                
                // Fecha a janela
                Close();
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao provisionar pagamentos: {ex.Message}");
        }
    }

    private void SalvarPrevisoes()
    {
        try
        {
            // Atualiza a lista completa com as mudanças
            // Remove os pendentes antigos e adiciona os atualizados
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

