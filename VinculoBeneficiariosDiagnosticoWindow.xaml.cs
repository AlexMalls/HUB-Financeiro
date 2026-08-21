using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class VinculoBeneficiariosDiagnosticoWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly VinculoBeneficiariosDiagnostico _diagnostico;
    private readonly LancamentosConsolidacaoDiagnostico _consolidacao;
    private readonly List<LinhaVinculoDiagnostico> _linhas;
    private readonly List<LinhaComposicaoDiagnostico> _composicoes;
    private readonly ComparacaoPrincipalDiagnostico _comparacao;
    private readonly ContextoTemporalDiagnostico _contexto;
    private readonly List<LinhaComparacaoDiagnostico> _comparacoes;
    private readonly List<LinhaContextoDiagnostico> _contextos;

    public VinculoBeneficiariosDiagnosticoWindow(VinculoBeneficiariosDiagnostico diagnostico)
        : this(
            diagnostico,
            new LancamentosConsolidacaoDiagnostico(),
            new ComparacaoPrincipalDiagnostico(),
            new ContextoTemporalDiagnostico())
    {
    }

    public VinculoBeneficiariosDiagnosticoWindow(
        VinculoBeneficiariosDiagnostico diagnostico,
        LancamentosConsolidacaoDiagnostico consolidacao)
        : this(
            diagnostico,
            consolidacao,
            new ComparacaoPrincipalDiagnostico(),
            new ContextoTemporalDiagnostico())
    {
    }

    public VinculoBeneficiariosDiagnosticoWindow(
        VinculoBeneficiariosDiagnostico diagnostico,
        LancamentosConsolidacaoDiagnostico consolidacao,
        ComparacaoPrincipalDiagnostico comparacao,
        ContextoTemporalDiagnostico contexto)
    {
        InitializeComponent();

        _diagnostico = diagnostico ?? throw new ArgumentNullException(nameof(diagnostico));
        _consolidacao = consolidacao ?? throw new ArgumentNullException(nameof(consolidacao));
        _comparacao = comparacao ?? throw new ArgumentNullException(nameof(comparacao));
        _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
        _linhas = diagnostico.Resultados.Select(CriarLinha).ToList();
        _composicoes = consolidacao.Composicoes.Select(CriarLinhaComposicao).ToList();
        _comparacoes = comparacao.Resultados.Select(CriarLinhaComparacao).ToList();
        _contextos = contexto.Resultados.Select(CriarLinhaContexto).ToList();

        DirecaoComboBox.ItemsSource = new[]
        {
            "Todas as direções",
            "Over → Fatura",
            "Fatura → Over"
        };
        DirecaoComboBox.SelectedIndex = 0;

        CategoriaComparacaoComboBox.ItemsSource = new[]
        {
            "Todas as categorias",
            "Valor compatível",
            "Valor maior na fatura",
            "Valor maior no Over",
            "Não encontrado na fatura",
            "Não encontrado no Over",
            "Ambíguo"
        };
        CategoriaComparacaoComboBox.SelectedIndex = 0;

        ContextoStatusComboBox.ItemsSource = new[]
        {
            "Todos os resultados",
            "Explicadas pelo contexto",
            "Divergências que permanecem",
            "Sem contexto",
            "Não aplicável"
        };
        ContextoStatusComboBox.SelectedIndex = 0;

        AtualizarResumo();
        AplicarFiltro();
        AplicarFiltroComposicao();
        AplicarFiltroComparacao();
        AplicarFiltroContexto();
        LimparDetalheComposicao();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void PesquisaTextBox_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();
    private void DirecaoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => AplicarFiltro();
    private void PesquisaComposicaoTextBox_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltroComposicao();
    private void PesquisaComparacaoTextBox_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltroComparacao();
    private void CategoriaComparacaoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => AplicarFiltroComparacao();
    private void PesquisaContextoTextBox_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltroContexto();
    private void ContextoStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => AplicarFiltroContexto();

    private void AtualizarResumo()
    {
        int overUnicos = _diagnostico.Resultados.Count(x =>
            x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura &&
            x.Status == VinculoBeneficiarioStatus.EncontradoUnico);
        int overNome = _diagnostico.Resultados.Count(x =>
            x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura &&
            x.Status == VinculoBeneficiarioStatus.EncontradoPorNome);
        int overAmb = _diagnostico.Resultados.Count(x =>
            x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura &&
            x.Status == VinculoBeneficiarioStatus.Ambiguo);
        int overNao = _diagnostico.Resultados.Count(x =>
            x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura &&
            x.Status == VinculoBeneficiarioStatus.NaoEncontrado);

        ResumoText.Text =
            $"✓ {_diagnostico.TotalOverOcorrencias:N0} ocorrência(s) de beneficiário no Over  •  " +
            $"{_diagnostico.TotalFaturaOcorrencias:N0} ocorrência(s) nas faturas do mês passado";

        DetalhesText.Text =
            $"Over → Fatura: {overUnicos:N0} certificado único  •  {overNome:N0} por nome  •  " +
            $"{overAmb:N0} ambíguo(s)  •  {overNao:N0} não encontrado(s).  " +
            $"Composição disponível para {_consolidacao.TotalVinculosResolvidos:N0} vínculo(s) seguro(s).";

        IReadOnlyList<VinculoTesteResultado> testes = BeneficiarioVinculoServiceTestes.Executar();
        int ok = testes.Count(x => x.Sucesso);
        TestesText.Text = ok == testes.Count
            ? $"✓ vínculo: {ok}/{testes.Count} teste(s) interno(s)"
            : $"⚠ vínculo: {ok}/{testes.Count} teste(s). " +
              string.Join(" | ", testes.Where(x => !x.Sucesso).Take(3).Select(x => $"{x.Nome}: esperado {x.Esperado}, obtido {x.Obtido}"));

        IReadOnlyList<ConsolidacaoTesteResultado> testesConsolidacao = LancamentosConsolidacaoServiceTestes.Executar();
        int okConsolidacao = testesConsolidacao.Count(x => x.Sucesso);
        ConsolidacaoTestesText.Text = okConsolidacao == testesConsolidacao.Count
            ? $"✓ composição: {okConsolidacao}/{testesConsolidacao.Count} teste(s) interno(s) • IOF fora; copart conforme opção"
            : $"⚠ composição: {okConsolidacao}/{testesConsolidacao.Count} teste(s). " +
              string.Join(" | ", testesConsolidacao.Where(x => !x.Sucesso).Take(3).Select(x => $"{x.Nome}: {x.Detalhe}"));

        ComparacaoResumoText.Text = _comparacao.Resultados.Count == 0
            ? "Comparação principal ainda não disponível."
            : $"Competência {_comparacao.CompetenciaAnalisada:MM/yyyy} • {_comparacao.TotalCompativeis:N0} compatível(is) • {_comparacao.TotalDivergencias:N0} divergência(s).";

        IReadOnlyList<ComparacaoPrincipalTesteResultado> testesComparacao = ComparacaoPrincipalServiceTestes.Executar();
        int okComparacao = testesComparacao.Count(x => x.Sucesso);
        ComparacaoTestesText.Text = okComparacao == testesComparacao.Count
            ? $"✓ comparação principal: {okComparacao}/{testesComparacao.Count} teste(s) interno(s) • tolerância ±R$ 0,30 • sem Excel legado"
            : $"⚠ comparação principal: {okComparacao}/{testesComparacao.Count} teste(s). " +
              string.Join(" | ", testesComparacao.Where(x => !x.Sucesso).Take(3).Select(x => $"{x.Nome}: {x.Detalhe}"));

        if (_contexto.Disponivel)
        {
            ContextoResumoText.Text = $"{_contexto.TotalExplicadas:N0} divergência(s) explicada(s) pelo contexto • {_contexto.TotalPermanecem:N0} permanecem.";
            ContextoDetalhesText.Text = _contexto.Mensagem;
        }
        else
        {
            ContextoResumoText.Text = "Contexto temporal não disponível.";
            ContextoDetalhesText.Text = _contexto.Mensagem;
        }

        IReadOnlyList<ContextoTemporalTesteResultado> testesContexto = ContextoTemporalServiceTestes.Executar();
        int okContexto = testesContexto.Count(x => x.Sucesso);
        ContextoTestesText.Text = okContexto == testesContexto.Count
            ? $"✓ contexto temporal: {okContexto}/{testesContexto.Count} teste(s) interno(s) • meses seguintes nunca alteram os totais principais"
            : $"⚠ contexto temporal: {okContexto}/{testesContexto.Count} teste(s). " +
              string.Join(" | ", testesContexto.Where(x => !x.Sucesso).Take(3).Select(x => $"{x.Nome}: {x.Detalhe}"));
    }

    private void AplicarFiltro()
    {
        if (!IsInitialized || VinculosDataGrid == null || PesquisaTextBox == null || DirecaoComboBox == null)
            return;

        IEnumerable<LinhaVinculoDiagnostico> consulta = _linhas;

        if (DirecaoComboBox.SelectedIndex == 1)
            consulta = consulta.Where(x => x.Direcao == "Over → Fatura");
        else if (DirecaoComboBox.SelectedIndex == 2)
            consulta = consulta.Where(x => x.Direcao == "Fatura → Over");

        string pesquisa = AnaliseFaturasNormalizador.NormalizarNome(PesquisaTextBox.Text);
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            string termo = CompactarPesquisa(pesquisa);
            consulta = consulta.Where(x =>
                CompactarPesquisa(x.Certificado).Contains(termo, StringComparison.Ordinal) ||
                CompactarPesquisa(x.NomeOrigem).Contains(termo, StringComparison.Ordinal) ||
                CompactarPesquisa(x.NomeDestino).Contains(termo, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Status).Contains(termo, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Origem).Contains(termo, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Destino).Contains(termo, StringComparison.Ordinal));
        }

        List<LinhaVinculoDiagnostico> resultado = consulta.ToList();
        VinculosDataGrid.ItemsSource = resultado;
        FiltroResultadoText.Text = resultado.Count == 1 ? "1 vínculo" : $"{resultado.Count:N0} vínculos";
        AtualizarBotaoComposicao();
    }

    private void AplicarFiltroComposicao()
    {
        if (!IsInitialized || ComposicoesDataGrid == null || PesquisaComposicaoTextBox == null)
            return;

        IEnumerable<LinhaComposicaoDiagnostico> consulta = _composicoes;
        string pesquisa = CompactarPesquisa(PesquisaComposicaoTextBox.Text);

        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            consulta = consulta.Where(x =>
                CompactarPesquisa(x.Certificado).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Nome).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.NomeOver).Contains(pesquisa, StringComparison.Ordinal));
        }

        List<LinhaComposicaoDiagnostico> resultado = consulta.ToList();
        ComposicoesDataGrid.ItemsSource = resultado;
        ComposicaoFiltroResultadoText.Text = resultado.Count == 1
            ? "1 beneficiário vinculado"
            : $"{resultado.Count:N0} beneficiários vinculados";

        LinhaComposicaoDiagnostico? selecionada = ObterComposicaoSelecionada();
        if (resultado.Count > 0 && (selecionada == null || !resultado.Contains(selecionada)))
            SelecionarLinhaComposicao(resultado[0]);
        else if (resultado.Count == 0)
            LimparDetalheComposicao();
    }

    private void AplicarFiltroComparacao()
    {
        if (!IsInitialized || ComparacaoDataGrid == null || PesquisaComparacaoTextBox == null || CategoriaComparacaoComboBox == null)
            return;

        IEnumerable<LinhaComparacaoDiagnostico> consulta = _comparacoes;
        string? categoria = CategoriaComparacaoComboBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(categoria) && categoria != "Todas as categorias")
            consulta = consulta.Where(x => x.Categoria == categoria);

        string pesquisa = CompactarPesquisa(PesquisaComparacaoTextBox.Text);
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            consulta = consulta.Where(x =>
                CompactarPesquisa(x.Certificado).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Beneficiario).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Categoria).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.OrigemFatura).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.OrigemOver).Contains(pesquisa, StringComparison.Ordinal));
        }

        List<LinhaComparacaoDiagnostico> resultado = consulta.ToList();
        ComparacaoDataGrid.ItemsSource = resultado;
        ComparacaoFiltroResultadoText.Text = resultado.Count == 1 ? "1 resultado" : $"{resultado.Count:N0} resultados";
    }

    private void AplicarFiltroContexto()
    {
        if (!IsInitialized || ContextoDataGrid == null || PesquisaContextoTextBox == null || ContextoStatusComboBox == null)
            return;

        IEnumerable<LinhaContextoDiagnostico> consulta = _contextos;
        switch (ContextoStatusComboBox.SelectedIndex)
        {
            case 1:
                consulta = consulta.Where(x => x.Explicada);
                break;
            case 2:
                consulta = consulta.Where(x => x.DivergenciaPermanece);
                break;
            case 3:
                consulta = consulta.Where(x => x.StatusContexto == "Sem contexto");
                break;
            case 4:
                consulta = consulta.Where(x => x.StatusContexto.StartsWith("Não aplicável", StringComparison.Ordinal));
                break;
        }

        string pesquisa = CompactarPesquisa(PesquisaContextoTextBox.Text);
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            consulta = consulta.Where(x =>
                CompactarPesquisa(x.Certificado).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Beneficiario).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.CategoriaOriginal).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.StatusContexto).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Movimentos).Contains(pesquisa, StringComparison.Ordinal) ||
                CompactarPesquisa(x.Evidencias).Contains(pesquisa, StringComparison.Ordinal));
        }

        List<LinhaContextoDiagnostico> resultado = consulta.ToList();
        ContextoDataGrid.ItemsSource = resultado;
        ContextoFiltroResultadoText.Text = resultado.Count == 1 ? "1 resultado" : $"{resultado.Count:N0} resultados";
    }

    private void VinculosDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        => AtualizarBotaoComposicao();

    private void VinculosDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BtnVerComposicao.IsEnabled)
            AbrirComposicaoSelecionada();
    }

    private void VerComposicao_Click(object sender, RoutedEventArgs e)
        => AbrirComposicaoSelecionada();

    private void AtualizarBotaoComposicao()
    {
        if (!IsInitialized || BtnVerComposicao == null || VinculosDataGrid == null)
            return;

        LinhaVinculoDiagnostico? linha = ObterLinhaVinculoSelecionada();
        BtnVerComposicao.IsEnabled = linha != null && LocalizarComposicao(linha) != null;
    }

    private LinhaVinculoDiagnostico? ObterLinhaVinculoSelecionada()
    {
        if (VinculosDataGrid?.CurrentItem is LinhaVinculoDiagnostico atual)
            return atual;

        return VinculosDataGrid?.SelectedCells
            .Select(x => x.Item)
            .OfType<LinhaVinculoDiagnostico>()
            .FirstOrDefault();
    }

    private void AbrirComposicaoSelecionada()
    {
        LinhaVinculoDiagnostico? linha = ObterLinhaVinculoSelecionada();
        if (linha == null)
            return;

        ComposicaoBeneficiario? composicao = LocalizarComposicao(linha);
        if (composicao == null)
            return;

        DiagnosticoTabControl.SelectedIndex = 1;
        PesquisaComposicaoTextBox.Text = composicao.Certificado;
        AplicarFiltroComposicao();

        LinhaComposicaoDiagnostico? item = (ComposicoesDataGrid.ItemsSource as IEnumerable<LinhaComposicaoDiagnostico>)?
            .FirstOrDefault(x => ReferenceEquals(x.Origem, composicao));

        if (item != null)
        {
            SelecionarLinhaComposicao(item);
        }
    }

    private ComposicaoBeneficiario? LocalizarComposicao(LinhaVinculoDiagnostico linha)
    {
        if (linha.ResultadoOriginal.Status != VinculoBeneficiarioStatus.EncontradoUnico &&
            linha.ResultadoOriginal.Status != VinculoBeneficiarioStatus.EncontradoPorNome)
        {
            return null;
        }

        List<ComposicaoBeneficiario> candidatos = _consolidacao.Composicoes
            .Where(x => string.Equals(x.Certificado, linha.ResultadoOriginal.Certificado, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidatos.Count == 1)
            return candidatos[0];

        string nomeOver = linha.ResultadoOriginal.Direcao == VinculoBeneficiarioDirecao.OverParaFatura
            ? linha.ResultadoOriginal.NomeOrigem
            : linha.ResultadoOriginal.NomeDestino;
        string nomeFatura = linha.ResultadoOriginal.Direcao == VinculoBeneficiarioDirecao.OverParaFatura
            ? linha.ResultadoOriginal.NomeDestino
            : linha.ResultadoOriginal.NomeOrigem;

        string nomeOverNorm = AnaliseFaturasNormalizador.NormalizarNome(nomeOver);
        string nomeFaturaNorm = AnaliseFaturasNormalizador.NormalizarNome(nomeFatura);

        List<ComposicaoBeneficiario> porNome = candidatos.Where(x =>
            (string.IsNullOrWhiteSpace(nomeOverNorm) || AnaliseFaturasNormalizador.NormalizarNome(x.NomeOver) == nomeOverNorm) &&
            (string.IsNullOrWhiteSpace(nomeFaturaNorm) || AnaliseFaturasNormalizador.NormalizarNome(x.NomeFatura) == nomeFaturaNorm))
            .ToList();

        return porNome.Count == 1 ? porNome[0] : null;
    }

    private void ComposicoesDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        LinhaComposicaoDiagnostico? linha = ObterComposicaoSelecionada();
        if (linha != null)
            ExibirComposicao(linha.Origem);
        else
            LimparDetalheComposicao();
    }

    private LinhaComposicaoDiagnostico? ObterComposicaoSelecionada()
    {
        if (ComposicoesDataGrid?.CurrentItem is LinhaComposicaoDiagnostico atual)
            return atual;

        return ComposicoesDataGrid?.SelectedCells
            .Select(x => x.Item)
            .OfType<LinhaComposicaoDiagnostico>()
            .FirstOrDefault();
    }

    private void SelecionarLinhaComposicao(LinhaComposicaoDiagnostico item)
    {
        if (ComposicoesDataGrid == null || ComposicoesDataGrid.Columns.Count == 0)
            return;

        // A grade de composição é copiável por célula. Garante a configuração
        // antes de qualquer seleção programática para evitar conflito com FullRow.
        if (ComposicoesDataGrid.SelectionUnit != DataGridSelectionUnit.Cell)
            ComposicoesDataGrid.SelectionUnit = DataGridSelectionUnit.Cell;

        if (ComposicoesDataGrid.SelectionMode != DataGridSelectionMode.Extended)
            ComposicoesDataGrid.SelectionMode = DataGridSelectionMode.Extended;

        var info = new DataGridCellInfo(item, ComposicoesDataGrid.Columns[0]);
        ComposicoesDataGrid.SelectedCells.Clear();
        ComposicoesDataGrid.SelectedCells.Add(info);
        ComposicoesDataGrid.CurrentCell = info;
        ComposicoesDataGrid.ScrollIntoView(item);
        ExibirComposicao(item.Origem);
    }

    private void ExibirComposicao(ComposicaoBeneficiario composicao)
    {
        ComposicaoTituloText.Text = $"{composicao.NomeFatura}  •  {composicao.Certificado}";
        bool temCopart = composicao.ComponentesOver.Any(x =>
            AnaliseFaturasRegrasComparacao.EhCoparticipacao(x.Evento, x.Descricao));
        bool copartIgnorada = composicao.ComponentesOver.Any(x =>
            AnaliseFaturasRegrasComparacao.EhCoparticipacao(x.Evento, x.Descricao) && !x.ConsiderarNoNETComparavel);

        string regraCopart = !temCopart
            ? "Sem coparticipação neste beneficiário."
            : copartIgnorada
                ? "Coparticipação visível e ignorada no NET comparável."
                : "Coparticipação incluída no NET comparável pela opção do usuário.";

        ComposicaoResumoText.Text =
            $"Vínculo: {composicao.StatusVinculo}  •  " +
            $"{composicao.ComponentesFatura.Count:N0} componente(s) na fatura  •  " +
            $"{composicao.ComponentesOver.Count:N0} componente(s) no Over.  " +
            "IOF permanece visível e sempre é excluído. " + regraCopart +
            (composicao.ComponentesFatura.Any(x => !x.ConsiderarNoComparavel)
                ? " Há lançamentos de competências anteriores visíveis, porém fora do valor comparável."
                : string.Empty);
        ComposicaoOrigemText.Text = $"Fatura: {composicao.OrigemFatura}   |   Over: {composicao.OrigemOver}";

        ComponentesFaturaDataGrid.ItemsSource = composicao.ComponentesFatura.Select(CriarLinhaFatura).ToList();
        ComponentesOverDataGrid.ItemsSource = composicao.ComponentesOver.Select(CriarLinhaOver).ToList();

        TotalFaturaText.Text =
            $"Valor comparável: {composicao.TotalValorFatura.ToString("N2", PtBr)}  •  " +
            $"Bruto: {composicao.TotalValorFaturaBruto.ToString("N2", PtBr)}  •  " +
            $"Comp. anteriores ignoradas: {composicao.TotalValorFaturaIgnoradoCompetenciasAnteriores.ToString("N2", PtBr)}  •  " +
            $"Participação: {composicao.TotalParticipacaoFatura.ToString("N2", PtBr)}";

        string sufixoNet = copartIgnorada ? "sem IOF e copart" : "sem IOF";
        TotalOverText.Text =
            $"NET comparável ({sufixoNet}): {composicao.TotalNETOver.ToString("N2", PtBr)}";
        TotalOverAuxText.Text =
            $"NET bruto: {composicao.TotalNETBrutoOver.ToString("N2", PtBr)}  •  " +
            $"IOF ignorado: {composicao.TotalIOFNETIgnorado.ToString("N2", PtBr)}  •  " +
            $"Copart ignorada: {composicao.TotalCopartNETIgnorado.ToString("N2", PtBr)}  •  " +
            $"PV: {composicao.TotalPVOver.ToString("N2", PtBr)}  •  " +
            $"Over: {composicao.TotalOver.ToString("N2", PtBr)}";
    }

    private void LimparDetalheComposicao()
    {
        if (!IsInitialized || ComposicaoTituloText == null)
            return;

        ComposicaoTituloText.Text = "Selecione um beneficiário";
        ComposicaoResumoText.Text = "A composição é exibida somente para vínculos seguros.";
        ComposicaoOrigemText.Text = string.Empty;
        ComponentesFaturaDataGrid.ItemsSource = null;
        ComponentesOverDataGrid.ItemsSource = null;
        TotalFaturaText.Text = string.Empty;
        TotalOverText.Text = string.Empty;
        TotalOverAuxText.Text = string.Empty;
    }

    private static LinhaVinculoDiagnostico CriarLinha(VinculoBeneficiarioResultado resultado)
        => new()
        {
            ResultadoOriginal = resultado,
            Direcao = resultado.Direcao == VinculoBeneficiarioDirecao.OverParaFatura ? "Over → Fatura" : "Fatura → Over",
            Status = resultado.Status.ToString(),
            Certificado = VazioComoTraco(resultado.Certificado),
            NomeOrigem = VazioComoTraco(resultado.NomeOrigem),
            NomeDestino = VazioComoTraco(resultado.NomeDestino),
            CandidatosCertificado = resultado.QuantidadeCandidatosCertificado.ToString(PtBr),
            CandidatosNome = resultado.QuantidadeCandidatosNome.ToString(PtBr),
            LancamentosOrigem = resultado.QuantidadeLancamentosOrigem.ToString(PtBr),
            Origem = VazioComoTraco(resultado.OrigemDetalhe),
            Destino = VazioComoTraco(resultado.DestinoDetalhe),
            Observacao = resultado.Observacao
        };

    private static LinhaComposicaoDiagnostico CriarLinhaComposicao(ComposicaoBeneficiario composicao)
        => new()
        {
            Origem = composicao,
            Certificado = composicao.Certificado,
            Nome = composicao.NomeFatura,
            NomeOver = composicao.NomeOver,
            Status = composicao.StatusVinculo.ToString()
        };

    private static LinhaComponenteFatura CriarLinhaFatura(ComponenteFatura x)
        => new()
        {
            PaginaPdf = x.PaginaPdf.ToString(PtBr),
            PaginaFatura = x.PaginaFatura?.ToString(PtBr) ?? "—",
            Subfatura = x.Subfatura.ToString(PtBr),
            Entidade = VazioComoTraco(x.Entidade),
            Movimento = VazioComoTraco(x.Movimento),
            Competencia = x.Competencia.ToString("MM/yyyy", PtBr),
            Natureza = x.ConsiderarNoComparavel
                ? x.Natureza
                : $"{x.Natureza} • IGNORADO NO COMPARÁVEL",
            Plano = VazioComoTraco(x.Plano),
            Valor = x.Valor.ToString("N2", PtBr),
            Participacao = x.Participacao?.ToString("N2", PtBr) ?? "—",
            TextoOrigem = VazioComoTraco(x.TextoOrigem)
        };

    private static LinhaComponenteOver CriarLinhaOver(ComponenteOver x)
        => new()
        {
            Linha = x.NumeroLinha.ToString(PtBr),
            Evento = VazioComoTraco(x.Evento),
            Descricao = VazioComoTraco(x.Descricao),
            Competencia = x.Competencia?.ToString("MM/yyyy", PtBr) ?? "—",
            Natureza = x.Natureza,
            UsoComparacao = x.RegraComparacao,
            IgnoradoNoComparavel = !x.ConsiderarNoNETComparavel,
            PV = x.ValorPV?.ToString("N2", PtBr) ?? "—",
            NET = x.ValorNET?.ToString("N2", PtBr) ?? "—",
            Over = x.ValorOver?.ToString("N2", PtBr) ?? "—",
            Entidade = VazioComoTraco(x.Entidade),
            Matricula = VazioComoTraco(x.Matricula),
            Cartao = VazioComoTraco(x.Cartao)
        };

    private static LinhaComparacaoDiagnostico CriarLinhaComparacao(ComparacaoPrincipalResultado x)
        => new()
        {
            Original = x,
            Categoria = DescreverCategoria(x.Categoria),
            Certificado = VazioComoTraco(x.Certificado),
            Beneficiario = VazioComoTraco(x.NomeReferencia),
            Vinculo = x.StatusVinculo.ToString(),
            ValorFatura = x.ValorFatura?.ToString("N2", PtBr) ?? "—",
            ValorOver = x.ValorOverComparavel?.ToString("N2", PtBr) ?? "—",
            Diferenca = x.DiferencaFaturaMenosOver?.ToString("N2", PtBr) ?? "—",
            OrigemFatura = VazioComoTraco(x.OrigemFatura),
            OrigemOver = VazioComoTraco(x.OrigemOver),
            Observacao = x.Observacao
        };

    private static LinhaContextoDiagnostico CriarLinhaContexto(ContextoTemporalResultado x)
    {
        string meses = string.Join(", ", x.Evidencias
            .Select(e => e.CompetenciaFatura.ToString("MM/yyyy", PtBr))
            .Distinct());
        string movimentos = string.Join(", ", x.Evidencias
            .Select(e => string.IsNullOrWhiteSpace(e.Movimento) ? "—" : e.Movimento)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        string evidencias = string.Join(" | ", x.Evidencias.Take(8).Select(e =>
            $"{e.CompetenciaFatura:MM/yyyy} • {e.Arquivo} • Subf. {e.Subfatura} • Pág. {e.PaginaPdf} • {VazioComoTraco(e.Movimento)} {e.CompetenciaLancamento:MM/yyyy} {e.Valor.ToString("N2", PtBr)}"));
        if (x.Evidencias.Count > 8)
            evidencias += $" | ... +{x.Evidencias.Count - 8} evidência(s)";

        return new LinhaContextoDiagnostico
        {
            Original = x,
            CategoriaOriginal = DescreverCategoria(x.ComparacaoOriginal.Categoria),
            StatusContexto = DescreverContextoStatus(x.Status),
            Certificado = VazioComoTraco(x.Certificado),
            Beneficiario = VazioComoTraco(x.Nome),
            ValorFatura = x.ComparacaoOriginal.ValorFatura?.ToString("N2", PtBr) ?? "—",
            ValorOver = x.ComparacaoOriginal.ValorOverComparavel?.ToString("N2", PtBr) ?? "—",
            DiferencaOriginal = x.ComparacaoOriginal.DiferencaFaturaMenosOver?.ToString("N2", PtBr) ?? "—",
            AjustesContexto = x.ValorAjustesContexto.ToString("N2", PtBr),
            MesesContexto = string.IsNullOrWhiteSpace(meses) ? "—" : meses,
            Movimentos = string.IsNullOrWhiteSpace(movimentos) ? "—" : movimentos,
            SituacaoFinal = x.Explicada ? "Explicada" : x.DivergenciaPermanece ? "Permanece" : "—",
            Evidencias = string.IsNullOrWhiteSpace(evidencias) ? "—" : evidencias,
            Observacao = x.Observacao,
            Explicada = x.Explicada,
            DivergenciaPermanece = x.DivergenciaPermanece
        };
    }

    private static string DescreverCategoria(ComparacaoPrincipalCategoria categoria) => categoria switch
    {
        ComparacaoPrincipalCategoria.EncontradoValorCompativel => "Valor compatível",
        ComparacaoPrincipalCategoria.ValorMaiorNaFatura => "Valor maior na fatura",
        ComparacaoPrincipalCategoria.ValorMaiorNoOver => "Valor maior no Over",
        ComparacaoPrincipalCategoria.NaoEncontradoNaFatura => "Não encontrado na fatura",
        ComparacaoPrincipalCategoria.NaoEncontradoNoOver => "Não encontrado no Over",
        ComparacaoPrincipalCategoria.Ambiguo => "Ambíguo",
        _ => categoria.ToString()
    };

    private static string DescreverContextoStatus(ContextoTemporalStatus status) => status switch
    {
        ContextoTemporalStatus.NaoAplicavelValorCompativel => "Não aplicável — valor compatível",
        ContextoTemporalStatus.NaoAplicavelAmbiguo => "Não aplicável — ambíguo",
        ContextoTemporalStatus.SemContexto => "Sem contexto",
        ContextoTemporalStatus.AmbiguoNoContexto => "Ambíguo no contexto",
        ContextoTemporalStatus.ContextoEncontradoSemJustificativa => "Contexto encontrado — não justifica",
        ContextoTemporalStatus.ExplicadaPorVigenciaPosterior => "Explicada por vigência posterior",
        ContextoTemporalStatus.ExplicadaPorInclusao => "Explicada por inclusão",
        ContextoTemporalStatus.ExplicadaPorRetroativo => "Explicada por retroativo",
        ContextoTemporalStatus.ExplicadaPorCancelamento => "Explicada por cancelamento",
        ContextoTemporalStatus.ExplicadaPorReativacao => "Explicada por reativação",
        ContextoTemporalStatus.ExplicadaPorAlteracao => "Explicada por alteração",
        ContextoTemporalStatus.ExplicadaPorTransferencia => "Explicada por transferência",
        ContextoTemporalStatus.ExplicadaPorDevolucao => "Explicada por devolução",
        _ => status.ToString()
    };

    private static string VazioComoTraco(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? "—" : texto;

    private static string CompactarPesquisa(string? texto)
    {
        string normalizado = AnaliseFaturasNormalizador.NormalizarNome(texto);
        return new string(normalizado.Where(char.IsLetterOrDigit).ToArray());
    }

    private sealed class LinhaVinculoDiagnostico
    {
        public VinculoBeneficiarioResultado ResultadoOriginal { get; init; } = new();
        public string Direcao { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Certificado { get; init; } = string.Empty;
        public string NomeOrigem { get; init; } = string.Empty;
        public string NomeDestino { get; init; } = string.Empty;
        public string CandidatosCertificado { get; init; } = string.Empty;
        public string CandidatosNome { get; init; } = string.Empty;
        public string LancamentosOrigem { get; init; } = string.Empty;
        public string Origem { get; init; } = string.Empty;
        public string Destino { get; init; } = string.Empty;
        public string Observacao { get; init; } = string.Empty;
    }

    private sealed class LinhaComposicaoDiagnostico
    {
        public ComposicaoBeneficiario Origem { get; init; } = new();
        public string Certificado { get; init; } = string.Empty;
        public string Nome { get; init; } = string.Empty;
        public string NomeOver { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
    }

    private sealed class LinhaComparacaoDiagnostico
    {
        public ComparacaoPrincipalResultado Original { get; init; } = new();
        public string Categoria { get; init; } = string.Empty;
        public string Certificado { get; init; } = string.Empty;
        public string Beneficiario { get; init; } = string.Empty;
        public string Vinculo { get; init; } = string.Empty;
        public string ValorFatura { get; init; } = string.Empty;
        public string ValorOver { get; init; } = string.Empty;
        public string Diferenca { get; init; } = string.Empty;
        public string OrigemFatura { get; init; } = string.Empty;
        public string OrigemOver { get; init; } = string.Empty;
        public string Observacao { get; init; } = string.Empty;
    }

    private sealed class LinhaContextoDiagnostico
    {
        public ContextoTemporalResultado Original { get; init; } = new();
        public string CategoriaOriginal { get; init; } = string.Empty;
        public string StatusContexto { get; init; } = string.Empty;
        public string Certificado { get; init; } = string.Empty;
        public string Beneficiario { get; init; } = string.Empty;
        public string ValorFatura { get; init; } = string.Empty;
        public string ValorOver { get; init; } = string.Empty;
        public string DiferencaOriginal { get; init; } = string.Empty;
        public string AjustesContexto { get; init; } = string.Empty;
        public string MesesContexto { get; init; } = string.Empty;
        public string Movimentos { get; init; } = string.Empty;
        public string SituacaoFinal { get; init; } = string.Empty;
        public string Evidencias { get; init; } = string.Empty;
        public string Observacao { get; init; } = string.Empty;
        public bool Explicada { get; init; }
        public bool DivergenciaPermanece { get; init; }
    }

    private sealed class LinhaComponenteFatura
    {
        public string PaginaPdf { get; init; } = string.Empty;
        public string PaginaFatura { get; init; } = string.Empty;
        public string Subfatura { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public string Movimento { get; init; } = string.Empty;
        public string Competencia { get; init; } = string.Empty;
        public string Natureza { get; init; } = string.Empty;
        public string Plano { get; init; } = string.Empty;
        public string Valor { get; init; } = string.Empty;
        public string Participacao { get; init; } = string.Empty;
        public string TextoOrigem { get; init; } = string.Empty;
    }

    private sealed class LinhaComponenteOver
    {
        public string Linha { get; init; } = string.Empty;
        public string Evento { get; init; } = string.Empty;
        public string Descricao { get; init; } = string.Empty;
        public string Competencia { get; init; } = string.Empty;
        public string Natureza { get; init; } = string.Empty;
        public string UsoComparacao { get; init; } = string.Empty;
        public bool IgnoradoNoComparavel { get; init; }
        public string PV { get; init; } = string.Empty;
        public string NET { get; init; } = string.Empty;
        public string Over { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public string Matricula { get; init; } = string.Empty;
        public string Cartao { get; init; } = string.Empty;
    }
}
