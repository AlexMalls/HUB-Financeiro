$ErrorActionPreference = 'Stop'

function Read-Normalized([string]$Path) {
    return ([System.IO.File]::ReadAllText($Path)).Replace("`r`n", "`n")
}

function Write-Normalized([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text.Replace("`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))
}

function Replace-ExactOnce([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $oldNorm = $Old.Replace("`r`n", "`n")
    $newNorm = $New.Replace("`r`n", "`n")
    $first = $Text.IndexOf($oldNorm, [System.StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Trecho nao encontrado: $Label" }
    $second = $Text.IndexOf($oldNorm, $first + $oldNorm.Length, [System.StringComparison]::Ordinal)
    if ($second -ge 0) { throw "Trecho nao e unico: $Label" }
    return $Text.Substring(0, $first) + $newNorm + $Text.Substring($first + $oldNorm.Length)
}

function Replace-RegexOnce([string]$Text, [string]$Pattern, [string]$Replacement, [string]$Label) {
    $regex = [System.Text.RegularExpressions.Regex]::new(
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )
    $matches = $regex.Matches($Text)
    if ($matches.Count -ne 1) { throw "Regex '$Label' encontrou $($matches.Count) ocorrencia(s)." }
    return $regex.Replace($Text, $Replacement, 1)
}

# -----------------------------------------------------------------------------
# MainWindow.xaml — adiciona o botão ao lado das ações do O.P.E.X.
# -----------------------------------------------------------------------------
$path = 'MainWindow.xaml'
$text = Read-Normalized $path
$text = Replace-ExactOnce $text @'
                                        <ColumnDefinition Width="135"/>  <!-- Botão Importar -->
                                    </Grid.ColumnDefinitions>
'@ @'
                                        <ColumnDefinition Width="135"/>  <!-- Botão Importar -->
                                        <ColumnDefinition Width="8"/>    <!-- Espaçamento -->
                                        <ColumnDefinition Width="155"/>  <!-- Conferir Pagamentos -->
                                    </Grid.ColumnDefinitions>
'@ 'colunas Conferir Pagamentos'

$newButton = @'

                                    <!-- Botão Conferir Pagamentos -->
                                    <Button x:Name="BtnConferirPagamentos"
                                            Grid.Column="14"
                                            Content="Conferir Pagamentos"
                                            Height="38"
                                            Background="#2D6D50"
                                            Foreground="White"
                                            BorderThickness="0"
                                            FontWeight="SemiBold"
                                            FontSize="13"
                                            Cursor="Hand"
                                            Click="BtnConferirPagamentos_Click">
                                        <Button.Style>
                                            <Style TargetType="Button">
                                                <Setter Property="Background" Value="#2D6D50"/>
                                                <Setter Property="Template">
                                                    <Setter.Value>
                                                        <ControlTemplate TargetType="Button">
                                                            <Border Background="{TemplateBinding Background}"
                                                                   CornerRadius="6"
                                                                   BorderThickness="0">
                                                                <ContentPresenter HorizontalAlignment="Center"
                                                                                 VerticalAlignment="Center"/>
                                                            </Border>
                                                        </ControlTemplate>
                                                    </Setter.Value>
                                                </Setter>
                                                <Style.Triggers>
                                                    <Trigger Property="IsMouseOver" Value="True">
                                                        <Setter Property="Background" Value="#388563"/>
                                                    </Trigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Button.Style>
                                    </Button>
'@

$pattern = '(<!-- Botão Movimentar Registros -->.*?</Button>)(\s*</Grid>\s*<!-- ============================================ -->\s*<!-- LISTA DE PREVISÕES DE PAGAMENTO -->)'
$replacement = '$1' + $newButton + '$2'
$text = Replace-RegexOnce $text $pattern $replacement 'insercao do botao Conferir Pagamentos'
Write-Normalized $path $text

# -----------------------------------------------------------------------------
# MainWindow.xaml.cs — fluxo Hoje / Outra data + execução da conferência.
# -----------------------------------------------------------------------------
$path = 'MainWindow.xaml.cs'
$text = Read-Normalized $path
$anchor = @'
    /// <summary>
    /// Salva a lista de previsões no arquivo JSON
    /// </summary>
    private void SalvarPrevisoes(List<PrevisaoPagamento> previsoes, string caminhoArquivo)
'@
$method = @'
    /// <summary>
    /// Confere os lançamentos do O.P.E.X. contra os compromissos Santander
    /// armazenados pelo monitor operacional do HUB.
    /// </summary>
    private void BtnConferirPagamentos_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hoje = DateTime.Today;
            var escolha = CustomMessageBox.ShowQuestion(
                $"Deseja conferir os pagamentos de hoje ({hoje:dd/MM/yyyy})?",
                "Conferir Pagamentos",
                "Escolha Hoje para analisar a data atual ou Outra data para selecionar um dia diferente.",
                "Hoje",
                "Outra data");

            DateTime dataAnalise;
            if (escolha == MessageBoxResult.Yes)
            {
                dataAnalise = hoje;
            }
            else if (escolha == MessageBoxResult.No)
            {
                var selecionarData = new SelecionarDataWindow(
                    hoje,
                    "Conferir Pagamentos",
                    "Qual data deseja conferir?",
                    "Conferir",
                    confirmarFimSemana: false,
                    confirmarPassado: false)
                {
                    Owner = this
                };

                if (selecionarData.ShowDialog() != true)
                    return;

                dataAnalise = selecionarData.DataSelecionada.Date;
            }
            else
            {
                return;
            }

            var pagamentosHub = CarregarPrevisoesPagamento();
            var memoriaBanco = SantanderCommitmentMemoryService.Snapshot();
            var resultado = OpexPaymentReconciliationService.Conferir(
                dataAnalise,
                pagamentosHub,
                memoriaBanco);

            DebugService.Record(
                "OPEX",
                $"Conferir Pagamentos | Data: {dataAnalise:dd/MM/yyyy} | HUB No Banco: {resultado.TotalHubNoBanco} | Santander: {resultado.TotalBanco} | Divergências: {resultado.Divergencias.Count}.",
                DebugEntryLevel.Action);

            var janela = new ConferirPagamentosWindow(resultado)
            {
                Owner = this
            };
            janela.ShowDialog();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao conferir pagamentos", ex);
        }
    }

'@
$text = Replace-ExactOnce $text $anchor ($method + $anchor) 'handler Conferir Pagamentos'
Write-Normalized $path $text

# -----------------------------------------------------------------------------
# CustomMessageBox.xaml.cs — rótulos customizáveis (Hoje / Outra data).
# -----------------------------------------------------------------------------
$path = 'CustomMessageBox.xaml.cs'
$text = Read-Normalized $path
$text = Replace-ExactOnce $text @'
    private CustomMessageBox(string message, string title, MessageBoxType type, bool showYesNo, string? detail = null)
'@ @'
    private CustomMessageBox(
        string message,
        string title,
        MessageBoxType type,
        bool showYesNo,
        string? detail = null,
        string yesText = "Sim",
        string noText = "Não")
'@ 'assinatura CustomMessageBox'

$text = Replace-ExactOnce $text @'
        if (showYesNo)
        {
            YesButton.Visibility = Visibility.Visible;
            NoButton.Visibility = Visibility.Visible;
            OkButton.Visibility = Visibility.Collapsed;
        }
'@ @'
        if (showYesNo)
        {
            YesButton.Content = yesText;
            NoButton.Content = noText;
            YesButton.Visibility = Visibility.Visible;
            NoButton.Visibility = Visibility.Visible;
            OkButton.Visibility = Visibility.Collapsed;
        }
'@ 'rotulos customizados do CustomMessageBox'

$text = Replace-ExactOnce $text @'
    public static MessageBoxResult ShowQuestion(string message, string title = "Confirmação", string? detail = null)
    {
        var msgBox = new CustomMessageBox(message, title, MessageBoxType.Question, true, detail);
'@ @'
    public static MessageBoxResult ShowQuestion(
        string message,
        string title = "Confirmação",
        string? detail = null,
        string yesText = "Sim",
        string noText = "Não")
    {
        var msgBox = new CustomMessageBox(message, title, MessageBoxType.Question, true, detail, yesText, noText);
'@ 'ShowQuestion com rotulos customizados'
Write-Normalized $path $text

# -----------------------------------------------------------------------------
# SelecionarDataWindow.xaml — torna o modal reutilizável para a conferência.
# -----------------------------------------------------------------------------
$path = 'SelecionarDataWindow.xaml'
$text = Read-Normalized $path
$text = Replace-ExactOnce $text @'
                    <TextBlock Text="Selecionar Data de Provisionamento"
'@ @'
                    <TextBlock x:Name="TitleTextBlock"
                              Text="Selecionar Data de Provisionamento"
'@ 'nome do titulo da janela de data'
$text = Replace-ExactOnce $text @'
                <TextBlock Text="Para qual data deseja provisionar os pagamentos?"
'@ @'
                <TextBlock x:Name="PromptTextBlock"
                          Text="Para qual data deseja provisionar os pagamentos?"
'@ 'nome do prompt da janela de data'
$text = Replace-ExactOnce $text @'
                    <Button Grid.Column="2"
                            Content="Provisionar"
'@ @'
                    <Button x:Name="ActionButton"
                            Grid.Column="2"
                            Content="Provisionar"
'@ 'nome do botao de acao da janela de data'
Write-Normalized $path $text

# -----------------------------------------------------------------------------
# SelecionarDataWindow.xaml.cs — mantém o provisionamento e adiciona modo genérico.
# -----------------------------------------------------------------------------
$path = 'SelecionarDataWindow.xaml.cs'
$text = Read-Normalized $path
$text = Replace-ExactOnce $text @'
    private int _cursorPosition = 0;
    private bool _isFormattingDate = false; // Flag para evitar recursão

    public SelecionarDataWindow()
    {
        InitializeComponent();
        
        // Define a data sugerida (próximo dia útil)
        DateTime dataSugerida = ObterProximoDiaUtil(DateTime.Now);
        DataTextBox.Text = dataSugerida.ToString("dd/MM/yyyy");
        DataSelecionada = dataSugerida;
    }
'@ @'
    private int _cursorPosition = 0;
    private bool _isFormattingDate = false; // Flag para evitar recursão
    private readonly bool _confirmarFimSemana;
    private readonly bool _confirmarPassado;

    public SelecionarDataWindow()
    {
        InitializeComponent();
        _confirmarFimSemana = true;
        _confirmarPassado = true;

        // Mantém exatamente o comportamento histórico do provisionamento.
        DateTime dataSugerida = ObterProximoDiaUtil(DateTime.Now);
        ConfigurarModo(
            dataSugerida,
            "Selecionar Data de Provisionamento",
            "Para qual data deseja provisionar os pagamentos?",
            "Provisionar");
    }

    public SelecionarDataWindow(
        DateTime dataSugerida,
        string tituloJanela,
        string textoPergunta,
        string textoAcao,
        bool confirmarFimSemana = false,
        bool confirmarPassado = false)
    {
        InitializeComponent();
        _confirmarFimSemana = confirmarFimSemana;
        _confirmarPassado = confirmarPassado;
        ConfigurarModo(dataSugerida, tituloJanela, textoPergunta, textoAcao);
    }

    private void ConfigurarModo(
        DateTime dataSugerida,
        string tituloJanela,
        string textoPergunta,
        string textoAcao)
    {
        Title = tituloJanela;
        TitleTextBlock.Text = tituloJanela;
        PromptTextBlock.Text = textoPergunta;
        ActionButton.Content = textoAcao;
        DataTextBox.Text = dataSugerida.ToString("dd/MM/yyyy");
        DataSelecionada = dataSugerida.Date;
    }
'@ 'construtores da janela de data'

$text = Replace-ExactOnce $text @'
                if (data.DayOfWeek == DayOfWeek.Saturday || 
                   data.DayOfWeek == DayOfWeek.Sunday)
'@ @'
                if (_confirmarFimSemana &&
                    (data.DayOfWeek == DayOfWeek.Saturday ||
                     data.DayOfWeek == DayOfWeek.Sunday))
'@ 'validacao opcional de fim de semana'

$text = Replace-ExactOnce $text @'
                if (data.Date < DateTime.Now.Date)
'@ @'
                if (_confirmarPassado && data.Date < DateTime.Now.Date)
'@ 'validacao opcional de data passada'
Write-Normalized $path $text

# -----------------------------------------------------------------------------
# SantanderCommitmentAnalyzerService.cs — dados operacionais não dependem do Debug.
# -----------------------------------------------------------------------------
$path = 'SantanderCommitmentAnalyzerService.cs'
$text = Read-Normalized $path
$text = Replace-ExactOnce $text @'
    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            DebugService.EnabledChanged += DebugService_EnabledChanged;
            Application.Current.Exit += Application_Exit;
        }

        if (DebugService.IsEnabled)
            Start();
    }

    private static void DebugService_EnabledChanged(bool enabled)
    {
        if (enabled)
            Start();
        else
            Stop();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        Stop();
        DebugService.EnabledChanged -= DebugService_EnabledChanged;
        Application.Current.Exit -= Application_Exit;
    }
'@ @'
    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            Application.Current.Exit += Application_Exit;
        }

        // A leitura dos compromissos agora é infraestrutura operacional do O.P.E.X.,
        // portanto permanece ativa independentemente do Modo Debug.
        Start();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        Stop();
        Application.Current.Exit -= Application_Exit;
    }
'@ 'inicializacao operacional do analisador Santander'
Write-Normalized $path $text

# -----------------------------------------------------------------------------
# SantanderCommitmentMemoryService.cs — memória operacional sempre ativa.
# -----------------------------------------------------------------------------
$path = 'SantanderCommitmentMemoryService.cs'
$text = Read-Normalized $path
$text = Replace-ExactOnce $text @'
    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            DebugService.EnabledChanged += DebugService_EnabledChanged;
            Application.Current.Exit += Application_Exit;
        }

        LoadPersistedEntries();

        if (DebugService.IsEnabled)
            Start();
    }
'@ @'
    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            Application.Current.Exit += Application_Exit;
        }

        LoadPersistedEntries();

        // A memória de compromissos alimenta a conferência O.P.E.X. e não é mais
        // condicionada ao Modo Debug.
        Start();
    }
'@ 'inicializacao operacional da memoria Santander'

$text = Replace-ExactOnce $text @'
    private static void DebugService_EnabledChanged(bool enabled)
    {
        if (enabled)
            Start();
        else
            StopMonitoring();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        StopMonitoring();

        DebugService.EnabledChanged -= DebugService_EnabledChanged;
        Application.Current.Exit -= Application_Exit;
    }
'@ @'
    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        StopMonitoring();
        Application.Current.Exit -= Application_Exit;
    }
'@ 'remocao do acoplamento Debug da memoria Santander'
Write-Normalized $path $text

# -----------------------------------------------------------------------------
# Program.cs — entrada de teste isolada para o motor de conferência.
# -----------------------------------------------------------------------------
$path = 'Program.cs'
$text = Read-Normalized $path
$anchor = @'
        var app = new App();
'@
$testBlock = @'
        if (args.Any(arg => string.Equals(arg, "--validar-conferencia-pagamentos", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                OpexPaymentReconciliationServiceTestes.Executar();
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }

            return;
        }

'@
$text = Replace-ExactOnce $text $anchor ($testBlock + $anchor) 'entrada de teste Conferir Pagamentos'
Write-Normalized $path $text

Write-Host 'Patch Conferir Pagamentos aplicado com sucesso.'
