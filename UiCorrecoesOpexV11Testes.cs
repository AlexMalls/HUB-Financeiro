using System;
using System.IO;

namespace HubFinanceiro;

public static class UiCorrecoesOpexV11Testes
{
    public static void Executar()
    {
        ValidarBaixaDireta();
        ValidarFornecedorFantasma();
        ValidarDesselecaoExterna();
        ValidarExclusaoFornecedorVisivel();
    }

    private static void ValidarBaixaDireta()
    {
        string codigo = File.ReadAllText("MainWindow.OpexV11.cs");
        string janela = File.ReadAllText("BaixarRegistroWindow.xaml.cs");
        string xaml = File.ReadAllText("BaixarRegistroWindow.xaml");

        if (!codigo.Contains("Baixar Registro", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: menu Ações O.P.E.X. deve conter 'Baixar Registro'.");

        if (!codigo.Contains("_pagamentoSelecionado != null", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: Baixar Registro só pode ser habilitado com registro selecionado.");

        if (!codigo.Contains("new BaixarRegistroWindow(_pagamentoSelecionado.DataPagamento)", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: janela de baixa deve iniciar na data de vencimento do registro.");

        if (!codigo.Contains("Status = \"Pago\"", StringComparison.Ordinal)
            && !codigo.Contains("Status=\"Pago\"", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: baixa direta deve marcar o registro como Pago.");

        if (!codigo.Contains("DataProvisionamento = janela.DataSelecionada.Date", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: baixa direta deve gravar a data escolhida em DataProvisionamento.");

        if (!janela.Contains("DataBaixaDatePicker.SelectedDate = DataSelecionada", StringComparison.Ordinal)
            || !xaml.Contains("Content=\"OK\"", StringComparison.Ordinal)
            || !xaml.Contains("Content=\"Cancelar\"", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: janela de baixa precisa trazer data editável, OK e Cancelar.");
    }

    private static void ValidarFornecedorFantasma()
    {
        string codigo = File.ReadAllText("MainWindow.OpexV11.cs");

        if (!codigo.Contains("FornecedorFantasma", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: fornecedor transitório de registros importados não foi identificado internamente.");

        if (!codigo.Contains("CriarFornecedorFantasma", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: deve existir criação transitória do fornecedor fantasma para edição.");

        if (!codigo.Contains("OpexFornecedorComboBox.SelectedItem = _fornecedorFantasmaOpexV11", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: fornecedor fantasma deve ser carregado no campo de fornecedor durante a edição.");

        if (codigo.Contains("SalvarFornecedores", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: fornecedor fantasma não pode ser persistido na lista oficial de fornecedores.");
    }

    private static void ValidarDesselecaoExterna()
    {
        string codigo = File.ReadAllText("MainWindow.OpexV11.cs");

        if (!codigo.Contains("OpexLayoutGrid_PreviewMouseDownV11", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: clique fora do registro/campos deve limpar a seleção da O.P.E.X.");

        if (!codigo.Contains("DesselecionarPagamento();", StringComparison.Ordinal)
            || !codigo.Contains("atual is ButtonBase", StringComparison.Ordinal)
            || !codigo.Contains("atual is TextBoxBase", StringComparison.Ordinal)
            || !codigo.Contains("atual is ComboBox", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: desseleção externa precisa preservar botões e campos de edição.");
    }

    private static void ValidarExclusaoFornecedorVisivel()
    {
        string codigo = File.ReadAllText("MainWindow.OpexV11.cs");

        if (!codigo.Contains("Excluir fornecedor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("V11: exclusão de fornecedor precisa de ação visível na lista.");

        if (!codigo.Contains("ExcluirFornecedorV10(fornecedor)", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: ação visível deve reutilizar a exclusão segura da V10.");
    }
}
