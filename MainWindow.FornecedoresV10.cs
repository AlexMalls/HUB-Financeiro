using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace HubFinanceiro;

public partial class MainWindow
{
    private bool _fornecedoresV10Configurado;
    private bool _identidadesFornecedoresV10Carregadas;
    private List<FornecedorIdentidadeRegistro> _identidadesFornecedoresV10 = new();
    private Dictionary<Fornecedor, int> _idsFornecedoresV10 = new();

    private void ConfigurarFornecedoresV10()
    {
        if (_fornecedoresV10Configurado)
            return;

        _fornecedoresV10Configurado = true;

        // O código do fornecedor passa a ser opcional. O handler antigo exigia código.
        BtnCadastrarFornecedor.Click -= BtnCadastrarFornecedor_Click;
        BtnCadastrarFornecedor.Click += BtnCadastrarFornecedorV10_Click;

        FornecedorNomeTextBox.TextChanged += FornecedorCadastroV10_TextChanged;
        FornecedorCodigoTextBox.TextChanged += FornecedorCadastroV10_TextChanged;

        // A exclusão antiga localizava pelo código e poderia remover vários códigos 0.
        FornecedoresLayoutGrid.KeyDown -= FornecedoresLayoutGrid_KeyDown;
        FornecedoresLayoutGrid.KeyDown += FornecedoresLayoutGridV10_KeyDown;

        // Depois do clique original preencher os campos, garantimos que código 0 continue invisível.
        FornecedoresItemsControl.PreviewMouseLeftButtonDown += FornecedoresItemsControlV10_PreviewMouseLeftButtonDown;

        FornecedoresItemsControl.ItemContainerGenerator.StatusChanged += FornecedoresV10_ItemContainerGenerator_StatusChanged;
        ValidarBotaoCadastroV10();
    }

    private void FornecedoresV10_ItemContainerGenerator_StatusChanged(object? sender, EventArgs e)
    {
        if (FornecedoresItemsControl.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
            return;

        if (_todosFornecedores.Count == 0)
            return;

        try
        {
            GarantirIdentidadesFornecedoresV10(_todosFornecedores);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[V10] Não foi possível reconciliar IDs internos: {ex.Message}");
        }
    }

    private void FornecedoresItemsControlV10_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_fornecedorItemSelecionado?.DataContext is Fornecedor fornecedor && fornecedor.Codigo <= 0)
                FornecedorCodigoTextBox.Text = string.Empty;
        }), DispatcherPriority.Input);
    }

    private void FornecedorCadastroV10_TextChanged(object sender, TextChangedEventArgs e)
        => ValidarBotaoCadastroV10();

    private void ValidarBotaoCadastroV10()
    {
        bool nomePreenchido = !string.IsNullOrWhiteSpace(FornecedorNomeTextBox.Text);
        string codigoTexto = FornecedorCodigoTextBox.Text.Trim();
        bool codigoValido = string.IsNullOrWhiteSpace(codigoTexto)
            || (int.TryParse(codigoTexto, out int codigo) && codigo > 0);

        bool habilitado = nomePreenchido && codigoValido;
        BtnCadastrarFornecedor.IsEnabled = habilitado;
        BtnCadastrarFornecedor.Opacity = habilitado ? 1.0 : 0.4;
    }

    private void BtnCadastrarFornecedorV10_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FornecedorNomeTextBox.Text))
            {
                MostrarAviso("Informe o nome do fornecedor.");
                return;
            }

            int codigo = 0;
            string codigoTexto = FornecedorCodigoTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(codigoTexto))
            {
                if (!int.TryParse(codigoTexto, out codigo) || codigo <= 0)
                {
                    MostrarAviso("O código deve ser um número válido ou pode ficar em branco.");
                    return;
                }
            }

            int natureza = 0;
            if (!string.IsNullOrWhiteSpace(FornecedorNaturezaTextBox.Text)
                && !int.TryParse(FornecedorNaturezaTextBox.Text, out natureza))
            {
                MostrarAviso("A natureza deve ser um número válido.");
                return;
            }

            int diaPagamento = 0;
            if (!string.IsNullOrWhiteSpace(FornecedorDiaPagamentoTextBox.Text))
            {
                if (!int.TryParse(FornecedorDiaPagamentoTextBox.Text, out diaPagamento)
                    || diaPagamento < 1 || diaPagamento > 31)
                {
                    MostrarAviso("O dia de pagamento deve estar entre 1 e 31.");
                    return;
                }
            }

            int tipoPagamento = 0;
            if (!string.IsNullOrWhiteSpace(FornecedorTipoPagamentoTextBox.Text))
            {
                if (!int.TryParse(FornecedorTipoPagamentoTextBox.Text, out tipoPagamento)
                    || tipoPagamento < 1 || tipoPagamento > 3)
                {
                    MostrarAviso("O tipo de pagamento deve estar entre 01 e 03.");
                    return;
                }
            }

            var novosDados = new Fornecedor
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

            string caminhoArquivo = ObterCaminhoArquivoFornecedores();
            var fornecedores = CarregarFornecedoresDiretoV10(caminhoArquivo);
            GarantirIdentidadesFornecedoresV10(_todosFornecedores);

            if (_fornecedorItemSelecionado?.DataContext is Fornecedor fornecedorExistente)
            {
                if (!_idsFornecedoresV10.TryGetValue(fornecedorExistente, out int idInterno))
                    throw new InvalidOperationException("Não foi possível identificar internamente o fornecedor selecionado.");

                var identidade = _identidadesFornecedoresV10.FirstOrDefault(r => r.IdInterno == idInterno)
                    ?? throw new InvalidOperationException("O vínculo interno do fornecedor selecionado não foi encontrado.");

                var fornecedorArquivo = FornecedorIdentidadeService.LocalizarFornecedor(fornecedores, identidade)
                    ?? throw new InvalidOperationException("O fornecedor selecionado não foi localizado no arquivo de dados.");

                if (codigo > 0 && fornecedores.Any(f => !ReferenceEquals(f, fornecedorArquivo) && f.Codigo == codigo))
                {
                    MostrarAviso("Já existe um fornecedor com este código.");
                    return;
                }

                CopiarDadosFornecedorV10(novosDados, fornecedorArquivo);
                identidade.AtualizarSnapshot(fornecedorArquivo);
            }
            else
            {
                if (codigo > 0 && fornecedores.Any(f => f.Codigo == codigo))
                {
                    MostrarAviso("Já existe um fornecedor com este código.");
                    return;
                }

                fornecedores.Add(novosDados);

                int idInterno = FornecedorIdentidadeService.GerarIdInterno(
                    _identidadesFornecedoresV10,
                    fornecedores);

                var identidade = new FornecedorIdentidadeRegistro { IdInterno = idInterno };
                identidade.AtualizarSnapshot(novosDados);
                _identidadesFornecedoresV10.Add(identidade);
            }

            SalvarFornecedores(fornecedores, caminhoArquivo);
            SalvarIdentidadesFornecedoresV10();

            if (_fornecedorItemSelecionado != null)
            {
                AnimarSelecaoFornecedor(_fornecedorItemSelecionado, false);
                _fornecedorItemSelecionado = null;
            }

            LimparCamposFornecedor();
            RecarregarListaFornecedores();
            GarantirIdentidadesFornecedoresV10(_todosFornecedores);

            MostrarSucesso("Fornecedor salvo com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao salvar fornecedor", ex);
        }
    }

    private void FornecedoresLayoutGridV10_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _fornecedorItemSelecionado != null)
        {
            AnimarSelecaoFornecedor(_fornecedorItemSelecionado, false);
            _fornecedorItemSelecionado = null;
            LimparCamposFornecedor();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete || _fornecedorItemSelecionado?.DataContext is not Fornecedor fornecedor)
            return;

        e.Handled = true;
        ExcluirFornecedorV10(fornecedor);
    }

    private void ExcluirFornecedorV10(Fornecedor fornecedor)
    {
        bool confirmado = MostrarPergunta(
            "Você deseja excluir o fornecedor:",
            "Excluir Fornecedor",
            fornecedor.Nome);

        if (!confirmado)
            return;

        try
        {
            GarantirIdentidadesFornecedoresV10(_todosFornecedores);

            if (!_idsFornecedoresV10.TryGetValue(fornecedor, out int idInterno))
                throw new InvalidOperationException("Não foi possível identificar internamente o fornecedor selecionado.");

            var identidade = _identidadesFornecedoresV10.FirstOrDefault(r => r.IdInterno == idInterno)
                ?? throw new InvalidOperationException("O vínculo interno do fornecedor selecionado não foi encontrado.");

            string caminhoArquivo = ObterCaminhoArquivoFornecedores();
            var fornecedores = CarregarFornecedoresDiretoV10(caminhoArquivo);
            var fornecedorArquivo = FornecedorIdentidadeService.LocalizarFornecedor(fornecedores, identidade)
                ?? throw new InvalidOperationException("O fornecedor selecionado não foi localizado no arquivo de dados.");

            fornecedores.Remove(fornecedorArquivo);
            _identidadesFornecedoresV10.RemoveAll(r => r.IdInterno == idInterno);

            SalvarFornecedores(fornecedores, caminhoArquivo);
            SalvarIdentidadesFornecedoresV10();

            if (_fornecedorItemSelecionado != null)
                AnimarSelecaoFornecedor(_fornecedorItemSelecionado, false);

            _fornecedorItemSelecionado = null;
            LimparCamposFornecedor();
            RecarregarListaFornecedores();
            GarantirIdentidadesFornecedoresV10(_todosFornecedores);

            MostrarSucesso("Fornecedor excluído com sucesso!");
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao excluir fornecedor", ex);
        }
    }

    private void GarantirIdentidadesFornecedoresV10(IReadOnlyList<Fornecedor> fornecedores)
    {
        if (!_identidadesFornecedoresV10Carregadas)
        {
            _identidadesFornecedoresV10 = CarregarIdentidadesFornecedoresV10();
            _identidadesFornecedoresV10Carregadas = true;
        }

        _idsFornecedoresV10 = FornecedorIdentidadeService.Reconciliar(
            fornecedores,
            _identidadesFornecedoresV10,
            out bool alterado);

        if (alterado)
            SalvarIdentidadesFornecedoresV10();
    }

    private List<FornecedorIdentidadeRegistro> CarregarIdentidadesFornecedoresV10()
    {
        string caminho = ObterCaminhoIdentidadesFornecedoresV10();
        if (!File.Exists(caminho))
            return new List<FornecedorIdentidadeRegistro>();

        try
        {
            string json = File.ReadAllText(caminho);
            return JsonSerializer.Deserialize<List<FornecedorIdentidadeRegistro>>(json)
                ?? new List<FornecedorIdentidadeRegistro>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[V10] Erro ao ler IDs internos de fornecedores: {ex.Message}");
            return new List<FornecedorIdentidadeRegistro>();
        }
    }

    private void SalvarIdentidadesFornecedoresV10()
    {
        string caminho = ObterCaminhoIdentidadesFornecedoresV10();
        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrWhiteSpace(pasta))
            Directory.CreateDirectory(pasta);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        File.WriteAllText(caminho, JsonSerializer.Serialize(_identidadesFornecedoresV10, options));
    }

    private string ObterCaminhoIdentidadesFornecedoresV10()
        => Path.Combine(ObterCaminhoBase(), "fornecedores_ids_internos.json");

    private static List<Fornecedor> CarregarFornecedoresDiretoV10(string caminhoArquivo)
    {
        if (!File.Exists(caminhoArquivo))
            return new List<Fornecedor>();

        string json = File.ReadAllText(caminhoArquivo);
        return JsonSerializer.Deserialize<List<Fornecedor>>(json) ?? new List<Fornecedor>();
    }

    private static void CopiarDadosFornecedorV10(Fornecedor origem, Fornecedor destino)
    {
        destino.Nome = origem.Nome;
        destino.Codigo = origem.Codigo;
        destino.Natureza = origem.Natureza;
        destino.Email = origem.Email;
        destino.DiaPagamento = origem.DiaPagamento;
        destino.TipoPagamento = origem.TipoPagamento;
        destino.Ativo = origem.Ativo;
        destino.Administradora = origem.Administradora;
        destino.Corretora = origem.Corretora;
    }
}
