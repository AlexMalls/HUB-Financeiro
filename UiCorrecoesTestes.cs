namespace HubFinanceiro;

public static class UiCorrecoesTestes
{
    public static void Executar()
    {
        DeveFiltrarFornecedoresAtivosPorNome();
        DeveOcultarFornecedorSemEmailDaLista();
        DeveConstruirTabelaFornecedoresMesmoComPainelEmailOculto();
        DeveAlinharLayoutComPesquisaFornecedores();
        DeveUsarEstruturaVisualDaTabelaOpexNosFornecedores();
        DeveReservarRoxoParaEstadosAtivosNaTelaFornecedores();
        DeveSimplificarAcoesOpexEmMenuFlutuante();
        DeveDimensionarBotoesOpexPeloConteudo();
        DeveExibirLixeiraEmCadaRegistroOpex();
        DeveExcluirComDeleteSomenteForaDeCamposEditaveis();
        DeveIgnorarClientesCanceladosPorPadrao();
    }

    private static void DeveFiltrarFornecedoresAtivosPorNome()
    {
        var fornecedores = new[]
        {
            new Fornecedor { Nome = "Alpha Serviços", Email = "alpha@teste.com", Ativo = true },
            new Fornecedor { Nome = "Beta Comércio", Email = "beta@teste.com", Ativo = true },
            new Fornecedor { Nome = "Alpha Inativo", Email = "inativo@teste.com", Ativo = false }
        };

        var resultado = UiCorrecoesPolicy.FiltrarFornecedoresEmail(fornecedores, "alpha");

        Assert(resultado.Count == 1, "a pesquisa deve considerar apenas fornecedores ativos");
        Assert(resultado[0].Nome == "Alpha Serviços", "a pesquisa deve filtrar pelo nome do fornecedor");
    }

    private static void DeveOcultarFornecedorSemEmailDaLista()
    {
        var semEmail = new Fornecedor { Nome = "Fornecedor sem e-mail", Email = "   ", Ativo = true };
        var resultado = UiCorrecoesPolicy.FiltrarFornecedoresEmail(new[] { semEmail }, string.Empty);

        Assert(resultado.Count == 0, "fornecedor sem e-mail não deve aparecer na lista do Envio de E-mails");
        Assert(!UiCorrecoesPolicy.FornecedorPodeReceberEmail(semEmail), "fornecedor sem e-mail deve ficar bloqueado para seleção");
    }

    private static void DeveConstruirTabelaFornecedoresMesmoComPainelEmailOculto()
    {
        if (System.Windows.Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            Assert(window.EmailLayoutGrid.Visibility == System.Windows.Visibility.Collapsed,
                "o teste precisa reproduzir o estado inicial oculto do Envio de E-mails");
            Assert(window.FindName("FornecedorEmailPesquisaTextBox") is System.Windows.Controls.TextBox,
                "a pesquisa deve existir antes de o painel de e-mails ficar visível");
            Assert(window.FindName("FornecedorEmailCabecalhoGrid") is System.Windows.Controls.Grid,
                "o cabeçalho Fornecedor | E-mail deve existir antes de o painel ficar visível");
            Assert(window.FindName("FornecedorEmailTabelaBorder") is System.Windows.Controls.Border,
                "a tabela no padrão O.P.E.X. deve existir antes de o painel ficar visível");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveAlinharLayoutComPesquisaFornecedores()
    {
        if (System.Windows.Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            window.EmailLayoutGrid.Visibility = System.Windows.Visibility.Visible;
            window.EmailLayoutGrid.Measure(new System.Windows.Size(1000, 600));
            window.EmailLayoutGrid.Arrange(new System.Windows.Rect(0, 0, 1000, 600));
            window.EmailLayoutGrid.UpdateLayout();

            var pesquisa = window.FindName("FornecedorEmailPesquisaBorder") as System.Windows.FrameworkElement;
            var layout = window.FindName("LayoutEmailBorder") as System.Windows.FrameworkElement;

            Assert(pesquisa != null, "o contêiner da pesquisa de fornecedores deve estar identificado");
            Assert(layout != null, "o card Layout deve estar identificado");

            var topoPesquisa = pesquisa!.TranslatePoint(new System.Windows.Point(0, 0), window.EmailLayoutGrid).Y;
            var topoLayout = layout!.TranslatePoint(new System.Windows.Point(0, 0), window.EmailLayoutGrid).Y;

            Assert(Math.Abs(topoPesquisa - topoLayout) <= 1d,
                "o card Layout deve começar na mesma altura da pesquisa de fornecedores");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveUsarEstruturaVisualDaTabelaOpexNosFornecedores()
    {
        Assert(UiCorrecoesPolicy.AlturaCabecalhoFornecedorEmail == 45d,
            "o cabeçalho deve ter os mesmos 45 px da tabela O.P.E.X.");
        Assert(UiCorrecoesPolicy.AlturaLinhaFornecedorEmail == 38d,
            "as linhas devem ter os mesmos 38 px da tabela O.P.E.X.");
        Assert(UiCorrecoesPolicy.CabecalhoFornecedorEmail == "Fornecedor",
            "a primeira coluna deve se chamar Fornecedor");
        Assert(UiCorrecoesPolicy.CabecalhoEmailFornecedor == "E-mail",
            "a segunda coluna deve se chamar E-mail");
        Assert(UiCorrecoesPolicy.FundoTabelaFornecedorEmail == "#99252526",
            "o fundo da tabela deve usar a mesma transparência da O.P.E.X.");
        Assert(UiCorrecoesPolicy.FundoCabecalhoFornecedorEmail == "#992A2A2D",
            "o cabeçalho deve usar o mesmo fundo semitransparente da O.P.E.X.");
        Assert(UiCorrecoesPolicy.OpacidadeSeparadorFornecedorEmail == 0.3d,
            "o separador das linhas deve usar opacidade 0,3 como a O.P.E.X.");
    }

    private static void DeveReservarRoxoParaEstadosAtivosNaTelaFornecedores()
    {
        if (System.Windows.Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            var contornos = new[]
            {
                window.FindName("FornecedoresListaBorder") as System.Windows.Controls.Border,
                window.FindName("FornecedorPesquisaBorder") as System.Windows.Controls.Border,
                window.FindName("FornecedorInfoBorder") as System.Windows.Controls.Border,
                window.FindName("AdministradoraOptionBorder") as System.Windows.Controls.Border,
                window.FindName("CorretoraOptionBorder") as System.Windows.Controls.Border,
                window.FindName("AtivoOptionBorder") as System.Windows.Controls.Border
            };

            Assert(contornos.All(b => b != null),
                "os principais contornos da tela Fornecedores devem estar identificados");

            var corNeutra = ((System.Windows.Media.SolidColorBrush)window.FindResource("BorderColor")).Color;
            foreach (var contorno in contornos)
            {
                Assert(contorno!.BorderBrush is System.Windows.Media.SolidColorBrush brush
                    && brush.Color == corNeutra,
                    "painéis e opções da tela Fornecedores devem usar borda neutra");
            }

            var campos = new[]
            {
                window.FornecedorNomeTextBox,
                window.FornecedorCodigoTextBox,
                window.FornecedorNaturezaTextBox,
                window.FornecedorEmailTextBox,
                window.FornecedorDiaPagamentoTextBox,
                window.FornecedorTipoPagamentoTextBox
            };

            foreach (var campo in campos)
            {
                Assert(campo.BorderBrush is System.Windows.Media.SolidColorBrush brush
                    && brush.Color == corNeutra,
                    "campos do fornecedor devem usar borda neutra");
            }

            var linhaFornecedor = window.FornecedoresItemsControl.ItemTemplate.LoadContent()
                as System.Windows.Controls.Border;
            Assert(linhaFornecedor != null, "a linha de fornecedor deve continuar sendo um Border selecionável");
            Assert(linhaFornecedor!.BorderThickness.Left == 0d
                && linhaFornecedor.BorderThickness.Top == 0d
                && linhaFornecedor.BorderThickness.Right == 0d
                && linhaFornecedor.BorderThickness.Bottom == 1d,
                "cada fornecedor deve usar apenas um separador inferior, sem caixa roxa completa");
            Assert(linhaFornecedor.BorderBrush is System.Windows.Media.SolidColorBrush separador
                && separador.Color == corNeutra,
                "o separador da lista de fornecedores deve ser neutro");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveSimplificarAcoesOpexEmMenuFlutuante()
    {
        if (System.Windows.Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            var grupo = EncontrarDescendentePorTag<System.Windows.Controls.StackPanel>(
                window.OpexInputsGrid,
                "OpexV8Actions");

            Assert(grupo != null,
                "a O.P.E.X. deve ter um único grupo de ações primárias alinhado à direita");
            Assert(grupo!.Children.Count == 2,
                "apenas Registrar/Atualizar e Ações O.P.E.X. devem ficar aparentes");
            Assert(ReferenceEquals(grupo.Children[0], window.BtnRegistrarPagamento),
                "Registrar/Atualizar Pagamento deve ser o primeiro botão do grupo");

            var botaoAcoes = grupo.Children[1] as System.Windows.Controls.Button;
            Assert(botaoAcoes != null && Equals(botaoAcoes.Content, "Ações O.P.E.X. ▾"),
                "o segundo botão deve abrir as Ações O.P.E.X.");
            Assert(botaoAcoes!.ContextMenu != null,
                "Ações O.P.E.X. deve abrir um menu flutuante");

            var cabecalhos = botaoAcoes.ContextMenu!.Items
                .OfType<System.Windows.Controls.MenuItem>()
                .Select(item => item.Header?.ToString())
                .ToArray();

            Assert(cabecalhos.SequenceEqual(new[]
                {
                    "Importar",
                    "Movimentar Registros",
                    "Conferir Pagamentos"
                }),
                "o menu deve conter Importar, Movimentar Registros e Conferir Pagamentos nessa ordem");

            Assert(window.BtnExcluirPagamento.Parent == null,
                "o botão Excluir Registro antigo não deve permanecer na barra da O.P.E.X.");
            Assert(window.BtnImportar.Parent == null
                && window.BtnMovimentarRegistros.Parent == null
                && window.BtnConferirPagamentos.Parent == null,
                "as três ações secundárias antigas não devem permanecer expostas na barra");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveDimensionarBotoesOpexPeloConteudo()
    {
        if (System.Windows.Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            var grupo = EncontrarDescendentePorTag<System.Windows.Controls.StackPanel>(
                window.OpexInputsGrid,
                "OpexV8Actions");
            Assert(grupo != null, "o grupo de ações O.P.E.X. deve existir");
            Assert(grupo!.HorizontalAlignment == System.Windows.HorizontalAlignment.Right,
                "os botões da O.P.E.X. devem permanecer alinhados à direita");

            var botaoAcoes = grupo.Children.OfType<System.Windows.Controls.Button>().Last();
            Assert(double.IsNaN(window.BtnRegistrarPagamento.Width),
                "Registrar/Atualizar deve usar largura automática baseada no conteúdo");
            Assert(double.IsNaN(botaoAcoes.Width),
                "Ações O.P.E.X. deve usar largura automática baseada no conteúdo");
            Assert(window.BtnRegistrarPagamento.MinWidth <= 1d && botaoAcoes.MinWidth <= 1d,
                "os botões não devem carregar larguras mínimas fixas do layout anterior");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveExibirLixeiraEmCadaRegistroOpex()
    {
        if (System.Windows.Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            var pagamento = new PrevisaoPagamento
            {
                Id = 991,
                CodigoFornecedor = 123,
                NomeFornecedor = "Fornecedor teste V8",
                Natureza = 20805,
                TipoPagamento = 3,
                Valor = 250.75m,
                DataPagamento = new DateTime(2026, 8, 28),
                Empresa = "ADM",
                Status = "Pendente"
            };

            window.OpexLayoutGrid.Visibility = System.Windows.Visibility.Visible;
            window.PagamentosItemsControl.ItemsSource = new[] { pagamento };
            window.OpexLayoutGrid.Measure(new System.Windows.Size(1100, 650));
            window.OpexLayoutGrid.Arrange(new System.Windows.Rect(0, 0, 1100, 650));
            window.OpexLayoutGrid.UpdateLayout();
            window.Dispatcher.Invoke(
                () => { },
                System.Windows.Threading.DispatcherPriority.Loaded);
            window.OpexLayoutGrid.UpdateLayout();

            var container = window.PagamentosItemsControl.ItemContainerGenerator.ContainerFromIndex(0);
            Assert(container != null, "a linha de pagamento de teste deve gerar um container visual");

            var lixeira = EncontrarDescendentePorTag<System.Windows.Controls.Button>(
                container!,
                "OpexExcluirRegistro");
            Assert(lixeira != null,
                "cada registro da O.P.E.X. deve exibir uma lixeira individual à direita");
            Assert(ReferenceEquals(lixeira!.DataContext, pagamento),
                "a lixeira deve apontar para o registro da própria linha");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveExcluirComDeleteSomenteForaDeCamposEditaveis()
    {
        var metodo = typeof(UiCorrecoesPolicy).GetMethod(
            "DeveExcluirPagamentoComDelete",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert(metodo != null,
            "deve existir uma política explícita para exclusão pelo teclado na O.P.E.X.");

        bool deveExcluir = (bool)metodo!.Invoke(null, new object[] { true, true, false })!;
        bool naoExcluirEditando = (bool)metodo.Invoke(null, new object[] { true, true, true })!;
        bool naoExcluirSemSelecao = (bool)metodo.Invoke(null, new object[] { true, false, false })!;
        bool naoExcluirOutraTecla = (bool)metodo.Invoke(null, new object[] { false, true, false })!;

        Assert(deveExcluir,
            "Delete deve excluir quando há registro selecionado e nenhum campo está sendo editado");
        Assert(!naoExcluirEditando,
            "Delete dentro de um campo editável não pode excluir o registro");
        Assert(!naoExcluirSemSelecao,
            "Delete sem registro selecionado não deve excluir nada");
        Assert(!naoExcluirOutraTecla,
            "outras teclas não devem acionar a exclusão");
    }

    private static T? EncontrarDescendentePorTag<T>(System.Windows.DependencyObject raiz, object tag)
        where T : System.Windows.FrameworkElement
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = System.Windows.Media.VisualTreeHelper.GetChild(raiz, i);
            if (filho is T elemento && Equals(elemento.Tag, tag))
                return elemento;

            var encontrado = EncontrarDescendentePorTag<T>(filho, tag);
            if (encontrado != null)
                return encontrado;
        }

        return null;
    }

    private static void DeveIgnorarClientesCanceladosPorPadrao()
    {
        Assert(UiCorrecoesPolicy.IgnorarClientesCanceladosPorPadrao,
            "Ignorar clientes cancelados deve iniciar marcado");
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste de correções de UI: {scenario}.");
    }
}
