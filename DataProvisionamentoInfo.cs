using System;

namespace HubFinanceiro;

/// <summary>
/// Informações sobre uma data de provisionamento
/// </summary>
public class DataProvisionamentoInfo
{
    public DateTime Data { get; set; }
    public int Quantidade { get; set; }
    public decimal Total { get; set; }
    
    public string DataFormatada => Data.ToString("dd/MM/yyyy");
    public string TotalFormatado => Total.ToString("N2");
}
