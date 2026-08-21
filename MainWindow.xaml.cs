using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using ClosedXML.Excel;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Tesseract;
using Docnet.Core;
using Docnet.Core.Models;

namespace HubFinanceiro;

/// <summary>
/// Janela principal do Hub Financeiro
/// </summary>
public partial class MainWindow : Window
{
    #region Campos Privados

    private ToggleButton? _fornecedorSelecionado = null;
    private Border? _fornecedorItemSelecionado = null;
    private List<Fornecedor> _todosFornecedores = new List<Fornecedor>();
    private List<Fornecedor> _fornecedoresOpex = new List<Fornecedor>();
    private List<PrevisaoPagamento> _previsoesPagamento = new List<PrevisaoPagamento>();
    private PrevisaoPagamento? _pagamentoSelecionado = null;
    private Border? _pagamentoBorderSelecionado = null;
    private bool _isLoadingPagamento = false; // Flag para evitar ativar edição ao carregar dados
    private bool _isAtualizandoAutocompleteOpex = false; // Evita reentrada ao filtrar o ComboBox editável
    private const double ANIMATION_DURATION = 0.5; // Duração padrão das animações em segundos
    private const double INTRO_DISPLAY_TIME = 2.0; // Tempo de exibição da intro em segundos
    
    // FileSystemWatcher para monitorar mudanças nos arquivos JSON
    private FileSystemWatcher? _fornecedoresWatcher;
    private FileSystemWatcher? _pagamentosWatcher;
    private static readonly object _fileLock = new object(); // Lock para evitar conflitos de escrita
    private bool _isFormattingDate = false; // Flag para formatação de data
    
    // Sidebar colapsável
    private bool _sidebarExpanded = true;
    private const double SIDEBAR_WIDTH_EXPANDED  = 190;
    private const double SIDEBAR_WIDTH_COLLAPSED = 47;

    // Listas para CalcSubconjunto
    private ObservableCollection<ValorItem> _valoresConjunto1 = new ObservableCollection<ValorItem>();
    private ObservableCollection<ValorItem> _valoresConjunto2 = new ObservableCollection<ValorItem>();
    private const int LIMITE_CONJUNTO1 = 23; // Máximo de itens na primeira lista

    // Caminho do arquivo de log (pasta onde o executável está rodando)
    private static readonly string _logPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "HubFinanceiro_log.txt");

    // Listas para CompararValores (input)
    private ObservableCollection<ValorItem> _compararConjunto1 = new ObservableCollection<ValorItem>();
    private ObservableCollection<ValorItem> _compararConjunto2 = new ObservableCollection<ValorItem>();
    // Listas de resultado para exibição
    private ObservableCollection<ComparacaoLinhaItem> _compararResultado1 = new ObservableCollection<ComparacaoLinhaItem>();
    private ObservableCollection<ComparacaoLinhaItem> _compararResultado2 = new ObservableCollection<ComparacaoLinhaItem>();
    private ObservableCollection<DivergenciaLinhaItem> _compararResultadoDiv = new ObservableCollection<DivergenciaLinhaItem>();

    // Decodificador / Criador CNAB
    private CnabArquivo? _cnabArquivoAtual;
    private readonly ObservableCollection<CnabPagamentoItem> _cnabPagamentos = new ObservableCollection<CnabPagamentoItem>();
    private bool _cnabAtualizandoDataTodos = false;

    private CnabCriacaoArquivo? _cnabCriacaoArquivoAtual;
    private readonly ObservableCollection<CnabCriacaoPagamentoItem> _cnabCriacaoPagamentos = new ObservableCollection<CnabCriacaoPagamentoItem>();
    private readonly ObservableCollection<CnabDadosBancariosFuncionario> _cnabColaboradores = new ObservableCollection<CnabDadosBancariosFuncionario>();
    private bool _cnabCriacaoAtualizandoDataTodos = false;

    #endregion

    #region Construtor

    public MainWindow()
    {
        InitializeComponent();
        CnabPagamentosItemsControl.ItemsSource = _cnabPagamentos;
        CnabCriarPagamentosItemsControl.ItemsSource = _cnabCriacaoPagamentos;
        CnabCadastroColaboradoresItemsControl.ItemsSource = _cnabColaboradores;
        InicializarEstadoInicial();
    }

    #endregion

    #region Inicialização

    /// <summary>
    /// Configura o estado inicial da interface
    /// </summary>
    private void InicializarEstadoInicial()
    {
        // Programa principal começa invisível
        AppRoot.Visibility = Visibility.Hidden;
        AppRoot.Opacity = 0;

        // Intro começa visível mas transparente
        IntroScreen.Visibility = Visibility.Visible;
        IntroScreen.Opacity = 0;
    }

    /// <summary>
    /// Evento disparado quando a janela é carregada
    /// </summary>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        IniciarSessaoLog();
        Log("Window_Loaded: início");

        // ── Handlers globais: capturam crashes que escapam de qualquer try/catch ──
        Application.Current.DispatcherUnhandledException += (s, ex) =>
        {
            Log("💥 CRASH FATAL — DispatcherUnhandledException (UI thread)", ex.Exception);
            // NÃO marcar como Handled: queremos que o log seja gravado e o app feche normalmente
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            var exObj = ex.ExceptionObject as Exception;
            Log($"💥 CRASH FATAL — AppDomain.UnhandledException (IsTerminating={ex.IsTerminating})" +
                (exObj != null ? "" : $" | Objeto: {ex.ExceptionObject}"), exObj);
        };
        TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            Log("💥 CRASH FATAL — TaskScheduler.UnobservedTaskException", ex.Exception);
        };
        // ────────────────────────────────────────────────────────────────────────

        try
        {
            Log("Window_Loaded: chamando CarregarDadosAsync...");
            await CarregarDadosAsync();
            Log("Window_Loaded: CarregarDadosAsync concluído");

            Log("Window_Loaded: chamando ConfigurarJanela...");
            ConfigurarJanela();
            Log("Window_Loaded: ConfigurarJanela concluído");

            Log("Window_Loaded: chamando ExecutarIntroFadeAsync...");
            await ExecutarIntroFadeAsync();
            Log("Window_Loaded: ExecutarIntroFadeAsync concluído");

            Log("Window_Loaded: chamando AbrirHome...");
            AbrirHome();
            IniciarAnimacaoGradienteSidebar();
            Log("Window_Loaded: inicialização concluída com sucesso ✅");
        }
        catch (Exception ex)
        {
            Log("Window_Loaded: ERRO na inicialização", ex);
            MostrarErro("Erro ao inicializar a aplicação", ex);
        }
    }

    /// <summary>
    /// Carrega os dados dos fornecedores de forma assíncrona
    /// </summary>
    private async Task CarregarDadosAsync()
    {
        await Task.Run(() =>
        {
            Log("CarregarDadosAsync: Task.Run iniciado");
            var fornecedores = CarregarFornecedores();
            Log($"CarregarDadosAsync: {fornecedores.Count} fornecedores carregados");
            var previsoes = CarregarPrevisoesPagamento();
            Log($"CarregarDadosAsync: {previsoes.Count} previsões carregadas");

            Dispatcher.Invoke(() =>
            {
                Log("CarregarDadosAsync: Dispatcher.Invoke iniciado");
                _todosFornecedores = fornecedores.OrderBy(f => f.Nome).ToList();
                FornecedoresItemsControl.ItemsSource = _todosFornecedores;

                var apenasAtivos = fornecedores.Where(f => f.Ativo).OrderBy(f => f.Nome).ToList();
                FornecedorItemsControl.ItemsSource = apenasAtivos;

                _fornecedoresOpex = fornecedores.OrderBy(f => f.Nome).ToList();
                OpexFornecedorComboBox.ItemsSource = _fornecedoresOpex;

                _previsoesPagamento = previsoes
                    .Where(p => p.Status != "Pago")
                    .OrderBy(p => p.DataPagamento)
                    .ToList();
                PagamentosItemsControl.ItemsSource = _previsoesPagamento;

                Log("CarregarDadosAsync: configurando ItemsSources do CompararValores...");
                CompararConjunto1ItemsControl.ItemsSource = _compararResultado1;
                CompararConjunto2ItemsControl.ItemsSource = _compararResultado2;
                CompararDivergenciasItemsControl.ItemsSource = _compararResultadoDiv;
                Log("CarregarDadosAsync: ItemsSources configurados, iniciando monitoramento...");

                IniciarMonitoramentoArquivos();
                Log("CarregarDadosAsync: Dispatcher.Invoke concluído ✅");
            });
        });
    }

    /// <summary>
    /// Recarrega apenas as previsões de pagamento (chamado após provisionamento)
    /// </summary>
    public void RecarregarPagamentos()
    {
        try
        {
            // Salva o ID do pagamento selecionado (se houver)
            int? pagamentoSelecionadoId = _pagamentoSelecionado?.Id;
            
            var previsoes = CarregarPrevisoesPagamento();
            
            // Filtra pagamentos "Pago" se a checkbox não estiver marcada
            if (MostrarPagosCheckBox != null && MostrarPagosCheckBox.IsChecked != true)
            {
                previsoes = previsoes.Where(p => p.Status != "Pago").ToList();
            }
            
            // Filtra por empresa (ADM ou COR)
            if (SomenteAdmCheckBox?.IsChecked == true)
            {
                previsoes = previsoes.Where(p => p.Empresa == "ADM").ToList();
            }
            else if (SomenteCorCheckBox?.IsChecked == true)
            {
                previsoes = previsoes.Where(p => p.Empresa == "COR").ToList();
            }
            // Se nenhuma estiver marcada, mostra todos
            
            _previsoesPagamento = previsoes.OrderBy(p => p.DataPagamento).ToList();
            PagamentosItemsControl.ItemsSource = null;
            PagamentosItemsControl.ItemsSource = _previsoesPagamento;
            
            // Restaura a seleção visual se o pagamento ainda existir na lista
            if (pagamentoSelecionadoId.HasValue)
            {
                var pagamentoAtualizado = _previsoesPagamento.FirstOrDefault(p => p.Id == pagamentoSelecionadoId.Value);
                
                if (pagamentoAtualizado != null)
                {
                    // Aguarda a UI atualizar e depois restaura a seleção visual
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RestaurarSelecaoVisual(pagamentoAtualizado);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else
                {
                    // Se o pagamento não existe mais, limpa a seleção
                    LimparSelecaoPagamento();
                }
            }
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao recarregar pagamentos", ex);
        }
    }
    
    /// <summary>
    /// Restaura a seleção visual de um pagamento após recarregar a lista
    /// </summary>
    private void RestaurarSelecaoVisual(PrevisaoPagamento pagamento)
    {
        try
        {
            // Procura o Border correspondente ao pagamento na lista visual
            var itemsControl = PagamentosItemsControl;
            
            if (itemsControl == null || itemsControl.Items.Count == 0)
                return;
            
            // Percorre os itens visuais
            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                var item = itemsControl.Items[i];
                
                if (item is PrevisaoPagamento pag && pag.Id == pagamento.Id)
                {
                    // Encontra o container visual (Border)
                    var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
                    
                    if (container != null)
                    {
                        // Procura o Border dentro do container
                        var border = FindVisualChild<Border>(container);
                        
                        if (border != null)
                        {
                            // Aplica o destaque roxo
                            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D2354"));
                            _pagamentoBorderSelecionado = border;
                            _pagamentoSelecionado = pagamento;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao restaurar seleção visual: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Procura um elemento filho específico na árvore visual
    /// </summary>
    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
            return null;
        
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is T typedChild)
                return typedChild;
            
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        
        return null;
    }
    
    /// <summary>
    /// Evento quando marca/desmarca a checkbox "Mostrar Pagos"
    /// </summary>
    private void MostrarPagosCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        LimparSelecaoPagamento();
        RecarregarPagamentos();
    }
    
    /// <summary>
    /// Evento quando marca/desmarca "Somente ADM"
    /// </summary>
    private void SomenteAdmCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (SomenteAdmCheckBox?.IsChecked == true)
        {
            // Desmarca "Somente COR" (comportamento exclusivo)
            if (SomenteCorCheckBox != null)
                SomenteCorCheckBox.IsChecked = false;
        }
        
        LimparSelecaoPagamento();
        RecarregarPagamentos();
    }
    
    /// <summary>
    /// Evento quando marca/desmarca "Somente COR"
    /// </summary>
    private void SomenteCorCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (SomenteCorCheckBox?.IsChecked == true)
        {
            // Desmarca "Somente ADM" (comportamento exclusivo)
            if (SomenteAdmCheckBox != null)
                SomenteAdmCheckBox.IsChecked = false;
        }
        
        LimparSelecaoPagamento();
        RecarregarPagamentos();
    }
    
    /// <summary>
    /// Limpa a seleção de pagamento atual
    /// </summary>
    private void LimparSelecaoPagamento()
    {
        // Remove visual de seleção
        if (_pagamentoBorderSelecionado != null)
        {
            _pagamentoBorderSelecionado.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2D"));
        }
        
        // Limpa referências
        _pagamentoSelecionado = null;
        _pagamentoBorderSelecionado = null;
        
        // Limpa os campos
        if (OpexFornecedorComboBox != null)
            OpexFornecedorComboBox.SelectedItem = null;
        if (OpexDataTextBox != null)
            OpexDataTextBox.Text = string.Empty;
        if (OpexValorTextBox != null)
            OpexValorTextBox.Text = string.Empty;
        if (OpexEmpresaComboBox != null)
            OpexEmpresaComboBox.SelectedItem = null;
        
        // Atualiza estado dos botões
        AtualizarEstadoBotoes();
    }

    /// <summary>
    /// Carrega a lista de fornecedores do arquivo JSON
    /// </summary>
    private List<Fornecedor> CarregarFornecedores()
    {
        try
        {
            string caminhoArquivo = ObterCaminhoArquivoFornecedores();

            if (!File.Exists(caminhoArquivo))
            {
                MostrarAviso($"Arquivo de fornecedores não encontrado:\n{caminhoArquivo}");
                return new List<Fornecedor>();
            }

            string json = File.ReadAllText(caminhoArquivo);
            return JsonSerializer.Deserialize<List<Fornecedor>>(json) ?? new List<Fornecedor>();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao carregar fornecedores", ex);
            return new List<Fornecedor>();
        }
    }

    /// <summary>
    /// Obtém o caminho do arquivo de fornecedores
    /// </summary>
    /// <summary>
    /// Detecta qual usuário está rodando o programa verificando as pastas
    /// que existem em C:/Users, sem nenhuma chamada a API do Windows.
    /// Retorna o caminho da pasta 'data' correspondente ao usuário detectado.
    /// Para adicionar um novo usuário, basta incluir mais uma entrada no dicionário.
    /// </summary>
    private string ObterCaminhoBase()
    {
        // Mapa: pasta do usuário em C:/Users → caminho completo da pasta 'data'
        var mapeamentoUsuarios = new Dictionary<string, string>
        {
            {
                @"C:/Users/Alexandre Mallorca",
                @"C:/Users/Alexandre Mallorca/OneDrive - Positiva Administradora de Benefícios Ltda/Documentos/Financeiro/HUB Financeiro/data"
            },
            {
                @"C:/Users/Vinícius Oliveira",
                @"C:/Users/Vinícius Oliveira/Positiva Administradora de Benefícios Ltda/Alexandre Mallorca Silveira - data"
            }
        };

        foreach (var entrada in mapeamentoUsuarios)
        {
            if (Directory.Exists(entrada.Key))
                return entrada.Value;
        }

        // Nenhum usuário reconhecido: lança erro descritivo
        string usuariosConhecidos = string.Join(", ", mapeamentoUsuarios.Keys);
        throw new DirectoryNotFoundException(
            $"Usuário não reconhecido. Usuários suportados: {usuariosConhecidos}. " +
            $"Verifique se o programa está sendo executado em uma máquina cadastrada.");
    }

    private string ObterCaminhoArquivoFornecedores()
    {
        return Path.Combine(ObterCaminhoBase(), "fornecedores.json");
    }

    // Chave do Registro: HKCU\Software\HubFinanceiro
    // Por usuário, sem arquivo externo, sem bloqueio do Windows
    private const string REGISTRO_CHAVE = @"Software\HubFinanceiro";

    /// <summary>
    /// Salva as preferências no Registro do Windows.
    /// </summary>
    private void SalvarPreferencias()
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REGISTRO_CHAVE);
            chave?.SetValue("SidebarExpanded", _sidebarExpanded ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AVISO] Não foi possível salvar preferências: {ex.Message}");
        }
    }

    /// <summary>
    /// Carrega as preferências do Registro do Windows.
    /// Na primeira execução a chave não existe — usa padrões silenciosamente.
    /// </summary>
    private void CarregarPreferencias()
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REGISTRO_CHAVE);
            if (chave == null) return;

            int expandida = (int)(chave.GetValue("SidebarExpanded", 1) ?? 1);
            if (expandida == 0)
            {
                _sidebarExpanded = false;
                SidebarColumn.Width = new GridLength(SIDEBAR_WIDTH_COLLAPSED);
                UpdateCollapseButtonArrow();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AVISO] Não foi possível carregar preferências: {ex.Message}");
        }
    }

    /// <summary>
    /// Carrega a lista de previsões de pagamento do arquivo JSON
    /// </summary>
    private List<PrevisaoPagamento> CarregarPrevisoesPagamento()
    {
        try
        {
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();

            if (!File.Exists(caminhoArquivo))
            {
                // Se o arquivo não existe, cria um vazio
                var listaVazia = new List<PrevisaoPagamento>();
                string jsonVazio = JsonSerializer.Serialize(listaVazia, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(caminhoArquivo, jsonVazio);
                return listaVazia;
            }

            string json = File.ReadAllText(caminhoArquivo);
            return JsonSerializer.Deserialize<List<PrevisaoPagamento>>(json) ?? new List<PrevisaoPagamento>();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao carregar previsões de pagamento", ex);
            return new List<PrevisaoPagamento>();
        }
    }

    /// <summary>
    /// Obtém o caminho do arquivo de previsões de pagamento
    /// </summary>
    private string ObterCaminhoArquivoPrevisoes()
    {
        return Path.Combine(ObterCaminhoBase(), "previsoes_pagamento.json");
    }

    /// <summary>
    /// Configura o tamanho e posicionamento da janela
    /// </summary>
    private void ConfigurarJanela()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        // Define tamanho como 65% da tela
        Width = screenWidth * 0.65;
        Height = screenHeight * 0.65;

        // Centraliza a janela
        Left = (screenWidth - Width) / 2;
        Top = (screenHeight - Height) / 2;
    }

    #endregion

    #region Animações

    /// <summary>
    /// Executa a animação de fade da tela de introdução
    /// </summary>
    private async Task ExecutarIntroFadeAsync()
    {
        // Fade in da intro (0 → 1)
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));
        IntroScreen.BeginAnimation(OpacityProperty, fadeIn);
        await Task.Delay(1000);

        // Torna o programa principal visível (mas ainda coberto pela intro)
        AppRoot.Visibility = Visibility.Visible;
        AppRoot.Opacity = 1;

        // Carrega logo e preferências AQUI: o AppRoot já está renderizado
        // mas a intro ainda cobre tudo — nenhuma transição visível pro usuário
        CarregarLogo();
        CarregarPreferencias();

        // Mantém a intro visível
        await Task.Delay((int)(INTRO_DISPLAY_TIME * 1000));

        // Fade out da intro
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
        IntroScreen.BeginAnimation(OpacityProperty, fadeOut);
        await Task.Delay(500);

        // Remove a intro da visualização
        IntroScreen.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Anima o blur e opacidade da logo
    /// </summary>
    private void AnimarLogo(bool esconder)
    {
        double targetOpacity = esconder ? 0.3 : 1.0;
        double targetBlur = esconder ? 25 : 0;

        var duration = TimeSpan.FromSeconds(ANIMATION_DURATION);
        var blurAnimation = new DoubleAnimation(targetBlur, duration);
        var opacityAnimation = new DoubleAnimation(targetOpacity, duration);

        LogoBlur.BeginAnimation(BlurEffect.RadiusProperty, blurAnimation);
        LogoExecutionArea.BeginAnimation(OpacityProperty, opacityAnimation);
    }

    /// <summary>
    /// Anima a rotação da seta do expander
    /// </summary>
    private void AnimarSetaExpander(bool expandir)
    {
        double targetAngle = expandir ? 90 : 0;
        var animation = new DoubleAnimation(targetAngle, TimeSpan.FromMilliseconds(200));
        
        var rotateTransform = (RotateTransform)ArrowIcon.RenderTransform;
        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    #endregion

    #region Eventos da Barra Superior

    /// <summary>
    /// Minimiza a janela
    /// </summary>
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Maximiza ou restaura a janela
    /// </summary>
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        AlternarMaximizacao();
    }

    /// <summary>
    /// Fecha a aplicação
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SalvarPreferencias();
        Close();
    }

    /// <summary>
    /// Permite arrastar a janela e maximizar com duplo clique
    /// </summary>
    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            AlternarMaximizacao();
        }
        else
        {
            DragMove();
        }
    }

    /// <summary>
    /// Alterna entre janela maximizada e normal
    /// </summary>
    private void AlternarMaximizacao()
    {
        WindowState = WindowState == WindowState.Normal 
            ? WindowState.Maximized 
            : WindowState.Normal;
    }

    /// <summary>
    /// Abre/fecha o menu hamburguer
    /// </summary>
    private void MenuHamburguer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MenuPopup.IsOpen = !MenuPopup.IsOpen;
            System.Diagnostics.Debug.WriteLine($"Menu Popup IsOpen: {MenuPopup.IsOpen}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao abrir menu: {ex.Message}");
            CustomMessageBox.ShowError($"Erro ao abrir menu: {ex.Message}");
        }
    }

    /// <summary>
    /// Anima o popup quando abre
    /// </summary>
    private void MenuPopup_Opened(object sender, EventArgs e)
    {
        try
        {
            var scaleAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            System.Diagnostics.Debug.WriteLine("Animação do popup iniciada");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro na animação: {ex.Message}");
        }
    }

    /// <summary>
    /// Handler para quando clicar em Opções - abre a janela
    /// </summary>
    private void OpcoesMenu_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        
        var opcoesWindow = new OpcoesWindow
        {
            Owner = this
        };
        opcoesWindow.ShowDialog();
    }

    #endregion

    #region Eventos da Barra Lateral

    /// <summary>
    /// Gerencia o clique nos botões da barra lateral
    /// </summary>
    private void SidebarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clickedButton)
            return;

        // Desmarca os outros botões
        DesselecionarOutrosBotoesSidebar(clickedButton);

        // Verifica se algum botão está selecionado
        bool algumSelecionado = VerificarBotaoSelecionado();

        // Anima a logo (só esconde se não for Home)
        string conteudo = clickedButton.Content.ToString() ?? "";
        ExibirOcultarLayoutAuxConciliacao(false); // AUX-CONCILIACAO-NAV
        bool naHome = conteudo == "Home";
        AnimarLogo(algumSelecionado && !naHome);

        if (clickedButton.IsChecked == true)
        {
            switch (conteudo)
            {
                case "Home":
                    ExibirOcultarLayoutHome(true);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    break;

                case "Envio de E-mails":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(true);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    break;
                    
                case "Fornecedores":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(true);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    break;
                    
                case "O.P.E.X":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(true);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    break;
                    
                case "Calc subconjunto":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(true);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    break;

                case "Compara valores":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(true);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    break;

                case "Decodificador CNAB":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(true);
                    break;

                                case "Aux Conciliação":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(false);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    ExibirOcultarLayoutAuxConciliacao(true);
                    break;

                case "Analise de Faturas":
                    ExibirOcultarLayoutHome(false);
                    ExibirOcultarLayoutEmail(false);
                    ExibirOcultarLayoutFornecedores(false);
                    ExibirOcultarLayoutOpex(false);
                    ExibirOcultarLayoutCalcSubconjunto(false);
                    ExibirOcultarLayoutCompararValores(false);
                    ExibirOcultarLayoutAnaliseFaturas(true);
                    ExibirOcultarLayoutDecodificadorCnab(false);
                    CarregarHistoricoAnaliseFaturas();
                    break;
            }
        }
        else
        {
            // Se desmarcou tudo, volta para Home
            AbrirHome();
        }
    }

    /// <summary>
    /// Desmarca todos os botões da sidebar exceto o especificado
    /// </summary>
    private void DesselecionarOutrosBotoesSidebar(ToggleButton exceto)
    {
        foreach (var child in SidebarPanel.Children)
        {
            if (child is ToggleButton tb && tb != exceto)
            {
                tb.IsChecked = false;
            }
        }
    }

    /// <summary>
    /// Verifica se há algum botão selecionado na sidebar
    /// </summary>
    private bool VerificarBotaoSelecionado()
    {
        return SidebarPanel.Children
            .OfType<ToggleButton>()
            .Any(tb => tb.IsChecked == true);
    }

    /// <summary>
    /// Exibe ou oculta o layout Home com animação
    /// </summary>
    private void ExibirOcultarLayoutHome(bool exibir)
    {
        if (exibir)
        {
            HomeLayoutGrid.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            HomeLayoutGrid.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => HomeLayoutGrid.Visibility = Visibility.Collapsed;
            HomeLayoutGrid.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    /// <summary>
    /// Abre a tela Home e seleciona o botão correspondente na sidebar
    /// </summary>
    /// <summary>
    /// Constrói e anima o gradiente deslizante da sidebar.
    ///
    /// COMO FUNCIONA — modelo "faixa de cores":
    ///   Imagine uma fita longa com as cores lado a lado (C1 | C2 | C3 | C1 | C2 | C3 | …).
    ///   Cada elemento da sidebar é uma "janela" que enxerga um pedaço dessa fita.
    ///   A fita desliza da direita para a esquerda ao longo do tempo:
    ///     t=0s  → janela vê C1 (início do ciclo)
    ///     t≈3s  → C2 entra pela direita, C1 ainda à esquerda
    ///     t≈5s  → C2 dominante, pontas C1/C3 visíveis
    ///     t≈7s  → C3 entra pela direita
    ///     t=10s → volta ao início seamlessly (SpreadMethod=Repeat)
    ///
    /// CONFIGURAÇÃO — altere só este bloco:
    ///   coresSidebar : lista de cores hex em ordem (mínimo 2)
    ///   duracaoSeg   : segundos para completar um ciclo
    ///   larguraZonaPx: largura em px de cada "zona de cor" — valores menores =
    ///                  mais cores visíveis ao mesmo tempo; maiores = transição mais
    ///                  suave com 1-2 cores por vez (recomendado: 100–200px)
    /// </summary>
    private void IniciarAnimacaoGradienteSidebar()
    {
        // ┌─────────────────────────────────────────────────────────┐
        // │              CONFIGURAÇÃO DO GRADIENTE                  │
        var coresSidebar  = new[] { "#47089f", "#76089F", "#8E089D", "#5f089d", "#920d70" };
        const double duracaoSeg    = 300.0;   // segundos por ciclo
        const double larguraZonaPx = 130.0;  // px por zona de cor
        // └─────────────────────────────────────────────────────────┘

        try
        {
            int    n            = coresSidebar.Length;
            double larguraBrush = larguraZonaPx * n;  // extensão total da fita

            // ── Constrói o brush ───────────────────────────────────
            var brush = new LinearGradientBrush
            {
                MappingMode  = BrushMappingMode.Absolute,
                StartPoint   = new Point(0, 0),
                EndPoint     = new Point(larguraBrush, 0),
                SpreadMethod = GradientSpreadMethod.Repeat
            };

            // Distribui as cores uniformemente ao longo da fita
            for (int i = 0; i < n; i++)
            {
                var cor = (Color)ColorConverter.ConvertFromString(coresSidebar[i]);
                brush.GradientStops.Add(new GradientStop(cor, (double)i / n));
            }
            // Stop de fechamento = primeira cor → torna o repeat seamless
            var corFechamento = (Color)ColorConverter.ConvertFromString(coresSidebar[0]);
            brush.GradientStops.Add(new GradientStop(corFechamento, 1.0));

            // ── Animação de translação (desliza a fita para a esquerda) ──
            var translate = new TranslateTransform(0, 0);
            brush.Transform = translate;           // Absolute → pixels locais do elemento

            var anim = new DoubleAnimation
            {
                From           = 0,
                To             = -larguraBrush,    // desloca exatamente um ciclo completo
                Duration       = TimeSpan.FromSeconds(duracaoSeg),
                RepeatBehavior = RepeatBehavior.Forever
                // sem EasingFunction → velocidade constante (linear)
            };
            translate.BeginAnimation(TranslateTransform.XProperty, anim);

            // ── Registra como DynamicResource → todos os botões atualizam ──
            Resources["SidebarAnimGradient"] = brush;
        }
        catch (Exception ex)
        {
            Log("IniciarAnimacaoGradienteSidebar: erro (não crítico)", ex);
        }
    }

    private void AbrirHome()
    {
        // Desmarca todos e marca o Home
        foreach (var child in SidebarPanel.Children)
        {
            if (child is ToggleButton tb)
                tb.IsChecked = (tb == SidebarHomeButton);
        }

        // Mostra Home, esconde o resto
        ExibirOcultarLayoutHome(true);
        ExibirOcultarLayoutEmail(false);
        ExibirOcultarLayoutFornecedores(false);
        ExibirOcultarLayoutOpex(false);
        ExibirOcultarLayoutCalcSubconjunto(false);
        ExibirOcultarLayoutCompararValores(false);
        ExibirOcultarLayoutDecodificadorCnab(false);

        // Logo permanece visível (sem blur) na Home
        AnimarLogo(false);
    }

    /// <summary>
    /// Colapsa ou expande a barra lateral com animação suave via DispatcherTimer
    /// (GridLength não suporta DoubleAnimation nativa — usamos timer manual)
    /// </summary>
    private void SidebarCollapse_Click(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;

        double fromWidth   = _sidebarExpanded ? SIDEBAR_WIDTH_COLLAPSED : SIDEBAR_WIDTH_EXPANDED;
        double toWidth     = _sidebarExpanded ? SIDEBAR_WIDTH_EXPANDED  : SIDEBAR_WIDTH_COLLAPSED;
        double totalMs     = 200.0;
        double stepMs      = 10.0;
        double steps       = totalMs / stepMs;
        double stepSize    = (toWidth - fromWidth) / steps;
        double current     = fromWidth;

        // Congela a largura atual antes de animar (remove binding automático)
        SidebarColumn.Width = new GridLength(fromWidth);

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(stepMs)
        };

        timer.Tick += (s, args) =>
        {
            current += stepSize;

            bool chegou = stepSize > 0 ? current >= toWidth : current <= toWidth;
            if (chegou)
            {
                SidebarColumn.Width = new GridLength(toWidth);
                timer.Stop();
            }
            else
            {
                SidebarColumn.Width = new GridLength(current);
            }
        };

        timer.Start();
        UpdateCollapseButtonArrow();
    }

    /// <summary>
    /// Atualiza o símbolo da seta do botão colapsar
    /// </summary>
    private void UpdateCollapseButtonArrow()
    {
        // ApplyTemplate garante que o template foi instanciado mesmo antes do primeiro render
        SidebarCollapseButton.ApplyTemplate();

        // Acessa o TextBlock pelo nome definido no ControlTemplate ("CollapseArrow")
        // Mais confiável que FindVisualChild pois não depende do estado de render
        var arrow = SidebarCollapseButton.Template.FindName("CollapseArrow", SidebarCollapseButton) as TextBlock;
        if (arrow != null)
            arrow.Text = _sidebarExpanded ? "«" : "»";

        SidebarCollapseButton.ToolTip = _sidebarExpanded ? "Recolher menu" : "Expandir menu";
    }

    /// <summary>
    /// Carrega a logo de forma compatível com desenvolvimento e publicação.
    /// Tenta primeiro como Resource embutido no assembly, depois como arquivo
    /// ao lado do executável (útil quando publicado com "Copy to Output").
    /// </summary>
    private void CarregarLogo()
    {
        const string nomeArquivo = "Hub Finance - Logo - Transparent.png";

        // Tentativa 1: Resource embutido no assembly (Build Action = Resource no VS)
        try
        {
            var uri = new Uri($"pack://application:,,,/{nomeArquivo}", UriKind.Absolute);
            var stream = Application.GetResourceStream(uri);
            if (stream != null)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream.Stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                LogoExecutionArea.Source = bitmap;
                return;
            }
        }
        catch { }

        // Tentativa 2: sobe a árvore de diretórios a partir do executável.
        // Em dev o exe fica em bin\Debug\net8.0-windows\ — precisa subir 3 níveis
        // até a raiz do projeto onde o PNG está. Em publicação encontra de imediato.
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            if (dir == null) break;
            string candidato = Path.Combine(dir, nomeArquivo);
            if (File.Exists(candidato))
            {
                CarregarBitmapDoDisco(candidato);
                return;
            }
            dir = Path.GetDirectoryName(dir);
        }

        System.Diagnostics.Debug.WriteLine($"[AVISO] Logo não encontrada. Certifique-se que '{nomeArquivo}' está na pasta raiz do projeto.");
    }

    private void CarregarBitmapDoDisco(string caminho)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(caminho, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        LogoExecutionArea.Source = bitmap;
    }

    /// <summary>
    /// Exibe ou oculta o layout de envio de emails com animação
    /// </summary>
    private void ExibirOcultarLayoutEmail(bool exibir)
    {
        if (exibir)
        {
            EmailLayoutGrid.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            EmailLayoutGrid.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => EmailLayoutGrid.Visibility = Visibility.Collapsed;
            EmailLayoutGrid.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    /// <summary>
    /// Exibe ou oculta o layout de fornecedores com animação
    /// </summary>
    private void ExibirOcultarLayoutFornecedores(bool exibir)
    {
        if (exibir)
        {
            FornecedoresLayoutGrid.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            fadeIn.Completed += (s, a) => FornecedoresLayoutGrid.Focus(); // Dá foco para receber eventos de teclado
            FornecedoresLayoutGrid.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => FornecedoresLayoutGrid.Visibility = Visibility.Collapsed;
            FornecedoresLayoutGrid.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    /// <summary>
    /// Exibe ou oculta o layout O.P.E.X com animação
    /// </summary>
    private void ExibirOcultarLayoutOpex(bool exibir)
    {
        if (exibir)
        {
            OpexLayoutGrid.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            fadeIn.Completed += (s, a) => OpexLayoutGrid.Focus(); // Dá foco para receber eventos de teclado
            OpexLayoutGrid.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => OpexLayoutGrid.Visibility = Visibility.Collapsed;
            OpexLayoutGrid.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    #endregion

    #region Eventos de Fornecedores na Aba Fornecedores

    /// <summary>
    /// Gerencia o clique em um item de fornecedor na aba Fornecedores
    /// </summary>
    private void FornecedorItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border clickedBorder)
            return;

        // Se clicou no mesmo item, desseleciona
        if (_fornecedorItemSelecionado == clickedBorder)
        {
            AnimarSelecaoFornecedor(clickedBorder, false);
            _fornecedorItemSelecionado = null;
            LimparCamposFornecedor();
            return;
        }

        // Desmarca o item anterior com animação
        if (_fornecedorItemSelecionado != null)
        {
            AnimarSelecaoFornecedor(_fornecedorItemSelecionado, false);
        }

        // Marca o novo item com animação
        AnimarSelecaoFornecedor(clickedBorder, true);
        _fornecedorItemSelecionado = clickedBorder;
        
        // Preenche os campos com os dados do fornecedor
        if (clickedBorder.DataContext is Fornecedor fornecedor)
        {
            PreencherCamposFornecedor(fornecedor);
        }
        
        // Garante que o Grid tenha foco para receber eventos de teclado
        FornecedoresLayoutGrid.Focus();
    }

    /// <summary>
    /// Gerencia teclas pressionadas no layout de fornecedores
    /// </summary>
    private void FornecedoresLayoutGrid_KeyDown(object sender, KeyEventArgs e)
    {
        // Esc: desseleciona o fornecedor atual
        if (e.Key == Key.Escape && _fornecedorItemSelecionado != null)
        {
            AnimarSelecaoFornecedor(_fornecedorItemSelecionado, false);
            _fornecedorItemSelecionado = null;
            LimparCamposFornecedor();
        }

        // Delete: exclui o fornecedor selecionado
        if (e.Key == Key.Delete && _fornecedorItemSelecionado != null)
        {
            ExcluirFornecedorSelecionado();
        }
    }

    /// <summary>
    /// Gerencia teclas pressionadas no layout O.P.E.X
    /// </summary>
    private void OpexLayoutGrid_KeyDown(object sender, KeyEventArgs e)
    {
        // Esc: desseleciona o pagamento atual
        if (e.Key == Key.Escape && _pagamentoBorderSelecionado != null)
        {
            DesselecionarPagamento();
        }
    }

    /// <summary>
    /// Exclui o fornecedor atualmente selecionado
    /// </summary>
    private void ExcluirFornecedorSelecionado()
    {
        if (_fornecedorItemSelecionado?.DataContext is not Fornecedor fornecedor)
            return;

        // Pergunta ao usuário
        string mensagem = "Você deseja excluir o fornecedor:";
        bool confirmado = MostrarPergunta(mensagem, "Excluir Fornecedor", fornecedor.Nome);

        if (!confirmado)
            return;

        try
        {
            // Carrega todos os fornecedores
            string caminhoArquivo = ObterCaminhoArquivoFornecedores();
            string json = File.ReadAllText(caminhoArquivo);
            var fornecedores = JsonSerializer.Deserialize<List<Fornecedor>>(json) ?? new List<Fornecedor>();

            // Remove o fornecedor pelo código
            fornecedores.RemoveAll(f => f.Codigo == fornecedor.Codigo);

            // Salva de volta no arquivo
            SalvarFornecedores(fornecedores, caminhoArquivo);

            // Limpa a seleção
            AnimarSelecaoFornecedor(_fornecedorItemSelecionado, false);
            _fornecedorItemSelecionado = null;
            LimparCamposFornecedor();

            // Recarrega a lista
            RecarregarListaFornecedores();

            MostrarSucesso("Fornecedor excluído com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao excluir fornecedor", ex);
        }
    }

    /// <summary>
    /// Anima a seleção/desseleção de um item de fornecedor
    /// </summary>
    private void AnimarSelecaoFornecedor(Border border, bool selecionar)
    {
        var duration = TimeSpan.FromSeconds(0.2);
        
        // Define as cores
        Color corNormal = Color.FromRgb(42, 42, 45);      // BackgroundLight #2A2A2D
        Color corSelecionada = Color.FromRgb(94, 23, 170); // PrimaryHoverColor #5e17aa
        
        Color corInicial = selecionar ? corNormal : corSelecionada;
        Color corFinal = selecionar ? corSelecionada : corNormal;

        // Cria a animação de cor
        var colorAnimation = new ColorAnimation
        {
            From = corInicial,
            To = corFinal,
            Duration = duration
        };

        // Aplica a animação
        var brush = new SolidColorBrush(corInicial);
        border.Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
    }

    /// <summary>
    /// Preenche os campos com os dados do fornecedor selecionado
    /// </summary>
    private void PreencherCamposFornecedor(Fornecedor fornecedor)
    {
        FornecedorNomeTextBox.Text = fornecedor.Nome;
        FornecedorCodigoTextBox.Text = fornecedor.Codigo.ToString();
        FornecedorNaturezaTextBox.Text = fornecedor.Natureza > 0 ? fornecedor.Natureza.ToString() : string.Empty;
        FornecedorEmailTextBox.Text = fornecedor.Email ?? string.Empty;
        FornecedorDiaPagamentoTextBox.Text = fornecedor.DiaPagamento > 0 ? fornecedor.DiaPagamento.ToString() : string.Empty;
        FornecedorTipoPagamentoTextBox.Text = fornecedor.TipoPagamento > 0 ? fornecedor.TipoPagamento.ToString("D2") : string.Empty;
        
        // Atualiza checkboxes customizadas
        AtivoCheck.Visibility = fornecedor.Ativo ? Visibility.Visible : Visibility.Collapsed;
        AdministradoraCheck.Visibility = fornecedor.Administradora ? Visibility.Visible : Visibility.Collapsed;
        CorretoraCheck.Visibility = fornecedor.Corretora ? Visibility.Visible : Visibility.Collapsed;
        
        // Muda o botão para "Atualizar Fornecedor"
        BtnCadastrarFornecedor.Content = "Atualizar Fornecedor";
    }

    /// <summary>
    /// Limpa todos os campos de fornecedor
    /// </summary>
    private void LimparCamposFornecedor()
    {
        FornecedorNomeTextBox.Text = string.Empty;
        FornecedorCodigoTextBox.Text = string.Empty;
        FornecedorNaturezaTextBox.Text = string.Empty;
        FornecedorEmailTextBox.Text = string.Empty;
        FornecedorDiaPagamentoTextBox.Text = string.Empty;
        FornecedorTipoPagamentoTextBox.Text = string.Empty;
        
        // Reset checkboxes customizadas (Ativo marcado por padrão)
        AtivoCheck.Visibility = Visibility.Visible;
        AdministradoraCheck.Visibility = Visibility.Collapsed;
        CorretoraCheck.Visibility = Visibility.Collapsed;
        
        // Volta o botão para "Cadastrar Fornecedor"
        BtnCadastrarFornecedor.Content = "Cadastrar Fornecedor";
    }

    /// <summary>
    /// Evento disparado quando o texto do nome muda para validar o botão
    /// </summary>
    private void FornecedorNomeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidarBotaoCadastro();
    }

    /// <summary>
    /// Evento disparado quando o texto do código muda para validar o botão
    /// </summary>
    private void FornecedorCodigoTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidarBotaoCadastro();
    }

    /// <summary>
    /// Valida se o botão de cadastro deve estar habilitado
    /// </summary>
    private void ValidarBotaoCadastro()
    {
        bool nomePreenchido = !string.IsNullOrWhiteSpace(FornecedorNomeTextBox.Text);
        bool codigoPreenchido = !string.IsNullOrWhiteSpace(FornecedorCodigoTextBox.Text) && 
                                int.TryParse(FornecedorCodigoTextBox.Text, out _);
        
        bool habilitado = nomePreenchido && codigoPreenchido;
        
        BtnCadastrarFornecedor.IsEnabled = habilitado;
        BtnCadastrarFornecedor.Opacity = habilitado ? 1.0 : 0.4;
    }

    /// <summary>
    /// Valida entrada apenas numérica nos TextBoxes
    /// </summary>
    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// Toggle checkbox Ativo customizada
    /// </summary>
    private void AtivoBox_Click(object sender, MouseButtonEventArgs e)
    {
        AtivoCheck.Visibility = AtivoCheck.Visibility == Visibility.Visible 
            ? Visibility.Collapsed 
            : Visibility.Visible;
    }

    /// <summary>
    /// Toggle checkbox Administradora customizada
    /// </summary>
    private void AdministradoraBox_Click(object sender, MouseButtonEventArgs e)
    {
        AdministradoraCheck.Visibility = AdministradoraCheck.Visibility == Visibility.Visible 
            ? Visibility.Collapsed 
            : Visibility.Visible;
    }

    /// <summary>
    /// Toggle checkbox Corretora customizada
    /// </summary>
    private void CorretoraBox_Click(object sender, MouseButtonEventArgs e)
    {
        CorretoraCheck.Visibility = CorretoraCheck.Visibility == Visibility.Visible 
            ? Visibility.Collapsed 
            : Visibility.Visible;
    }

    /// <summary>
    /// Evento do botão Cadastrar/Atualizar Fornecedor
    /// </summary>
    private void BtnCadastrarFornecedor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Valida e obtém o código
            if (!int.TryParse(FornecedorCodigoTextBox.Text, out int codigo))
            {
                MostrarAviso("O código deve ser um número válido.");
                return;
            }

            // Valida e obtém a natureza (opcional)
            int natureza = 0;
            if (!string.IsNullOrWhiteSpace(FornecedorNaturezaTextBox.Text))
            {
                if (!int.TryParse(FornecedorNaturezaTextBox.Text, out natureza))
                {
                    MostrarAviso("A natureza deve ser um número válido.");
                    return;
                }
            }

            // Valida e obtém o dia de pagamento (opcional, 1-31)
            int diaPagamento = 0;
            if (!string.IsNullOrWhiteSpace(FornecedorDiaPagamentoTextBox.Text))
            {
                if (!int.TryParse(FornecedorDiaPagamentoTextBox.Text, out diaPagamento))
                {
                    MostrarAviso("O dia de pagamento deve ser um número válido.");
                    return;
                }
                
                if (diaPagamento < 1 || diaPagamento > 31)
                {
                    MostrarAviso("O dia de pagamento deve estar entre 1 e 31.");
                    return;
                }
            }

            // Valida e obtém o tipo de pagamento (opcional, 1-3)
            int tipoPagamento = 0;
            if (!string.IsNullOrWhiteSpace(FornecedorTipoPagamentoTextBox.Text))
            {
                if (!int.TryParse(FornecedorTipoPagamentoTextBox.Text, out tipoPagamento))
                {
                    MostrarAviso("O tipo de pagamento deve ser um número válido.");
                    return;
                }
                
                if (tipoPagamento < 1 || tipoPagamento > 3)
                {
                    MostrarAviso("O tipo de pagamento deve estar entre 01 e 03.");
                    return;
                }
            }

            // Cria o objeto fornecedor com os dados dos campos
            var fornecedor = new Fornecedor
            {
                Nome = FornecedorNomeTextBox.Text.Trim(),
                Codigo = codigo,
                Natureza = natureza,
                Email = FornecedorEmailTextBox.Text.Trim(),
                DiaPagamento = diaPagamento,
                TipoPagamento = tipoPagamento,
                Ativo = AtivoCheck.Visibility == Visibility.Visible,
                Administradora = AdministradoraCheck.Visibility == Visibility.Visible,
                Corretora = CorretoraCheck.Visibility == Visibility.Visible
            };

            // Verifica se é atualização ou cadastro novo
            if (_fornecedorItemSelecionado != null && 
                _fornecedorItemSelecionado.DataContext is Fornecedor fornecedorExistente)
            {
                AtualizarFornecedor(fornecedorExistente, fornecedor);
            }
            else
            {
                CadastrarNovoFornecedor(fornecedor);
            }

            // Limpa a seleção e campos
            if (_fornecedorItemSelecionado != null)
            {
                AnimarSelecaoFornecedor(_fornecedorItemSelecionado, false);
                _fornecedorItemSelecionado = null;
            }
            
            LimparCamposFornecedor();

            MostrarSucesso("Fornecedor salvo com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao salvar fornecedor", ex);
        }
    }

    /// <summary>
    /// Atualiza um fornecedor existente
    /// </summary>
    private void AtualizarFornecedor(Fornecedor fornecedorExistente, Fornecedor novosDados)
    {
        // Carrega todos os fornecedores
        string caminhoArquivo = ObterCaminhoArquivoFornecedores();
        string json = File.ReadAllText(caminhoArquivo);
        var fornecedores = JsonSerializer.Deserialize<List<Fornecedor>>(json) ?? new List<Fornecedor>();

        // Encontra e atualiza o fornecedor
        var fornecedor = fornecedores.FirstOrDefault(f => f.Codigo == fornecedorExistente.Codigo);
        if (fornecedor != null)
        {
            fornecedor.Nome = novosDados.Nome;
            fornecedor.Codigo = novosDados.Codigo;
            fornecedor.Natureza = novosDados.Natureza;
            fornecedor.Email = novosDados.Email;
            fornecedor.DiaPagamento = novosDados.DiaPagamento;
            fornecedor.TipoPagamento = novosDados.TipoPagamento;
            fornecedor.Ativo = novosDados.Ativo;
            fornecedor.Administradora = novosDados.Administradora;
            fornecedor.Corretora = novosDados.Corretora;
        }

        // Salva de volta no arquivo
        SalvarFornecedores(fornecedores, caminhoArquivo);
        
        // Recarrega a lista
        RecarregarListaFornecedores();
    }

    /// <summary>
    /// Cadastra um novo fornecedor
    /// </summary>
    private void CadastrarNovoFornecedor(Fornecedor novoFornecedor)
    {
        // Carrega todos os fornecedores
        string caminhoArquivo = ObterCaminhoArquivoFornecedores();
        string json = File.ReadAllText(caminhoArquivo);
        var fornecedores = JsonSerializer.Deserialize<List<Fornecedor>>(json) ?? new List<Fornecedor>();

        // Verifica se já existe um fornecedor com o mesmo código
        if (fornecedores.Any(f => f.Codigo == novoFornecedor.Codigo))
        {
            MostrarAviso("Já existe um fornecedor com este código.");
            return;
        }

        // Adiciona o novo fornecedor
        fornecedores.Add(novoFornecedor);

        // Salva de volta no arquivo
        SalvarFornecedores(fornecedores, caminhoArquivo);
        
        // Recarrega a lista
        RecarregarListaFornecedores();
    }

    /// <summary>
    /// Salva a lista de fornecedores no arquivo JSON
    /// </summary>
    private void SalvarFornecedores(List<Fornecedor> fornecedores, string caminhoArquivo)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string jsonAtualizado = JsonSerializer.Serialize(fornecedores, options);
        File.WriteAllText(caminhoArquivo, jsonAtualizado);
    }

    /// <summary>
    /// Recarrega a lista de fornecedores após alterações
    /// </summary>
    private void RecarregarListaFornecedores()
    {
        _todosFornecedores = CarregarFornecedores()
            .OrderBy(f => f.Nome)
            .ToList();

        // Aplica filtro de pesquisa se houver
        AplicarFiltroPesquisa();
        
        // Atualiza a lista da aba Email (mostra apenas ativos)
        var fornecedoresAtivos = _todosFornecedores.Where(f => f.Ativo).ToList();
        FornecedorItemsControl.ItemsSource = fornecedoresAtivos;
    }

    /// <summary>
    /// Aplica o filtro de pesquisa na lista de fornecedores
    /// </summary>
    private void AplicarFiltroPesquisa()
    {
        string termoPesquisa = PesquisaFornecedorTextBox?.Text?.Trim().ToLower() ?? string.Empty;
        
        if (string.IsNullOrWhiteSpace(termoPesquisa))
        {
            // Sem filtro, mostra todos
            FornecedoresItemsControl.ItemsSource = _todosFornecedores;
        }
        else
        {
            // Filtra por nome ou email
            var fornecedoresFiltrados = _todosFornecedores
                .Where(f => f.Nome.ToLower().Contains(termoPesquisa) ||
                           (!string.IsNullOrEmpty(f.Email) && f.Email.ToLower().Contains(termoPesquisa)))
                .ToList();
            
            FornecedoresItemsControl.ItemsSource = fornecedoresFiltrados;
        }
    }

    /// <summary>
    /// Evento disparado quando o texto da pesquisa muda
    /// </summary>
    private void PesquisaFornecedorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AplicarFiltroPesquisa();
    }

    #endregion

    #region Eventos de Fornecedores

    /// <summary>
    /// Gerencia a seleção de fornecedores
    /// </summary>
    private void FornecedorToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clickedButton)
            return;

        // Desmarca o fornecedor anterior
        if (_fornecedorSelecionado != null && _fornecedorSelecionado != clickedButton)
        {
            _fornecedorSelecionado.IsChecked = false;
        }

        // Atualiza a seleção atual
        _fornecedorSelecionado = clickedButton.IsChecked == true ? clickedButton : null;
    }

    #endregion

    #region Eventos do Layout Dropdown

    /// <summary>
    /// Evento de expansão do dropdown de layout
    /// </summary>
    private void LayoutExpander_Expanded(object sender, RoutedEventArgs e)
    {
        AnimarSetaExpander(true);
    }

    /// <summary>
    /// Evento de colapso do dropdown de layout
    /// </summary>
    private void LayoutExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        AnimarSetaExpander(false);
    }

    /// <summary>
    /// Gerencia o clique no cabeçalho do layout
    /// </summary>
    private void LayoutHeader_Click(object sender, MouseButtonEventArgs e)
    {
        LayoutExpander.IsExpanded = !LayoutExpander.IsExpanded;
    }

    /// <summary>
    /// Gerencia o clique em um item do layout
    /// </summary>
    private void LayoutItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock textBlock)
            return;

        LayoutHeaderText.Text = textBlock.Text;
        LayoutExpander.IsExpanded = false;
    }

    /// <summary>
    /// Efeito hover nos itens do layout
    /// </summary>
    private void LayoutItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            tb.Foreground = new SolidColorBrush(Color.FromRgb(121, 32, 220)); // #7920dc
        }
    }

    /// <summary>
    /// Remove efeito hover nos itens do layout
    /// </summary>
    private void LayoutItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            tb.Foreground = Brushes.White;
        }
    }

    #endregion

    #region Envio de Emails

    /// <summary>
    /// Gerencia o envio de emails para fornecedores
    /// </summary>
    private void EnviarEmailsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ValidarSelecaoFornecedor())
                return;

            var fornecedor = (_fornecedorSelecionado!.DataContext as Fornecedor)!;
            
            string mailtoUrl = ConstruirMailtoUrl(fornecedor);
            AbrirClienteEmail(mailtoUrl);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao enviar email", ex);
        }
    }

    /// <summary>
    /// Valida se há um fornecedor selecionado
    /// </summary>
    private bool ValidarSelecaoFornecedor()
    {
        if (_fornecedorSelecionado == null || _fornecedorSelecionado.DataContext is not Fornecedor)
        {
            MostrarAviso("Selecione um fornecedor primeiro.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Constrói a URL mailto com os dados do email
    /// </summary>
    private string ConstruirMailtoUrl(Fornecedor fornecedor)
    {
        string saudacao = ObterSaudacao();
        string nomeMes = ObterNomeMesAnterior();
        string corpo = GerarCorpoEmail(saudacao, nomeMes);
        string assunto = "Solicitação de Nota Fiscal - Positiva";

        return $"mailto:{fornecedor.Email}" +
               $"?from=Financeiro@positiva.com.br" +
               $"&subject={Uri.EscapeDataString(assunto)}" +
               $"&body={Uri.EscapeDataString(corpo)}";
    }

    /// <summary>
    /// Retorna a saudação adequada baseada no horário
    /// </summary>
    private string ObterSaudacao()
    {
        return DateTime.Now.Hour < 12 ? "bom dia" : "boa tarde";
    }

    /// <summary>
    /// Retorna o nome do mês anterior formatado
    /// </summary>
    private string ObterNomeMesAnterior()
    {
        DateTime mesAnterior = DateTime.Now.AddMonths(-1);
        string nomeMes = mesAnterior.ToString("MMMM", new System.Globalization.CultureInfo("pt-BR"));
        return char.ToUpper(nomeMes[0]) + nomeMes.Substring(1);
    }

    /// <summary>
    /// Gera o corpo do email de cobrança
    /// </summary>
    private string GerarCorpoEmail(string saudacao, string nomeMes)
    {
        return $"Prezados, {saudacao},\n\n" +
               $"Encaminhamos recentemente o extrato de comissão referente ao mês de {nomeMes}. " +
               "Contudo, ainda não recebemos a sua nota fiscal correspondente. " +
               "Gostaríamos de verificar a possibilidade de envio da nota fiscal o quanto antes, " +
               "a fim de viabilizar a adequada programação financeira.\n\n" +
               "Caso precise do reencaminhamento do extrato me notifique que irei providenciar.\n\n" +
               "Atenciosamente,\n" +
               "Equipe Financeira - Positiva";
    }

    /// <summary>
    /// Abre o cliente de email padrão com a URL mailto
    /// </summary>
    private void AbrirClienteEmail(string mailtoUrl)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo(mailtoUrl)
            {
                UseShellExecute = true
            };

            Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            throw new Exception("Não foi possível abrir o cliente de email.", ex);
        }
    }

    #endregion

    #region Eventos O.P.E.X

    /// <summary>
    /// Evento disparado quando uma tecla é pressionada no ComboBox (para impedir auto-seleção)
    /// </summary>
    private void OpexFornecedorComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        // Permite navegação com setas quando o dropdown está aberto
        if (comboBox.IsDropDownOpen && (e.Key == Key.Down || e.Key == Key.Up))
        {
            return; // Deixa o comportamento padrão de navegação
        }

        // Se pressionar Enter, seleciona o item destacado
        if (e.Key == Key.Enter && comboBox.IsDropDownOpen)
        {
            e.Handled = true;
            comboBox.IsDropDownOpen = false;
        }
    }

    /// <summary>
    /// Evento disparado quando o texto do ComboBox de fornecedores muda (autocomplete)
    /// </summary>
    private void OpexFornecedorComboBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox comboBox)
                return;

            // Se está carregando programaticamente ou já estamos atualizando o filtro, ignora.
            if (_isLoadingPagamento || _isAtualizandoAutocompleteOpex)
                return;

            // Obtém o TextBox interno do ComboBox.
            var textBox = comboBox.Template?.FindName("PART_EditableTextBox", comboBox) as TextBox;
            if (textBox == null)
                return;

            // IMPORTANTE: preserva exatamente o texto e o cursor digitados pelo usuário.
            // Ao trocar o ItemsSource, o ComboBox editável pode selecionar temporariamente
            // o texto. Se a próxima tecla chegar antes de limpar essa seleção, ela substitui
            // a primeira letra (ex.: INSS vira NSS).
            string textoDigitado = textBox.Text;
            int cursorPosition = textBox.SelectionStart;
            string textoPesquisa = textoDigitado.Trim();

            if (OpexPlaceholder != null)
            {
                OpexPlaceholder.Visibility = string.IsNullOrWhiteSpace(textoPesquisa)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            _isAtualizandoAutocompleteOpex = true;
            try
            {
                if (string.IsNullOrWhiteSpace(textoPesquisa))
                {
                    comboBox.ItemsSource = _fornecedoresOpex;
                    comboBox.SelectedItem = null;
                    comboBox.IsDropDownOpen = false;
                }
                else
                {
                    string textoPesquisaLower = textoPesquisa.ToLowerInvariant();
                    var fornecedoresFiltrados = _fornecedoresOpex
                        .Where(f => f.Nome.ToLowerInvariant().Contains(textoPesquisaLower))
                        .OrderBy(f => f.Nome.ToLowerInvariant().StartsWith(textoPesquisaLower) ? 0 : 1)
                        .ThenBy(f => f.Nome)
                        .ToList();

                    comboBox.ItemsSource = fornecedoresFiltrados;
                    comboBox.SelectedItem = null;
                    comboBox.IsDropDownOpen = fornecedoresFiltrados.Any();
                }

                // Restaura já no mesmo ciclo do evento, sem esperar o Background dispatcher.
                textBox.Text = textoDigitado;
                textBox.SelectionStart = Math.Min(cursorPosition, textBox.Text.Length);
                textBox.SelectionLength = 0;
            }
            finally
            {
                _isAtualizandoAutocompleteOpex = false;
            }

            // O template do ComboBox ainda pode tentar mexer na seleção ao abrir o dropdown.
            // Reforça o cursor na fila de INPUT, antes que a próxima tecla do usuário seja processada.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!textBox.IsKeyboardFocusWithin)
                    return;

                int posicao = Math.Min(textBox.Text.Length, cursorPosition);
                textBox.SelectionStart = posicao;
                textBox.SelectionLength = 0;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao filtrar fornecedores", ex);
        }
    }

    /// <summary>
    /// Evento disparado quando um fornecedor é selecionado no ComboBox
    /// </summary>
    private void OpexFornecedorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox comboBox)
                return;

            // Oculta o placeholder quando há seleção
            if (comboBox.SelectedItem is Fornecedor fornecedor)
            {
                if (OpexPlaceholder != null)
                    OpexPlaceholder.Visibility = Visibility.Collapsed;
                
                // Apenas executa ações automáticas se NÃO estiver carregando programaticamente
                if (!_isLoadingPagamento)
                {
                    // Calcula e preenche a data automaticamente
                    CalcularEPreencherDataPagamento(fornecedor);
                    
                    // Preenche empresa automaticamente baseado no fornecedor
                    PreencherEmpresaAutomatica(fornecedor);
                }
            }
            
            // Fecha o dropdown quando um item é selecionado (apenas se não estiver carregando)
            if (comboBox.SelectedItem != null && !_isLoadingPagamento)
            {
                comboBox.IsDropDownOpen = false;
            }
            
            // Atualiza estado dos botões
            AtualizarEstadoBotoes();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao selecionar fornecedor", ex);
        }
    }

    /// <summary>
    /// Preenche a empresa automaticamente baseado no fornecedor
    /// </summary>
    private void PreencherEmpresaAutomatica(Fornecedor fornecedor)
    {
        try
        {
            // Se tem ambos ADM e COR, não seleciona nenhum
            if (fornecedor.Administradora && fornecedor.Corretora)
            {
                OpexEmpresaComboBox.SelectedItem = null;
                return;
            }
            
            // Se só tem ADM
            if (fornecedor.Administradora)
            {
                foreach (ComboBoxItem item in OpexEmpresaComboBox.Items)
                {
                    if (item.Content.ToString() == "ADM")
                    {
                        OpexEmpresaComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            // Se só tem COR
            else if (fornecedor.Corretora)
            {
                foreach (ComboBoxItem item in OpexEmpresaComboBox.Items)
                {
                    if (item.Content.ToString() == "COR")
                    {
                        OpexEmpresaComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            // Se não tem nem ADM nem COR, não seleciona
            else
            {
                OpexEmpresaComboBox.SelectedItem = null;
            }
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao preencher empresa", ex);
        }
    }

    /// <summary>
    /// Evento de mudança de seleção da empresa
    /// </summary>
    private void OpexEmpresaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            // Oculta placeholder quando há seleção
            if (OpexEmpresaPlaceholder != null)
            {
                OpexEmpresaPlaceholder.Visibility = OpexEmpresaComboBox.SelectedItem != null
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            
            // Atualiza estado dos botões
            AtualizarEstadoBotoes();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao selecionar empresa", ex);
        }
    }

    /// <summary>
    /// Calcula a data de pagamento baseada no dia de pagamento do fornecedor
    /// </summary>
    private void CalcularEPreencherDataPagamento(Fornecedor fornecedor)
    {
        try
        {
            DateTime hoje = DateTime.Now;
            int diaPagamento = fornecedor.DiaPagamento;
            
            // Cria a data do pagamento no mês atual
            DateTime dataPagamentoMesAtual = new DateTime(hoje.Year, hoje.Month, 
                Math.Min(diaPagamento, DateTime.DaysInMonth(hoje.Year, hoje.Month)));
            
            DateTime dataPagamento;
            
            // Verifica se a data do mês atual já passou
            if (hoje.Day > diaPagamento || (hoje.Day == diaPagamento && hoje.TimeOfDay.TotalHours >= 24))
            {
                // Já passou, pega o próximo mês
                DateTime proximoMes = hoje.AddMonths(1);
                int diasNoProximoMes = DateTime.DaysInMonth(proximoMes.Year, proximoMes.Month);
                dataPagamento = new DateTime(proximoMes.Year, proximoMes.Month, 
                    Math.Min(diaPagamento, diasNoProximoMes));
            }
            else
            {
                // Ainda não passou, usa o mês atual
                dataPagamento = dataPagamentoMesAtual;
            }
            
            // Preenche a TextBox com a data formatada
            DefinirDataNoTexto(dataPagamento);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao calcular data de pagamento", ex);
        }
    }

    /// <summary>
    /// Valida entrada numérica no campo de valor (apenas números, vírgula e ponto)
    /// </summary>
    private void OpexValorTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Permite apenas números, vírgula e ponto
        if (!char.IsDigit(e.Text[0]) && e.Text[0] != ',' && e.Text[0] != '.')
        {
            e.Handled = true;
            return;
        }
        
        // Impede mais de uma vírgula ou ponto
        if ((e.Text[0] == ',' || e.Text[0] == '.') && 
            (OpexValorTextBox.Text.Contains(',') || OpexValorTextBox.Text.Contains('.')))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Controla o placeholder do campo de valor
    /// </summary>
    private void OpexValorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OpexValorPlaceholder != null)
        {
            OpexValorPlaceholder.Visibility = string.IsNullOrWhiteSpace(OpexValorTextBox.Text) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
        
        // Atualiza estado dos botões
        AtualizarEstadoBotoes();
    }

    #region Eventos da Aba O.P.E.X - Data (TextBox com Formatação)


    /// <summary>
    /// Valida entrada de texto para aceitar apenas números na data
    /// </summary>
    private void OpexDataTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Permite apenas dígitos
        e.Handled = !char.IsDigit(e.Text[0]);
    }

    /// <summary>
    /// Formata automaticamente a data enquanto o usuário digita
    /// Formato: DD/MM/AAAA
    /// </summary>
    private void OpexDataTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFormattingDate) return;

        try
        {
            _isFormattingDate = true;

            var textBox = (TextBox)sender;
            string texto = textBox.Text.Replace("/", ""); // Remove barras existentes
            int cursorPos = textBox.SelectionStart;
            
            // Controla o placeholder
            if (OpexDataPlaceholder != null)
            {
                OpexDataPlaceholder.Visibility = string.IsNullOrWhiteSpace(texto) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }

            if (string.IsNullOrWhiteSpace(texto))
            {
                _isFormattingDate = false;
                return;
            }

            // Remove caracteres não numéricos
            texto = new string(texto.Where(char.IsDigit).ToArray());
            
            // Limita a 8 dígitos (DDMMAAAA)
            if (texto.Length > 8)
                texto = texto.Substring(0, 8);

            // Formata conforme o usuário digita
            string textoFormatado = "";
            
            if (texto.Length >= 1)
            {
                // Primeiros 2 dígitos (DD)
                textoFormatado = texto.Substring(0, Math.Min(2, texto.Length));
                
                if (texto.Length >= 3)
                {
                    // Adiciona primeira barra após DD
                    textoFormatado += "/" + texto.Substring(2, Math.Min(2, texto.Length - 2));
                    
                    if (texto.Length >= 5)
                    {
                        // Adiciona segunda barra após MM
                        textoFormatado += "/" + texto.Substring(4);
                    }
                }
            }

            // Atualiza o texto apenas se mudou
            if (textBox.Text != textoFormatado)
            {
                int barrasAntes = textBox.Text.Substring(0, Math.Min(cursorPos, textBox.Text.Length)).Count(c => c == '/');
                
                textBox.Text = textoFormatado;
                
                int barrasDepois = textoFormatado.Substring(0, Math.Min(cursorPos, textoFormatado.Length)).Count(c => c == '/');
                int novoCursor = cursorPos + (barrasDepois - barrasAntes);
                
                // Ajusta posição do cursor
                textBox.SelectionStart = Math.Max(0, Math.Min(novoCursor, textoFormatado.Length));
            }
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao formatar data", ex);
        }
        finally
        {
            _isFormattingDate = false;
            // Atualiza estado dos botões
            AtualizarEstadoBotoes();
        }
    }

    /// <summary>
    /// Trata teclas especiais (Backspace, Delete) para formatação correta
    /// </summary>
    private void OpexDataTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var textBox = (TextBox)sender;
        
        // Se pressionar Backspace em uma barra, apaga o número antes dela
        if (e.Key == Key.Back && textBox.SelectionStart > 0)
        {
            int pos = textBox.SelectionStart;
            if (pos > 0 && textBox.Text[pos - 1] == '/')
            {
                textBox.Text = textBox.Text.Remove(pos - 2, 2);
                textBox.SelectionStart = pos - 2;
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Obtém a data da TextBox formatada ou retorna null se inválida
    /// </summary>
    private DateTime? ObterDataDoTexto()
    {
        try
        {
            string texto = OpexDataTextBox.Text;
            
            // Verifica se está no formato completo DD/MM/AAAA
            if (texto.Length == 10 && texto.Count(c => c == '/') == 2)
            {
                if (DateTime.TryParseExact(texto, "dd/MM/yyyy", 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, 
                    out DateTime data))
                {
                    return data;
                }
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Define a data na TextBox no formato DD/MM/AAAA
    /// </summary>
    private void DefinirDataNoTexto(DateTime data)
    {
        try
        {
            OpexDataTextBox.Text = data.ToString("dd/MM/yyyy");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao definir data", ex);
        }
    }

    /// <summary>
    /// Evento de clique em um item de pagamento (CLIQUE ÚNICO)
    /// </summary>
    private void PagamentoItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not Border border || border.Tag is not PrevisaoPagamento pagamento)
                return;

            // Só permite selecionar pagamentos com status "Pendente"
            if (pagamento.Status != "Pendente")
                return;

            // Se clicou no mesmo item já selecionado, desseleciona
            if (_pagamentoBorderSelecionado == border)
            {
                DesselecionarPagamento();
                return;
            }
            
            // Desseleciona o anterior (se houver)
            if (_pagamentoBorderSelecionado != null)
            {
                _pagamentoBorderSelecionado.Background = new SolidColorBrush(Colors.Transparent);
            }
            
            // Seleciona o novo com cor roxa (SEM alterar opacidade do texto)
            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4d2266"));
            _pagamentoBorderSelecionado = border;
            
            // Abre para edição
            CarregarPagamentoParaEdicao(pagamento);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao selecionar pagamento", ex);
        }
    }

    /// <summary>
    /// Copia a natureza do pagamento para o clipboard
    /// </summary>
    private void CopiarNatureza_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Obtém o MenuItem e seu contexto
            if (sender is not MenuItem menuItem)
                return;

            // Obtém o ContextMenu pai
            if (menuItem.Parent is not ContextMenu contextMenu)
                return;

            // Obtém o Border que possui o ContextMenu
            if (contextMenu.PlacementTarget is not Border border)
                return;

            // Obtém o pagamento do Tag
            if (border.Tag is not PrevisaoPagamento pagamento)
                return;

            // Copia a natureza para o clipboard (converte int para string)
            string naturezaTexto = pagamento.Natureza.ToString();
            Clipboard.SetText(naturezaTexto);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao copiar natureza", ex);
        }
    }

    /// <summary>
    /// Copia o código do fornecedor para o clipboard
    /// </summary>
    private void CopiarCodigo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Obtém o MenuItem e seu contexto
            if (sender is not MenuItem menuItem)
                return;

            // Obtém o ContextMenu pai
            if (menuItem.Parent is not ContextMenu contextMenu)
                return;

            // Obtém o Border que possui o ContextMenu
            if (contextMenu.PlacementTarget is not Border border)
                return;

            // Obtém o pagamento do Tag
            if (border.Tag is not PrevisaoPagamento pagamento)
                return;

            // Copia o código formatado (6 dígitos) para o clipboard
            string codigoFormatado = pagamento.CodigoFornecedor.ToString("D6");
            Clipboard.SetText(codigoFormatado);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao copiar código", ex);
        }
    }

    /// <summary>
    /// Copia o tipo de pagamento para o clipboard
    /// </summary>
    private void CopiarTipoPgto_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem menuItem)
                return;

            if (menuItem.Parent is not ContextMenu contextMenu)
                return;

            if (contextMenu.PlacementTarget is not Border border)
                return;

            if (border.Tag is not PrevisaoPagamento pagamento)
                return;

            if (pagamento.TipoPagamento == 0)
            {
                MostrarAviso("Este pagamento não possui Tipo de Pagamento definido.");
                return;
            }

            // Copia formatado com 2 dígitos (01, 02, 03)
            string tipoPgtoFormatado = pagamento.TipoPagamento.ToString("D2");
            Clipboard.SetText(tipoPgtoFormatado);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao copiar tipo de pagamento", ex);
        }
    }
    private void DesselecionarPagamento()
    {
        // Remove seleção visual
        if (_pagamentoBorderSelecionado != null)
        {
            _pagamentoBorderSelecionado.Background = new SolidColorBrush(Colors.Transparent);
            _pagamentoBorderSelecionado = null;
        }
        
        // Limpa dados
        _pagamentoSelecionado = null;
        
        // Limpa campos
        OpexFornecedorComboBox.SelectedItem = null;
        OpexDataTextBox.Text = string.Empty;
        OpexValorTextBox.Text = string.Empty;
        OpexEmpresaComboBox.SelectedItem = null;
        
        // Atualiza botões
        AtualizarEstadoBotoes();
    }

    /// <summary>
    /// Carrega os dados do pagamento nos campos de edição
    /// </summary>
    private void CarregarPagamentoParaEdicao(PrevisaoPagamento pagamento)
    {
        try
        {
            // Ativa flag para indicar que está carregando programaticamente
            _isLoadingPagamento = true;
            
            // Armazena o pagamento selecionado
            _pagamentoSelecionado = pagamento;

            // Busca o fornecedor correspondente
            // Se o código for 000000, busca pelo nome (pois vários podem ter código zerado)
            Fornecedor? fornecedor = null;
            
            if (pagamento.CodigoFornecedor == 0 || pagamento.CodigoFornecedor.ToString() == "000000")
            {
                // Busca pelo nome para códigos zerados
                fornecedor = _fornecedoresOpex.FirstOrDefault(f => f.Nome == pagamento.NomeFornecedor);
            }
            else
            {
                // Busca pelo código normalmente
                fornecedor = _fornecedoresOpex.FirstOrDefault(f => f.Codigo == pagamento.CodigoFornecedor);
            }
            
            if (fornecedor != null)
            {
                // Define o texto e a seleção
                OpexFornecedorComboBox.Text = fornecedor.Nome;
                OpexFornecedorComboBox.SelectedItem = fornecedor;
                
                // Oculta o placeholder
                if (OpexPlaceholder != null)
                    OpexPlaceholder.Visibility = Visibility.Collapsed;
            }

            // Preenche a data
            DefinirDataNoTexto(pagamento.DataPagamento);

            // Preenche o valor (converte para formato brasileiro)
            OpexValorTextBox.Text = pagamento.Valor.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

            // Preenche a empresa
            if (!string.IsNullOrEmpty(pagamento.Empresa))
            {
                foreach (ComboBoxItem item in OpexEmpresaComboBox.Items)
                {
                    if (item.Content.ToString() == pagamento.Empresa)
                    {
                        OpexEmpresaComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            // Atualiza botões
            AtualizarEstadoBotoes();
            
            // Garante que o dropdown do ComboBox não abra
            OpexFornecedorComboBox.IsDropDownOpen = false;
            
            // Desativa flag após carregar todos os dados
            _isLoadingPagamento = false;
        }
        catch (Exception ex)
        {
            _isLoadingPagamento = false; // Garante que desativa mesmo em caso de erro
            MostrarErro("Erro ao carregar pagamento para edição", ex);
        }
    }

    /// <summary>
    /// Atualiza o estado (habilitado/desabilitado) dos botões
    /// </summary>
    private void AtualizarEstadoBotoes()
    {
        try
        {
            // Verifica se os campos obrigatórios estão preenchidos
            bool fornecedorPreenchido = OpexFornecedorComboBox.SelectedItem != null;
            bool dataPreenchida = !string.IsNullOrWhiteSpace(OpexDataTextBox.Text) && OpexDataTextBox.Text.Length == 10;
            bool valorPreenchido = !string.IsNullOrWhiteSpace(OpexValorTextBox.Text);
            bool empresaPreenchida = OpexEmpresaComboBox.SelectedItem != null;
            
            bool camposValidos = fornecedorPreenchido && dataPreenchida && valorPreenchido && empresaPreenchida;
            
            // Botão Registrar/Atualizar
            if (_pagamentoSelecionado != null)
            {
                // Modo Atualizar
                BtnRegistrarPagamento.Content = "Atualizar Registro";
                BtnRegistrarPagamento.IsEnabled = camposValidos;
                BtnRegistrarPagamento.Opacity = camposValidos ? 1.0 : 0.4;
            }
            else
            {
                // Modo Registrar
                BtnRegistrarPagamento.Content = "Registrar Pagamento";
                BtnRegistrarPagamento.IsEnabled = camposValidos;
                BtnRegistrarPagamento.Opacity = camposValidos ? 1.0 : 0.4;
            }
            
            // Botão Excluir (só ativo se tiver pagamento selecionado)
            bool temSelecionado = _pagamentoSelecionado != null;
            BtnExcluirPagamento.IsEnabled = temSelecionado;
            BtnExcluirPagamento.Opacity = temSelecionado ? 1.0 : 0.4;
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao atualizar botões", ex);
        }
    }

    /// <summary>
    /// Evento do botão Registrar/Atualizar Pagamento
    /// </summary>
    private void BtnRegistrarPagamento_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Valida os campos
            if (OpexFornecedorComboBox.SelectedItem is not Fornecedor fornecedor)
            {
                MostrarAviso("Selecione um fornecedor.");
                return;
            }

            DateTime? data = ObterDataDoTexto();
            if (!data.HasValue)
            {
                MostrarAviso("Digite uma data válida no formato DD/MM/AAAA.");
                return;
            }

            if (!decimal.TryParse(OpexValorTextBox.Text.Replace(".", "").Replace(",", "."), 
                System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, 
                out decimal valor) || valor <= 0)
            {
                MostrarAviso("Digite um valor válido maior que zero.");
                return;
            }

            // Valida empresa
            if (OpexEmpresaComboBox.SelectedItem is not ComboBoxItem empresaItem)
            {
                MostrarAviso("Selecione uma empresa (ADM ou COR).");
                return;
            }
            string empresa = empresaItem.Content.ToString() ?? string.Empty;

            // Modo Atualizar
            if (_pagamentoSelecionado != null)
            {
                AtualizarPagamento(fornecedor, data.Value, valor, empresa);
            }
            // Modo Registrar
            else
            {
                RegistrarNovoPagamento(fornecedor, data.Value, valor, empresa);
            }
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao processar pagamento", ex);
        }
    }

    /// <summary>
    /// Registra um novo pagamento
    /// </summary>
    private void RegistrarNovoPagamento(Fornecedor fornecedor, DateTime data, decimal valor, string empresa)
    {
        try
        {
            // Carrega todos os pagamentos
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            var pagamentos = CarregarPrevisoesPagamento();

            // Gera novo ID (maior ID + 1)
            int novoId = pagamentos.Any() ? pagamentos.Max(p => p.Id) + 1 : 1;

            // Cria novo pagamento
            var novoPagamento = new PrevisaoPagamento
            {
                Id = novoId,
                CodigoFornecedor = fornecedor.Codigo,
                NomeFornecedor = fornecedor.Nome,
                Natureza = fornecedor.Natureza,
                TipoPagamento = fornecedor.TipoPagamento,
                Valor = valor,
                DataPagamento = data,
                Status = "Pendente",
                Empresa = empresa
            };

            // Adiciona à lista
            pagamentos.Add(novoPagamento);

            // Salva no arquivo
            SalvarPrevisoes(pagamentos, caminhoArquivo);

            // Atualiza a interface
            _previsoesPagamento = pagamentos.OrderBy(p => p.DataPagamento).ToList();
            PagamentosItemsControl.ItemsSource = null;
            PagamentosItemsControl.ItemsSource = _previsoesPagamento;

            // Limpa os campos
            DesselecionarPagamento();

            // Mensagem removida para agilizar o fluxo
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao registrar pagamento", ex);
        }
    }

    /// <summary>
    /// Atualiza um pagamento existente
    /// </summary>
    private void AtualizarPagamento(Fornecedor fornecedor, DateTime data, decimal valor, string empresa)
    {
        try
        {
            if (_pagamentoSelecionado == null)
                return;

            // Carrega todos os pagamentos
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            var pagamentos = CarregarPrevisoesPagamento();

            // Encontra o pagamento a atualizar
            var pagamento = pagamentos.FirstOrDefault(p => p.Id == _pagamentoSelecionado.Id);
            if (pagamento == null)
            {
                MostrarErro("Pagamento não encontrado.");
                return;
            }

            // Atualiza os dados
            pagamento.CodigoFornecedor = fornecedor.Codigo;
            pagamento.NomeFornecedor = fornecedor.Nome;
            pagamento.Natureza = fornecedor.Natureza;
            pagamento.TipoPagamento = fornecedor.TipoPagamento;
            pagamento.Valor = valor;
            pagamento.DataPagamento = data;
            pagamento.Empresa = empresa;

            // Salva no arquivo
            SalvarPrevisoes(pagamentos, caminhoArquivo);

            // Atualiza a interface
            _previsoesPagamento = pagamentos.OrderBy(p => p.DataPagamento).ToList();
            PagamentosItemsControl.ItemsSource = null;
            PagamentosItemsControl.ItemsSource = _previsoesPagamento;

            // Limpa a seleção
            DesselecionarPagamento();

            MostrarSucesso("Pagamento atualizado com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao atualizar pagamento", ex);
        }
    }

    /// <summary>
    /// Evento do botão Excluir Pagamento
    /// </summary>
    private void BtnExcluirPagamento_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pagamentoSelecionado == null)
                return;

            // Pergunta ao usuário
            string mensagem = "Você deseja excluir o pagamento:";
            string detalhe = $"{_pagamentoSelecionado.NomeFornecedor} - R$ {_pagamentoSelecionado.Valor:N2}";
            bool confirmado = MostrarPergunta(mensagem, "Excluir Pagamento", detalhe);

            if (!confirmado)
                return;

            // Carrega todos os pagamentos
            string caminhoArquivo = ObterCaminhoArquivoPrevisoes();
            var pagamentos = CarregarPrevisoesPagamento();

            // Remove o pagamento
            pagamentos.RemoveAll(p => p.Id == _pagamentoSelecionado.Id);

            // Salva no arquivo
            SalvarPrevisoes(pagamentos, caminhoArquivo);

            // Atualiza a interface
            _previsoesPagamento = pagamentos.OrderBy(p => p.DataPagamento).ToList();
            PagamentosItemsControl.ItemsSource = null;
            PagamentosItemsControl.ItemsSource = _previsoesPagamento;

            // Limpa a seleção
            DesselecionarPagamento();

            MostrarSucesso("Pagamento excluído com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao excluir pagamento", ex);
        }
    }

    /// <summary>
    /// Abre janela de Movimentação de Registros
    /// </summary>
    private void BtnMovimentarRegistros_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var movimentacaoWindow = new MovimentacaoWindow
            {
                Owner = this
            };
            movimentacaoWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao abrir janela de movimentação", ex);
        }
    }

    // Caminho do último arquivo de lote selecionado
    private string? _caminhoArquivoImportacaoLote = null;

    /// <summary>
    /// Abre o seletor de arquivo .xlsx e dispara a importação do lote de pagamentos.
    /// Usa ClosedXML 
    /// </summary>
    private void BtnImportar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var importarWindow = new ImportarWindow { Owner = this };
            if (importarWindow.ShowDialog() != true)
                return;

            if (importarWindow.TipoImportacao == TipoImportacao.Lote)
            {
                _caminhoArquivoImportacaoLote = importarWindow.CaminhoArquivo;
                Debug.WriteLine($"📂 Lote selecionado: {_caminhoArquivoImportacaoLote}");
                ImportarLotePagamentos(_caminhoArquivoImportacaoLote);
            }
            else if (importarWindow.TipoImportacao == TipoImportacao.Nota)
            {
                string caminhoPdf = importarWindow.CaminhoArquivo;
                Debug.WriteLine($"📄 Nota fiscal selecionada: {caminhoPdf}");
                ImportarNotaFiscal(caminhoPdf);
            }
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao importar", ex);
        }
    }

    // ============================================================
    // IMPORTAR NOTA FISCAL — NFS-e SP
    // ============================================================
    // DEPENDÊNCIA: adicionar ao .csproj via NuGet:
    //   <PackageReference Include="itext7" Version="8.*" />
    // Ou via terminal: dotnet add package itext7
    // ============================================================

    // CNPJs fixos das empresas tomadoras
    private const string CNPJ_ADM = "25006061000197";
    private const string CNPJ_COR = "28929873000100";

    /// <summary>
    /// Lê o PDF de nota fiscal, interpreta os dados via NfseSpParser,
    /// exibe um resumo ao usuário e — após confirmação — importa o
    /// registro como pagamento Pendente na O.P.E.X.
    /// </summary>
    private void ImportarNotaFiscal(string caminhoPdf)
    {
        try
        {
            Log($"ImportarNotaFiscal: iniciando leitura de '{Path.GetFileName(caminhoPdf)}'");

            // ── 1. Extrai e valida o texto do PDF ────────────────────────────
            string texto = ExtrairTextoPdf(caminhoPdf);

            // ⚠️ IMPORTANTE: Se o texto extraído for muito pequeno ou vazio,
            // NÃO mostra erro - tenta OCR automaticamente (PDF pode ser imagem)
            if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < 100)
            {
                Log($"ImportarNotaFiscal: texto insuficiente ({texto?.Length ?? 0} chars), PDF pode ser imagem escaneada.");
                Log("ImportarNotaFiscal: ExtrairTextoPdf já tentou OCR automaticamente.");
                
                // Se mesmo com OCR não conseguiu texto suficiente, mostra erro
                if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < 100)
                {
                    CustomMessageBox.ShowWarning(
                        "Não foi possível extrair texto do PDF, mesmo com OCR.\n\n" +
                        "Possíveis causas:\n" +
                        "• Arquivo corrompido ou protegido\n" +
                        "• Qualidade da imagem muito baixa\n" +
                        "• Formato não suportado");
                    return;
                }
            }

            var nota = NfseSpParser.Parse(texto);

            // ── Fallback: número da nota pelo nome do arquivo ─────────────────
            // Quando o OCR garbleia os dígitos do número (ex: "4088961" → "sioago6"),
            // o regex do parser falha. Se o arquivo foi nomeado com o número
            // (ex: "NF de n° 004088961.pdf"), extraímos daqui como último recurso.
            if (string.IsNullOrEmpty(nota.NumeroNota))
            {
                var mFilename = System.Text.RegularExpressions.Regex.Match(
                    Path.GetFileNameWithoutExtension(caminhoPdf), @"\d{4,}");
                if (mFilename.Success)
                {
                    nota.NumeroNota = mFilename.Value.TrimStart('0').PadLeft(1, '0');
                    Log($"ImportarNotaFiscal: NumeroNota obtido via nome do arquivo → [{nota.NumeroNota}]");
                }
            }

            if (!nota.EhNfseSpValida)
            {
                CustomMessageBox.ShowWarning(
                    "O PDF selecionado não foi reconhecido como uma NFS-e do município de São Paulo.\n\n" +
                    "Atualmente apenas notas do município de SP são suportadas.\n" +
                    "O suporte a outros estados/municípios será implementado futuramente.");
                return;
            }

            // Log diagnóstico: exibe as primeiras linhas brutas
            {
                var lb = texto.Split('\n');
                var sb = new System.Text.StringBuilder();
                for (int _i = 0; _i < Math.Min(lb.Length, 15); _i++)
                    sb.AppendLine($"  L{_i:00}: [{lb[_i].Replace("\r","")}]");
                Log($"ImportarNotaFiscal: RAW texto extraído (15 linhas):\n{sb}");
            }
            Log($"ImportarNotaFiscal: DataEmissao capturada: [{nota.DataEmissao}]");
            Log($"ImportarNotaFiscal: nota SP Nº {nota.NumeroNota} — Prestador: {nota.PrestadorNome}");

            // ── 2. Localiza o fornecedor na base cadastrada ───────────────────
            var fornecedores = CarregarFornecedores();
            var (fornecedor, erroFornecedor) = BuscarFornecedorPorNomeNota(nota.PrestadorNome, fornecedores);

            if (fornecedor is null)
            {
                CustomMessageBox.ShowWarning(erroFornecedor!, "Fornecedor não identificado");
                Log($"ImportarNotaFiscal: {erroFornecedor}");
                return;
            }

            // ── 3. Determina a empresa pelo CNPJ do tomador ───────────────────
            string empresa = DeterminarEmpresaPorCnpj(nota.TomadorCnpj);
            if (empresa == "DESCONHECIDO")
            {
                CustomMessageBox.ShowWarning(
                    $"CNPJ do tomador não reconhecido: {nota.TomadorCnpj}\n\n" +
                    "Verifique se a nota pertence à Administradora ou à Corretora.\n" +
                    "Nota não importada.");
                return;
            }

            // ── 4. Converte o valor total da nota ─────────────────────────────
            string valorStr = nota.ValorTotal.Replace(".", "").Replace(",", ".");
            if (!decimal.TryParse(valorStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal valorDecimal)
                || valorDecimal <= 0)
            {
                CustomMessageBox.ShowWarning(
                    $"Não foi possível interpretar o valor total da nota: '{nota.ValorTotal}'\n" +
                    "Nota não importada.");
                return;
            }

            // ── 5. Calcula a data de pagamento pelo DiaPagamento do fornecedor ─
            DateTime dataPagamento = CalcularProximaDataPagamento(fornecedor.DiaPagamento, DateTime.Today);

            // ── 6. Abre janela de confirmação com data editável ───────────────
            var confirmacao = new ConfirmarNotaWindow(
                numeroNota:       nota.NumeroNota,
                dataEmissao:      nota.DataEmissao,
                nomeFornecedor:   fornecedor.Nome,
                codigoFornecedor: fornecedor.Codigo,
                valor:            valorDecimal,
                dataPagamento:    dataPagamento,
                empresa:          empresa)
            {
                Owner = this
            };

            if (confirmacao.ShowDialog() != true || !confirmacao.Confirmado)
                return;

            // Usa a data possivelmente editada pelo usuário
            dataPagamento = confirmacao.DataPagamentoFinal;

            // ── 7. Copia o PDF para a pasta de arquivamento ────────────────────
            try
            {
                // Usa o mesmo caminho base dos arquivos JSON (OneDrive/data)
                string pastaBase = ObterCaminhoBase();
                string pastaNotas = Path.Combine(pastaBase, "notas");
                
                // Cria o diretório se não existir
                if (!Directory.Exists(pastaNotas))
                {
                    Directory.CreateDirectory(pastaNotas);
                    Log($"ImportarNotaFiscal: diretório criado: {pastaNotas}");
                }
                
                // Formata o número da nota com 9 dígitos (preenche com zeros à esquerda)
                string numeroNotaFormatado = nota.NumeroNota.PadLeft(9, '0');
                
                // Monta o nome do arquivo: "{Empresa} - {Fornecedor} - NF-e {NumeroNota}.pdf"
                string nomeArquivo = $"{empresa} - {fornecedor.Nome} - NF-e {numeroNotaFormatado}.pdf";
                string caminhoDestino = Path.Combine(pastaNotas, nomeArquivo);
                
                // Copia o arquivo
                File.Copy(caminhoPdf, caminhoDestino, overwrite: true);
                
                Log($"ImportarNotaFiscal: PDF arquivado em: {caminhoDestino}");
            }
            catch (Exception exCopia)
            {
                // Não interrompe a importação se falhar ao copiar o arquivo
                Log($"ImportarNotaFiscal: AVISO - Erro ao copiar PDF para pasta de arquivamento", exCopia);
            }

            // ── 8. Monta e grava o novo registro no JSON ──────────────────────
            string caminhoJson       = ObterCaminhoArquivoPrevisoes();
            var    previsoes         = CarregarPrevisoesPagamento();
            int    proximoId         = previsoes.Any() ? previsoes.Max(p => p.Id) + 1 : 1;

            var novo = new PrevisaoPagamento
            {
                Id                  = proximoId,
                CodigoFornecedor    = fornecedor.Codigo,
                NomeFornecedor      = fornecedor.Nome,
                TipoPagamento       = fornecedor.TipoPagamento,
                Natureza            = fornecedor.Natureza,
                Valor               = valorDecimal,
                DataPagamento       = dataPagamento,
                Status              = "Pendente",
                Empresa             = empresa,
                DataProvisionamento = null,
                NumeroNota          = nota.NumeroNota,
                DataEmissaoNota     = nota.DataEmissao
            };

            previsoes.Add(novo);
            SalvarPrevisoes(previsoes, caminhoJson);

            // Atualiza a lista em memória e a UI (mesmo padrão do ImportarLotePagamentos)
            _previsoesPagamento = previsoes.OrderBy(p => p.DataPagamento).ToList();
            PagamentosItemsControl.ItemsSource = null;
            PagamentosItemsControl.ItemsSource = _previsoesPagamento;
            DesselecionarPagamento();

            Log($"ImportarNotaFiscal: gravado Id={novo.Id}, Fornecedor={novo.NomeFornecedor}, Valor={novo.Valor}, Data={novo.DataPagamento:yyyy-MM-dd}");
            Log($"ImportarNotaFiscal: ✅ NOTA IMPORTADA COM SUCESSO\n" +
                $"  Nº Nota:       {nota.NumeroNota}\n" +
                $"  Emissão:       {nota.DataEmissao}\n" +
                $"  Prestador:     {nota.PrestadorNome} (CNPJ: {nota.PrestadorCnpj})\n" +
                $"  Tomador:       {nota.TomadorNome} (CNPJ: {nota.TomadorCnpj})\n" +
                $"  Fornecedor BD: {novo.NomeFornecedor} (cód. {novo.CodigoFornecedor})\n" +
                $"  Serviço:       {nota.CodigoServico} - {nota.DescricaoServico}\n" +
                $"  Valor Total:   R$ {novo.Valor:N2}\n" +
                $"  ISS:           {nota.Aliquota} = R$ {nota.ValorIss}\n" +
                $"  Empresa:       {novo.Empresa}\n" +
                $"  Dt Pagamento:  {novo.DataPagamento:dd/MM/yyyy}\n" +
                $"  ID gravado:    {novo.Id}");
            CustomMessageBox.ShowSuccess(
                $"Nota Nº {nota.NumeroNota} importada com sucesso!\n\n" +
                $"Fornecedor:  {fornecedor.Nome}\n" +
                $"Valor:       R$ {valorDecimal:N2}\n" +
                $"Pagamento:   {dataPagamento:dd/MM/yyyy}\n" +
                $"Empresa:     {empresa}",
                "Nota importada");
            
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao importar nota fiscal", ex);
            Log("ImportarNotaFiscal: ERRO", ex);
        }
    }

    /// <summary>
    /// Busca progressiva do fornecedor pelo nome da nota (palavra a palavra).
    ///
    /// Regras:
    ///  • 1ª palavra → se 0 resultados: erro "não encontrado"
    ///  • 1ª palavra → se 1 resultado: encontrado ✓
    ///  • 1ª palavra → se 2+ resultados: tenta 2 palavras
    ///  • 2ª palavra → se 0 resultados: erro "não encontrado"
    ///  • 2ª palavra → se 1 resultado: encontrado ✓
    ///  • 2ª palavra → se 2+ resultados: erro "nomes semelhantes"
    ///
    /// A comparação usa StartsWith case-insensitive.
    /// </summary>
    private static (Fornecedor? fornecedor, string? erro)
        BuscarFornecedorPorNomeNota(string nomeNota, List<Fornecedor> fornecedores)
    {
        string[] palavras = nomeNota.ToUpperInvariant().Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);

        for (int n = 1; n <= Math.Min(palavras.Length, 2); n++)
        {
            string termo = string.Join(" ", palavras[..n]);

            var encontrados = fornecedores
                .Where(f => f.Nome.StartsWith(termo, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (encontrados.Count == 0)
                return (null, $"Fornecedor não encontrado — nota não importada.\n(Termo buscado: \"{termo}\")");

            if (encontrados.Count == 1)
                return (encontrados[0], null);

            // Mais de 1 e já usamos 2 palavras → ambíguo
            if (n == 2)
                return (null,
                    $"Fornecedor com nomes semelhantes — nota não importada.\n" +
                    $"(Termo buscado: \"{termo}\" — {encontrados.Count} correspondências)");

            // n == 1 e count > 1: continua para a 2ª palavra
        }

        return (null, "Fornecedor não encontrado — nota não importada.");
    }

    /// <summary>
    /// Determina a empresa (ADM / COR) pelo CNPJ do tomador presente na nota.
    /// Remove pontuação antes de comparar.
    /// </summary>
    private static string DeterminarEmpresaPorCnpj(string cnpjTomador)
    {
        string cnpjLimpo = new string(cnpjTomador.Where(char.IsDigit).ToArray());
        return cnpjLimpo switch
        {
            CNPJ_ADM => "ADM",
            CNPJ_COR => "COR",
            _        => "DESCONHECIDO"
        };
    }

    /// <summary>
    /// Calcula o próximo dia de pagamento a partir de uma data de referência.
    /// Se o dia no mês atual já passou (ou é hoje), avança para o mês seguinte.
    /// Respeita fim de mês (ex: dia 31 em fevereiro → último dia do mês).
    /// Mesma lógica utilizada no preenchimento manual da O.P.E.X.
    /// </summary>
    private static DateTime CalcularProximaDataPagamento(int diaPagamento, DateTime referencia)
    {
        int ano = referencia.Year;
        int mes = referencia.Month;

        int ultimoDia   = DateTime.DaysInMonth(ano, mes);
        int diaEfetivo  = Math.Min(diaPagamento, ultimoDia);

        if (diaEfetivo > referencia.Day)
            return new DateTime(ano, mes, diaEfetivo);

        // Já passou ou é hoje → próximo mês
        if (mes == 12) { ano++; mes = 1; }
        else             mes++;

        ultimoDia  = DateTime.DaysInMonth(ano, mes);
        diaEfetivo = Math.Min(diaPagamento, ultimoDia);
        return new DateTime(ano, mes, diaEfetivo);
    }

    /// <summary>
    /// <summary>
    /// Extrai o texto de todas as páginas do PDF usando itext7,
    /// preservando as quebras de linha necessárias para o parser de regex.
    /// Requer NuGet: itext7
    /// </summary>
    private string ExtrairTextoPdf(string caminhoPdf)
    {
        var sb = new StringBuilder();

        using var reader   = new PdfReader(caminhoPdf);
        using var documento = new PdfDocument(reader);

        for (int i = 1; i <= documento.GetNumberOfPages(); i++)
        {
            // GetTextFromPage extrai o texto preservando \n por linha —
            // exatamente o formato que os padrões regex do NfseSpParser esperam.
            string textoPagina = PdfTextExtractor.GetTextFromPage(documento.GetPage(i));
            sb.AppendLine(textoPagina);
        }

        string textoExtraido = sb.ToString();

        // Se o texto extraído for muito pequeno (< 100 caracteres),
        // provavelmente o PDF é uma imagem escaneada → tenta OCR
        if (textoExtraido.Trim().Length < 100)
        {
            Log($"ExtrairTextoPdf: texto extraído muito pequeno ({textoExtraido.Length} chars), tentando OCR...");
            
            try
            {
                string textoOcr = ExtrairTextoPdfComOcr(caminhoPdf);
                if (!string.IsNullOrWhiteSpace(textoOcr) && textoOcr.Length > textoExtraido.Length)
                {
                    Log($"ExtrairTextoPdf: OCR bem-sucedido! Extraídos {textoOcr.Length} caracteres.");
                    return textoOcr;
                }
            }
            catch (Exception exOcr)
            {
                Log($"ExtrairTextoPdf: OCR falhou, usando texto original", exOcr);
            }
        }

        return textoExtraido;
    }

    /// <summary>
    /// Extrai texto de PDF usando OCR (para PDFs que são imagens escaneadas).
    /// Converte cada página em imagem e usa Tesseract para reconhecer o texto.
    /// </summary>
    private string ExtrairTextoPdfComOcr(string caminhoPdf)
    {
        var sb = new StringBuilder();
        var arquivosTemp = new List<string>(); // Para deletar depois

        try
        {
            // Determina o caminho da pasta tessdata (onde está o por.traineddata)
            string pastaBase = AppDomain.CurrentDomain.BaseDirectory;
            string pastaTessdata = Path.Combine(pastaBase, "tessdata");

            if (!Directory.Exists(pastaTessdata))
            {
                throw new DirectoryNotFoundException(
                    $"Pasta tessdata não encontrada em: {pastaTessdata}\n" +
                    "Certifique-se de que o arquivo por.traineddata está na pasta tessdata do projeto.");
            }

            // Inicializa o Tesseract OCR Engine
            using var engine = new TesseractEngine(pastaTessdata, "por", EngineMode.Default);

            // Converte PDF para imagens usando Docnet.Core
            // ⚠️ PageDimensions exige que dimOne (largura) <= dimTwo (altura)
            // A4 portrait: 1080 x 1920 → largura sempre menor que altura
            using var docReader = DocLib.Instance.GetDocReader(caminhoPdf, new PageDimensions(1080, 1920));
            
            int numPaginas = docReader.GetPageCount();
            Log($"ExtrairTextoPdfComOcr: processando {numPaginas} página(s)...");

            for (int i = 0; i < numPaginas; i++)
            {
                using var pageReader = docReader.GetPageReader(i);
                var rawBytes = pageReader.GetImage();
                int width = pageReader.GetPageWidth();
                int height = pageReader.GetPageHeight();

                // Converte bytes BGRA para Bitmap
                using var bitmap = ConverterBytesParaBitmap(rawBytes, width, height);
                
                // Salva bitmap temporariamente (Tesseract.Process precisa de Pix, não Bitmap)
                string arquivoTemp = Path.Combine(Path.GetTempPath(), $"ocr_temp_{Guid.NewGuid()}.png");
                bitmap.Save(arquivoTemp, System.Drawing.Imaging.ImageFormat.Png);
                arquivosTemp.Add(arquivoTemp);
                
                // Carrega como Pix e processa com Tesseract
                using var pix = Pix.LoadFromFile(arquivoTemp);
                using var page = engine.Process(pix);
                
                string textoPagina = page.GetText();
                sb.AppendLine(textoPagina);
                
                Log($"ExtrairTextoPdfComOcr: página {i + 1}/{numPaginas} → {textoPagina.Length} chars");
            }

            return sb.ToString();
        }
        finally
        {
            // Limpa arquivos temporários
            foreach (var arquivo in arquivosTemp)
            {
                try
                {
                    if (File.Exists(arquivo))
                        File.Delete(arquivo);
                }
                catch
                {
                    // Ignora erros ao deletar temporários
                }
            }
        }
    }

    /// <summary>
    /// Converte array de bytes BGRA (formato Docnet) para Bitmap.
    /// </summary>
    private System.Drawing.Bitmap ConverterBytesParaBitmap(byte[] bytes, int width, int height)
    {
        // Docnet retorna pixels em formato BGRA (4 bytes por pixel)
        var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        
        // Lock dos pixels do bitmap para acesso direto
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        // Copia os bytes para o bitmap
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmapData.Scan0, bytes.Length);
        bitmap.UnlockBits(bitmapData);

        return bitmap;
    }

    /// <summary>
    /// Lê o arquivo .xlsx via ClosedXML, extrai os pagamentos a partir da linha 3
    /// e os grava no JSON de previsões, sem tocar no JSON de fornecedores.
    ///
    /// Mapeamento de colunas:
    ///   E  (5)  → CodigoFornecedor
    ///   G  (7)  → NomeFornecedor  (formatado como "Dev P/ Segurado - Title Case")
    ///   H  (8)  → Natureza
    ///   L  (12) → DataPagamento
    ///   M  (13) → Valor
    ///   Q  (17) → TipoPagamento   (09/PIX e demais não reconhecidos → 1)
    ///
    /// Campos fixos: Status="Pendente", Empresa="ADM", DataProvisionamento=null
    /// </summary>
    private void ImportarLotePagamentos(string caminhoXlsx)
    {
        try
        {
            // ── Carrega lista existente e calcula próximo ID ──────────────────
            string caminhoJson       = ObterCaminhoArquivoPrevisoes();
            var pagamentosExistentes = CarregarPrevisoesPagamento();
            int proximoId = pagamentosExistentes.Any() ? pagamentosExistentes.Max(p => p.Id) + 1 : 1;

            var novos       = new List<PrevisaoPagamento>();
            var linhasErro  = new List<int>();
            var culturaBR   = CultureInfo.GetCultureInfo("pt-BR");
            var textInfo    = culturaBR.TextInfo;

            // ── Abre o arquivo com ClosedXML ──────────────────────────────────
            using var workbook = new XLWorkbook(caminhoXlsx);
            var planilha = workbook.Worksheet(1);

            // ── Lê a partir da linha 3 ────────────────────────────────────────
            int linha = 3;
            while (true)
            {
                // Coluna E (5): código do fornecedor — célula vazia encerra a leitura
                var celE = planilha.Cell(linha, 5);
                if (celE.IsEmpty())
                    break;

                string rawE = celE.GetString().Trim();
                if (string.IsNullOrWhiteSpace(rawE))
                    break;

                try
                {
                    // E → CodigoFornecedor (ex: "002203" → 2203)
                    if (!int.TryParse(rawE, out int codigoFornecedor))
                        throw new FormatException($"Código inválido na coluna E: '{rawE}'");

                    // G → NomeFornecedor
                    string nomeRaw = planilha.Cell(linha, 7).GetString().Trim();
                    string nomeFmt = FormatarNomeFornecedorLote(nomeRaw, textInfo);

                    // H → Natureza
                    string rawH = planilha.Cell(linha, 8).GetString().Trim();
                    if (!int.TryParse(rawH, out int natureza))
                        throw new FormatException($"Natureza inválida na coluna H: '{rawH}'");

                    // L → DataPagamento (coluna 12)
                    DateTime dataPagamento = LerDataCelulaXL(planilha.Cell(linha, 12), culturaBR)
                        ?? throw new FormatException("Data inválida ou ausente na coluna L");

                    // M → Valor (coluna 13)
                    decimal valor = LerDecimalCelulaXL(planilha.Cell(linha, 13), culturaBR);
                    if (valor <= 0)
                        throw new FormatException("Valor inválido ou zero na coluna M");

                    // Q → TipoPagamento (coluna 17)
                    string rawQ  = planilha.Cell(linha, 17).GetString().Trim();
                    int tipoPgto = MapearTipoPagamento(rawQ);

                    novos.Add(new PrevisaoPagamento
                    {
                        Id                  = proximoId++,
                        CodigoFornecedor    = codigoFornecedor,
                        NomeFornecedor      = nomeFmt,
                        TipoPagamento       = tipoPgto,
                        Natureza            = natureza,
                        Valor               = valor,
                        DataPagamento       = dataPagamento,
                        Status              = "Pendente",
                        Empresa             = "ADM",
                        DataProvisionamento = null
                    });
                }
                catch (Exception exLinha)
                {
                    Debug.WriteLine($"⚠️ Linha {linha} ignorada: {exLinha.Message}");
                    linhasErro.Add(linha);
                }

                linha++;
            }

            // ── Nenhum registro válido ────────────────────────────────────────
            if (novos.Count == 0)
            {
                CustomMessageBox.ShowWarning("Nenhum pagamento válido foi encontrado no arquivo selecionado.");
                return;
            }

            // ── Confirmação com resumo ────────────────────────────────────────
            string detalhe = $"{novos.Count} pagamento(s) prontos para importar." +
                             (linhasErro.Count > 0
                                 ? $"\n{linhasErro.Count} linha(s) ignorada(s) por erro (linhas: {string.Join(", ", linhasErro)})."
                                 : string.Empty);

            var resposta = CustomMessageBox.ShowQuestion(
                "Confirma a importação do lote para a O.P.E.X?",
                "Importar Lote",
                detalhe);

            if (resposta != MessageBoxResult.Yes)
                return;

            // ── Salva e atualiza a interface ──────────────────────────────────
            pagamentosExistentes.AddRange(novos);
            SalvarPrevisoes(pagamentosExistentes, caminhoJson);

            _previsoesPagamento = pagamentosExistentes.OrderBy(p => p.DataPagamento).ToList();
            PagamentosItemsControl.ItemsSource = null;
            PagamentosItemsControl.ItemsSource = _previsoesPagamento;

            DesselecionarPagamento();

            CustomMessageBox.ShowSuccess($"{novos.Count} pagamento(s) importados com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao processar o arquivo Excel", ex);
        }
    }

    /// <summary>
    /// Formata o nome do fornecedor vindo do lote Excel.
    /// "CAROLINA PIRES" → "Dev P/ Segurado - Carolina Pires"
    /// </summary>
    private static string FormatarNomeFornecedorLote(string nomeRaw, System.Globalization.TextInfo textInfo)
    {
        if (string.IsNullOrWhiteSpace(nomeRaw))
            return "Dev P/ Segurado - Desconhecido";

        // ToLower antes para que ToTitleCase funcione corretamente em strings ALL CAPS
        string nomeTitleCase = textInfo.ToTitleCase(nomeRaw.ToLowerInvariant());
        return $"Dev P/ Segurado - {nomeTitleCase}";
    }

    /// <summary>
    /// Mapeia o código de tipo de pagamento do arquivo bancário para o tipo interno.
    /// PIX (09) e qualquer código não reconhecido → 1.
    /// Valores válidos internos: 1, 2, 3.
    /// </summary>
    private static int MapearTipoPagamento(string? rawQ)
    {
        if (string.IsNullOrWhiteSpace(rawQ))
            return 1;

        if (!int.TryParse(rawQ.Trim(), out int codigo))
            return 1;

        return codigo is 1 or 2 or 3 ? codigo : 1;
    }

    /// <summary>
    /// Lê uma célula ClosedXML como DateTime.
    /// Suporta: valor DateTime nativo da célula e strings "dd/MM/yyyy".
    /// </summary>
    private static DateTime? LerDataCelulaXL(IXLCell cel, CultureInfo culturaBR)
    {
        if (cel.IsEmpty()) return null;

        // ClosedXML retorna DateTime diretamente para células de data
        if (cel.DataType == XLDataType.DateTime)
            return cel.GetDateTime();

        // Fallback: tenta converter a partir do texto da célula
        string texto = cel.GetString().Trim();
        if (DateTime.TryParseExact(texto,
                                   new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" },
                                   culturaBR,
                                   DateTimeStyles.None,
                                   out DateTime parsed))
            return parsed;

        return null;
    }

    /// <summary>
    /// Lê uma célula ClosedXML como decimal.
    /// Suporta: número nativo, string pt-BR ("1.547,57") e invariant ("1547.57").
    /// </summary>
    private static decimal LerDecimalCelulaXL(IXLCell cel, CultureInfo culturaBR)
    {
        if (cel.IsEmpty()) return 0m;

        // Número nativo — caminho mais comum
        if (cel.DataType == XLDataType.Number)
            return (decimal)cel.GetDouble();

        string texto = cel.GetString().Trim();
        if (string.IsNullOrEmpty(texto)) return 0m;

        if (decimal.TryParse(texto, NumberStyles.Any, culturaBR, out decimal v1))
            return v1;

        if (decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v2))
            return v2;

        return 0m;
    }

    /// <summary>
    /// Salva a lista de previsões no arquivo JSON
    /// </summary>
    private void SalvarPrevisoes(List<PrevisaoPagamento> previsoes, string caminhoArquivo)
    {
        try
        {
            string json = JsonSerializer.Serialize(previsoes, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(caminhoArquivo, json);
        }
        catch (Exception ex)
        {
            throw new Exception($"Erro ao salvar previsões: {ex.Message}", ex);
        }
    }

    #endregion

    #endregion

    #region Métodos Auxiliares

    /// <summary>
    /// Grava uma entrada no arquivo de log. Thread-safe.
    /// </summary>
    private static void Log(string mensagem, Exception? ex = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{System.Threading.Thread.CurrentThread.ManagedThreadId,2}] ");
            sb.Append(mensagem);
            if (ex != null)
            {
                sb.AppendLine();
                sb.Append($"    EXCEPTION: {ex.GetType().FullName}: {ex.Message}");
                if (ex.InnerException != null)
                    sb.Append($" → Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                sb.AppendLine();
                // Primeiras 3 linhas do stack trace
                var stackLines = (ex.StackTrace ?? "").Split('\n')
                    .Where(l => l.Trim().Length > 0).Take(3);
                foreach (var line in stackLines)
                    sb.AppendLine($"    {line.Trim()}");
            }
            lock (_logPath)
                File.AppendAllText(_logPath, sb.ToString().TrimEnd() + Environment.NewLine);
        }
        catch { /* log nunca deve crashar o app */ }
    }

    /// <summary>
    /// Inicia nova sessão de log (sobrescreve o anterior).
    /// </summary>
    private static void IniciarSessaoLog()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 70));
            sb.AppendLine($"  HUB FINANCEIRO — Nova sessão em {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine($"  Usuário: {Environment.UserName}  |  Máquina: {Environment.MachineName}");
            sb.AppendLine($"  OS: {Environment.OSVersion}  |  .NET: {Environment.Version}");
            sb.AppendLine(new string('=', 70));
            File.WriteAllText(_logPath, sb.ToString());
        }
        catch { }
    }

    /// <summary>
    /// Exibe uma mensagem de erro ao usuário
    /// </summary>
    private void MostrarErro(string mensagem, Exception? ex = null)
    {
        string mensagemCompleta = ex != null 
            ? $"{mensagem}\n\nDetalhes: {ex.Message}" 
            : mensagem;

        CustomMessageBox.ShowError(mensagemCompleta, "Erro - Hub Financeiro");
    }

    /// <summary>
    /// Exibe uma mensagem de aviso ao usuário
    /// </summary>
    private void MostrarAviso(string mensagem)
    {
        CustomMessageBox.ShowWarning(mensagem, "Aviso - Hub Financeiro");
    }

    /// <summary>
    /// Exibe uma mensagem de sucesso ao usuário
    /// </summary>
    private void MostrarSucesso(string mensagem)
    {
        CustomMessageBox.ShowInformation(mensagem, "Sucesso - Hub Financeiro");
    }

    /// <summary>
    /// Exibe uma pergunta e retorna a resposta do usuário
    /// </summary>
    private bool MostrarPergunta(string mensagem, string titulo = "Confirmação", string? detalhe = null)
    {
        return CustomMessageBox.ShowQuestion(mensagem, titulo, detalhe) == MessageBoxResult.Yes;
    }

    #endregion
    
    #region Monitoramento de Arquivos Multi-Usuário
    
    /// <summary>
    /// Inicializa o monitoramento dos arquivos JSON para detectar mudanças de outros usuários
    /// </summary>
    private void IniciarMonitoramentoArquivos()
    {
        try
        {
            string diretorioData = ObterDiretorioData();
            
            // Watcher para fornecedores.json
            _fornecedoresWatcher = new FileSystemWatcher(diretorioData)
            {
                Filter = "fornecedores.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            
            _fornecedoresWatcher.Changed += (s, e) =>
            {
                // Aguarda um pouco para garantir que o arquivo foi salvo completamente
                System.Threading.Thread.Sleep(100);
                
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        RecarregarFornecedores();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erro ao recarregar fornecedores: {ex.Message}");
                    }
                });
            };
            
            // Watcher para previsoes_pagamento.json
            _pagamentosWatcher = new FileSystemWatcher(diretorioData)
            {
                Filter = "previsoes_pagamento.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            
            _pagamentosWatcher.Changed += (s, e) =>
            {
                // Aguarda um pouco para garantir que o arquivo foi salvo completamente
                System.Threading.Thread.Sleep(100);
                
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        RecarregarPagamentos();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erro ao recarregar pagamentos: {ex.Message}");
                    }
                });
            };
            
            Debug.WriteLine("✅ Monitoramento de arquivos iniciado com sucesso");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"⚠️ Erro ao iniciar monitoramento: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Recarrega a lista de fornecedores quando detecta mudança no arquivo
    /// </summary>
    private void RecarregarFornecedores()
    {
        try
        {
            var fornecedores = CarregarFornecedores();
            
            // Atualiza lista completa
            _todosFornecedores = fornecedores.OrderBy(f => f.Nome).ToList();
            FornecedoresItemsControl.ItemsSource = null;
            FornecedoresItemsControl.ItemsSource = _todosFornecedores;
            
            // Atualiza apenas ativos (aba Email)
            var apenasAtivos = fornecedores.Where(f => f.Ativo).OrderBy(f => f.Nome).ToList();
            FornecedorItemsControl.ItemsSource = null;
            FornecedorItemsControl.ItemsSource = apenasAtivos;
            
            // Atualiza OPEX
            _fornecedoresOpex = fornecedores.OrderBy(f => f.Nome).ToList();
            OpexFornecedorComboBox.ItemsSource = null;
            OpexFornecedorComboBox.ItemsSource = _fornecedoresOpex;
            
            Debug.WriteLine("🔄 Fornecedores recarregados do arquivo");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao recarregar fornecedores: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Salva fornecedores com lock para evitar conflitos
    /// </summary>
    private void SalvarFornecedoresComLock(List<Fornecedor> fornecedores)
    {
        lock (_fileLock)
        {
            try
            {
                string json = JsonSerializer.Serialize(fornecedores, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                string caminho = ObterCaminhoArquivoFornecedores();
                
                // Desabilita temporariamente o watcher para não disparar evento ao salvar
                if (_fornecedoresWatcher != null)
                    _fornecedoresWatcher.EnableRaisingEvents = false;
                
                File.WriteAllText(caminho, json);
                
                // Aguarda um pouco antes de reativar
                System.Threading.Thread.Sleep(100);
                
                if (_fornecedoresWatcher != null)
                    _fornecedoresWatcher.EnableRaisingEvents = true;
                
                Debug.WriteLine("💾 Fornecedores salvos com sucesso");
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao salvar fornecedores: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Salva previsões com lock para evitar conflitos
    /// </summary>
    private void SalvarPrevisoesComLock(List<PrevisaoPagamento> previsoes)
    {
        lock (_fileLock)
        {
            try
            {
                string json = JsonSerializer.Serialize(previsoes, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                string caminho = ObterCaminhoArquivoPrevisoes();
                
                // Desabilita temporariamente o watcher para não disparar evento ao salvar
                if (_pagamentosWatcher != null)
                    _pagamentosWatcher.EnableRaisingEvents = false;
                
                File.WriteAllText(caminho, json);
                
                // Aguarda um pouco antes de reativar
                System.Threading.Thread.Sleep(100);
                
                if (_pagamentosWatcher != null)
                    _pagamentosWatcher.EnableRaisingEvents = true;
                
                Debug.WriteLine("💾 Previsões salvas com sucesso");
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao salvar previsões: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Obtém o diretório data
    /// </summary>
    private string ObterDiretorioData()
    {
        // Usa a mesma lógica de detecção de usuário do ObterCaminhoBase()
        // para garantir que o watcher monitore a pasta correta
        return ObterCaminhoBase();
    }
    
    /// <summary>
    /// Para o monitoramento ao fechar a janela
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        Log("OnClosed: janela sendo fechada normalmente");
        base.OnClosed(e);
        _fornecedoresWatcher?.Dispose();
        _pagamentosWatcher?.Dispose();
        Log("OnClosed: encerrado ✅");
    }

    #endregion
    
    #region Calc Subconjunto
    
    /// <summary>
    /// Exibe ou oculta o layout de Calc Subconjunto
    /// </summary>
    private void ExibirOcultarLayoutCalcSubconjunto(bool exibir)
    {
        if (exibir)
        {
            CalcSubconjuntoLayout.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            CalcSubconjuntoLayout.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => CalcSubconjuntoLayout.Visibility = Visibility.Collapsed;
            CalcSubconjuntoLayout.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    /// <summary>
    /// Exibe ou oculta o layout de Compara Valores
    /// </summary>
    private void ExibirOcultarLayoutCompararValores(bool exibir)
    {
        if (exibir)
        {
            CompararValoresLayout.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            CompararValoresLayout.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => CompararValoresLayout.Visibility = Visibility.Collapsed;
            CompararValoresLayout.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

        /// <summary>
    /// Exibe ou oculta o Aux Conciliação.
    /// </summary>
    private void ExibirOcultarLayoutAuxConciliacao(bool exibir)
    {
        if (AuxConciliacaoLayout == null)
            return;

        if (exibir)
        {
            AuxConciliacaoLayout.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            AuxConciliacaoLayout.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            AuxConciliacaoLayout.BeginAnimation(OpacityProperty, null);
            AuxConciliacaoLayout.Opacity = 0;
            AuxConciliacaoLayout.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Exibe ou oculta o layout de Analise de Faturas
    /// </summary>
    private void ExibirOcultarLayoutAnaliseFaturas(bool exibir)
    {
        if (exibir)
        {
            AnaliseFaturasLayout.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            AnaliseFaturasLayout.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => AnaliseFaturasLayout.Visibility = Visibility.Collapsed;
            AnaliseFaturasLayout.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    /// <summary>
    /// Abre a preparação de uma nova análise. O histórico persistente é atualizado
    /// ao retornar para a página, independentemente de a preparação temporária ter mudado.
    /// </summary>
    private void BtnIncluirAnaliseFatura_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var janela = new IncluirAnaliseFaturaWindow(ObterCaminhoBase())
            {
                Owner = this
            };

            bool? concluida = janela.ShowDialog();

            if (concluida == true &&
                janela.ResultadoFinalGerado != null &&
                janela.ContextoHistoricoGerado != null)
            {
                var resultado = new ResultadoAnaliseFaturasWindow(
                    janela.ResultadoFinalGerado,
                    janela.ContextoHistoricoGerado)
                {
                    Owner = this
                };

                resultado.ShowDialog();
            }

            // Se o usuário salvou o resultado no histórico, o card aparece assim que voltar.
            CarregarHistoricoAnaliseFaturas();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao abrir a inclusão de análise de faturas", ex);
        }
    }

    private void CarregarHistoricoAnaliseFaturas()
    {
        try
        {
            if (AnalisesFaturaItemsControl == null || AnalisesFaturaEmptyState == null)
                return;

            var service = new AnaliseFaturasHistoricoService(ObterCaminhoBase());
            IReadOnlyList<AnaliseFaturasHistoricoResumo> historicos = service.Listar();

            AnalisesFaturaItemsControl.ItemsSource = historicos;
            AnalisesFaturaEmptyState.Visibility = historicos.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log($"CarregarHistoricoAnaliseFaturas: {ex}");

            if (AnalisesFaturaItemsControl != null)
                AnalisesFaturaItemsControl.ItemsSource = null;
            if (AnalisesFaturaEmptyState != null)
                AnalisesFaturaEmptyState.Visibility = Visibility.Visible;
        }
    }

    private bool _abrindoHistoricoAnaliseFaturas;

    private void AbrirHistoricoAnaliseFaturas_Click(object sender, RoutedEventArgs e)
    {
        if (_abrindoHistoricoAnaliseFaturas)
            return;

        if (sender is not FrameworkElement elemento ||
            elemento.DataContext is not AnaliseFaturasHistoricoResumo resumo)
        {
            return;
        }

        _abrindoHistoricoAnaliseFaturas = true;

        try
        {
            var service = new AnaliseFaturasHistoricoService(ObterCaminhoBase());
            AnaliseFaturasHistoricoSnapshot snapshot = service.Carregar(resumo.CaminhoArquivo);

            var janela = new ResultadoAnaliseFaturasWindow(snapshot)
            {
                Owner = this
            };

            janela.ShowDialog();
        }
        catch (Exception ex)
        {
            Log($"AbrirHistoricoAnaliseFaturas_Click: {ex}");

            CustomMessageBox.ShowError(
                "Não foi possível abrir esta análise do histórico.\n\n" + ex.Message,
                "Erro ao abrir histórico");

            CarregarHistoricoAnaliseFaturas();
        }
        finally
        {
            _abrindoHistoricoAnaliseFaturas = false;

            if (IsLoaded)
            {
                Activate();
                Focus();
            }
        }
    }

    private void ExcluirHistoricoAnaliseFaturas_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement elemento ||
            elemento.DataContext is not AnaliseFaturasHistoricoResumo resumo)
        {
            return;
        }

        MessageBoxResult resposta = CustomMessageBox.ShowQuestion(
            $"Excluir a análise {resumo.Competencia:MM/yyyy} do histórico?",
            "Excluir análise",
            "O snapshot salvo desta competência será removido. Esta ação não apaga nem altera a preparação temporária atual.");

        if (resposta != MessageBoxResult.Yes)
            return;

        try
        {
            var service = new AnaliseFaturasHistoricoService(ObterCaminhoBase());
            if (!service.Excluir(resumo.Competencia))
            {
                CustomMessageBox.ShowWarning(
                    "O arquivo desta análise já não existe no histórico.",
                    "Análise não encontrada");
            }

            CarregarHistoricoAnaliseFaturas();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError(
                "Não foi possível excluir esta análise do histórico.\n\n" + ex.Message,
                "Erro ao excluir histórico");
            CarregarHistoricoAnaliseFaturas();
        }
    }

    /// <summary>
    /// Exibe ou oculta o layout do Decodificador CNAB
    /// </summary>
    private void ExibirOcultarLayoutDecodificadorCnab(bool exibir)
    {
        if (exibir)
        {
            DecodificadorCnabLayout.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3));
            DecodificadorCnabLayout.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => DecodificadorCnabLayout.Visibility = Visibility.Collapsed;
            DecodificadorCnabLayout.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    // ============================================================
    // DECODIFICADOR CNAB
    // ============================================================

    private void CnabDropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        AbrirSeletorCnab();
    }

    private void CnabDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void CnabDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] arquivos || arquivos.Length == 0)
            return;

        if (arquivos.Length > 1)
        {
            CustomMessageBox.ShowWarning("Arraste apenas um arquivo CNAB por vez.");
            return;
        }

        CarregarArquivoCnab(arquivos[0]);
    }

    private void CnabTrocarArquivo_Click(object sender, RoutedEventArgs e)
        => AbrirSeletorCnab();

    private void AbrirSeletorCnab()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecionar arquivo CNAB",
                Filter = "Arquivos CNAB (*.rem;*.cnab;*.txt)|*.rem;*.cnab;*.txt|Todos os arquivos (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
                CarregarArquivoCnab(dialog.FileName);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Não foi possível selecionar o arquivo CNAB.\n\n{ex.Message}");
        }
    }

    private void CarregarArquivoCnab(string caminho)
    {
        try
        {
            var arquivo = CnabDecoderService.Carregar(caminho);

            _cnabArquivoAtual = arquivo;
            _cnabPagamentos.Clear();
            foreach (var pagamento in arquivo.PagamentosOriginais)
                _cnabPagamentos.Add(pagamento);

            CnabArquivoTextBlock.Text = Path.GetFileName(caminho);
            CnabArquivoTextBlock.ToolTip = caminho;
            CnabLayoutTextBlock.Text = arquivo.DescricaoLayout;

            // Todo CNAB válido também alimenta a base bancária usada pelo Criador CNAB.
            // A matrícula/código é recuperada do campo "Seu Número" quando disponível.
            try
            {
                CnabDadosBancariosRepository.AprenderDoCnab(ObterCaminhoBaseBancariaCnab(), arquivo);
                CnabAdmConfiguracaoRepository.AtualizarComCnabExistente(
                    ObterCaminhoConfiguracaoCnabAdm(), arquivo.NumeroSequencialArquivo);
            }
            catch (Exception exBase)
            {
                Log("CNAB carregado, mas não foi possível atualizar a base bancária local", exBase);
            }

            _cnabAtualizandoDataTodos = true;
            try
            {
                CnabAlterarTodosCheckBox.IsChecked = true;

                var datas = _cnabPagamentos.Select(p => p.DataOriginal.Date).Distinct().ToList();
                CnabDataTodosTextBox.Text = datas.Count == 1
                    ? datas[0].ToString("dd/MM/yyyy")
                    : string.Empty;
            }
            finally
            {
                _cnabAtualizandoDataTodos = false;
            }

            AtualizarModoDataCnab();
            AtualizarResumoCnab();

            CnabImportPanel.Visibility = Visibility.Collapsed;
            CnabEditorPanel.Visibility = Visibility.Visible;

            Log($"CNAB carregado: {Path.GetFileName(caminho)} | {arquivo.DescricaoLayout} | {_cnabPagamentos.Count} pagamentos");
        }
        catch (CnabNaoSuportadoException ex)
        {
            CustomMessageBox.ShowWarning(ex.Message, "CNAB não suportado");
        }
        catch (Exception ex)
        {
            Log("Erro ao carregar CNAB", ex);
            CustomMessageBox.ShowError($"Não foi possível decodificar o arquivo.\n\n{ex.Message}", "Erro no CNAB");
        }
    }

    private void CnabAlterarTodosCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        AtualizarModoDataCnab();

        if (CnabAlterarTodosCheckBox?.IsChecked == true &&
            CnabDecoderService.TentarParsearData(CnabDataTodosTextBox?.Text, out DateTime data))
        {
            AplicarDataTodosCnab(data);
        }
    }

    private void AtualizarModoDataCnab()
    {
        bool alterarTodos = CnabAlterarTodosCheckBox?.IsChecked == true;

        foreach (var item in _cnabPagamentos)
            item.DataIndividualHabilitada = !alterarTodos;

        if (CnabDataTodosTextBox != null)
        {
            CnabDataTodosTextBox.IsEnabled = alterarTodos;
            CnabDataTodosTextBox.Opacity = alterarTodos ? 1.0 : 0.45;
        }
    }

    private void CnabDataTodosTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_cnabAtualizandoDataTodos || CnabAlterarTodosCheckBox?.IsChecked != true)
            return;

        if (CnabDecoderService.TentarParsearData(CnabDataTodosTextBox.Text, out DateTime data))
        {
            AplicarDataTodosCnab(data);
            CnabDataTodosTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 93));
        }
        else if (CnabDataTodosTextBox.Text.Length >= 8)
        {
            CnabDataTodosTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 72, 72));
        }
    }

    private void AplicarDataTodosCnab(DateTime data)
    {
        string texto = data.ToString("dd/MM/yyyy");
        foreach (var item in _cnabPagamentos)
            item.DataTexto = texto;
    }

    private void CnabValorTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not CnabPagamentoItem item)
            return;

        if (item.TentarObterValor(out decimal valor) && valor > 0)
        {
            item.ValorTexto = valor.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 76, 83));
            AtualizarResumoCnab();
        }
        else
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 72, 72));
        }
    }

    private void CnabDataTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not CnabPagamentoItem item)
            return;

        if (item.TentarObterData(out DateTime data))
        {
            item.DataTexto = data.ToString("dd/MM/yyyy");
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 76, 83));
        }
        else
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 72, 72));
        }
    }

    private void CnabBanco_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao || botao.DataContext is not CnabPagamentoItem item)
            return;

        string banco = CnabDecoderService.NomeBanco(item.BancoCodigo);
        string documento = FormatarDocumentoCnab(item.DocumentoFavorecido);
        string agencia = string.IsNullOrWhiteSpace(item.AgenciaDv) ? item.Agencia : $"{item.Agencia}-{item.AgenciaDv}";
        string conta = string.IsNullOrWhiteSpace(item.ContaDv) ? item.Conta : $"{item.Conta}-{item.ContaDv}";

        string detalhes =
            $"Favorecido: {item.Nome}\n\n" +
            $"Banco: {banco} ({item.BancoCodigo})\n" +
            $"Agência: {agencia}\n" +
            $"Conta: {conta}\n" +
            (string.IsNullOrWhiteSpace(item.AgenciaContaDv) ? "" : $"DV Agência/Conta: {item.AgenciaContaDv}\n") +
            $"CPF/CNPJ: {documento}";

        CustomMessageBox.ShowInformation(detalhes, "Dados bancários");
    }

    private static string FormatarDocumentoCnab(string documento)
    {
        string digitos = new string((documento ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitos.Length == 11)
            return $"{digitos[..3]}.{digitos[3..6]}.{digitos[6..9]}-{digitos[9..]}";
        if (digitos.Length == 14)
            return $"{digitos[..2]}.{digitos[2..5]}.{digitos[5..8]}/{digitos[8..12]}-{digitos[12..]}";
        return string.IsNullOrWhiteSpace(digitos) ? "Não informado" : digitos;
    }

    private void CnabExcluir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao || botao.DataContext is not CnabPagamentoItem item)
            return;

        string detalhe = $"{item.Nome} — R$ {item.ValorTexto}";
        if (CustomMessageBox.ShowQuestion(
                "Deseja remover este pagamento do novo arquivo CNAB?",
                "Excluir pagamento",
                detalhe) != MessageBoxResult.Yes)
            return;

        _cnabPagamentos.Remove(item);
        AtualizarResumoCnab();
    }

    private void AtualizarResumoCnab()
    {
        decimal total = 0m;
        int invalidos = 0;

        foreach (var item in _cnabPagamentos)
        {
            if (item.TentarObterValor(out decimal valor) && valor > 0)
                total += valor;
            else
                invalidos++;
        }

        if (CnabResumoTextBlock != null)
        {
            CnabResumoTextBlock.Text =
                $"{_cnabPagamentos.Count} pagamento(s)  •  Total R$ {total:N2}" +
                (invalidos > 0 ? $"  •  {invalidos} valor(es) inválido(s)" : string.Empty);
        }

        if (BtnSalvarCnab != null)
            BtnSalvarCnab.IsEnabled = _cnabPagamentos.Count > 0 && invalidos == 0;
    }

    private void CnabSalvar_Click(object sender, RoutedEventArgs e)
    {
        if (_cnabArquivoAtual == null)
        {
            CustomMessageBox.ShowWarning("Selecione um arquivo CNAB primeiro.");
            return;
        }

        try
        {
            // Se o modo em massa estiver ativo e houver uma data digitada, ela precisa ser válida.
            if (CnabAlterarTodosCheckBox.IsChecked == true &&
                !string.IsNullOrWhiteSpace(CnabDataTodosTextBox.Text))
            {
                if (!CnabDecoderService.TentarParsearData(CnabDataTodosTextBox.Text, out DateTime dataTodos))
                {
                    CustomMessageBox.ShowWarning("A data para alteração em massa é inválida. Use DD/MM/AAAA.");
                    return;
                }
                AplicarDataTodosCnab(dataTodos);
            }

            foreach (var item in _cnabPagamentos)
            {
                if (!item.TentarObterValor(out decimal valor) || valor <= 0)
                {
                    CustomMessageBox.ShowWarning($"Valor inválido para '{item.Nome}'.");
                    return;
                }

                if (!item.TentarObterData(out _))
                {
                    CustomMessageBox.ShowWarning($"Data inválida para '{item.Nome}'. Use DD/MM/AAAA.");
                    return;
                }
            }

            string pasta = Path.GetDirectoryName(_cnabArquivoAtual.CaminhoOriginal) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string nome = Path.GetFileNameWithoutExtension(_cnabArquivoAtual.CaminhoOriginal);
            string extensao = Path.GetExtension(_cnabArquivoAtual.CaminhoOriginal);
            if (string.IsNullOrEmpty(extensao)) extensao = ".rem";

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Salvar novo arquivo CNAB",
                InitialDirectory = pasta,
                FileName = $"{nome}_ajustado{extensao}",
                Filter = $"Arquivo CNAB (*{extensao})|*{extensao}|Todos os arquivos (*.*)|*.*",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            CnabDecoderService.Salvar(_cnabArquivoAtual, _cnabPagamentos, dialog.FileName);

            Log($"CNAB salvo: {dialog.FileName} | {_cnabPagamentos.Count} pagamentos");
            CustomMessageBox.ShowSuccess(
                $"Novo CNAB gerado com sucesso!\n\n" +
                $"Pagamentos: {_cnabPagamentos.Count}\n" +
                $"Arquivo: {Path.GetFileName(dialog.FileName)}",
                "CNAB salvo");
        }
        catch (Exception ex)
        {
            Log("Erro ao salvar CNAB", ex);
            CustomMessageBox.ShowError($"Não foi possível gerar o novo CNAB.\n\n{ex.Message}", "Erro ao salvar");
        }
    }


    // ============================================================
    // CRIADOR CNAB — VALE TRANSPORTE
    // ============================================================

    private string ObterCaminhoBaseBancariaCnab()
        => Path.Combine(ObterCaminhoBase(), "funcionarios_cnab.json");

    private string ObterCaminhoConfiguracaoCnabAdm()
        => Path.Combine(ObterCaminhoBase(), "cnab_adm_config.json");

    private void CnabAbaDecodificar_Click(object sender, RoutedEventArgs e)
        => SelecionarAbaInternaCnab(0);

    private void CnabAbaCriar_Click(object sender, RoutedEventArgs e)
        => SelecionarAbaInternaCnab(1);

    private void CnabAbaCadastro_Click(object sender, RoutedEventArgs e)
        => SelecionarAbaInternaCnab(2);

    private void SelecionarAbaInternaCnab(int aba)
    {
        bool decodificar = aba == 0;
        bool criar = aba == 1;
        bool cadastro = aba == 2;

        if (CnabAbaDecodificarButton != null)
            CnabAbaDecodificarButton.IsChecked = decodificar;
        if (CnabAbaCriarButton != null)
            CnabAbaCriarButton.IsChecked = criar;
        if (CnabAbaCadastroButton != null)
            CnabAbaCadastroButton.IsChecked = cadastro;

        if (CnabDecodificarTabPanel != null)
            CnabDecodificarTabPanel.Visibility = decodificar ? Visibility.Visible : Visibility.Collapsed;
        if (CnabCriarTabPanel != null)
            CnabCriarTabPanel.Visibility = criar ? Visibility.Visible : Visibility.Collapsed;
        if (CnabCadastroTabPanel != null)
            CnabCadastroTabPanel.Visibility = cadastro ? Visibility.Visible : Visibility.Collapsed;

        if (cadastro)
            CarregarCadastroColaboradoresCnab();
        else if (criar)
            AtualizarDadosCriacaoComCadastro();
    }

    private void CarregarCadastroColaboradoresCnab()
    {
        try
        {
            var registros = CnabDadosBancariosRepository.Carregar(ObterCaminhoBaseBancariaCnab())
                .OrderBy(x => x.Nome)
                .ToList();

            _cnabColaboradores.Clear();
            foreach (var registro in registros)
                _cnabColaboradores.Add(registro);

            int completos = registros.Count(x => x.EstaCompleto);
            int pendentes = registros.Count - completos;
            if (CnabCadastroResumoTextBlock != null)
                CnabCadastroResumoTextBlock.Text =
                    $"{registros.Count} colaborador(es) cadastrado(s)" +
                    (pendentes > 0 ? $"  •  {pendentes} pendente(s)" : "  •  Todos com dados completos");
        }
        catch (Exception ex)
        {
            Log("Erro ao carregar cadastro CNAB", ex);
            CustomMessageBox.ShowError($"Não foi possível carregar o cadastro de colaboradores.\n\n{ex.Message}");
        }
    }

    private void AtualizarDadosCriacaoComCadastro()
    {
        if (_cnabCriacaoPagamentos.Count == 0)
            return;

        var baseAtual = CnabDadosBancariosRepository.Carregar(ObterCaminhoBaseBancariaCnab());
        foreach (var item in _cnabCriacaoPagamentos)
        {
            var dados = baseAtual.FirstOrDefault(x =>
                (x.CodigoFuncionario > 0 && x.CodigoFuncionario == item.CodigoFuncionario) ||
                ValeTransporteCnabService.NormalizarChaveNome(x.Nome) == ValeTransporteCnabService.NormalizarChaveNome(item.Nome));

            item.DadosBancarios = dados?.Clone();
        }

        AtualizarResumoCriacaoCnab();
    }

    private void CnabCadastroImportarBase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecionar CNAB para atualizar o cadastro de colaboradores",
                Filter = "Arquivos CNAB (*.rem;*.cnab;*.txt)|*.rem;*.cnab;*.txt|Todos os arquivos (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var cnab = CnabDecoderService.Carregar(dialog.FileName);
            string caminhoBase = ObterCaminhoBaseBancariaCnab();
            CnabDadosBancariosRepository.AprenderDoCnab(caminhoBase, cnab);
            CnabAdmConfiguracaoRepository.AtualizarComCnabExistente(
                ObterCaminhoConfiguracaoCnabAdm(), cnab.NumeroSequencialArquivo);

            CarregarCadastroColaboradoresCnab();
            AtualizarDadosCriacaoComCadastro();

            int total = _cnabColaboradores.Count;
            int completos = _cnabColaboradores.Count(x => x.EstaCompleto);
            CustomMessageBox.ShowInformation(
                $"Cadastro atualizado a partir do CNAB.\n\n" +
                $"Colaboradores cadastrados: {total}\n" +
                $"Com dados completos: {completos}\n\n" +
                "Se banco, agência ou conta tiverem mudado, os dados do CNAB recém-lido passam a valer.",
                "Cadastro atualizado");
        }
        catch (CnabNaoSuportadoException ex)
        {
            CustomMessageBox.ShowWarning(ex.Message, "CNAB base não suportado");
        }
        catch (Exception ex)
        {
            Log("Erro ao atualizar cadastro a partir de CNAB", ex);
            CustomMessageBox.ShowError($"Não foi possível atualizar o cadastro.\n\n{ex.Message}");
        }
    }

    private void CnabCadastroNovo_Click(object sender, RoutedEventArgs e)
    {
        var novo = new CnabDadosBancariosFuncionario { BancoCodigo = "033" };
        var janela = new CnabDadosBancariosWindow(novo, editarIdentificacao: true) { Owner = this };
        if (janela.ShowDialog() != true)
            return;

        CnabDadosBancariosRepository.Upsert(ObterCaminhoBaseBancariaCnab(), janela.DadosSalvos);
        CarregarCadastroColaboradoresCnab();
        AtualizarDadosCriacaoComCadastro();
    }

    private void CnabCadastroEditar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao || botao.DataContext is not CnabDadosBancariosFuncionario registro)
            return;

        var janela = new CnabDadosBancariosWindow(registro) { Owner = this };
        if (janela.ShowDialog() != true)
            return;

        CnabDadosBancariosRepository.Upsert(ObterCaminhoBaseBancariaCnab(), janela.DadosSalvos);
        CarregarCadastroColaboradoresCnab();
        AtualizarDadosCriacaoComCadastro();
    }

    private void CnabCadastroExcluir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao || botao.DataContext is not CnabDadosBancariosFuncionario registro)
            return;

        if (CustomMessageBox.ShowQuestion(
                "Deseja excluir este colaborador do cadastro CNAB?",
                "Excluir colaborador",
                registro.Nome) != MessageBoxResult.Yes)
            return;

        CnabDadosBancariosRepository.Remover(ObterCaminhoBaseBancariaCnab(), registro);
        CarregarCadastroColaboradoresCnab();
        AtualizarDadosCriacaoComCadastro();
    }

    private void CnabCriarDropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            AbrirSeletorValeTransporte();
    }

    private void CnabCriarDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void CnabCriarDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] arquivos || arquivos.Length == 0)
            return;

        if (arquivos.Length > 1)
        {
            CustomMessageBox.ShowWarning("Arraste apenas um PDF por vez.");
            return;
        }

        CarregarValeTransporteParaCriacao(arquivos[0]);
    }

    private void CnabCriarTrocarArquivo_Click(object sender, RoutedEventArgs e)
        => AbrirSeletorValeTransporte();

    private void CnabCriarImportarBase_Click(object sender, RoutedEventArgs e)
        => CnabCadastroImportarBase_Click(sender, e);

    private void AbrirSeletorValeTransporte()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecionar relatório de Vale Transporte",
                Filter = "Arquivo PDF (*.pdf)|*.pdf|Todos os arquivos (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
                CarregarValeTransporteParaCriacao(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log("Erro ao selecionar PDF de Vale Transporte", ex);
            CustomMessageBox.ShowError($"Não foi possível selecionar o PDF.\n\n{ex.Message}");
        }
    }

    private void CarregarValeTransporteParaCriacao(string caminho)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(caminho), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                CustomMessageBox.ShowWarning("Para criar o CNAB de Vale Transporte, selecione o relatório em PDF.");
                return;
            }

            string texto = ExtrairTextoPdf(caminho);
            var baseBancaria = CnabDadosBancariosRepository.Carregar(ObterCaminhoBaseBancariaCnab());
            var arquivo = ValeTransporteCnabService.Carregar(caminho, texto, baseBancaria);

            _cnabCriacaoArquivoAtual = arquivo;
            _cnabCriacaoPagamentos.Clear();
            foreach (var item in arquivo.Pagamentos)
                _cnabCriacaoPagamentos.Add(item);

            CnabCriarArquivoTextBlock.Text = Path.GetFileName(caminho);
            CnabCriarArquivoTextBlock.ToolTip = caminho;
            CnabCriarLayoutTextBlock.Text =
                $"Vale Transporte · Competência {arquivo.Competencia} · Santander 033 · CNAB 240";

            _cnabCriacaoAtualizandoDataTodos = true;
            try
            {
                CnabCriarAlterarTodosCheckBox.IsChecked = true;
                CnabCriarDataTodosTextBox.Text = arquivo.DataPagamentoSugerida.ToString("dd/MM/yyyy");
            }
            finally
            {
                _cnabCriacaoAtualizandoDataTodos = false;
            }

            AtualizarModoDataCriacaoCnab();
            AplicarDataTodosCriacaoCnab(arquivo.DataPagamentoSugerida);
            AtualizarResumoCriacaoCnab();

            CnabCriarImportPanel.Visibility = Visibility.Collapsed;
            CnabCriarEditorPanel.Visibility = Visibility.Visible;

            Log($"Criador CNAB: VT carregado | {Path.GetFileName(caminho)} | competência {arquivo.Competencia} | {_cnabCriacaoPagamentos.Count} funcionários");
        }
        catch (CnabCriacaoNaoSuportadaException ex)
        {
            CustomMessageBox.ShowWarning(ex.Message, "Arquivo não suportado");
        }
        catch (Exception ex)
        {
            Log("Erro ao carregar Vale Transporte para criação do CNAB", ex);
            CustomMessageBox.ShowError($"Não foi possível ler o Vale Transporte.\n\n{ex.Message}", "Erro ao criar CNAB");
        }
    }

    private void CnabCriarAlterarTodosCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        AtualizarModoDataCriacaoCnab();

        if (CnabCriarAlterarTodosCheckBox?.IsChecked == true &&
            CnabDecoderService.TentarParsearData(CnabCriarDataTodosTextBox?.Text, out DateTime data))
        {
            AplicarDataTodosCriacaoCnab(data);
        }
    }

    private void AtualizarModoDataCriacaoCnab()
    {
        bool alterarTodos = CnabCriarAlterarTodosCheckBox?.IsChecked == true;
        foreach (var item in _cnabCriacaoPagamentos)
            item.DataIndividualHabilitada = !alterarTodos;

        if (CnabCriarDataTodosTextBox != null)
        {
            CnabCriarDataTodosTextBox.IsEnabled = alterarTodos;
            CnabCriarDataTodosTextBox.Opacity = alterarTodos ? 1.0 : 0.45;
        }
    }

    private void CnabCriarDataTodosTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_cnabCriacaoAtualizandoDataTodos || CnabCriarAlterarTodosCheckBox?.IsChecked != true)
            return;

        if (CnabDecoderService.TentarParsearData(CnabCriarDataTodosTextBox.Text, out DateTime data))
        {
            AplicarDataTodosCriacaoCnab(data);
            CnabCriarDataTodosTextBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B3E59"));
        }
        else if (CnabCriarDataTodosTextBox.Text.Length >= 8)
        {
            CnabCriarDataTodosTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 72, 72));
        }
    }

    private void AplicarDataTodosCriacaoCnab(DateTime data)
    {
        string texto = data.ToString("dd/MM/yyyy");
        foreach (var item in _cnabCriacaoPagamentos)
            item.DataTexto = texto;
    }

    private void CnabCriarValorTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not CnabCriacaoPagamentoItem item)
            return;

        if (item.TentarObterValor(out decimal valor) && valor > 0)
        {
            item.ValorTexto = valor.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
            textBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B3E59"));
            AtualizarResumoCriacaoCnab();
        }
        else
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 72, 72));
        }
    }

    private void CnabCriarDataTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not CnabCriacaoPagamentoItem item)
            return;

        if (item.TentarObterData(out DateTime data))
        {
            item.DataTexto = data.ToString("dd/MM/yyyy");
            textBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B3E59"));
        }
        else
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 72, 72));
        }
    }

    private void CnabCriarBanco_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao || botao.DataContext is not CnabCriacaoPagamentoItem item)
            return;

        if (item.DadosBancarios?.EstaCompleto != true)
        {
            CustomMessageBox.ShowWarning(
                $"'{item.Nome}' ainda não possui dados bancários completos.\n\n" +
                "Cadastre ou atualize o colaborador na aba 'Cadastro de Colaborador'.",
                "Cadastro pendente");
            return;
        }

        var dados = item.DadosBancarios;
        string banco = CnabDecoderService.NomeBanco(dados.BancoCodigo);
        string documento = FormatarDocumentoCnab(dados.Documento);
        string agencia = string.IsNullOrWhiteSpace(dados.AgenciaDv) ? dados.Agencia : $"{dados.Agencia}-{dados.AgenciaDv}";
        string conta = string.IsNullOrWhiteSpace(dados.ContaDv) ? dados.Conta : $"{dados.Conta}-{dados.ContaDv}";

        CustomMessageBox.ShowInformation(
            $"Colaborador: {item.Nome}\n\n" +
            $"CPF: {documento}\n" +
            $"Banco: {banco} ({dados.BancoCodigo})\n" +
            $"Agência: {agencia}\n" +
            $"Conta: {conta}",
            "Dados do cadastro");
    }

    private void CnabCriarExcluir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao || botao.DataContext is not CnabCriacaoPagamentoItem item)
            return;

        if (CustomMessageBox.ShowQuestion(
                "Deseja remover este funcionário do CNAB que será gerado?",
                "Excluir pagamento",
                $"{item.Nome} — R$ {item.ValorTexto}") != MessageBoxResult.Yes)
            return;

        _cnabCriacaoPagamentos.Remove(item);
        AtualizarResumoCriacaoCnab();
    }

    private void AtualizarResumoCriacaoCnab()
    {
        decimal total = 0m;
        int valoresInvalidos = 0;
        int dadosPendentes = 0;
        int datasInvalidas = 0;

        foreach (var item in _cnabCriacaoPagamentos)
        {
            if (item.TentarObterValor(out decimal valor) && valor > 0)
                total += valor;
            else
                valoresInvalidos++;

            if (!item.TentarObterData(out _))
                datasInvalidas++;

            if (!item.DadosBancariosOk)
                dadosPendentes++;
        }

        if (CnabCriarResumoTextBlock != null)
        {
            CnabCriarResumoTextBlock.Text =
                $"{_cnabCriacaoPagamentos.Count} pagamento(s)  •  Total R$ {total:N2}" +
                (dadosPendentes > 0 ? $"  •  {dadosPendentes} dados bancários pendentes" : string.Empty) +
                (valoresInvalidos > 0 ? $"  •  {valoresInvalidos} valor(es) inválido(s)" : string.Empty) +
                (datasInvalidas > 0 ? $"  •  {datasInvalidas} data(s) inválida(s)" : string.Empty);
        }

        if (CnabCriarAvisoTextBlock != null)
        {
            CnabCriarAvisoTextBlock.Text = dadosPendentes > 0
                ? "Cadastre ou atualize os colaboradores pendentes na aba 'Cadastro de Colaborador'."
                : "Todos os registros estão prontos para gerar o CNAB 240.";
            CnabCriarAvisoTextBlock.Foreground = dadosPendentes > 0
                ? new SolidColorBrush(Color.FromRgb(205, 166, 86))
                : new SolidColorBrush(Color.FromRgb(127, 127, 135));
        }

        if (BtnGerarCnabValeTransporte != null)
            BtnGerarCnabValeTransporte.IsEnabled =
                _cnabCriacaoPagamentos.Count > 0 &&
                dadosPendentes == 0 && valoresInvalidos == 0 && datasInvalidas == 0;
    }

    private void CnabCriarSalvar_Click(object sender, RoutedEventArgs e)
    {
        if (_cnabCriacaoArquivoAtual == null)
        {
            CustomMessageBox.ShowWarning("Selecione o PDF de Vale Transporte primeiro.");
            return;
        }

        try
        {
            if (CnabCriarAlterarTodosCheckBox.IsChecked == true)
            {
                if (!CnabDecoderService.TentarParsearData(CnabCriarDataTodosTextBox.Text, out DateTime dataTodos))
                {
                    CustomMessageBox.ShowWarning("A data em massa é inválida. Use DD/MM/AAAA.");
                    return;
                }
                AplicarDataTodosCriacaoCnab(dataTodos);
            }

            foreach (var item in _cnabCriacaoPagamentos)
            {
                if (!item.TentarObterValor(out decimal valor) || valor <= 0)
                {
                    CustomMessageBox.ShowWarning($"Valor inválido para '{item.Nome}'.");
                    return;
                }
                if (!item.TentarObterData(out _))
                {
                    CustomMessageBox.ShowWarning($"Data inválida para '{item.Nome}'.");
                    return;
                }
                if (!item.DadosBancariosOk)
                {
                    CustomMessageBox.ShowWarning($"Dados bancários incompletos para '{item.Nome}'.");
                    return;
                }
            }

            string competenciaArquivo = _cnabCriacaoArquivoAtual.Competencia.Replace("/", "");
            string dataNome = _cnabCriacaoPagamentos.First().TentarObterData(out DateTime dataPrimeiro)
                ? dataPrimeiro.ToString("ddMMyyyy")
                : DateTime.Today.ToString("ddMMyyyy");

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Gerar CNAB 240 de Vale Transporte",
                InitialDirectory = Path.GetDirectoryName(_cnabCriacaoArquivoAtual.CaminhoOriginal),
                FileName = $"CNAB_VT_{competenciaArquivo}_{dataNome}.txt",
                Filter = "Arquivo CNAB (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
                AddExtension = true,
                DefaultExt = ".txt",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            string caminhoConfig = ObterCaminhoConfiguracaoCnabAdm();
            int sequencial = CnabAdmConfiguracaoRepository.ObterProximo(caminhoConfig);

            CnabSantanderFolhaAdmGenerator.GerarArquivo(
                _cnabCriacaoPagamentos,
                sequencial,
                dialog.FileName,
                DateTime.Now);

            CnabAdmConfiguracaoRepository.ConfirmarUso(caminhoConfig, sequencial);

            decimal total = _cnabCriacaoPagamentos.Sum(x =>
                x.TentarObterValor(out decimal v) ? v : 0m);

            Log($"CNAB VT gerado: {dialog.FileName} | seq={sequencial:D6} | {_cnabCriacaoPagamentos.Count} pagamentos | total={total:N2}");
            CustomMessageBox.ShowSuccess(
                $"CNAB de Vale Transporte gerado com sucesso!\n\n" +
                $"Competência: {_cnabCriacaoArquivoAtual.Competencia}\n" +
                $"Pagamentos: {_cnabCriacaoPagamentos.Count}\n" +
                $"Total: R$ {total:N2}\n" +
                $"Sequencial: {sequencial:D6}\n\n" +
                $"Arquivo: {Path.GetFileName(dialog.FileName)}",
                "CNAB gerado");
        }
        catch (Exception ex)
        {
            Log("Erro ao gerar CNAB de Vale Transporte", ex);
            CustomMessageBox.ShowError($"Não foi possível gerar o CNAB.\n\n{ex.Message}", "Erro ao gerar CNAB");
        }
    }

    // ============================================
    // COMPARA VALORES - HANDLERS
    // ============================================

    private void AdicionarValorConjunto1_Click(object sender, RoutedEventArgs e)
    {
        Log("AdicionarValorConjunto1_Click: abrindo ImportarValoresWindow...");
        try
        {
            var importarWindow = new ImportarValoresWindow { Owner = this };
            if (importarWindow.ShowDialog() == true)
            {
                Log($"AdicionarValorConjunto1_Click: importação confirmada — {importarWindow.ValoresImportados.Count} valores");
                foreach (var valor in importarWindow.ValoresImportados)
                    _compararConjunto1.Add(new ValorItem { Valor = valor });

                Log("AdicionarValorConjunto1_Click: chamando ReiniciarExibicaoComparar...");
                ReiniciarExibicaoComparar();
                Log("AdicionarValorConjunto1_Click: concluído ✅");
            }
            else
            {
                Log("AdicionarValorConjunto1_Click: importação cancelada pelo usuário");
            }
        }
        catch (Exception ex)
        {
            Log("AdicionarValorConjunto1_Click: ERRO", ex);
            CustomMessageBox.ShowError($"Erro ao importar valores: {ex.Message}");
        }
    }

    private void AdicionarValorConjunto2_Click(object sender, RoutedEventArgs e)
    {
        Log("AdicionarValorConjunto2_Click: abrindo ImportarValoresWindow...");
        try
        {
            var importarWindow = new ImportarValoresWindow { Owner = this };
            if (importarWindow.ShowDialog() == true)
            {
                Log($"AdicionarValorConjunto2_Click: importação confirmada — {importarWindow.ValoresImportados.Count} valores");
                foreach (var valor in importarWindow.ValoresImportados)
                    _compararConjunto2.Add(new ValorItem { Valor = valor });

                Log("AdicionarValorConjunto2_Click: chamando ReiniciarExibicaoComparar...");
                ReiniciarExibicaoComparar();
                Log("AdicionarValorConjunto2_Click: concluído ✅");
            }
            else
            {
                Log("AdicionarValorConjunto2_Click: importação cancelada pelo usuário");
            }
        }
        catch (Exception ex)
        {
            Log("AdicionarValorConjunto2_Click: ERRO", ex);
            CustomMessageBox.ShowError($"Erro ao importar valores: {ex.Message}");
        }
    }

    /// <summary>
    /// Reexibe os valores brutos em ambas as listas e limpa divergências (estado pré-comparação)
    /// </summary>
    private void ReiniciarExibicaoComparar()
    {
        Log($"ReiniciarExibicaoComparar: C1={_compararConjunto1.Count}, C2={_compararConjunto2.Count}");

        Log("ReiniciarExibicaoComparar: limpando _compararResultado1...");
        _compararResultado1.Clear();
        Log("ReiniciarExibicaoComparar: populando _compararResultado1...");
        foreach (var v in _compararConjunto1)
            _compararResultado1.Add(new ComparacaoLinhaItem { Valor = v.Valor });
        Log($"ReiniciarExibicaoComparar: _compararResultado1 com {_compararResultado1.Count} itens");

        Log("ReiniciarExibicaoComparar: limpando _compararResultado2...");
        _compararResultado2.Clear();
        Log("ReiniciarExibicaoComparar: populando _compararResultado2...");
        foreach (var v in _compararConjunto2)
            _compararResultado2.Add(new ComparacaoLinhaItem { Valor = v.Valor });
        Log($"ReiniciarExibicaoComparar: _compararResultado2 com {_compararResultado2.Count} itens");

        Log("ReiniciarExibicaoComparar: limpando _compararResultadoDiv...");
        _compararResultadoDiv.Clear();
        Log("ReiniciarExibicaoComparar: concluído ✅");
    }

    private void ItemCompararConjunto_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Reservado para uso futuro
    }

    private void EncontrarDivergencias_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_compararConjunto1.Count == 0 || _compararConjunto2.Count == 0)
            {
                CustomMessageBox.ShowWarning("Adicione valores nos dois conjuntos antes de comparar.");
                return;
            }

            // Ordena ambas as listas do menor para o maior
            var lista1 = _compararConjunto1.Select(v => v.Valor).OrderBy(v => v).ToList();
            var lista2 = _compararConjunto2.Select(v => v.Valor).OrderBy(v => v).ToList();

            var rows1   = new List<ComparacaoLinhaItem>();
            var rows2   = new List<ComparacaoLinhaItem>();
            var rowsDiv = new List<DivergenciaLinhaItem>();

            int i = 0, j = 0;

            // Merge ordenado: cada linha alinha valores iguais ou empurra o menor para o lado correto
            while (i < lista1.Count || j < lista2.Count)
            {
                if (i >= lista1.Count)
                {
                    // Só sobrou C2 → C1 vazio, C2 tem valor, divergência "<- Faltante"
                    rows1.Add(new ComparacaoLinhaItem { Valor = null });
                    rows2.Add(new ComparacaoLinhaItem { Valor = lista2[j] });
                    rowsDiv.Add(new DivergenciaLinhaItem { Texto = "<- Faltante" });
                    j++;
                }
                else if (j >= lista2.Count)
                {
                    // Só sobrou C1 → C2 vazio, C1 tem valor, divergência "Faltante ->"
                    rows1.Add(new ComparacaoLinhaItem { Valor = lista1[i] });
                    rows2.Add(new ComparacaoLinhaItem { Valor = null });
                    rowsDiv.Add(new DivergenciaLinhaItem { Texto = "Faltante ->" });
                    i++;
                }
                else if (lista1[i] == lista2[j])
                {
                    // Mesmos valores → linha neutra
                    rows1.Add(new ComparacaoLinhaItem { Valor = lista1[i] });
                    rows2.Add(new ComparacaoLinhaItem { Valor = lista2[j] });
                    rowsDiv.Add(new DivergenciaLinhaItem { Texto = "—" });
                    i++; j++;
                }
                else if (lista1[i] < lista2[j])
                {
                    // C1 tem esse valor, C2 não tem ainda → "Faltante ->"
                    rows1.Add(new ComparacaoLinhaItem { Valor = lista1[i] });
                    rows2.Add(new ComparacaoLinhaItem { Valor = null });
                    rowsDiv.Add(new DivergenciaLinhaItem { Texto = "Faltante ->" });
                    i++;
                }
                else
                {
                    // C2 tem esse valor, C1 não tem ainda → "<- Faltante"
                    rows1.Add(new ComparacaoLinhaItem { Valor = null });
                    rows2.Add(new ComparacaoLinhaItem { Valor = lista2[j] });
                    rowsDiv.Add(new DivergenciaLinhaItem { Texto = "<- Faltante" });
                    j++;
                }
            }

            _compararResultado1.Clear();
            foreach (var r in rows1) _compararResultado1.Add(r);

            _compararResultado2.Clear();
            foreach (var r in rows2) _compararResultado2.Add(r);

            _compararResultadoDiv.Clear();
            foreach (var r in rowsDiv) _compararResultadoDiv.Add(r);

            // Informa se não houve nenhuma divergência
            if (rowsDiv.All(d => d.IsNeutro))
                CustomMessageBox.ShowInformation("Nenhuma divergência encontrada! Os conjuntos são idênticos.", "Resultado");
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao encontrar divergências: {ex.Message}");
        }
    }
    // ============================================================
    // SCROLL SINCRONIZADO
    // ============================================================
    private bool _syncingScroll = false;

    private void CompararScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll) return;
        if (e.VerticalChange == 0) return;
        _syncingScroll = true;
        try
        {
            double offset = ((ScrollViewer)sender).VerticalOffset;
            if (!ReferenceEquals(sender, ScrollC1))  ScrollC1.ScrollToVerticalOffset(offset);
            if (!ReferenceEquals(sender, ScrollDiv)) ScrollDiv.ScrollToVerticalOffset(offset);
            if (!ReferenceEquals(sender, ScrollC2))  ScrollC2.ScrollToVerticalOffset(offset);
        }
        finally { _syncingScroll = false; }
    }

    // ============================================================
    // LIMPAR LISTAS
    // ============================================================
    private void LimparComparacao_Click(object sender, RoutedEventArgs e)
    {
        _compararConjunto1.Clear();
        _compararConjunto2.Clear();
        _compararResultado1.Clear();
        _compararResultado2.Clear();
        _compararResultadoDiv.Clear();
    }

    // ============================================================
    // LABELS EDITÁVEIS — duplo clique para editar
    // ============================================================
    private DateTime _ultimoCliqueLabel1 = DateTime.MinValue;
    private DateTime _ultimoCliqueLabel2 = DateTime.MinValue;
    private const int DUPLO_CLIQUE_MS = 400;

    private void LabelConjunto1_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var agora = DateTime.Now;
        if ((agora - _ultimoCliqueLabel1).TotalMilliseconds <= DUPLO_CLIQUE_MS)
        {
            LabelConjunto1.Visibility = Visibility.Collapsed;
            EditConjunto1.Text = LabelConjunto1.Text;
            EditConjunto1.Visibility = Visibility.Visible;
            EditConjunto1.Focus();
            EditConjunto1.SelectAll();
        }
        _ultimoCliqueLabel1 = agora;
    }

    private void LabelConjunto2_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var agora = DateTime.Now;
        if ((agora - _ultimoCliqueLabel2).TotalMilliseconds <= DUPLO_CLIQUE_MS)
        {
            LabelConjunto2.Visibility = Visibility.Collapsed;
            EditConjunto2.Text = LabelConjunto2.Text;
            EditConjunto2.Visibility = Visibility.Visible;
            EditConjunto2.Focus();
            EditConjunto2.SelectAll();
        }
        _ultimoCliqueLabel2 = agora;
    }

    private void EditConjunto1_LostFocus(object sender, RoutedEventArgs e)
    {
        var texto = EditConjunto1.Text.Trim();
        LabelConjunto1.Text = string.IsNullOrEmpty(texto) ? "Conjunto 1" : texto;
        EditConjunto1.Visibility = Visibility.Collapsed;
        LabelConjunto1.Visibility = Visibility.Visible;
    }

    private void EditConjunto2_LostFocus(object sender, RoutedEventArgs e)
    {
        var texto = EditConjunto2.Text.Trim();
        LabelConjunto2.Text = string.IsNullOrEmpty(texto) ? "Conjunto 2" : texto;
        EditConjunto2.Visibility = Visibility.Collapsed;
        LabelConjunto2.Visibility = Visibility.Visible;
    }

    private void EditConjunto_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Escape)
        {
            var tb = (TextBox)sender;
            tb.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
        }
    }

        private void AdicionarValor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var importarWindow = new ImportarValoresWindow
            {
                Owner = this
            };
            
            if (importarWindow.ShowDialog() == true && importarWindow.Importado)
            {
                // Adiciona os valores importados
                foreach (var valor in importarWindow.ValoresImportados)
                {
                    AdicionarValorNasListas(new ValorItem { Valor = valor });
                }
                
                AtualizarListasVisuais();
                
                System.Diagnostics.Debug.WriteLine($"✅ {importarWindow.ValoresImportados.Count} valores importados");
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao importar valores: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Adiciona valor respeitando o limite: preenche Conjunto1 primeiro, depois Conjunto2
    /// </summary>
    private void AdicionarValorNasListas(ValorItem valor)
    {
        if (_valoresConjunto1.Count < LIMITE_CONJUNTO1)
        {
            _valoresConjunto1.Add(valor);
        }
        else
        {
            _valoresConjunto2.Add(valor);
        }
    }
    
    /// <summary>
    /// Atualiza as listas visuais
    /// </summary>
    private void AtualizarListasVisuais()
    {
        Conjunto1ItemsControl.ItemsSource = null;
        Conjunto1ItemsControl.ItemsSource = _valoresConjunto1;
        
        Conjunto2ItemsControl.ItemsSource = null;
        Conjunto2ItemsControl.ItemsSource = _valoresConjunto2;
    }
    
    /// <summary>
    /// Rebalanceia as listas: se Conjunto1 tem espaço e Conjunto2 tem itens, move o primeiro
    /// </summary>
    private void RebalancearListas()
    {
        while (_valoresConjunto1.Count < LIMITE_CONJUNTO1 && _valoresConjunto2.Count > 0)
        {
            var primeiroDoConjunto2 = _valoresConjunto2[0];
            _valoresConjunto2.RemoveAt(0);
            _valoresConjunto1.Add(primeiroDoConjunto2);
        }
        
        AtualizarListasVisuais();
    }
    
    /// <summary>
    /// Remove item ao duplo-clique e rebalanceia as listas
    /// </summary>
    private void ItemConjunto_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Verifica se é duplo clique
        if (e.ClickCount == 2)
        {
            try
            {
                if (sender is Border border && border.Tag is ValorItem valor)
                {
                    // Tenta remover do Conjunto1 primeiro
                    bool removidoDoConjunto1 = _valoresConjunto1.Remove(valor);
                    
                    // Se não estava no Conjunto1, remove do Conjunto2
                    if (!removidoDoConjunto1)
                    {
                        _valoresConjunto2.Remove(valor);
                    }
                    else
                    {
                        // Se removeu do Conjunto1, rebalanceia (puxa do Conjunto2)
                        RebalancearListas();
                        return; // RebalancearListas já atualiza as listas visuais
                    }
                    
                    // Atualiza listas visuais
                    AtualizarListasVisuais();
                    
                    System.Diagnostics.Debug.WriteLine($"🗑️ Valor R$ {valor.Valor:N2} removido");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Erro ao remover valor: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Calcula o subconjunto - Algoritmo idêntico ao VBA do Excel
    /// </summary>
    private void Calcular_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validações
            if (string.IsNullOrWhiteSpace(ValorObjetivoTextBox.Text))
            {
                CustomMessageBox.ShowWarning("Por favor, informe o Valor Objetivo.");
                return;
            }
            
            // Parsing com cultura pt-BR (vírgula = decimal, ponto = milhares)
            if (!ParseDecimalBR(ValorObjetivoTextBox.Text, out decimal alvo))
            {
                CustomMessageBox.ShowWarning("Valor Objetivo inválido. Use vírgula como decimal (ex: 7.518,91)");
                return;
            }
            
            decimal margemInformada = 0;
            if (!string.IsNullOrWhiteSpace(MargemTextBox.Text))
            {
                if (!ParseDecimalBR(MargemTextBox.Text, out margemInformada))
                {
                    CustomMessageBox.ShowWarning("Margem inválida. Use vírgula como decimal (ex: 0,01)");
                    return;
                }
            }
            
            // Combina os valores das duas listas
            var todosValores = _valoresConjunto1.Concat(_valoresConjunto2).ToList();
            
            if (todosValores.Count == 0)
            {
                CustomMessageBox.ShowWarning("Nenhum valor na lista para calcular.");
                return;
            }
            
            // Debug - mostra soma total para diagnóstico
            decimal somaTotal = todosValores.Sum(v => v.Valor);
            System.Diagnostics.Debug.WriteLine($"🔢 Total de valores: {todosValores.Count}");
            System.Diagnostics.Debug.WriteLine($"🔢 Soma total: {somaTotal:N2}");
            System.Diagnostics.Debug.WriteLine($"🎯 Alvo: {alvo:N2}, Margem: {margemInformada:N2}");
            foreach (var v in todosValores)
                System.Diagnostics.Debug.WriteLine($"   - R$ {v.Valor:N2}");
            
            // Array de tentativas de margem (igual ao VBA: margem informada, 0.1, 1, 10)
            var tentativas = new List<decimal> { margemInformada, 0.1m, 1m, 10m };
            // Remove duplicatas
            tentativas = tentativas.Distinct().ToList();
            
            bool sucesso = false;
            bool[] usados = null;
            decimal margemUtilizada = 0;
            bool cancelado = false;
            
            // Tenta com cada margem
            foreach (var margem in tentativas)
            {
                usados = new bool[todosValores.Count];
                var startTime = DateTime.Now;
                
                var resultado = EncontrarSubconjunto(todosValores, usados, alvo, margem, 0, 0, ref startTime, ref cancelado);
                
                if (cancelado) break;
                
                if (resultado)
                {
                    sucesso = true;
                    margemUtilizada = margem;
                    break;
                }
            }
            
            if (cancelado) return;
            
            if (sucesso)
            {
                // Marca os valores utilizados em roxo
                DestacaValoresUtilizados(todosValores, usados);
                
                // Calcula a soma encontrada
                decimal somaEncontrada = 0;
                for (int i = 0; i < usados.Length; i++)
                {
                    if (usados[i]) somaEncontrada += todosValores[i].Valor;
                }
                
                CustomMessageBox.ShowSuccess(
                    $"Combinação encontrada!\n\n" +
                    $"Alvo: R$ {alvo:N2}\n" +
                    $"Encontrado: R$ {somaEncontrada:N2}\n" +
                    $"Diferença: R$ {Math.Abs(alvo - somaEncontrada):N2}\n" +
                    $"Margem utilizada: R$ {margemUtilizada:N2}"
                );
            }
            else
            {
                decimal somaMaxima = todosValores.Sum(v => v.Valor);
                CustomMessageBox.ShowInformation(
                    $"Nenhuma combinação encontrada dentro da margem máxima.\n\n" +
                    $"Alvo: R$ {alvo:N2}\n" +
                    $"Soma de TODOS os valores: R$ {somaMaxima:N2}\n\n" +
                    $"Verifique se o alvo é alcançável com os valores informados."
                );
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Erro ao calcular: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Parsing de decimal no formato brasileiro (vírgula = decimal, ponto = milhares)
    /// Exemplos: "7.518,91" → 7518.91 | "0,01" → 0.01 | "123" → 123
    /// </summary>
    private bool ParseDecimalBR(string texto, out decimal resultado)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            resultado = 0;
            return false;
        }
        
        // Remove espaços e símbolo R$
        texto = texto.Trim().Replace("R$", "").Replace(" ", "");
        
        // Tenta com cultura pt-BR (vírgula = decimal)
        var culturaBR = CultureInfo.GetCultureInfo("pt-BR");
        if (decimal.TryParse(texto, NumberStyles.Number, culturaBR, out resultado))
            return true;
        
        // Tenta com cultura invariante (ponto = decimal)
        if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out resultado))
            return true;
        
        // Última tentativa: remove pontos e troca vírgula por ponto
        var textoNormalizado = texto.Replace(".", "").Replace(",", ".");
        return decimal.TryParse(textoNormalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out resultado);
    }
    
    /// <summary>
    /// Algoritmo de Backtracking Recursivo (idêntico ao VBA)
    /// startTime passado por ref para resetar o timer corretamente quando usuário continua
    /// </summary>
    private bool EncontrarSubconjunto(List<ValorItem> valores, bool[] usados, decimal alvo,
                                      decimal margem, int indice, decimal somaAtual,
                                      ref DateTime startTime, ref bool cancelado)
    {
        // Sai imediatamente se foi cancelado
        if (cancelado) return false;
        
        // Verifica timeout de 15 segundos
        if ((DateTime.Now - startTime).TotalSeconds > 15)
        {
            var resposta = CustomMessageBox.ShowQuestion(
                "A execução está demorando mais de 15 segundos.\n\nDeseja continuar?",
                "Tempo Limite"
            );
            
            if (resposta != HubFinanceiro.MessageBoxResult.Yes)
            {
                cancelado = true;
                return false;
            }
            
            // Reseta o timer
            startTime = DateTime.Now;
        }
        
        // Verifica se a soma atual está dentro da margem
        if (Math.Abs(somaAtual - alvo) <= margem)
            return true;
        
        // Se passou do alvo + margem, ou acabaram os valores, para
        if (somaAtual > alvo + margem || indice >= valores.Count)
            return false;
        
        // Tenta INCLUIR o valor atual
        usados[indice] = true;
        if (EncontrarSubconjunto(valores, usados, alvo, margem, indice + 1,
                                somaAtual + valores[indice].Valor, ref startTime, ref cancelado))
            return true;
        
        // Verifica cancelamento antes de tentar a segunda ramificação
        if (cancelado) return false;
        
        // Tenta NÃO INCLUIR o valor atual
        usados[indice] = false;
        if (EncontrarSubconjunto(valores, usados, alvo, margem, indice + 1,
                                somaAtual, ref startTime, ref cancelado))
            return true;
        
        return false;
    }
    
    /// <summary>
    /// Destaca os valores utilizados na solução (roxo = selecionado)
    /// </summary>
    private void DestacaValoresUtilizados(List<ValorItem> valores, bool[] usados)
    {
        // Limpa destaques anteriores
        LimparDestaques();
        
        for (int i = 0; i < usados.Length; i++)
        {
            if (usados[i])
            {
                valores[i].Destacado = true;
            }
        }
        
        AtualizarListasVisuais();
    }
    
    /// <summary>
    /// Limpa todos os destaques
    /// </summary>
    private void LimparDestaques()
    {
        foreach (var valor in _valoresConjunto1)
        {
            valor.Destacado = false;
        }
        
        foreach (var valor in _valoresConjunto2)
        {
            valor.Destacado = false;
        }
    }
    
    private void ValorObjetivoTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ValorObjetivoPlaceholder != null)
        {
            ValorObjetivoPlaceholder.Visibility = string.IsNullOrEmpty(ValorObjetivoTextBox.Text) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
    }
    
    private void MargemTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (MargemPlaceholder != null)
        {
            MargemPlaceholder.Visibility = string.IsNullOrEmpty(MargemTextBox.Text) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
    }
    
    #endregion
}

#region Classes de Modelo

/// <summary>
/// Representa um fornecedor no sistema
/// </summary>
public class Fornecedor
{
    public string Nome { get; set; } = string.Empty;
    public int Codigo { get; set; }
    public int Natureza { get; set; }
    public string Email { get; set; } = string.Empty;
    public int DiaPagamento { get; set; } // 1-31
    public int TipoPagamento { get; set; } // 01-03
    public bool Ativo { get; set; } = true;
    public bool Administradora { get; set; } = false;
    public bool Corretora { get; set; } = false;
}

/// <summary>
/// Representa uma previsão de pagamento
/// </summary>
public class PrevisaoPagamento
{
    public int Id { get; set; }
    public int CodigoFornecedor { get; set; }
    public string NomeFornecedor { get; set; } = string.Empty;
    public int TipoPagamento { get; set; } // 0 = não definido, 1-3
    public int Natureza { get; set; }
    public decimal Valor { get; set; }
    public DateTime DataPagamento { get; set; }
    public string Status { get; set; } = "Pendente";
    public string Empresa { get; set; } = string.Empty; // "ADM", "COR" ou vazio
    public DateTime? DataProvisionamento { get; set; } = null; // Data quando foi provisionado

    // Campos opcionais — preenchidos apenas quando o pagamento for importado via nota fiscal.
    // Pagamentos inseridos manualmente ou via lote Excel manterão esses campos como null/vazio.
    public string? NumeroNota       { get; set; } = null; // Ex: "00100449"
    public string? DataEmissaoNota  { get; set; } = null; // Ex: "03/03/2026 10:37:09"
}

/// <summary>
/// Representa um valor importado para o CalcSubconjunto
/// </summary>
public class ValorItem : INotifyPropertyChanged
{
    private bool _destacado;
    
    public decimal Valor { get; set; }
    
    public bool Destacado
    {
        get => _destacado;
        set
        {
            if (_destacado != value)
            {
                _destacado = value;
                OnPropertyChanged(nameof(Destacado));
            }
        }
    }
    
    public event PropertyChangedEventHandler PropertyChanged;
    
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Representa uma linha na exibição do Compara Valores (pode ser vazia)
/// </summary>
public class ComparacaoLinhaItem
{
    public decimal? Valor { get; set; }
    public bool IsVazio => !Valor.HasValue;
    public string ValorTexto => Valor.HasValue ? Valor.Value.ToString("N2") : "—";
}

/// <summary>
/// Representa uma linha na lista de divergências
/// </summary>
public class DivergenciaLinhaItem
{
    public string Texto { get; set; } = "—";
    /// <summary>Linha neutra: ambos conjuntos têm o mesmo valor</summary>
    public bool IsNeutro => Texto == "—";
    /// <summary>Existe no C2 mas não no C1 (falta no C1)</summary>
    public bool IsFaltanteNoC1 => Texto.StartsWith("<-");
    /// <summary>Existe no C1 mas não no C2 (falta no C2)</summary>
    public bool IsFaltanteNoC2 => Texto.EndsWith("->");
}

#endregion
