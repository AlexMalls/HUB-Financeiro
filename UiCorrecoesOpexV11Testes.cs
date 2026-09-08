using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        string codigo = File.ReadAllText("MainWindow.OpexV8.cs");

        if (!codigo.Contains("Baixar Registro", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: menu Ações O.P.E.X. deve conter 'Baixar Registro'.");

        if (!codigo.Contains("ExecutarBaixarRegistroOpexV11", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: fluxo de baixa direta não foi implementado.");

        if (!codigo.Contains("Status = \"Pago\"", StringComparison.Ordinal)
            && !codigo.Contains("Status=\"Pago\"", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: baixa direta deve marcar o registro como Pago.");

        if (!codigo.Contains("DataProvisionamento", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: baixa direta deve registrar a data em DataProvisionamento.");
    }

    private static void ValidarFornecedorFantasma()
    {
        string codigo = File.ReadAllText("MainWindow.xaml.cs");

        if (!codigo.Contains("FornecedorFantasma", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: fornecedor transitório de registros importados não foi identificado internamente.");

        if (!codigo.Contains("CriarFornecedorFantasma", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: deve existir criação transitória do fornecedor fantasma para edição.");

        if (!codigo.Contains("_pagamentoSelecionado", StringComparison.Ordinal)
            || !codigo.Contains("AtualizarPagamento", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: edição de pagamento não está disponível para o fornecedor fantasma.");
    }

    private static void ValidarDesselecaoExterna()
    {
        string codigo = File.ReadAllText("MainWindow.OpexV8.cs");

        if (!codigo.Contains("OpexLayoutGrid_PreviewMouseDownV11", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: clique fora do registro/campos deve limpar a seleção da O.P.E.X.");
    }

    private static void ValidarExclusaoFornecedorVisivel()
    {
        string codigo = File.ReadAllText("MainWindow.FornecedoresV10.cs");

        if (!codigo.Contains("Excluir fornecedor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("V11: exclusão de fornecedor precisa de ação visível na lista.");

        if (!codigo.Contains("ExcluirFornecedorV10", StringComparison.Ordinal))
            throw new InvalidOperationException("V11: ação visível deve reutilizar a exclusão segura da V10.");
    }
}
