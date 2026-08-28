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
        DeveManterAcoesOpexVisiveisEmLarguraCompacta();
        DeveIgnorarClientesCanceladosPorPadrao();
        DeveReservarLarguraSuficienteParaBotoesOpex();
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

    private static void DeveManterAcoesOpexVisiveisEmLarguraCompacta()
    {
        if (System.Windows.Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
        }

        var window = new MainWindow();
        try
        {
            const double larguraCompacta = 1000d;
            window.OpexLayoutGrid.Visibility = System.Windows.Visibility.Visible;
            window.OpexInputsGrid.Measure(new System.Windows.Size(larguraCompacta, double.PositiveInfinity));
            var altura = Math.Max(84d, window.OpexInputsGrid.DesiredSize.Height);
            window.OpexInputsGrid.Arrange(new System.Windows.Rect(0, 0, larguraCompacta, altura));
            window.OpexInputsGrid.UpdateLayout();

            var botoes = new System.Windows.FrameworkElement[]
            {
                window.BtnRegistrarPagamento,
                window.BtnExcluirPagamento,
                window.BtnImportar,
                window.BtnMovimentarRegistros,
                window.BtnConferirPagamentos
            };

            foreach (var botao in botoes)
            {
                var posicao = botao.TranslatePoint(new System.Windows.Point(0, 0), window.OpexInputsGrid);
                Assert(posicao.X >= -1d && posicao.X + botao.ActualWidth <= larguraCompacta + 1d,
                    $"o botão {botao.Name} deve ficar totalmente visível em uma área de 1000 px");
            }

            var topoRegistrar = window.BtnRegistrarPagamento
                .TranslatePoint(new System.Windows.Point(0, 0), window.OpexInputsGrid).Y;
            var topoImportar = window.BtnImportar
                .TranslatePoint(new System.Windows.Point(0, 0), window.OpexInputsGrid).Y;
            Assert(topoImportar > topoRegistrar + 1d,
                "as ações secundárias da O.P.E.X. devem ocupar uma segunda linha");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DeveIgnorarClientesCanceladosPorPadrao()
    {
        Assert(UiCorrecoesPolicy.IgnorarClientesCanceladosPorPadrao,
            "Ignorar clientes cancelados deve iniciar marcado");
    }

    private static void DeveReservarLarguraSuficienteParaBotoesOpex()
    {
        Assert(UiCorrecoesPolicy.LarguraMinimaRegistrarPagamento >= 185,
            "Registrar/Alterar Pagamento precisa de largura suficiente para o texto completo");
        Assert(UiCorrecoesPolicy.LarguraConferirPagamentos >= 175,
            "Conferir Pagamentos precisa de largura suficiente para o texto completo");
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Falha no teste de correções de UI: {scenario}.");
    }
}
