using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HubFinanceiro;

public static class UiCorrecoesOpexV10Testes
{
    public static void Executar()
    {
        DeveExibirAcoesOpexNaOrdemAprovada();
        DevePermitirCodigoVazioComIdInternoUnicoEPersistente();
        DeveHabilitarCadastroSemCodigoVisivel();
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

    private static void DevePermitirCodigoVazioComIdInternoUnicoEPersistente()
    {
        var semCodigoA = new Fornecedor { Nome = "Fornecedor sem código A", Codigo = 0, Ativo = true };
        var semCodigoB = new Fornecedor { Nome = "Fornecedor sem código B", Codigo = 0, Ativo = true };
        var codigoRealNoveDigitos = new Fornecedor { Nome = "Fornecedor real", Codigo = 123456789, Ativo = true };
        var fornecedores = new List<Fornecedor> { semCodigoA, semCodigoB, codigoRealNoveDigitos };
        var registros = new List<FornecedorIdentidadeRegistro>();

        var ids = FornecedorIdentidadeService.Reconciliar(fornecedores, registros, out bool alterado);
        Assert(alterado, "a primeira reconciliação deve criar identidades internas");
        Assert(FornecedorIdentidadeService.IdInternoValido(ids[semCodigoA]),
            "o fornecedor sem código deve receber ID interno de 9 dígitos");
        Assert(FornecedorIdentidadeService.IdInternoValido(ids[semCodigoB]),
            "o segundo fornecedor sem código deve receber ID interno de 9 dígitos");
        Assert(ids[semCodigoA] != ids[semCodigoB],
            "fornecedores sem código devem receber IDs internos diferentes");
        Assert(ids[semCodigoA] != codigoRealNoveDigitos.Codigo && ids[semCodigoB] != codigoRealNoveDigitos.Codigo,
            "o ID interno não pode colidir com um código real existente de 9 dígitos");
        Assert(FornecedorIdentidadeService.CodigoVisivel(semCodigoA.Codigo) == string.Empty,
            "código 0 deve continuar totalmente vazio para o usuário");

        int idOriginal = ids[semCodigoA];
        semCodigoA.Codigo = 339;

        var idsDepoisDoCodigo = FornecedorIdentidadeService.Reconciliar(fornecedores, registros, out _);
        Assert(idsDepoisDoCodigo[semCodigoA] == idOriginal,
            "adicionar um código real depois não pode trocar a identidade interna do fornecedor");
    }

    private static void DeveHabilitarCadastroSemCodigoVisivel()
    {
        var window = new MainWindow();
        try
        {
            window.FornecedorCodigoTextBox.Text = string.Empty;
            window.FornecedorNomeTextBox.Text = "Fornecedor sem código";

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
