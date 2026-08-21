using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class MovimentacaoWindow : Window
{
    public MovimentacaoWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Provisionar_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            // Valida se há pagamentos no próximo escopo ANTES de abrir a janela
            if (!ValidarPagamentosNoEscopo())
            {
                CustomMessageBox.ShowInformation("Não há registros programados para o próximo escopo de pagamento.");
                return;
            }
            
            // Guarda referência ao Owner antes de fechar
            var mainWindow = this.Owner;
            
            // Fecha esta janela primeiro
            this.Close();
            
            // Aguarda um tick do dispatcher para garantir que fechou
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Abre janela de Provisionamento
                var provisionamentoWindow = new ProvisionamentoWindow
                {
                    Owner = mainWindow
                };
                provisionamentoWindow.ShowDialog();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao abrir provisionamento: {ex.Message}");
        }
    }

    private void Desprovisionar_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            // Verifica se há pagamentos "No Banco" para desprovisionar
            var pagamentos = CarregarPrevisoesPagamento();
            bool temProvisionados = pagamentos.Any(p => p.Status == "No Banco");

            if (!temProvisionados)
            {
                CustomMessageBox.ShowInformation("Não há pagamentos provisionados para reverter.");
                return;
            }

            // Guarda referência ao Owner antes de fechar
            var mainWindow = this.Owner;

            // Fecha esta janela primeiro
            this.Close();

            // Aguarda um tick do dispatcher para garantir que fechou
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var deprovisionamentoWindow = new DeprovisionamentoWindow
                {
                    Owner = mainWindow
                };
                deprovisionamentoWindow.ShowDialog();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao abrir desprovisionamento: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Valida se há pagamentos pendentes no próximo escopo de pagamento
    /// </summary>
    private bool ValidarPagamentosNoEscopo()
    {
        try
        {
            // Carrega todos os pagamentos
            var pagamentos = CarregarPrevisoesPagamento();
            
            // Calcula o próximo escopo
            var (dataInicio, dataFim) = CalcularProximoEscopo(DateTime.Now);
            
            // Verifica se há algum pagamento pendente no escopo
            bool temPagamentos = pagamentos.Any(p => 
                p.Status == "Pendente" 
                && p.DataPagamento.Date >= dataInicio.Date 
                && p.DataPagamento.Date < dataFim.Date
            );
            
            return temPagamentos;
        }
        catch
        {
            // Em caso de erro, permite abrir (melhor falhar aberto)
            return true;
        }
    }
    
    /// <summary>
    /// Calcula o próximo escopo de pagamento (mesmo algoritmo do ProvisionamentoWindow)
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

    private void Liquidar_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("=== INÍCIO LIQUIDAR ===");
            
            System.Diagnostics.Debug.WriteLine("1. Tentando carregar pagamentos...");
            var pagamentos = CarregarPrevisoesPagamento();
            System.Diagnostics.Debug.WriteLine($"1. OK - Carregou {pagamentos.Count} pagamentos");
            
            System.Diagnostics.Debug.WriteLine("2. Filtrando provisionados...");
            var provisionados = pagamentos
                .Where(p => p.Status == "No Banco" && p.DataProvisionamento.HasValue)
                .ToList();
            System.Diagnostics.Debug.WriteLine($"2. OK - Encontrou {provisionados.Count} provisionados");
            
            if (!provisionados.Any())
            {
                System.Diagnostics.Debug.WriteLine("2.1. Nenhum provisionado encontrado");
                CustomMessageBox.ShowInformation(
                    "Não há pagamentos provisionados para liquidar.",
                    "Informação"
                );
                return;
            }
            
            System.Diagnostics.Debug.WriteLine("3. Agrupando por data...");
            var datas = provisionados
                .Select(p => new { Pagamento = p, Data = p.DataProvisionamento.Value.Date })
                .GroupBy(x => x.Data)
                .OrderBy(g => g.Key)
                .Select(g => new DataProvisionamentoInfo
                {
                    Data = g.Key,
                    Quantidade = g.Count(),
                    Total = g.Sum(x => x.Pagamento.Valor)
                })
                .ToList();
            System.Diagnostics.Debug.WriteLine($"3. OK - Criou {datas.Count} grupos de datas");
            
            System.Diagnostics.Debug.WriteLine("4. Guardando Owner...");
            var mainWindow = this.Owner;
            System.Diagnostics.Debug.WriteLine($"4. OK - Owner: {mainWindow?.GetType().Name ?? "NULL"}");
            
            System.Diagnostics.Debug.WriteLine("5. Fechando MovimentacaoWindow...");
            this.Close();
            System.Diagnostics.Debug.WriteLine("5. OK - Fechou");
            
            System.Diagnostics.Debug.WriteLine("6. Agendando abertura da janela...");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("7. Dentro do Dispatcher...");
                    
                    System.Diagnostics.Debug.WriteLine("8. Criando SelecionarDataProvisionamentoWindow...");
                    var selecionarDataWindow = new SelecionarDataProvisionamentoWindow(datas)
                    {
                        Owner = mainWindow
                    };
                    System.Diagnostics.Debug.WriteLine("8. OK - Janela criada");
                    
                    System.Diagnostics.Debug.WriteLine("9. Abrindo janela de seleção...");
                    bool? resultado = selecionarDataWindow.ShowDialog();
                    System.Diagnostics.Debug.WriteLine($"9. OK - Resultado: {resultado}");
                    
                    if (resultado == true && selecionarDataWindow.DataSelecionada.HasValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"10. Data selecionada: {selecionarDataWindow.DataSelecionada.Value:dd/MM/yyyy}");
                        
                        System.Diagnostics.Debug.WriteLine("11. Criando LiquidacaoWindow...");
                        var liquidacaoWindow = new LiquidacaoWindow(selecionarDataWindow.DataSelecionada.Value)
                        {
                            Owner = mainWindow
                        };
                        System.Diagnostics.Debug.WriteLine("11. OK - Janela criada");
                        
                        System.Diagnostics.Debug.WriteLine("12. Abrindo janela de liquidação...");
                        liquidacaoWindow.ShowDialog();
                        System.Diagnostics.Debug.WriteLine("12. OK - Janela fechada");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("10. Usuário cancelou ou não selecionou data");
                    }
                    
                    System.Diagnostics.Debug.WriteLine("=== FIM LIQUIDAR (SUCESSO) ===");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"=== ERRO NO DISPATCHER ===");
                    System.Diagnostics.Debug.WriteLine($"Mensagem: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                    CustomMessageBox.ShowError($"Erro ao abrir janela de seleção:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
            
            System.Diagnostics.Debug.WriteLine("6. OK - Dispatcher agendado");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"=== ERRO GERAL ===");
            System.Diagnostics.Debug.WriteLine($"Mensagem: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            CustomMessageBox.ShowError($"Erro ao processar liquidação:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Carrega as previsões de pagamento do arquivo JSON
    /// </summary>
    private List<PrevisaoPagamento> CarregarPrevisoesPagamento()
    {
        try
        {
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            
            if (!System.IO.File.Exists(caminhoArquivo))
                return new List<PrevisaoPagamento>();

            string json = System.IO.File.ReadAllText(caminhoArquivo);
            return System.Text.Json.JsonSerializer.Deserialize<List<PrevisaoPagamento>>(json) 
                ?? new List<PrevisaoPagamento>();
        }
        catch
        {
            return new List<PrevisaoPagamento>();
        }
    }
    
    /// <summary>
    /// Obtém o caminho do arquivo de previsões
    /// </summary>
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

    private void Relatorio_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("=== INÍCIO RELATÓRIO ===");
            
            var pagamentos = CarregarPrevisoesPagamento();
            var provisionados = pagamentos.Where(p => p.Status == "No Banco").ToList();
            
            System.Diagnostics.Debug.WriteLine($"Total de pagamentos provisionados: {provisionados.Count}");
            
            if (!provisionados.Any())
            {
                CustomMessageBox.ShowInformation("Não há pagamentos com status \"No Banco\" para gerar relatório.");
                return;
            }
            
            var datasProvisionamento = provisionados
                .Where(p => p.DataProvisionamento.HasValue)
                .Select(p => new { Pagamento = p, Data = p.DataProvisionamento.Value.Date })
                .GroupBy(x => x.Data)
                .Select(g => new DataProvisionamentoInfo
                {
                    Data = g.Key,
                    Quantidade = g.Count(),
                    Total = g.Sum(x => x.Pagamento.Valor)
                })
                .OrderBy(d => d.Data)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"Datas de provisionamento encontradas: {datasProvisionamento.Count}");
            
            if (!datasProvisionamento.Any())
            {
                CustomMessageBox.ShowInformation("Não foram encontradas datas de provisionamento.");
                return;
            }
            
            var mainWindow = this.Owner;
            this.Close();
            
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var selecionarDataWindow = new SelecionarDataProvisionamentoWindow(datasProvisionamento)
                {
                    Owner = mainWindow
                };
                
                if (selecionarDataWindow.ShowDialog() == true)
                {
                    var dataSelecionada = selecionarDataWindow.DataSelecionada.Value;
                    
                    var pagamentosData = provisionados
                        .Where(p => p.DataProvisionamento.HasValue)
                        .Select(p => new { Pagamento = p, Data = p.DataProvisionamento.Value.Date })
                        .Where(x => x.Data == dataSelecionada.Date)
                        .Select(x => x.Pagamento)
                        .ToList();
                    
                    var admPagamentos = pagamentosData.Where(p => p.Empresa == "ADM").OrderBy(p => p.NomeFornecedor).ToList();
                    var corPagamentos = pagamentosData.Where(p => p.Empresa == "COR").OrderBy(p => p.NomeFornecedor).ToList();
                    
                    GerarRelatorioTxt(dataSelecionada, admPagamentos, corPagamentos);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao gerar relatório: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Gera o arquivo TXT do relatório e abre automaticamente
    /// </summary>
    private void GerarRelatorioTxt(DateTime data, List<PrevisaoPagamento> admPagamentos, List<PrevisaoPagamento> corPagamentos)
    {
        try
        {
            var relatorio = new System.Text.StringBuilder();
            
            relatorio.AppendLine("*Pagamentos Administradora*");
            relatorio.AppendLine($"*{data:dd/MM}*");
            relatorio.AppendLine();
            
            if (admPagamentos.Any())
            {
                foreach (var pag in admPagamentos)
                    relatorio.AppendLine($"{pag.NomeFornecedor} - R$ {pag.Valor:N2}");
            }
            else
            {
                relatorio.AppendLine("(Nenhum pagamento)");
            }
            
            relatorio.AppendLine();
            relatorio.AppendLine();
            
            relatorio.AppendLine("*Pagamentos Corretora*");
            relatorio.AppendLine($"*{data:dd/MM}*");
            relatorio.AppendLine();
            
            if (corPagamentos.Any())
            {
                foreach (var pag in corPagamentos)
                    relatorio.AppendLine($"{pag.NomeFornecedor} - R$ {pag.Valor:N2}");
            }
            else
            {
                relatorio.AppendLine("(Nenhum pagamento)");
            }
            
            string caminhoTemp = System.IO.Path.GetTempPath();
            string nomeArquivo = $"Relatorio_Pagamentos_{data:yyyy-MM-dd}.txt";
            string caminhoCompleto = System.IO.Path.Combine(caminhoTemp, nomeArquivo);
            
            System.IO.File.WriteAllText(caminhoCompleto, relatorio.ToString(), System.Text.Encoding.UTF8);
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = caminhoCompleto,
                UseShellExecute = true
            });
            
            System.Diagnostics.Debug.WriteLine($"Relatório gerado: {caminhoCompleto}");
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao criar arquivo de relatório: {ex.Message}");
        }
    }
}
