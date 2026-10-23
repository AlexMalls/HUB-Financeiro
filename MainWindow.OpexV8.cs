using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HubFinanceiro;

public partial class MainWindow
{
    private const string OpexV8ActionsTag = "OpexV8Actions";
    private const string OpexExcluirRegistroTag = "OpexExcluirRegistro";

    private bool _opexV8Configurado;
    private Button? _btnAcoesOpex;
    private ContextMenu? _menuAcoesOpex;

    private void ConfigurarOpexV8()
    {
        if (_opexV8Configurado)
            return;

        _opexV8Configurado = true;

        RemoverAcoesOpexV7();
        CriarGrupoAcoesOpex();
        ConfigurarCabecalhoTabelaOpex();
        ConfigurarTemplateEstavelPagamentosOpex();
        ConfigurarFornecedoresV10();

        OpexLayoutGrid.PreviewKeyDown -= OpexLayoutGrid_V8PreviewKeyDown;
        OpexLayoutGrid.PreviewKeyDown += OpexLayoutGrid_V8PreviewKeyDown;
    }

    private void RemoverAcoesOpexV7()
    {
        if (BtnExcluirPagamento.Parent is Panel painelExcluir)
            painelExcluir.Children.Remove(BtnExcluirPagamento);

        if (BtnImportar.Parent is Panel painelSecundario)
        {
            painelSecundario.Children.Remove(BtnImportar);
            painelSecundario.Children.Remove(BtnMovimentarRegistros);
            painelSecundario.Children.Remove(BtnConferirPagamentos);

            if (painelSecundario.Parent is Panel paiPainelSecundario)
                paiPainelSecundario.Children.Remove(painelSecundario);
        }

        if (OpexInputsGrid.RowDefinitions.Count >= 3)
        {
            OpexInputsGrid.RowDefinitions[1].Height = new GridLength(0);
            OpexInputsGrid.RowDefinitions[2].Height = new GridLength(0);
        }
    }

    private void CriarGrupoAcoesOpex()
    {
        if (BtnRegistrarPagamento.Parent is Panel painelRegistrar)
            painelRegistrar.Children.Remove(BtnRegistrarPagamento);

        BtnRegistrarPagamento.Width = double.NaN;
        BtnRegistrarPagamento.MinWidth = 0;
        BtnRegistrarPagamento.HorizontalAlignment = HorizontalAlignment.Left;
        BtnRegistrarPagamento.VerticalAlignment = VerticalAlignment.Center;
        BtnRegistrarPagamento.Margin = new Thickness(0);

        var grupoAcoes = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = OpexV8ActionsTag
        };

        Grid.SetRow(grupoAcoes, 0);
        Grid.SetColumn(grupoAcoes, 6);
        Grid.SetColumnSpan(grupoAcoes, 3);

        grupoAcoes.Children.Add(BtnRegistrarPagamento);

        _btnAcoesOpex = new Button
        {
            Content = "Ações O.P.E.X. ▾",
            Height = 38,
            Width = double.NaN,
            MinWidth = 0,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("PrimaryButtonStyle"),
            Cursor = Cursors.Hand
        };

        _menuAcoesOpex = CriarMenuAcoesOpex();
        _btnAcoesOpex.ContextMenu = _menuAcoesOpex;
        _btnAcoesOpex.Click += BtnAcoesOpex_Click;

        grupoAcoes.Children.Add(_btnAcoesOpex);
        OpexInputsGrid.Children.Add(grupoAcoes);
    }

    private ContextMenu CriarMenuAcoesOpex()
    {
        var menu = new ContextMenu
        {
            Background = (Brush)FindResource("BackgroundMedium"),
            BorderBrush = (Brush)FindResource("AccentColor"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            MinWidth = 230,
            HasDropShadow = true,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            Template = CriarTemplateContextMenuOpex()
        };

        var estiloOpcao = new Style(typeof(MenuItem));
        estiloOpcao.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        estiloOpcao.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)FindResource("TextColor")));
        estiloOpcao.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 10, 14, 10)));
        estiloOpcao.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        estiloOpcao.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        estiloOpcao.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        estiloOpcao.Setters.Add(new Setter(Control.TemplateProperty, CriarTemplateItemMenuOpex()));

        menu.Items.Add(CriarOpcaoMenuOpex("Provisionar Pagamentos", ExecutarProvisionarPagamentosOpexV10, estiloOpcao));
        menu.Items.Add(CriarOpcaoMenuOpex("Desprovisionar Pagamento", ExecutarDesprovisionarPagamentoOpexV10, estiloOpcao));
        menu.Items.Add(CriarOpcaoMenuOpex("Liquidar Pagamentos", ExecutarLiquidarPagamentosOpexV10, estiloOpcao));
        menu.Items.Add(CriarOpcaoMenuOpex("Relatório de Pagamentos", ExecutarRelatorioPagamentosOpexV10, estiloOpcao));
        menu.Items.Add(CriarOpcaoMenuOpex("Importar", BtnImportar, estiloOpcao));
        menu.Items.Add(CriarOpcaoMenuOpex("Conferir Pagamentos", BtnConferirPagamentos, estiloOpcao));

        return menu;
    }

    private ControlTemplate CriarTemplateContextMenuOpex()
    {
        var template = new ControlTemplate(typeof(ContextMenu));

        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        borda.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        borda.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        borda.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        borda.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        borda.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 14,
            ShadowDepth = 4,
            Direction = 270,
            Opacity = 0.45
        });

        var itens = new FrameworkElementFactory(typeof(StackPanel));
        itens.SetValue(Panel.IsItemsHostProperty, true);
        itens.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        borda.AppendChild(itens);

        template.VisualTree = borda;
        return template;
    }

    private ControlTemplate CriarTemplateItemMenuOpex()
    {
        var template = new ControlTemplate(typeof(MenuItem));

        var borda = new FrameworkElementFactory(typeof(Border), "ItemBorder");
        borda.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        borda.SetValue(FrameworkElement.MarginProperty, new Thickness(1));

        var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
        conteudo.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(MenuItem.Header))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        conteudo.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding(nameof(MenuItem.HeaderTemplate))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        conteudo.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, new Binding(nameof(MenuItem.HeaderTemplateSelector))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        conteudo.SetBinding(ContentPresenter.MarginProperty, new Binding(nameof(Control.Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        conteudo.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        conteudo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        conteudo.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        borda.AppendChild(conteudo);

        template.VisualTree = borda;

        var destacado = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true
        };
        destacado.Setters.Add(new Setter(Border.BackgroundProperty, CriarBrushOpex("#342244"), "ItemBorder"));
        template.Triggers.Add(destacado);

        var desabilitado = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        desabilitado.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(desabilitado);

        return template;
    }

    private static MenuItem CriarOpcaoMenuOpex(string texto, Button acaoOriginal, Style estilo)
    {
        var item = new MenuItem
        {
            Header = texto,
            Style = estilo
        };

        item.Click += (_, _) =>
            acaoOriginal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, acaoOriginal));

        return item;
    }

    private static MenuItem CriarOpcaoMenuOpex(string texto, Action acao, Style estilo)
    {
        var item = new MenuItem
        {
            Header = texto,
            Style = estilo
        };

        item.Click += (_, _) => acao();
        return item;
    }

    private void BtnAcoesOpex_Click(object sender, RoutedEventArgs e)
    {
        if (_btnAcoesOpex == null || _menuAcoesOpex == null)
            return;

        _menuAcoesOpex.PlacementTarget = _btnAcoesOpex;
        _menuAcoesOpex.Placement = PlacementMode.Bottom;
        _menuAcoesOpex.HorizontalOffset = 0;
        _menuAcoesOpex.VerticalOffset = 4;
        _menuAcoesOpex.IsOpen = true;
    }

    private void ConfigurarCabecalhoTabelaOpex()
    {
        var cabecalho = EncontrarGridCabecalhoOpex(OpexLayoutGrid);
        if (cabecalho == null || cabecalho.ColumnDefinitions.Count >= 9)
            return;

        cabecalho.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(40)
        });
    }

    private static Grid? EncontrarGridCabecalhoOpex(DependencyObject raiz)
    {
        if (raiz is Grid grid
            && Math.Abs(grid.Height - 45d) < 0.1d
            && grid.ColumnDefinitions.Count == 8
            && grid.Children.OfType<TextBlock>().Any(t => string.Equals(t.Text, "Status", StringComparison.Ordinal)))
        {
            return grid;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var encontrado = EncontrarGridCabecalhoOpex(VisualTreeHelper.GetChild(raiz, i));
            if (encontrado != null)
                return encontrado;
        }

        return null;
    }

    private void ConfigurarTemplateEstavelPagamentosOpex()
    {
        var templateOriginal = PagamentosItemsControl.ItemTemplate;
        if (templateOriginal == null)
            return;

        var raiz = new FrameworkElementFactory(typeof(DockPanel));
        raiz.SetValue(DockPanel.LastChildFillProperty, true);
        raiz.SetValue(FrameworkElement.HeightProperty, 38d);

        var botaoExcluir = new FrameworkElementFactory(typeof(Button));
        botaoExcluir.SetValue(FrameworkElement.TagProperty, OpexExcluirRegistroTag);
        botaoExcluir.SetValue(FrameworkElement.ToolTipProperty, "Excluir registro");
        botaoExcluir.SetValue(FrameworkElement.StyleProperty, (Style)FindResource("CnabActionIconButtonStyle"));
        botaoExcluir.SetValue(Control.ForegroundProperty, CriarBrushOpex("#D56A6A"));
        botaoExcluir.SetValue(UIElement.FocusableProperty, false);
        botaoExcluir.SetValue(DockPanel.DockProperty, Dock.Right);
        botaoExcluir.AddHandler(Button.ClickEvent, new RoutedEventHandler(BtnExcluirPagamentoLinha_Click));

        var iconeExcluir = new FrameworkElementFactory(typeof(TextBlock));
        iconeExcluir.SetValue(TextBlock.TextProperty, "\uE74D");
        iconeExcluir.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        iconeExcluir.SetValue(TextBlock.FontSizeProperty, 17d);
        iconeExcluir.SetValue(TextBlock.ForegroundProperty, CriarBrushOpex("#D56A6A"));
        iconeExcluir.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        iconeExcluir.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        botaoExcluir.AppendChild(iconeExcluir);
        raiz.AppendChild(botaoExcluir);

        var conteudoOriginal = new FrameworkElementFactory(typeof(ContentPresenter));
        conteudoOriginal.SetBinding(ContentPresenter.ContentProperty, new Binding());
        conteudoOriginal.SetValue(ContentPresenter.ContentTemplateProperty, templateOriginal);
        conteudoOriginal.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        raiz.AppendChild(conteudoOriginal);

        PagamentosItemsControl.ItemTemplate = new DataTemplate(typeof(PrevisaoPagamento))
        {
            VisualTree = raiz
        };
    }

    private void BtnExcluirPagamentoLinha_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PrevisaoPagamento pagamento })
            return;

        e.Handled = true;
        ExcluirPagamentoOpex(pagamento);
    }

    private void OpexLayoutGrid_V8PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var focoEmCampoEditavel = FocoOpexEmCampoEditavel();
        var deveExcluir = UiCorrecoesPolicy.DeveExcluirPagamentoComDelete(
            e.Key == Key.Delete,
            _pagamentoSelecionado != null,
            focoEmCampoEditavel);

        if (!deveExcluir || _pagamentoSelecionado == null)
            return;

        e.Handled = true;
        ExcluirPagamentoOpex(_pagamentoSelecionado);
    }

    private static bool FocoOpexEmCampoEditavel()
    {
        var foco = Keyboard.FocusedElement;

        if (foco is TextBoxBase textBox)
            return !textBox.IsReadOnly;

        if (foco is PasswordBox)
            return true;

        if (foco is ComboBox)
            return true;

        return false;
    }

    private void ExcluirPagamentoOpex(PrevisaoPagamento pagamento)
    {
        try
        {
            string detalhe = $"{pagamento.NomeFornecedor} - R$ {pagamento.Valor:N2} - {pagamento.DataPagamento:dd/MM/yyyy}";
            bool confirmado = MostrarPergunta(
                "Você deseja excluir o pagamento:",
                "Excluir Pagamento",
                detalhe);

            if (!confirmado)
                return;

            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            var pagamentos = CarregarPrevisoesPagamento();
            int removidos = pagamentos.RemoveAll(p => p.Id == pagamento.Id);

            if (removidos == 0)
                return;

            SalvarPrevisoes(pagamentos, caminhoArquivo);
            RecarregarPagamentos();
            MostrarSucesso("Pagamento excluído com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao excluir pagamento", ex);
        }
    }

    private void ExecutarProvisionarPagamentosOpexV10()
    {
        try
        {
            if (!ValidarPagamentosNoEscopoOpexV10())
            {
                CustomMessageBox.ShowInformation("Não há registros programados para o próximo escopo de pagamento.");
                return;
            }

            var janela = new ProvisionamentoWindow { Owner = this };
            janela.ShowDialog();
            RecarregarPagamentos();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao abrir provisionamento: {ex.Message}");
        }
    }

    private void ExecutarDesprovisionarPagamentoOpexV10()
    {
        try
        {
            var pagamentos = CarregarPrevisoesPagamento();
            if (!pagamentos.Any(p => p.Status == "No Banco"))
            {
                CustomMessageBox.ShowInformation("Não há pagamentos provisionados para reverter.");
                return;
            }

            var janela = new DeprovisionamentoWindow { Owner = this };
            janela.ShowDialog();
            RecarregarPagamentos();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao abrir desprovisionamento: {ex.Message}");
        }
    }

    private bool ValidarPagamentosNoEscopoOpexV10()
    {
        try
        {
            var pagamentos = CarregarPrevisoesPagamento();
            var (dataInicio, dataFim) = CalcularProximoEscopoOpexV10(DateTime.Now);

            return pagamentos.Any(p =>
                p.Status == "Pendente"
                && p.DataPagamento.Date >= dataInicio.Date
                && p.DataPagamento.Date < dataFim.Date);
        }
        catch
        {
            return true;
        }
    }

    private static (DateTime inicio, DateTime fim) CalcularProximoEscopoOpexV10(DateTime dataAtual)
    {
        int ultimoDiaMes = DateTime.DaysInMonth(dataAtual.Year, dataAtual.Month);
        int[] marcos = { 1, 5, 10, 15, 20, 25, ultimoDiaMes };

        for (var i = 0; i < marcos.Length; i++)
        {
            if (marcos[i] < dataAtual.Day)
                continue;

            if (i + 1 < marcos.Length)
            {
                return (
                    dataAtual.Date,
                    new DateTime(dataAtual.Year, dataAtual.Month, marcos[i + 1]));
            }

            var proximoMes = new DateTime(dataAtual.Year, dataAtual.Month, 1).AddMonths(1);
            return (dataAtual.Date, new DateTime(proximoMes.Year, proximoMes.Month, 5));
        }

        var mesSeguinte = new DateTime(dataAtual.Year, dataAtual.Month, 1).AddMonths(1);
        return (
            new DateTime(mesSeguinte.Year, mesSeguinte.Month, 1),
            new DateTime(mesSeguinte.Year, mesSeguinte.Month, 5));
    }

    private void ExecutarLiquidarPagamentosOpexV10()
    {
        try
        {
            var provisionados = CarregarPrevisoesPagamento()
                .Where(p => p.Status == "No Banco" && p.DataProvisionamento.HasValue)
                .ToList();

            if (!provisionados.Any())
            {
                CustomMessageBox.ShowInformation(
                    "Não há pagamentos provisionados para liquidar.",
                    "Informação");
                return;
            }

            var datas = provisionados
                .GroupBy(p => p.DataProvisionamento!.Value.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DataProvisionamentoInfo
                {
                    Data = g.Key,
                    Quantidade = g.Count(),
                    Total = g.Sum(p => p.Valor)
                })
                .ToList();

            var selecionarDataWindow = new SelecionarDataProvisionamentoWindow(datas)
            {
                Owner = this
            };

            if (selecionarDataWindow.ShowDialog() != true || !selecionarDataWindow.DataSelecionada.HasValue)
                return;

            var liquidacaoWindow = new LiquidacaoWindow(selecionarDataWindow.DataSelecionada.Value)
            {
                Owner = this
            };
            liquidacaoWindow.ShowDialog();
            RecarregarPagamentos();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao processar liquidação: {ex.Message}");
        }
    }

    private void ExecutarRelatorioPagamentosOpexV10()
    {
        try
        {
            var provisionados = CarregarPrevisoesPagamento()
                .Where(p => p.Status == "No Banco" && p.DataProvisionamento.HasValue)
                .ToList();

            if (!provisionados.Any())
            {
                CustomMessageBox.ShowInformation("Não há pagamentos com status \"No Banco\" para gerar relatório.");
                return;
            }

            var datas = provisionados
                .GroupBy(p => p.DataProvisionamento!.Value.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DataProvisionamentoInfo
                {
                    Data = g.Key,
                    Quantidade = g.Count(),
                    Total = g.Sum(p => p.Valor)
                })
                .ToList();

            var selecionarDataWindow = new SelecionarDataProvisionamentoWindow(datas)
            {
                Owner = this
            };

            if (selecionarDataWindow.ShowDialog() != true || !selecionarDataWindow.DataSelecionada.HasValue)
                return;

            var dataSelecionada = selecionarDataWindow.DataSelecionada.Value.Date;
            var pagamentosData = provisionados
                .Where(p => p.DataProvisionamento!.Value.Date == dataSelecionada)
                .ToList();

            GerarRelatorioPagamentosOpexV10(
                dataSelecionada,
                pagamentosData.Where(p => p.Empresa == "ADM").OrderBy(p => p.NomeFornecedor).ToList(),
                pagamentosData.Where(p => p.Empresa == "COR").OrderBy(p => p.NomeFornecedor).ToList());
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao gerar relatório: {ex.Message}");
        }
    }

    private static void GerarRelatorioPagamentosOpexV10(
        DateTime data,
        List<PrevisaoPagamento> admPagamentos,
        List<PrevisaoPagamento> corPagamentos)
    {
        try
        {
            var relatorio = new StringBuilder();
            relatorio.AppendLine("*Pagamentos Administradora*");
            relatorio.AppendLine($"*{data:dd/MM}*");
            relatorio.AppendLine();

            if (admPagamentos.Any())
            {
                foreach (var pagamento in admPagamentos)
                    relatorio.AppendLine($"{pagamento.NomeFornecedor} - R$ {pagamento.Valor:N2}");
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
                foreach (var pagamento in corPagamentos)
                    relatorio.AppendLine($"{pagamento.NomeFornecedor} - R$ {pagamento.Valor:N2}");
            }
            else
            {
                relatorio.AppendLine("(Nenhum pagamento)");
            }

            string caminhoCompleto = Path.Combine(
                Path.GetTempPath(),
                $"Relatorio_Pagamentos_{data:yyyy-MM-dd}.txt");

            File.WriteAllText(caminhoCompleto, relatorio.ToString(), Encoding.UTF8);
            Process.Start(new ProcessStartInfo
            {
                FileName = caminhoCompleto,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao criar arquivo de relatório: {ex.Message}");
        }
    }

    private static SolidColorBrush CriarBrushOpex(string cor)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cor));
        brush.Freeze();
        return brush;
    }
}
