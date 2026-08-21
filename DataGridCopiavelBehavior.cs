using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HubFinanceiro;

/// <summary>
/// Comportamento visual e de cópia usado nas grades do módulo de Análise de Faturas.
/// Mantém seleção por célula, Ctrl+C/menu de contexto, destaca suavemente a linha
/// da célula atual e recorta a própria grade com cantos arredondados.
/// </summary>
public static class DataGridCopiavelBehavior
{
    public static readonly DependencyProperty HabilitadoProperty = DependencyProperty.RegisterAttached(
        "Habilitado",
        typeof(bool),
        typeof(DataGridCopiavelBehavior),
        new PropertyMetadata(false, AoAlterarHabilitado));

    private static readonly DependencyProperty UltimoItemDestacadoProperty = DependencyProperty.RegisterAttached(
        "UltimoItemDestacado",
        typeof(object),
        typeof(DataGridCopiavelBehavior),
        new PropertyMetadata(null));

    private static readonly SolidColorBrush LinhaDestacadaBrush = CriarBrush("#30283A");

    public static void SetHabilitado(DependencyObject elemento, bool valor)
        => elemento.SetValue(HabilitadoProperty, valor);

    public static bool GetHabilitado(DependencyObject elemento)
        => (bool)elemento.GetValue(HabilitadoProperty);

    private static void AoAlterarHabilitado(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        bool habilitado = e.NewValue is bool valor && valor;

        RemoverEventos(grid);

        if (!habilitado)
        {
            LimparLinhaDestacada(grid);
            grid.ClearValue(UIElement.ClipProperty);
            return;
        }

        ConfigurarSelecaoPorCelula(grid);

        grid.PreviewMouseRightButtonDown += Grid_PreviewMouseRightButtonDown;
        grid.PreviewKeyDown += Grid_PreviewKeyDown;
        grid.CurrentCellChanged += Grid_CurrentCellChanged;
        grid.LoadingRow += Grid_LoadingRow;
        grid.SizeChanged += Grid_SizeChanged;
        grid.Loaded += Grid_Loaded;

        if (grid.ContextMenu == null)
            grid.ContextMenu = CriarMenuContexto(grid);
    }

    private static void RemoverEventos(DataGrid grid)
    {
        grid.PreviewMouseRightButtonDown -= Grid_PreviewMouseRightButtonDown;
        grid.PreviewKeyDown -= Grid_PreviewKeyDown;
        grid.CurrentCellChanged -= Grid_CurrentCellChanged;
        grid.LoadingRow -= Grid_LoadingRow;
        grid.SizeChanged -= Grid_SizeChanged;
        grid.Loaded -= Grid_Loaded;
    }

    private static ContextMenu CriarMenuContexto(DataGrid grid)
    {
        var menu = new ContextMenu
        {
            Background = CriarBrush("#252526"),
            Foreground = Brushes.White,
            BorderBrush = CriarBrush("#4B3B59"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3)
        };

        var copiar = new MenuItem
        {
            Header = "Copiar célula",
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            Padding = new Thickness(12, 7, 16, 7)
        };

        copiar.Click += (_, _) => CopiarSelecao(grid);
        menu.Items.Add(copiar);
        return menu;
    }

    private static void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        AtualizarClipArredondado(grid);
        AtualizarLinhaDestacada(grid, grid.CurrentItem);
    }

    private static void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is DataGrid grid)
            AtualizarClipArredondado(grid);
    }

    private static void AtualizarClipArredondado(DataGrid grid)
    {
        if (grid.ActualWidth <= 0 || grid.ActualHeight <= 0)
            return;

        // O Border externo das tabelas usa raio 9/10. Recortar o DataGrid em si
        // impede o template retangular do controle de cobrir esses cantos.
        const double raio = 9.0;
        grid.Clip = new RectangleGeometry(
            new Rect(0, 0, grid.ActualWidth, grid.ActualHeight),
            raio,
            raio);
    }

    private static void Grid_CurrentCellChanged(object? sender, System.EventArgs e)
    {
        if (sender is DataGrid grid)
            AtualizarLinhaDestacada(grid, grid.CurrentItem);
    }

    private static void Grid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        object? itemDestacado = grid.GetValue(UltimoItemDestacadoProperty);
        if (itemDestacado != null && ReferenceEquals(itemDestacado, e.Row.Item))
            e.Row.SetCurrentValue(DataGridRow.BackgroundProperty, LinhaDestacadaBrush);
        else
            e.Row.ClearValue(DataGridRow.BackgroundProperty);
    }

    private static void AtualizarLinhaDestacada(DataGrid grid, object? novoItem)
    {
        object? anterior = grid.GetValue(UltimoItemDestacadoProperty);

        if (anterior != null && !ReferenceEquals(anterior, novoItem))
        {
            if (grid.ItemContainerGenerator.ContainerFromItem(anterior) is DataGridRow linhaAnterior)
                linhaAnterior.ClearValue(DataGridRow.BackgroundProperty);
        }

        if (novoItem == null || !grid.CurrentCell.IsValid)
        {
            grid.ClearValue(UltimoItemDestacadoProperty);
            return;
        }

        grid.SetValue(UltimoItemDestacadoProperty, novoItem);

        if (grid.ItemContainerGenerator.ContainerFromItem(novoItem) is DataGridRow linhaAtual)
            linhaAtual.SetCurrentValue(DataGridRow.BackgroundProperty, LinhaDestacadaBrush);
    }

    private static void LimparLinhaDestacada(DataGrid grid)
    {
        object? anterior = grid.GetValue(UltimoItemDestacadoProperty);
        if (anterior != null && grid.ItemContainerGenerator.ContainerFromItem(anterior) is DataGridRow linha)
            linha.ClearValue(DataGridRow.BackgroundProperty);

        grid.ClearValue(UltimoItemDestacadoProperty);
    }

    private static void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        DataGridCell? celula = EncontrarAncestral<DataGridCell>(e.OriginalSource as DependencyObject);
        if (celula == null)
            return;

        ConfigurarSelecaoPorCelula(grid);
        grid.Focus();

        var info = new DataGridCellInfo(celula.DataContext, celula.Column);
        if (!grid.SelectedCells.Contains(info))
        {
            grid.SelectedCells.Clear();
            grid.SelectedCells.Add(info);
        }

        grid.CurrentCell = info;
        AtualizarLinhaDestacada(grid, celula.DataContext);
        celula.Focus();
    }

    private static void ConfigurarSelecaoPorCelula(DataGrid grid)
    {
        // Preserva IsReadOnly conforme definido no XAML. A tela de resultado possui
        // uma coluna específica editável para a explicação manual.
        if (grid.SelectionUnit != DataGridSelectionUnit.Cell)
            grid.SelectionUnit = DataGridSelectionUnit.Cell;

        if (grid.SelectionMode != DataGridSelectionMode.Extended)
            grid.SelectionMode = DataGridSelectionMode.Extended;

        grid.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
        grid.FocusVisualStyle = null;
        grid.ClipToBounds = true;
    }

    private static void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Em uma célula que está sendo editada, deixa o TextBox tratar o Ctrl+C.
            if (Keyboard.FocusedElement is TextBox)
                return;

            CopiarSelecao(grid);
            e.Handled = true;
        }
    }

    private static void CopiarSelecao(DataGrid grid)
    {
        if (grid.SelectedCells.Count == 0)
            return;

        grid.Focus();

        if (ApplicationCommands.Copy.CanExecute(null, grid))
            ApplicationCommands.Copy.Execute(null, grid);
    }

    private static T? EncontrarAncestral<T>(DependencyObject? origem) where T : DependencyObject
    {
        DependencyObject? atual = origem;
        while (atual != null)
        {
            if (atual is T encontrado)
                return encontrado;

            atual = VisualTreeHelper.GetParent(atual);
        }

        return null;
    }

    private static SolidColorBrush CriarBrush(string hexadecimal)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hexadecimal)!;
        brush.Freeze();
        return brush;
    }
}
