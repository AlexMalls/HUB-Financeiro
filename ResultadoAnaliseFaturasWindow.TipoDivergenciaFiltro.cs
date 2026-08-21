using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace HubFinanceiro;

public partial class ResultadoAnaliseFaturasWindow
{
    private bool _tipoDivergenciaFiltroInicializado;
    private bool _tipoDivergenciaFiltroAtualizandoOpcoes;
    private bool _tipoDivergenciaFiltroAtualizacaoAgendada;

    private DataGrid? _tipoDivergenciaFiltroGrid;
    private ICollectionView? _tipoDivergenciaFiltroView;
    private Predicate<object>? _tipoDivergenciaFiltroBase;
    private Predicate<object>? _tipoDivergenciaFiltroCombinado;

    private void TipoDivergenciaFiltroComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (_tipoDivergenciaFiltroInicializado)
            return;

        _tipoDivergenciaFiltroInicializado = true;

        // Escuta mudancas dos filtros que ja existem na janela.
        // O filtro por tipo sempre e reaplicado depois de Status/Pesquisa.
        AddHandler(
            Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(TipoDivergenciaFiltro_QualquerSelecaoChanged),
            true);

        AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(TipoDivergenciaFiltro_QualquerTextoChanged),
            true);

        Dispatcher.BeginInvoke(
            new Action(() => TipoDivergenciaFiltro_Sincronizar(atualizarOpcoes: true)),
            DispatcherPriority.Loaded);
    }

    private void TipoDivergenciaFiltroComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tipoDivergenciaFiltroAtualizandoOpcoes)
            return;

        TipoDivergenciaFiltro_Sincronizar(atualizarOpcoes: false);
    }

    private void TipoDivergenciaFiltro_QualquerSelecaoChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_tipoDivergenciaFiltroInicializado ||
            _tipoDivergenciaFiltroAtualizandoOpcoes)
        {
            return;
        }

        if (ReferenceEquals(e.Source, TipoDivergenciaFiltroComboBox) ||
            ReferenceEquals(e.OriginalSource, TipoDivergenciaFiltroComboBox))
        {
            return;
        }

        // So nos interessam selecoes vindas de outros ComboBox.
        if (e.OriginalSource is not ComboBox && e.Source is not ComboBox)
            return;

        TipoDivergenciaFiltro_AgendarSincronizacao();
    }

    private void TipoDivergenciaFiltro_QualquerTextoChanged(object sender, TextChangedEventArgs e)
    {
        if (!_tipoDivergenciaFiltroInicializado ||
            _tipoDivergenciaFiltroAtualizandoOpcoes)
        {
            return;
        }

        // Na janela de resultado, o TextBox editavel da faixa principal e a pesquisa.
        // Agendar no Background garante que o filtro original rode primeiro.
        TipoDivergenciaFiltro_AgendarSincronizacao();
    }

    private void TipoDivergenciaFiltro_AgendarSincronizacao()
    {
        if (_tipoDivergenciaFiltroAtualizacaoAgendada)
            return;

        _tipoDivergenciaFiltroAtualizacaoAgendada = true;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _tipoDivergenciaFiltroAtualizacaoAgendada = false;
                TipoDivergenciaFiltro_Sincronizar(atualizarOpcoes: true);
            }),
            DispatcherPriority.Background);
    }

    private void TipoDivergenciaFiltro_Sincronizar(bool atualizarOpcoes)
    {
        DataGrid? grid = TipoDivergenciaFiltro_EncontrarGrid();
        if (grid?.ItemsSource == null)
            return;

        ICollectionView view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        if (view == null)
            return;

        TipoDivergenciaFiltro_CapturarFiltroBase(view);

        if (atualizarOpcoes)
            TipoDivergenciaFiltro_AtualizarOpcoes(view);

        TipoDivergenciaFiltro_Aplicar(view);
    }

    private void TipoDivergenciaFiltro_CapturarFiltroBase(ICollectionView view)
    {
        if (!ReferenceEquals(_tipoDivergenciaFiltroView, view))
        {
            _tipoDivergenciaFiltroView = view;
            _tipoDivergenciaFiltroBase = view.Filter;
            _tipoDivergenciaFiltroCombinado = null;
            return;
        }

        // Se Status/Pesquisa trocaram o Filter da mesma view,
        // o novo predicate passa a ser a nossa base.
        if (_tipoDivergenciaFiltroCombinado != null &&
            !ReferenceEquals(view.Filter, _tipoDivergenciaFiltroCombinado))
        {
            _tipoDivergenciaFiltroBase = view.Filter;
            _tipoDivergenciaFiltroCombinado = null;
        }
    }

    private void TipoDivergenciaFiltro_AtualizarOpcoes(ICollectionView view)
    {
        string selecionado = TipoDivergenciaFiltroComboBox.SelectedItem?.ToString() ?? "Todos";

        IEnumerable fonte = view.SourceCollection ?? Array.Empty<object>();
        var tipos = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (object? item in fonte)
        {
            if (item == null)
                continue;

            bool passaNoFiltroBase;
            try
            {
                passaNoFiltroBase = _tipoDivergenciaFiltroBase?.Invoke(item) ?? true;
            }
            catch
            {
                passaNoFiltroBase = true;
            }

            if (!passaNoFiltroBase)
                continue;

            string tipo = TipoDivergenciaFiltro_ObterTipo(item);
            if (!string.IsNullOrWhiteSpace(tipo))
                tipos.Add(tipo.Trim());
        }

        var opcoes = new List<string> { "Todos" };
        opcoes.AddRange(tipos.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));

        if (!opcoes.Contains(selecionado, StringComparer.CurrentCultureIgnoreCase))
            selecionado = "Todos";

        _tipoDivergenciaFiltroAtualizandoOpcoes = true;
        try
        {
            TipoDivergenciaFiltroComboBox.ItemsSource = opcoes;
            TipoDivergenciaFiltroComboBox.SelectedItem =
                opcoes.First(x => string.Equals(
                    x,
                    selecionado,
                    StringComparison.CurrentCultureIgnoreCase));
        }
        finally
        {
            _tipoDivergenciaFiltroAtualizandoOpcoes = false;
        }
    }

    private void TipoDivergenciaFiltro_Aplicar(ICollectionView view)
    {
        string tipoSelecionado =
            TipoDivergenciaFiltroComboBox.SelectedItem?.ToString()?.Trim() ?? "Todos";

        if (string.IsNullOrWhiteSpace(tipoSelecionado) ||
            string.Equals(tipoSelecionado, "Todos", StringComparison.CurrentCultureIgnoreCase))
        {
            if (!ReferenceEquals(view.Filter, _tipoDivergenciaFiltroBase))
            {
                view.Filter = _tipoDivergenciaFiltroBase;
                view.Refresh();
            }

            _tipoDivergenciaFiltroCombinado = null;
            return;
        }

        Predicate<object>? filtroBase = _tipoDivergenciaFiltroBase;

        Predicate<object> combinado = item =>
        {
            bool passaBase;
            try
            {
                passaBase = filtroBase?.Invoke(item) ?? true;
            }
            catch
            {
                passaBase = true;
            }

            if (!passaBase)
                return false;

            string tipo = TipoDivergenciaFiltro_ObterTipo(item);

            return string.Equals(
                tipo?.Trim(),
                tipoSelecionado,
                StringComparison.CurrentCultureIgnoreCase);
        };

        _tipoDivergenciaFiltroCombinado = combinado;
        view.Filter = combinado;
        view.Refresh();
    }

    private DataGrid? TipoDivergenciaFiltro_EncontrarGrid()
    {
        if (_tipoDivergenciaFiltroGrid != null &&
            _tipoDivergenciaFiltroGrid.IsLoaded)
        {
            return _tipoDivergenciaFiltroGrid;
        }

        IReadOnlyList<DataGrid> grids =
            TipoDivergenciaFiltro_Descendentes<DataGrid>(this).ToList();

        _tipoDivergenciaFiltroGrid = grids.FirstOrDefault(g =>
            g.Columns.Any(c =>
                TipoDivergenciaFiltro_Cabecalho(c)
                    .Contains("Tipo de divergência", StringComparison.CurrentCultureIgnoreCase)))
            ?? grids.FirstOrDefault(g => g.ItemsSource != null);

        return _tipoDivergenciaFiltroGrid;
    }

    private static string TipoDivergenciaFiltro_Cabecalho(DataGridColumn coluna)
    {
        return coluna.Header switch
        {
            string s => s,
            TextBlock t => t.Text ?? string.Empty,
            ContentControl c => c.Content?.ToString() ?? string.Empty,
            _ => coluna.Header?.ToString() ?? string.Empty
        };
    }

    private static string TipoDivergenciaFiltro_ObterTipo(object item)
    {
        if (item is AnaliseFinalResultado resultado)
            return resultado.TipoDivergencia ?? string.Empty;

        string? direto = TipoDivergenciaFiltro_LerPropriedade(
            item,
            "TipoDivergencia",
            "TipoDeDivergencia",
            "TipoDivergenciaTexto");

        if (!string.IsNullOrWhiteSpace(direto))
            return direto;

        // Fallback para wrappers usados apenas pela apresentacao.
        foreach (string nomeInterno in new[] { "Resultado", "Item", "Original" })
        {
            object? interno = TipoDivergenciaFiltro_LerObjeto(item, nomeInterno);
            if (interno == null || ReferenceEquals(interno, item))
                continue;

            string? valor = TipoDivergenciaFiltro_LerPropriedade(
                interno,
                "TipoDivergencia",
                "TipoDeDivergencia",
                "TipoDivergenciaTexto");

            if (!string.IsNullOrWhiteSpace(valor))
                return valor;
        }

        return string.Empty;
    }

    private static string? TipoDivergenciaFiltro_LerPropriedade(
        object item,
        params string[] nomes)
    {
        foreach (string nome in nomes)
        {
            PropertyInfo? propriedade = item.GetType().GetProperty(
                nome,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.IgnoreCase);

            object? valor = propriedade?.GetValue(item);
            if (valor != null)
                return valor.ToString();
        }

        return null;
    }

    private static object? TipoDivergenciaFiltro_LerObjeto(object item, string nome)
    {
        PropertyInfo? propriedade = item.GetType().GetProperty(
            nome,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.IgnoreCase);

        return propriedade?.GetValue(item);
    }

    private static IEnumerable<T> TipoDivergenciaFiltro_Descendentes<T>(
        DependencyObject raiz)
        where T : DependencyObject
    {
        int total;
        try
        {
            total = VisualTreeHelper.GetChildrenCount(raiz);
        }
        catch
        {
            yield break;
        }

        for (int i = 0; i < total; i++)
        {
            DependencyObject filho = VisualTreeHelper.GetChild(raiz, i);

            if (filho is T encontrado)
                yield return encontrado;

            foreach (T descendente in TipoDivergenciaFiltro_Descendentes<T>(filho))
                yield return descendente;
        }
    }
}