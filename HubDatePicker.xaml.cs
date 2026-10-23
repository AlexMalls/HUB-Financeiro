using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class HubDatePicker : UserControl
{
    private static readonly CultureInfo CulturaPtBr = CultureInfo.GetCultureInfo("pt-BR");
    private DateTime _mesExibido = DateTime.Today;
    private bool _atualizandoTexto;

    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate),
        typeof(DateTime?),
        typeof(HubDatePicker),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedDateChanged));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value?.Date);
    }

    public string TextoData => DateTextBox?.Text ?? string.Empty;

    public HubDatePicker()
    {
        InitializeComponent();
        AtualizarTextoDaData();
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HubDatePicker seletor)
            return;

        if (e.NewValue is DateTime data)
            seletor._mesExibido = new DateTime(data.Year, data.Month, 1);

        seletor.AtualizarTextoDaData();
        if (seletor.CalendarPopup?.IsOpen == true)
            seletor.RenderizarMes();
    }

    private void AtualizarTextoDaData()
    {
        if (DateTextBox == null)
            return;

        _atualizandoTexto = true;
        try
        {
            DateTextBox.Text = SelectedDate?.ToString("dd/MM/yyyy", CulturaPtBr) ?? string.Empty;
        }
        finally
        {
            _atualizandoTexto = false;
        }
    }

    private void CalendarButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (SelectedDate.HasValue)
            _mesExibido = new DateTime(SelectedDate.Value.Year, SelectedDate.Value.Month, 1);
        else
            _mesExibido = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        CalendarPopup.IsOpen = !CalendarPopup.IsOpen;
    }

    private void CalendarPopup_Opened(object sender, EventArgs e)
        => RenderizarMes();

    private void MesAnterior_Click(object sender, RoutedEventArgs e)
    {
        _mesExibido = _mesExibido.AddMonths(-1);
        RenderizarMes();
    }

    private void ProximoMes_Click(object sender, RoutedEventArgs e)
    {
        _mesExibido = _mesExibido.AddMonths(1);
        RenderizarMes();
    }

    private void RenderizarMes()
    {
        if (DiasUniformGrid == null || MesAnoTextBlock == null)
            return;

        MesAnoTextBlock.Text = CulturaPtBr.TextInfo.ToTitleCase(
            _mesExibido.ToString("MMMM 'de' yyyy", CulturaPtBr));

        DiasUniformGrid.Children.Clear();

        DateTime primeiroDoMes = new(_mesExibido.Year, _mesExibido.Month, 1);
        int deslocamento = (int)primeiroDoMes.DayOfWeek;
        DateTime inicioGrade = primeiroDoMes.AddDays(-deslocamento);

        for (int i = 0; i < 42; i++)
        {
            DateTime data = inicioGrade.AddDays(i);
            var botao = new Button
            {
                Content = data.Day.ToString(CulturaPtBr),
                Tag = data,
                Style = (Style)FindResource("HubCalendarDayButtonStyle")
            };

            if (data.Month != _mesExibido.Month)
            {
                botao.Opacity = 0.38;
            }

            if (data.Date == DateTime.Today)
            {
                botao.BorderBrush = (Brush)FindResource("HubDateAccent");
                botao.BorderThickness = new Thickness(1);
            }

            if (SelectedDate.HasValue && data.Date == SelectedDate.Value.Date)
            {
                botao.Background = (Brush)FindResource("HubDatePrimary");
                botao.Foreground = Brushes.White;
                botao.BorderBrush = (Brush)FindResource("HubDatePrimary");
            }

            botao.Click += Dia_Click;
            DiasUniformGrid.Children.Add(botao);
        }
    }

    private void Dia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime data })
            return;

        SelectedDate = data.Date;
        CalendarPopup.IsOpen = false;
        DateTextBox.Focus();
        DateTextBox.SelectAll();
    }

    private void DateTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9/]$", RegexOptions.CultureInvariant);
    }

    private void DateTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => ConfirmarTextoDigitado();

    private void DateTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmarTextoDigitado();
            e.Handled = true;
            Keyboard.ClearFocus();
            return;
        }

        if (e.Key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            CalendarPopup.IsOpen = true;
            e.Handled = true;
        }
    }

    private void DateTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(DateTextBox.SelectAll));

    private void ConfirmarTextoDigitado()
    {
        if (_atualizandoTexto || DateTextBox == null)
            return;

        string texto = DateTextBox.Text.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            SelectedDate = null;
            return;
        }

        if (DateTime.TryParseExact(
            texto,
            "dd/MM/yyyy",
            CulturaPtBr,
            DateTimeStyles.None,
            out DateTime data))
        {
            SelectedDate = data.Date;
            AtualizarTextoDaData();
            return;
        }

        AtualizarTextoDaData();
    }
}
