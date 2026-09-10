using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HubFinanceiro;

public static class UiCorrecoesOpexV11Testes
{
    public static void Executar()
    {
        GarantirAplicacaoWpf();
        DeveHabilitarBaixaSomenteComRegistroSelecionado();
        DeveBaixarRegistroNaDataEscolhida();
        DeveIniciarJanelaNaDataDeVencimento();
        DevePermitirEditarFornecedorFantasmaSemPersistiLo();
        DevePreservarSelecaoEmControlesDeEdicao();
        DeveExibirExclusaoVisualDeFornecedor();
    }

    private static void GarantirAplicacaoWpf()
    {
        if (Application.Current != null)
            return;

        var app = new App();
        app.InitializeComponent();
    }

    private static void DeveHabilitarBaixaSomenteComRegistroSelecionado()
    {
        var window = new MainWindow();
        try
        {
            var grupo = EncontrarDescendentePorTag<StackPanel>(window.OpexInputsGrid, "OpexV8Actions");
            Assert(grupo != null && grupo.Children.Count == 2,
                "o grupo compacto da O.P.E.X. deve existir");

            var botaoAcoes = grupo!.Children[1] as Button;
            var itemBaixar = botaoAcoes?.ContextMenu?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Baixar Registro", StringComparison.Ordinal));

            Assert(itemBaixar != null, "Ações O.P.E.X. deve conter Baixar Registro");
            Assert(!itemBaixar!.IsEnabled, "Baixar Registro deve iniciar desabilitado sem seleção");

            DefinirCampoPrivado(window, "_pagamentoSelecionado", CriarPagamentoTeste());
            InvocarPrivado(window, "AtualizarEstadoBaixarRegistroOpexV11");
            Assert(itemBaixar.IsEnabled, "Baixar Registro deve habilitar quando existe registro selecionado");

            DefinirCampoPrivado(window, "_pagamentoSelecionado", null);
            InvocarPrivado(window, "AtualizarEstadoBaixarRegistroOpexV11");
            Assert(!itemBaixar.IsEnabled, "Baixar Registro deve desabilitar novamente sem seleção");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveBaixarRegistroNaDataEscolhida()
    {
        var pagamento = CriarPagamentoTeste();
        var dataBaixa = new DateTime(2026, 9, 8);

        MainWindow.AplicarBaixaDiretaOpexV11(pagamento, dataBaixa);

        Assert(pagamento.Status == "Pago", "a baixa direta deve alterar o status para Pago");
        Assert(pagamento.DataProvisionamento == dataBaixa.Date,
            "a baixa direta deve gravar a data escolhida em DataProvisionamento");
    }

    private static void DeveIniciarJanelaNaDataDeVencimento()
    {
        var vencimento = new DateTime(2026, 9, 2);
        var janela = new BaixarRegistroWindow(vencimento);
        try
        {
            Assert(janela.DataBaixaDatePicker.SelectedDate == vencimento.Date,
                "a janela de baixa deve iniciar com a data de vencimento selecionada");
        }
        finally
        {
            janela.Close();
        }
    }

    private static void DevePermitirEditarFornecedorFantasmaSemPersistiLo()
    {
        var window = new MainWindow();
        try
        {
            var pagamento = CriarPagamentoTeste();
            pagamento.CodigoFornecedor = 2343;
            pagamento.NomeFornecedor = "Dev P/ Segurado - Juliana Silva";
            pagamento.Natureza = 10202;
            pagamento.TipoPagamento = 1;
            pagamento.Empresa = "ADM";

            InvocarPrivado(window, "CarregarPagamentoParaEdicao", pagamento);
            Assert(window.OpexFornecedorComboBox.SelectedItem == null,
                "sem cadastro permanente, o fluxo antigo não deve localizar um fornecedor real");
            Assert(!window.BtnRegistrarPagamento.IsEnabled,
                "antes do fallback temporário, a edição deve reproduzir o bloqueio original");

            InvocarPrivado(window, "GarantirFornecedorFantasmaSelecionadoV11");

            var fantasma = window.OpexFornecedorComboBox.SelectedItem as Fornecedor;
            Assert(fantasma != null, "o fornecedor temporário deve ser carregado no campo Fornecedor");
            Assert(fantasma!.Nome == pagamento.NomeFornecedor
                && fantasma.Codigo == pagamento.CodigoFornecedor
                && fantasma.Natureza == pagamento.Natureza
                && fantasma.TipoPagamento == pagamento.TipoPagamento,
                "o fornecedor temporário deve reaproveitar os dados gravados no próprio registro");
            Assert(window.BtnRegistrarPagamento.IsEnabled,
                "o registro com fornecedor temporário deve poder ser atualizado normalmente");

            var fornecedoresPermanentes = ObterCampoPrivado<List<Fornecedor>>(window, "_fornecedoresOpex");
            Assert(fornecedoresPermanentes.Count == 0,
                "o fornecedor temporário não pode ser incluído na lista permanente de fornecedores");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DevePreservarSelecaoEmControlesDeEdicao()
    {
        var window = new MainWindow();
        try
        {
            Assert((bool)InvocarPrivado(window, "CliqueMantemSelecaoOpexV11", new Button())!,
                "clicar em botão deve preservar a seleção");
            Assert((bool)InvocarPrivado(window, "CliqueMantemSelecaoOpexV11", new TextBox())!,
                "clicar em caixa de texto deve preservar a seleção");
            Assert((bool)InvocarPrivado(window, "CliqueMantemSelecaoOpexV11", new ComboBox())!,
                "clicar em ComboBox deve preservar a seleção");
            Assert(!(bool)InvocarPrivado(window, "CliqueMantemSelecaoOpexV11", new Border())!,
                "clicar em área neutra deve permitir a desseleção do registro");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveExibirExclusaoVisualDeFornecedor()
    {
        var window = new MainWindow();
        try
        {
            var fornecedor = new Fornecedor
            {
                Nome = "Fornecedor teste exclusão V11",
                Codigo = 991122,
                Ativo = true
            };

            window.FornecedoresLayoutGrid.Visibility = Visibility.Visible;
            window.FornecedoresItemsControl.ItemsSource = new[] { fornecedor };
            window.FornecedoresLayoutGrid.Measure(new Size(1000, 650));
            window.FornecedoresLayoutGrid.Arrange(new Rect(0, 0, 1000, 650));
            window.FornecedoresLayoutGrid.UpdateLayout();

            var container = window.FornecedoresItemsControl.ItemContainerGenerator.ContainerFromIndex(0);
            Assert(container != null, "a lista de fornecedores deve gerar a linha de teste");

            var lixeira = EncontrarDescendente<Button>(
                container!,
                botao => string.Equals(botao.ToolTip?.ToString(), "Excluir fornecedor", StringComparison.Ordinal));

            Assert(lixeira != null, "cada fornecedor deve exibir uma ação visível de exclusão");
            Assert(ReferenceEquals(lixeira!.DataContext, fornecedor),
                "a exclusão visual deve estar vinculada ao fornecedor da própria linha");
        }
        finally
        {
            window.Close();
        }
    }

    private static PrevisaoPagamento CriarPagamentoTeste()
    {
        return new PrevisaoPagamento
        {
            Id = 11001,
            CodigoFornecedor = 991001,
            NomeFornecedor = "Fornecedor teste V11",
            Natureza = 10202,
            TipoPagamento = 1,
            Valor = 358.31m,
            DataPagamento = new DateTime(2026, 9, 2),
            Status = "Pendente",
            Empresa = "ADM",
            DataProvisionamento = null
        };
    }

    private static object? InvocarPrivado(object alvo, string nome, params object[] argumentos)
    {
        var metodo = alvo.GetType().GetMethod(nome, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"V11: método interno '{nome}' não encontrado.");
        return metodo.Invoke(alvo, argumentos);
    }

    private static void DefinirCampoPrivado(object alvo, string nome, object? valor)
    {
        var campo = alvo.GetType().GetField(nome, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"V11: campo interno '{nome}' não encontrado.");
        campo.SetValue(alvo, valor);
    }

    private static T ObterCampoPrivado<T>(object alvo, string nome)
    {
        var campo = alvo.GetType().GetField(nome, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"V11: campo interno '{nome}' não encontrado.");
        return (T)(campo.GetValue(alvo)
            ?? throw new InvalidOperationException($"V11: campo interno '{nome}' está nulo."));
    }

    private static T? EncontrarDescendentePorTag<T>(DependencyObject raiz, object tag)
        where T : FrameworkElement
        => EncontrarDescendente<T>(raiz, elemento => Equals(elemento.Tag, tag));

    private static T? EncontrarDescendente<T>(DependencyObject raiz, Func<T, bool> predicado)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T elemento && predicado(elemento))
                return elemento;

            var encontrado = EncontrarDescendente<T>(filho, predicado);
            if (encontrado != null)
                return encontrado;
        }

        return null;
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste O.P.E.X. V11: {scenario}.");
    }
}
