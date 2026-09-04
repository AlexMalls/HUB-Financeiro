using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HubFinanceiro;

public static class UiCorrecoesOpexV10Testes
{
    public static void Executar()
    {
        DeveExibirAcoesOpexNaOrdemAprovada();
        DevePrepararFornecedorParaCodigoOpcionalComIdInterno();
    }

    private static void DeveExibirAcoesOpexNaOrdemAprovada()
    {
        if (Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            var grupo = EncontrarDescendentePorTag<StackPanel>(window.OpexInputsGrid, "OpexV8Actions");
            Assert(grupo != null && grupo.Children.Count == 2,
                "o grupo compacto de ações O.P.E.X. deve existir");

            var botaoAcoes = grupo!.Children[1] as Button;
            Assert(botaoAcoes?.ContextMenu != null,
                "o botão Ações O.P.E.X. deve possuir menu flutuante");

            var titulos = botaoAcoes!.ContextMenu!.Items
                .OfType<MenuItem>()
                .Select(item => item.Header?.ToString() ?? string.Empty)
                .ToArray();

            var esperado = new[]
            {
                "Provisionar Pagamentos",
                "Desprovisionar Pagamento",
                "Liquidar Pagamentos",
                "Relatório de Pagamentos",
                "Importar",
                "Conferir Pagamentos"
            };

            Assert(titulos.SequenceEqual(esperado),
                $"o menu deve conter exatamente as seis ações aprovadas na ordem correta. Atual: [{string.Join(" | ", titulos)}]");
            Assert(!titulos.Contains("Movimentar Registros"),
                "a opção Movimentar Registros deve ser removida do menu");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DevePrepararFornecedorParaCodigoOpcionalComIdInterno()
    {
        var propriedadeId = typeof(Fornecedor).GetProperty("IdInterno", BindingFlags.Instance | BindingFlags.Public);
        Assert(propriedadeId?.PropertyType == typeof(int),
            "Fornecedor deve possuir IdInterno inteiro e persistente");

        var propriedadePagamentoId = typeof(PrevisaoPagamento).GetProperty("FornecedorIdInterno", BindingFlags.Instance | BindingFlags.Public);
        Assert(propriedadePagamentoId?.PropertyType == typeof(int),
            "PrevisaoPagamento deve guardar o IdInterno do fornecedor para preservar o vínculo quando o código visível estiver vazio ou mudar");

        var window = new MainWindow();
        try
        {
            window.FornecedorNomeTextBox.Text = "Fornecedor sem código";
            window.FornecedorCodigoTextBox.Text = string.Empty;

            var validar = typeof(MainWindow).GetMethod("ValidarBotaoCadastro", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(validar != null, "a validação do cadastro de fornecedor deve existir");
            validar!.Invoke(window, null);

            Assert(window.BtnCadastrarFornecedor.IsEnabled,
                "o fornecedor deve poder ser salvo apenas com o nome, mesmo sem código visível");
        }
        finally
        {
            window.Close();
        }
    }

    private static T? EncontrarDescendentePorTag<T>(DependencyObject raiz, object tag)
        where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T elemento && Equals(elemento.Tag, tag))
                return elemento;

            var encontrado = EncontrarDescendentePorTag<T>(filho, tag);
            if (encontrado != null)
                return encontrado;
        }

        return null;
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste O.P.E.X. V10: {scenario}.");
    }
}
