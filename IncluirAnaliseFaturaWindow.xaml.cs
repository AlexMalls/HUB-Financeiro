using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class IncluirAnaliseFaturaWindow : Window
{
    private readonly string _caminhoBaseData;
    private readonly AnaliseFaturasPreparacaoValidator _validator = new();

    private List<string> _faturasMesPassado = new();
    private List<string> _faturasMesAtual = new();
    private List<string> _faturasMesSeguinte = new();
    private string? _relatorioOver;

    private GrupoFaturasValidacao? _validacaoPassado;
    private GrupoFaturasValidacao? _validacaoAtual;
    private GrupoFaturasValidacao? _validacaoSeguinte;
    private OverValidacao? _validacaoOver;
    private bool _validando;

    // O botão Feito gera o resultado e fecha esta janela. A MainWindow abre o resultado
    // somente depois que o ShowDialog desta preparação terminar, evitando janelas modais aninhadas.
    public AnaliseFinalDiagnostico? ResultadoFinalGerado { get; private set; }
    public AnaliseFaturasHistoricoContextoCriacao? ContextoHistoricoGerado { get; private set; }

    public IncluirAnaliseFaturaWindow(string caminhoBaseData)
    {
        InitializeComponent();
        _caminhoBaseData = caminhoBaseData;
        AtualizarInterface();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void SelecionarFaturasPassado_Click(object sender, RoutedEventArgs e)
        => await SelecionarFaturasAsync(
            "Selecionar faturas do mês passado",
            () => _faturasMesPassado,
            arquivos => _faturasMesPassado = arquivos,
            validacao => _validacaoPassado = validacao);

    private async void SelecionarFaturasAtual_Click(object sender, RoutedEventArgs e)
        => await SelecionarFaturasAsync(
            "Selecionar faturas do mês atual",
            () => _faturasMesAtual,
            arquivos => _faturasMesAtual = arquivos,
            validacao => _validacaoAtual = validacao);

    private async void SelecionarFaturasSeguinte_Click(object sender, RoutedEventArgs e)
        => await SelecionarFaturasAsync(
            "Selecionar faturas do mês que vem",
            () => _faturasMesSeguinte,
            arquivos => _faturasMesSeguinte = arquivos,
            validacao => _validacaoSeguinte = validacao);

    private async void SelecionarPastaFaturasPassado_Click(object sender, RoutedEventArgs e)
        => await SelecionarPastaFaturasAsync(
            "Selecionar pasta com faturas do mês passado",
            () => _faturasMesPassado,
            arquivos => _faturasMesPassado = arquivos,
            validacao => _validacaoPassado = validacao);

    private async void SelecionarPastaFaturasAtual_Click(object sender, RoutedEventArgs e)
        => await SelecionarPastaFaturasAsync(
            "Selecionar pasta com faturas do mês atual",
            () => _faturasMesAtual,
            arquivos => _faturasMesAtual = arquivos,
            validacao => _validacaoAtual = validacao);

    private async void SelecionarPastaFaturasSeguinte_Click(object sender, RoutedEventArgs e)
        => await SelecionarPastaFaturasAsync(
            "Selecionar pasta com faturas do mês que vem",
            () => _faturasMesSeguinte,
            arquivos => _faturasMesSeguinte = arquivos,
            validacao => _validacaoSeguinte = validacao);

    private async Task SelecionarFaturasAsync(
        string titulo,
        Func<List<string>> obterArquivosAtuais,
        Action<List<string>> atribuirArquivos,
        Action<GrupoFaturasValidacao> atribuirValidacao)
    {
        if (_validando)
            return;

        try
        {
            var dialog = new OpenFileDialog
            {
                Title = titulo,
                Filter = "Arquivos PDF (*.pdf)|*.pdf|Todos os arquivos (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };

            TentarDefinirDiretorioInicial(dialog, ObterPastaInicialFaturas());

            if (dialog.ShowDialog(this) != true)
                return;

            List<string> novosArquivos = dialog.FileNames
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (novosArquivos.Count == 0)
                return;

            DefinirValidando(true, "Validando PDFs selecionados...");
            await AdicionarFaturasAsync(novosArquivos, obterArquivosAtuais, atribuirArquivos, atribuirValidacao);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Não foi possível selecionar ou validar as faturas.\n\n{ex.Message}");
        }
        finally
        {
            DefinirValidando(false);
        }
    }

    private async Task SelecionarPastaFaturasAsync(
        string titulo,
        Func<List<string>> obterArquivosAtuais,
        Action<List<string>> atribuirArquivos,
        Action<GrupoFaturasValidacao> atribuirValidacao)
    {
        if (_validando)
            return;

        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = titulo,
                Multiselect = false,
                InitialDirectory = ObterPastaInicialFaturas()
            };

            if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                return;

            string pastaSelecionada = dialog.FolderName;
            DefinirValidando(true, "Procurando PDFs de fatura na pasta e subpastas...");

            List<string> novosArquivos = await Task.Run(() => EncontrarFaturasNaPasta(pastaSelecionada));
            if (novosArquivos.Count == 0)
            {
                CustomMessageBox.ShowWarning(
                    "Nenhum arquivo PDF com 'Fatura' no nome foi encontrado na pasta selecionada nem em suas subpastas.",
                    "Nenhuma fatura encontrada");
                return;
            }

            DefinirValidando(true, $"Validando {novosArquivos.Count} PDF(s) encontrado(s)...");
            await AdicionarFaturasAsync(novosArquivos, obterArquivosAtuais, atribuirArquivos, atribuirValidacao);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Não foi possível carregar as faturas da pasta.\n\n{ex.Message}");
        }
        finally
        {
            DefinirValidando(false);
        }
    }

    private async Task AdicionarFaturasAsync(
        IEnumerable<string> novosArquivosOrigem,
        Func<List<string>> obterArquivosAtuais,
        Action<List<string>> atribuirArquivos,
        Action<GrupoFaturasValidacao> atribuirValidacao)
    {
        List<string> novosArquivos = novosArquivosOrigem
            .Where(File.Exists)
            .Where(x => string.Equals(Path.GetExtension(x), ".pdf", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (novosArquivos.Count == 0)
            return;

        List<string> arquivosAtuais = obterArquivosAtuais();
        List<string> arquivos = arquivosAtuais
            .Concat(novosArquivos)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Tanto "Selecionar PDFs" quanto "Selecionar pasta" são aditivos.
        if (arquivos.Count == arquivosAtuais.Count)
            return;

        List<string> nomesDuplicados = arquivos
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (nomesDuplicados.Count > 0)
        {
            CustomMessageBox.ShowWarning(
                "Há arquivos diferentes com o mesmo nome na seleção.\n\n" +
                string.Join("\n", nomesDuplicados) +
                "\n\nRenomeie um deles antes de continuar.",
                "Arquivos duplicados");
            return;
        }

        GrupoFaturasValidacao resultado = await Task.Run(
            () => _validator.ValidarGrupoFaturas(arquivos));

        if (!resultado.Valido)
        {
            CustomMessageBox.ShowWarning(resultado.Mensagem, "Faturas não validadas");
            return;
        }

        atribuirArquivos(arquivos);
        atribuirValidacao(resultado);
    }

    private static List<string> EncontrarFaturasNaPasta(string pastaRaiz)
    {
        if (!Directory.Exists(pastaRaiz))
            return new List<string>();

        var opcoes = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        return Directory.EnumerateFiles(pastaRaiz, "*.pdf", opcoes)
            .Where(x => Path.GetFileNameWithoutExtension(x)
                .Contains("fatura", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ObterPastaInicialFaturas()
    {
        string pasta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "OneDrive - Positiva Administradora de Benefícios Ltda",
            "Documentos",
            "Financeiro",
            "Faturas Operadora");

        return Directory.Exists(pasta)
            ? pasta
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private async void FaturasListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_validando || e.ChangedButton != MouseButton.Left || sender is not ListBox lista)
            return;

        if (e.OriginalSource is not DependencyObject origem)
            return;

        ListBoxItem? item = ItemsControl.ContainerFromElement(lista, origem) as ListBoxItem;
        if (item == null)
            return;

        int indice = lista.ItemContainerGenerator.IndexFromContainer(item);
        if (indice < 0)
            return;

        e.Handled = true;

        if (ReferenceEquals(lista, FaturasPassadoListBox))
        {
            await RemoverFaturaAsync(
                _faturasMesPassado, indice,
                arquivos => _faturasMesPassado = arquivos,
                validacao => _validacaoPassado = validacao);
        }
        else if (ReferenceEquals(lista, FaturasAtualListBox))
        {
            await RemoverFaturaAsync(
                _faturasMesAtual, indice,
                arquivos => _faturasMesAtual = arquivos,
                validacao => _validacaoAtual = validacao);
        }
        else if (ReferenceEquals(lista, FaturasSeguinteListBox))
        {
            await RemoverFaturaAsync(
                _faturasMesSeguinte, indice,
                arquivos => _faturasMesSeguinte = arquivos,
                validacao => _validacaoSeguinte = validacao);
        }
    }

    private async Task RemoverFaturaAsync(
        List<string> arquivosAtuais,
        int indice,
        Action<List<string>> atribuirArquivos,
        Action<GrupoFaturasValidacao?> atribuirValidacao)
    {
        if (indice < 0 || indice >= arquivosAtuais.Count)
            return;

        var restantes = arquivosAtuais
            .Where((_, i) => i != indice)
            .ToList();

        if (restantes.Count == 0)
        {
            atribuirArquivos(restantes);
            atribuirValidacao(null);
            AtualizarInterface();
            return;
        }

        try
        {
            DefinirValidando(true, "Removendo PDF e revalidando o grupo...");

            GrupoFaturasValidacao resultado = await Task.Run(
                () => _validator.ValidarGrupoFaturas(restantes));

            if (!resultado.Valido)
            {
                CustomMessageBox.ShowWarning(
                    "Não foi possível remover esse PDF sem invalidar o grupo.\n\n" + resultado.Mensagem,
                    "PDF não removido");
                return;
            }

            atribuirArquivos(restantes);
            atribuirValidacao(resultado);
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Não foi possível remover e revalidar a fatura.\n\n{ex.Message}");
        }
        finally
        {
            DefinirValidando(false);
        }
    }

    private async void SelecionarRelatorioOver_Click(object sender, RoutedEventArgs e)
    {
        if (_validando)
            return;

        try
        {
            string pastaInicialOver = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OneDrive - Positiva Administradora de Benefícios Ltda",
                "Documentos",
                "Financeiro",
                "ANALISE DE FATURA",
                "OVERS");

            var dialog = new OpenFileDialog
            {
                Title = "Selecionar relatório Over do mês passado",
                Filter = "Arquivos Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Todos os arquivos (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            TentarDefinirDiretorioInicial(dialog, pastaInicialOver);

            if (dialog.ShowDialog(this) != true)
                return;

            string arquivo = dialog.FileName;

            DefinirValidando(true, "Validando relatório Over...");

            OverValidacao resultado = await Task.Run(() => _validator.ValidarOver(arquivo));

            if (!resultado.Valido)
            {
                CustomMessageBox.ShowWarning(resultado.Mensagem, "Over não validado");
                return;
            }

            _relatorioOver = arquivo;
            _validacaoOver = resultado;
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError($"Não foi possível selecionar ou validar o relatório Over.\n\n{ex.Message}");
        }
        finally
        {
            DefinirValidando(false);
        }
    }

    private static void TentarDefinirDiretorioInicial(OpenFileDialog dialog, string pasta)
    {
        try
        {
            if (Directory.Exists(pasta))
                dialog.InitialDirectory = pasta;
        }
        catch
        {
            // Fallback silencioso: o Windows escolhe o diretório inicial padrão.
        }
    }

    private void DefinirValidando(bool validando, string? mensagem = null)
    {
        _validando = validando;
        Mouse.OverrideCursor = validando ? Cursors.Wait : null;

        if (validando && !string.IsNullOrWhiteSpace(mensagem))
        {
            PreparacaoStatusText.Text = mensagem;
            PreparacaoStatusText.Foreground = ObterBrush("ValidationWorkingColor");
        }

        AtualizarInterface();
    }

    private void AtualizarInterface()
    {
        FaturasPassadoListBox.ItemsSource = _faturasMesPassado.Select(Path.GetFileName).ToList();
        FaturasAtualListBox.ItemsSource = _faturasMesAtual.Select(Path.GetFileName).ToList();
        FaturasSeguinteListBox.ItemsSource = _faturasMesSeguinte.Select(Path.GetFileName).ToList();

        FaturasPassadoStatusText.Text = DescreverSelecao(_faturasMesPassado);
        FaturasAtualStatusText.Text = DescreverSelecao(_faturasMesAtual);
        FaturasSeguinteStatusText.Text = DescreverSelecao(_faturasMesSeguinte);

        DateTime? competenciaBase = ObterCompetenciaValida(_validacaoPassado);

        AtualizarCompetenciaText(
            FaturasPassadoCompetenciaText,
            _validacaoPassado,
            competenciaEsperada: null,
            ehCompetenciaBase: true);

        AtualizarCompetenciaText(
            FaturasAtualCompetenciaText,
            _validacaoAtual,
            competenciaBase?.AddMonths(1),
            ehCompetenciaBase: false);

        AtualizarCompetenciaText(
            FaturasSeguinteCompetenciaText,
            _validacaoSeguinte,
            competenciaBase?.AddMonths(2),
            ehCompetenciaBase: false);

        RelatorioOverStatusText.Text = string.IsNullOrWhiteSpace(_relatorioOver)
            ? "Nenhum arquivo selecionado"
            : Path.GetFileName(_relatorioOver);
        RelatorioOverStatusText.ToolTip = _relatorioOver ?? string.Empty;

        AtualizarCompetenciaOverText(competenciaBase);

        PreparacaoAnaliseValidacao validacaoPreparacao = _validator.ValidarSequencia(
            _validacaoPassado,
            _validacaoAtual,
            _validacaoSeguinte,
            _validacaoOver);

        if (_validando)
        {
            PreparacaoStatusText.Text = "Validando arquivos...";
            PreparacaoStatusText.Foreground = ObterBrush("ValidationWorkingColor");
        }
        else if (validacaoPreparacao.Valido)
        {
            PreparacaoStatusText.Text = "✓ " + validacaoPreparacao.Mensagem;
            PreparacaoStatusText.Foreground = ObterBrush("ValidationSuccessColor");
        }
        else if (TodosOsQuatroGruposValidados())
        {
            PreparacaoStatusText.Text = "⚠ " + validacaoPreparacao.Mensagem.Replace("\n", "  ");
            PreparacaoStatusText.Foreground = ObterBrush("ValidationErrorColor");
        }
        else
        {
            PreparacaoStatusText.Text = "Selecione e valide os quatro grupos. O Feito só será liberado quando as competências estiverem corretas.";
            PreparacaoStatusText.Foreground = ObterBrush("MutedTextColor");
        }

        bool fontesExistem =
            _faturasMesPassado.All(File.Exists) &&
            _faturasMesAtual.All(File.Exists) &&
            _faturasMesSeguinte.All(File.Exists) &&
            !string.IsNullOrWhiteSpace(_relatorioOver) &&
            File.Exists(_relatorioOver);

        BtnSelecionarPassado.IsEnabled = !_validando;
        BtnSelecionarPastaPassado.IsEnabled = !_validando;
        BtnSelecionarAtual.IsEnabled = !_validando;
        BtnSelecionarPastaAtual.IsEnabled = !_validando;
        BtnSelecionarSeguinte.IsEnabled = !_validando;
        BtnSelecionarPastaSeguinte.IsEnabled = !_validando;
        BtnSelecionarOver.IsEnabled = !_validando;

        BtnTestarOver.IsEnabled = !_validando &&
            _validacaoOver?.Valido == true &&
            !string.IsNullOrWhiteSpace(_relatorioOver) &&
            File.Exists(_relatorioOver);

        BtnTestarVinculo.IsEnabled = !_validando &&
            _validacaoPassado?.Valido == true &&
            _validacaoOver?.Valido == true &&
            _validacaoPassado.Competencia.HasValue &&
            _validacaoOver.Competencia.HasValue &&
            new DateTime(_validacaoPassado.Competencia.Value.Year, _validacaoPassado.Competencia.Value.Month, 1) ==
            new DateTime(_validacaoOver.Competencia.Value.Year, _validacaoOver.Competencia.Value.Month, 1) &&
            _faturasMesPassado.Count > 0 &&
            _faturasMesPassado.All(File.Exists) &&
            !string.IsNullOrWhiteSpace(_relatorioOver) &&
            File.Exists(_relatorioOver);

        BtnTestarPassado.IsEnabled = !_validando &&
            _validacaoPassado?.Valido == true &&
            _faturasMesPassado.Count > 0 &&
            _faturasMesPassado.All(File.Exists);

        BtnTestarAtual.IsEnabled = !_validando &&
            _validacaoAtual?.Valido == true &&
            _faturasMesAtual.Count > 0 &&
            _faturasMesAtual.All(File.Exists);

        BtnTestarSeguinte.IsEnabled = !_validando &&
            _validacaoSeguinte?.Valido == true &&
            _faturasMesSeguinte.Count > 0 &&
            _faturasMesSeguinte.All(File.Exists);

        BtnFeito.IsEnabled = !_validando && validacaoPreparacao.Valido && fontesExistem;
    }

    private bool TodosOsQuatroGruposValidados()
        => _validacaoPassado?.Valido == true
        && _validacaoAtual?.Valido == true
        && _validacaoSeguinte?.Valido == true
        && _validacaoOver?.Valido == true;

    private static DateTime? ObterCompetenciaValida(GrupoFaturasValidacao? validacao)
    {
        if (validacao?.Valido != true || !validacao.Competencia.HasValue)
            return null;

        DateTime data = validacao.Competencia.Value;
        return new DateTime(data.Year, data.Month, 1);
    }

    private void AtualizarCompetenciaText(
        TextBlock textBlock,
        GrupoFaturasValidacao? validacao,
        DateTime? competenciaEsperada,
        bool ehCompetenciaBase)
    {
        if (validacao?.Valido != true || !validacao.Competencia.HasValue)
        {
            textBlock.Text = "Competência: —";
            textBlock.Foreground = ObterBrush("MutedTextColor");
            textBlock.ToolTip = null;
            return;
        }

        DateTime detectadaOriginal = validacao.Competencia.Value;
        DateTime detectada = new(detectadaOriginal.Year, detectadaOriginal.Month, 1);
        string detectadaTexto = AnaliseFaturasPreparacaoValidator.FormatarCompetencia(detectada);

        if (ehCompetenciaBase)
        {
            textBlock.Text = $"✓ Competência detectada: {detectadaTexto}";
            textBlock.Foreground = ObterBrush("ValidationSuccessColor");
            textBlock.ToolTip = "Esta competência define a referência para mês atual, mês que vem e Over.";
            return;
        }

        if (!competenciaEsperada.HasValue)
        {
            textBlock.Text = $"• Competência detectada: {detectadaTexto} • aguardando mês passado";
            textBlock.Foreground = ObterBrush("ValidationWorkingColor");
            textBlock.ToolTip = "Selecione as faturas do mês passado para validar a sequência.";
            return;
        }

        DateTime esperadaOriginal = competenciaEsperada.Value;
        DateTime esperada = new(esperadaOriginal.Year, esperadaOriginal.Month, 1);
        string esperadaTexto = AnaliseFaturasPreparacaoValidator.FormatarCompetencia(esperada);

        if (detectada == esperada)
        {
            textBlock.Text = $"✓ Competência correta: {detectadaTexto}";
            textBlock.Foreground = ObterBrush("ValidationSuccessColor");
            textBlock.ToolTip = $"Competência esperada: {esperadaTexto}.";
        }
        else
        {
            textBlock.Text = $"✕ Competência incorreta: {detectadaTexto} • esperada {esperadaTexto}";
            textBlock.Foreground = ObterBrush("ValidationErrorColor");
            textBlock.ToolTip = $"Detectada: {detectadaTexto}. Esperada pela sequência: {esperadaTexto}.";
        }
    }

    private void AtualizarCompetenciaOverText(DateTime? competenciaBase)
    {
        if (_validacaoOver?.Valido != true || !_validacaoOver.Competencia.HasValue)
        {
            RelatorioOverCompetenciaText.Text = "Competência: —";
            RelatorioOverCompetenciaText.Foreground = ObterBrush("MutedTextColor");
            RelatorioOverCompetenciaText.ToolTip = null;
            return;
        }

        DateTime detectadaOriginal = _validacaoOver.Competencia.Value;
        DateTime detectada = new(detectadaOriginal.Year, detectadaOriginal.Month, 1);
        string detectadaTexto = AnaliseFaturasPreparacaoValidator.FormatarCompetencia(detectada);
        string detalhes = $"Planilha: {_validacaoOver.Planilha} • {_validacaoOver.QuantidadeLinhas:N0} linha(s) com período";

        if (!competenciaBase.HasValue)
        {
            RelatorioOverCompetenciaText.Text = $"• Competência detectada: {detectadaTexto} • aguardando mês passado";
            RelatorioOverCompetenciaText.Foreground = ObterBrush("ValidationWorkingColor");
            RelatorioOverCompetenciaText.ToolTip = detalhes + ". Selecione as faturas do mês passado para validar a competência do Over.";
            return;
        }

        DateTime esperadaOriginal = competenciaBase.Value;
        DateTime esperada = new(esperadaOriginal.Year, esperadaOriginal.Month, 1);
        string esperadaTexto = AnaliseFaturasPreparacaoValidator.FormatarCompetencia(esperada);

        if (detectada == esperada)
        {
            RelatorioOverCompetenciaText.Text = $"✓ Competência correta: {detectadaTexto}";
            RelatorioOverCompetenciaText.Foreground = ObterBrush("ValidationSuccessColor");
            RelatorioOverCompetenciaText.ToolTip = detalhes + $" • Competência esperada: {esperadaTexto}.";
        }
        else
        {
            RelatorioOverCompetenciaText.Text = $"✕ Competência incorreta: {detectadaTexto} • esperada {esperadaTexto}";
            RelatorioOverCompetenciaText.Foreground = ObterBrush("ValidationErrorColor");
            RelatorioOverCompetenciaText.ToolTip = detalhes + $" • Detectada: {detectadaTexto}. Esperada: {esperadaTexto}.";
        }
    }

    private Brush ObterBrush(string recurso)
        => (Brush)FindResource(recurso);

    private static string DescreverSelecao(IReadOnlyCollection<string> arquivos)
        => arquivos.Count == 0
            ? "Nenhum arquivo selecionado"
            : arquivos.Count == 1
                ? "1 arquivo selecionado"
                : $"{arquivos.Count} arquivos selecionados";

    private async void TestarLeituraOver_Click(object sender, RoutedEventArgs e)
    {
        if (_validando || string.IsNullOrWhiteSpace(_relatorioOver) || !File.Exists(_relatorioOver))
            return;

        try
        {
            DefinirValidando(true, "Lendo lançamentos do relatório Over...");

            OverArquivo leitura = await Task.Run(() =>
            {
                var parser = new OverParser();
                return parser.Ler(_relatorioOver);
            });

            DefinirValidando(false);

            var janela = new LeituraOverDiagnosticoWindow(leitura)
            {
                Owner = this
            };
            janela.ShowDialog();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError(
                "Não foi possível executar o diagnóstico da leitura do Over.\n\n" + ex.Message,
                "Erro no leitor do Over");
        }
        finally
        {
            if (IsVisible)
                DefinirValidando(false);
            else
                Mouse.OverrideCursor = null;
        }
    }

    private async void TestarVinculo_Click(object sender, RoutedEventArgs e)
    {
        if (_validando ||
            _validacaoPassado?.Valido != true ||
            _validacaoOver?.Valido != true ||
            _faturasMesPassado.Count == 0 ||
            _faturasMesPassado.Any(x => !File.Exists(x)) ||
            string.IsNullOrWhiteSpace(_relatorioOver) ||
            !File.Exists(_relatorioOver))
        {
            return;
        }

        try
        {
            DateTime? competenciaFatura = _validacaoPassado.Competencia;
            DateTime? competenciaOver = _validacaoOver.Competencia;
            if (!competenciaFatura.HasValue || !competenciaOver.HasValue ||
                new DateTime(competenciaFatura.Value.Year, competenciaFatura.Value.Month, 1) !=
                new DateTime(competenciaOver.Value.Year, competenciaOver.Value.Month, 1))
            {
                CustomMessageBox.ShowWarning(
                    "O diagnóstico só pode ser executado quando as faturas do mês passado e o Over possuem a mesma competência.",
                    "Competências incompatíveis");
                return;
            }

            bool contextoDisponivel =
                _validacaoAtual?.Valido == true &&
                _validacaoSeguinte?.Valido == true &&
                _faturasMesAtual.Count > 0 && _faturasMesAtual.All(File.Exists) &&
                _faturasMesSeguinte.Count > 0 && _faturasMesSeguinte.All(File.Exists);

            DefinirValidando(true, contextoDisponivel
                ? "Lendo faturas, Over e contexto temporal..."
                : "Lendo faturas e Over para vínculo, composição e comparação principal...");
            bool ignorarCoparticipacao = IgnorarCoparticipacaoCheckBox.IsChecked != false;
            bool ignorarCompetenciasAnteriores = IgnorarCompetenciasAnterioresCheckBox.IsChecked != false;

            var leituraDiagnostico = await Task.Run(() =>
            {
                var parserFatura = new FaturaBradescoParser();
                var faturasPassado = _faturasMesPassado.Select(parserFatura.Ler).ToList();

                var parserOver = new OverParser();
                OverArquivo over = parserOver.Ler(_relatorioOver);

                var vinculoService = new BeneficiarioVinculoService();
                VinculoBeneficiariosDiagnostico vinculos = vinculoService.CriarDiagnostico(faturasPassado, over);

                var consolidacaoService = new LancamentosConsolidacaoService();
                LancamentosConsolidacaoDiagnostico consolidacao = consolidacaoService.CriarDiagnostico(
                    faturasPassado,
                    over,
                    ignorarCoparticipacao,
                    ignorarCompetenciasAnteriores);

                var comparacaoService = new ComparacaoPrincipalService();
                ComparacaoPrincipalDiagnostico comparacao = comparacaoService.Comparar(
                    faturasPassado,
                    over,
                    ignorarCoparticipacao,
                    ignorarCompetenciasAnteriores);

                var contextoService = new ContextoTemporalService();
                ContextoTemporalDiagnostico contexto;

                if (contextoDisponivel)
                {
                    var faturasAtual = _faturasMesAtual.Select(parserFatura.Ler).ToList();
                    var faturasSeguinte = _faturasMesSeguinte.Select(parserFatura.Ler).ToList();
                    contexto = contextoService.Analisar(comparacao, faturasAtual, faturasSeguinte);
                }
                else
                {
                    contexto = contextoService.CriarIndisponivel(
                        comparacao,
                        "Selecione e valide também as faturas do mês atual e do mês que vem para habilitar o contexto temporal da Etapa 8.");
                }

                return (vinculos, consolidacao, comparacao, contexto);
            });

            DefinirValidando(false);

            var janela = new VinculoBeneficiariosDiagnosticoWindow(
                leituraDiagnostico.vinculos,
                leituraDiagnostico.consolidacao,
                leituraDiagnostico.comparacao,
                leituraDiagnostico.contexto)
            {
                Owner = this
            };
            janela.ShowDialog();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError(
                "Não foi possível executar o diagnóstico Fatura × Over.\n\n" + ex.Message,
                "Erro no diagnóstico da análise");
        }
        finally
        {
            if (IsVisible)
                DefinirValidando(false);
            else
                Mouse.OverrideCursor = null;
        }
    }

    private Task<AnaliseFinalDiagnostico> GerarResultadoFinalAsync(
        bool ignorarCoparticipacao,
        bool ignorarCompetenciasAnteriores)
    {
        return Task.Run(() =>
        {
            var parserFatura = new FaturaBradescoParser();
            List<FaturaBradescoArquivo> faturasPassado = _faturasMesPassado.Select(parserFatura.Ler).ToList();
            List<FaturaBradescoArquivo> faturasAtual = _faturasMesAtual.Select(parserFatura.Ler).ToList();
            List<FaturaBradescoArquivo> faturasSeguinte = _faturasMesSeguinte.Select(parserFatura.Ler).ToList();

            var parserOver = new OverParser();
            OverArquivo over = parserOver.Ler(_relatorioOver!);

            var consolidacaoService = new LancamentosConsolidacaoService();
            LancamentosConsolidacaoDiagnostico consolidacao = consolidacaoService.CriarDiagnostico(
                faturasPassado,
                over,
                ignorarCoparticipacao,
                ignorarCompetenciasAnteriores);

            var comparacaoService = new ComparacaoPrincipalService();
            ComparacaoPrincipalDiagnostico comparacao = comparacaoService.Comparar(
                faturasPassado,
                over,
                ignorarCoparticipacao,
                ignorarCompetenciasAnteriores);

            var contextoService = new ContextoTemporalService();
            ContextoTemporalDiagnostico contexto = contextoService.Analisar(
                comparacao,
                faturasAtual,
                faturasSeguinte);

            var finalService = new AnaliseFinalService();
            return finalService.Gerar(
                comparacao,
                consolidacao,
                contexto,
                faturasPassado,
                over,
                ignorarCoparticipacao,
                ignorarCompetenciasAnteriores);
        });
    }

    private async void TestarLeituraPassado_Click(object sender, RoutedEventArgs e)
        => await TestarLeituraFaturasAsync("Leitura — faturas mês passado", _faturasMesPassado);

    private async void TestarLeituraAtual_Click(object sender, RoutedEventArgs e)
        => await TestarLeituraFaturasAsync("Leitura — faturas mês atual", _faturasMesAtual);

    private async void TestarLeituraSeguinte_Click(object sender, RoutedEventArgs e)
        => await TestarLeituraFaturasAsync("Leitura — faturas mês que vem", _faturasMesSeguinte);

    private async Task TestarLeituraFaturasAsync(
        string titulo,
        IReadOnlyList<string> arquivos)
    {
        if (_validando || arquivos.Count == 0)
            return;

        try
        {
            DefinirValidando(true, "Lendo beneficiários e lançamentos das faturas...");

            var leitura = await Task.Run(() =>
            {
                var resultados = new List<FaturaBradescoArquivo>();
                var erros = new List<string>();
                var parser = new FaturaBradescoParser();

                foreach (string arquivo in arquivos)
                {
                    try
                    {
                        resultados.Add(parser.Ler(arquivo));
                    }
                    catch (Exception ex)
                    {
                        erros.Add($"• {Path.GetFileName(arquivo)}: {ex.Message}");
                    }
                }

                return (resultados, erros);
            });

            DefinirValidando(false);

            if (leitura.erros.Count > 0)
            {
                string detalhes = string.Join("\n", leitura.erros.Take(6));
                if (leitura.erros.Count > 6)
                    detalhes += $"\n• ... e mais {leitura.erros.Count - 6} arquivo(s).";

                CustomMessageBox.ShowWarning(
                    "O leitor estruturado encontrou problema em um ou mais PDFs.\n\n" + detalhes,
                    "Diagnóstico da leitura");
            }

            if (leitura.resultados.Count == 0)
                return;

            var janela = new LeituraFaturasDiagnosticoWindow(titulo, leitura.resultados)
            {
                Owner = this
            };
            janela.ShowDialog();
        }
        catch (Exception ex)
        {
            CustomMessageBox.ShowError(
                "Não foi possível executar o diagnóstico da leitura das faturas.\n\n" + ex.Message,
                "Erro no leitor de faturas");
        }
        finally
        {
            if (IsVisible)
                DefinirValidando(false);
            else
                Mouse.OverrideCursor = null;
        }
    }

    private async void Feito_Click(object sender, RoutedEventArgs e)
    {
        if (!BtnFeito.IsEnabled || _validando)
            return;

        try
        {
            ResultadoFinalGerado = null;
            ContextoHistoricoGerado = null;

            DefinirValidando(true, "Revalidando arquivos antes de concluir...");

            var revalidacao = await Task.Run(() =>
            {
                GrupoFaturasValidacao passado = _validator.ValidarGrupoFaturas(_faturasMesPassado);
                GrupoFaturasValidacao atual = _validator.ValidarGrupoFaturas(_faturasMesAtual);
                GrupoFaturasValidacao seguinte = _validator.ValidarGrupoFaturas(_faturasMesSeguinte);
                OverValidacao over = _validator.ValidarOver(_relatorioOver);
                PreparacaoAnaliseValidacao sequencia = _validator.ValidarSequencia(passado, atual, seguinte, over);

                return (passado, atual, seguinte, over, sequencia);
            });

            if (!revalidacao.passado.Valido)
                throw new InvalidOperationException(revalidacao.passado.Mensagem);
            if (!revalidacao.atual.Valido)
                throw new InvalidOperationException(revalidacao.atual.Mensagem);
            if (!revalidacao.seguinte.Valido)
                throw new InvalidOperationException(revalidacao.seguinte.Mensagem);
            if (!revalidacao.over.Valido)
                throw new InvalidOperationException(revalidacao.over.Mensagem);
            if (!revalidacao.sequencia.Valido)
                throw new InvalidOperationException(revalidacao.sequencia.Mensagem);

            _validacaoPassado = revalidacao.passado;
            _validacaoAtual = revalidacao.atual;
            _validacaoSeguinte = revalidacao.seguinte;
            _validacaoOver = revalidacao.over;

            bool ignorarCoparticipacao = IgnorarCoparticipacaoCheckBox.IsChecked != false;
            bool ignorarCompetenciasAnteriores = IgnorarCompetenciasAnterioresCheckBox.IsChecked != false;

            // A preparação continua sendo uma área temporária e transacional.
            DefinirValidando(true, "Copiando e conferindo a preparação...");
            await Task.Run(SalvarPreparacaoTransacional);

            // Depois de salvar a preparação, o mesmo clique em Feito gera o relatório final.
            DefinirValidando(true, "Gerando o relatório final...");
            AnaliseFinalDiagnostico resultadoFinal = await GerarResultadoFinalAsync(
                ignorarCoparticipacao,
                ignorarCompetenciasAnteriores);

            AnaliseFaturasHistoricoContextoCriacao contextoHistorico =
                AnaliseFaturasHistoricoContextoCriacao.Criar(
                    _caminhoBaseData,
                    _faturasMesPassado,
                    _faturasMesAtual,
                    _faturasMesSeguinte,
                    _relatorioOver!,
                    ignorarCoparticipacao,
                    ignorarCompetenciasAnteriores,
                    DateTime.Now);

            // O histórico deixou de depender de uma ação manual na tela de resultado.
            // O mesmo clique em Feito conclui, persiste (substituindo a mesma competência)
            // e só então entrega o resultado para a MainWindow abrir.
            DefinirValidando(true, "Salvando análise no histórico...");
            AnaliseFaturasHistoricoSnapshot snapshot = contextoHistorico.CriarSnapshot(resultadoFinal);
            await Task.Run(() =>
            {
                var historicoService = new AnaliseFaturasHistoricoService(_caminhoBaseData);
                historicoService.Salvar(snapshot);
            });

            ResultadoFinalGerado = resultadoFinal;
            ContextoHistoricoGerado = contextoHistorico;

            // A MainWindow abrirá o resultado imediatamente após esta janela fechar.
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ResultadoFinalGerado = null;
            ContextoHistoricoGerado = null;

            CustomMessageBox.ShowError(
                "Não foi possível concluir a análise. Se a falha ocorreu antes da substituição, a preparação anterior foi preservada; se ocorreu durante a geração do relatório, a nova preparação pode já ter sido salva.\n\n" +
                $"Detalhes: {ex.Message}",
                "Erro ao concluir análise");
        }
        finally
        {
            if (IsVisible)
                DefinirValidando(false);
            else
                Mouse.OverrideCursor = null;
        }
    }

    private void SalvarPreparacaoTransacional()
    {
        ValidarExistenciaDasFontes();

        string pastaRelatorios = Path.Combine(
            _caminhoBaseData,
            "Relatórios de Analise");

        string pastaAnalise = Path.Combine(pastaRelatorios, "Analise de Faturas");
        string id = Guid.NewGuid().ToString("N");
        string pastaTemporaria = Path.Combine(pastaRelatorios, $"Analise de Faturas.__staging_{id}");
        string pastaBackup = Path.Combine(pastaRelatorios, $"Analise de Faturas.__backup_{id}");

        Directory.CreateDirectory(pastaRelatorios);

        bool backupCriado = false;
        bool novaAtivada = false;

        try
        {
            Directory.CreateDirectory(pastaTemporaria);

            string tempPassado = Path.Combine(pastaTemporaria, "Faturas mês passado");
            string tempAtual = Path.Combine(pastaTemporaria, "Faturas mês atual");
            string tempSeguinte = Path.Combine(pastaTemporaria, "Faturas mês que vem");

            Directory.CreateDirectory(tempPassado);
            Directory.CreateDirectory(tempAtual);
            Directory.CreateDirectory(tempSeguinte);

            CopiarArquivosConferindo(_faturasMesPassado, tempPassado);
            CopiarArquivosConferindo(_faturasMesAtual, tempAtual);
            CopiarArquivosConferindo(_faturasMesSeguinte, tempSeguinte);

            if (string.IsNullOrWhiteSpace(_relatorioOver))
                throw new FileNotFoundException("O relatório Over não está selecionado.");

            string destinoOverTemporario = Path.Combine(pastaTemporaria, Path.GetFileName(_relatorioOver));
            CopiarArquivoConferindo(_relatorioOver, destinoOverTemporario);

            ConferirPreparacaoTemporaria(
                tempPassado,
                tempAtual,
                tempSeguinte,
                destinoOverTemporario);

            if (Directory.Exists(pastaAnalise))
            {
                Directory.Move(pastaAnalise, pastaBackup);
                backupCriado = true;
            }

            try
            {
                Directory.Move(pastaTemporaria, pastaAnalise);
                novaAtivada = true;
            }
            catch
            {
                if (backupCriado && !Directory.Exists(pastaAnalise) && Directory.Exists(pastaBackup))
                {
                    Directory.Move(pastaBackup, pastaAnalise);
                    backupCriado = false;
                }

                throw;
            }

            if (backupCriado && Directory.Exists(pastaBackup))
            {
                try
                {
                    Directory.Delete(pastaBackup, recursive: true);
                    backupCriado = false;
                }
                catch
                {
                    // A nova preparação já está ativa. Um eventual backup residual
                    // não pode invalidar a preparação recém-concluída.
                }
            }
        }
        finally
        {
            if (!novaAtivada && Directory.Exists(pastaTemporaria))
            {
                try { Directory.Delete(pastaTemporaria, recursive: true); }
                catch { }
            }

            if (!novaAtivada && backupCriado && !Directory.Exists(pastaAnalise) && Directory.Exists(pastaBackup))
            {
                try { Directory.Move(pastaBackup, pastaAnalise); }
                catch { }
            }
        }
    }

    private void ValidarExistenciaDasFontes()
    {
        ValidarArquivosSelecionados(_faturasMesPassado, "faturas do mês passado");
        ValidarArquivosSelecionados(_faturasMesAtual, "faturas do mês atual");
        ValidarArquivosSelecionados(_faturasMesSeguinte, "faturas do mês que vem");

        if (string.IsNullOrWhiteSpace(_relatorioOver) || !File.Exists(_relatorioOver))
            throw new FileNotFoundException("O relatório Over selecionado não foi encontrado.");
    }

    private static void ValidarArquivosSelecionados(IEnumerable<string> arquivos, string descricao)
    {
        foreach (string arquivo in arquivos)
        {
            if (!File.Exists(arquivo))
                throw new FileNotFoundException($"Um dos arquivos de {descricao} não foi encontrado: {arquivo}");
        }
    }

    private static void CopiarArquivosConferindo(IEnumerable<string> arquivos, string pastaDestino)
    {
        foreach (string origem in arquivos)
        {
            string destino = Path.Combine(pastaDestino, Path.GetFileName(origem));
            CopiarArquivoConferindo(origem, destino);
        }
    }

    private static void CopiarArquivoConferindo(string origem, string destino)
    {
        if (!File.Exists(origem))
            throw new FileNotFoundException($"Arquivo não encontrado: {origem}");

        string origemCompleta = Path.GetFullPath(origem);
        string destinoCompleto = Path.GetFullPath(destino);

        if (string.Equals(origemCompleta, destinoCompleto, StringComparison.OrdinalIgnoreCase))
            return;

        File.Copy(origemCompleta, destinoCompleto, overwrite: true);

        var infoOrigem = new FileInfo(origemCompleta);
        var infoDestino = new FileInfo(destinoCompleto);

        if (!infoDestino.Exists || infoOrigem.Length != infoDestino.Length)
        {
            throw new IOException(
                $"A cópia do arquivo não pôde ser confirmada: {Path.GetFileName(origem)}");
        }
    }

    private void ConferirPreparacaoTemporaria(
        string pastaPassado,
        string pastaAtual,
        string pastaSeguinte,
        string arquivoOver)
    {
        ConferirQuantidade(pastaPassado, _faturasMesPassado.Count, "faturas do mês passado");
        ConferirQuantidade(pastaAtual, _faturasMesAtual.Count, "faturas do mês atual");
        ConferirQuantidade(pastaSeguinte, _faturasMesSeguinte.Count, "faturas do mês que vem");

        if (!File.Exists(arquivoOver))
            throw new IOException("O arquivo Over não foi confirmado na preparação temporária.");
    }

    private static void ConferirQuantidade(string pasta, int esperado, string descricao)
    {
        int encontrado = Directory.Exists(pasta)
            ? Directory.EnumerateFiles(pasta).Count()
            : 0;

        if (encontrado != esperado)
        {
            throw new IOException(
                $"A preparação de {descricao} ficou incompleta. Esperado: {esperado}; encontrado: {encontrado}.");
        }
    }
}
