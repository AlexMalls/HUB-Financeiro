using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HubFinanceiro;

public partial class DetalheAnaliseFaturasWindow : Window
{
    private readonly AnaliseFinalResultado _resultado;
    private readonly AnaliseFaturasVisaoFinanceira _visaoFinanceira;

    public DetalheAnaliseFaturasWindow(AnaliseFinalResultado resultado)
    {
        InitializeComponent();
        _resultado = resultado ?? throw new ArgumentNullException(nameof(resultado));
        _visaoFinanceira = AnaliseFaturasVisaoFinanceiraService.Calcular(_resultado);
        Carregar();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Carregar()
    {
        string status = _resultado.Status == AnaliseFinalStatus.Ambiguo
            ? "Divergência"
            : AnaliseFinalService.TraduzirStatus(_resultado.Status);
        BeneficiarioText.Text = _resultado.Beneficiario;
        SubtituloText.Text = $"{_resultado.Certificado}  •  {_resultado.TipoDivergencia}  •  {_resultado.Competencia:MM/yyyy}";
        StatusText.Text = status;
        StatusText.Foreground = _resultado.Status switch
        {
            AnaliseFinalStatus.Compativel => new SolidColorBrush(Color.FromRgb(112, 214, 160)),
            AnaliseFinalStatus.DivergenciaExplicada => new SolidColorBrush(Color.FromRgb(185, 138, 240)),
            AnaliseFinalStatus.DivergenciaPendente => new SolidColorBrush(Color.FromRgb(242, 200, 121)),
            AnaliseFinalStatus.Ambiguo => new SolidColorBrush(Color.FromRgb(242, 200, 121)),
            _ => new SolidColorBrush(Color.FromRgb(255, 119, 119))
        };
        if (_visaoFinanceira.PossuiAjusteContexto && _visaoFinanceira.DiferencaOriginal.HasValue)
        {
            ValoresText.Text =
                $"Fatura líquida {Fmt(_visaoFinanceira.ValorFaturaLiquida)}  •  " +
                $"Over {Fmt(_resultado.ValorOver)}  •  " +
                $"Divergência {Fmt(_visaoFinanceira.DiferencaResidual)}";
        }
        else
        {
            ValoresText.Text = $"Fatura {Fmt(_resultado.ValorFatura)}  •  Over {Fmt(_resultado.ValorOver)}  •  Divergência {Fmt(_resultado.Diferenca)}";
        }

        bool possuiDevolucao = TentarObterDevolucaoResumida(out decimal valorDevolucao, out int diasDevolucao);
        BreveExplicacaoText.Text = CriarBreveExplicacao(possuiDevolucao);
        if (possuiDevolucao)
        {
            ResumoDevolucaoText.Text =
                $"Devolução da Bradesco: R$ {valorDevolucao:N2}, equivalente a {diasDevolucao} {(diasDevolucao == 1 ? "dia" : "dias")}.";
            ResumoDevolucaoText.Visibility = Visibility.Visible;
        }

        OrigemFaturaText.Text = string.IsNullOrWhiteSpace(_resultado.OrigemFatura) ? "—" : _resultado.OrigemFatura;
        OrigemOverText.Text = string.IsNullOrWhiteSpace(_resultado.OrigemOver) ? "—" : _resultado.OrigemOver;

        DadosFaturaText.Text = _resultado.DadosFatura == null
            ? "—"
            : $"Entidade: {_resultado.DadosFatura.Entidade}  •  Subfatura: {_resultado.DadosFatura.Subfatura}  •  Plano: {_resultado.DadosFatura.Plano}  •  " +
              $"Nascimento: {FmtData(_resultado.DadosFatura.DataNascimento)}  •  Início: {FmtData(_resultado.DadosFatura.DataInicio)}";

        CarregarLancamentos();

        RegrasDataGrid.ItemsSource = _resultado.RegrasTestadas.Select(x => new
        {
            Regra = x.NomeDaRegra,
            Resultado = TraduzirRegra(x.Resultado),
            x.Condicao,
            Dados = x.DadosUtilizados,
            x.Justificativa,
            Evidencias = x.Evidencias.Count == 0 ? "—" : string.Join(" | ", x.Evidencias)
        }).ToList();

        if (_resultado.ContextoTemporal == null)
        {
            ContextoResumoText.Text = "Nenhum contexto temporal foi associado a este resultado.";
            ContextoDataGrid.ItemsSource = Array.Empty<object>();
        }
        else
        {
            ContextoResumoText.Text =
                _visaoFinanceira.PossuiAjusteContexto &&
                _visaoFinanceira.DiferencaOriginal.HasValue
                    ? $"Status: {_resultado.ContextoTemporal.Status}  •  " +
                      $"Diferença original: R$ {_visaoFinanceira.DiferencaOriginal.Value:N2}  •  " +
                      $"Ajuste aplicado: R$ {_visaoFinanceira.AjusteContexto:N2}  •  " +
                      $"Fatura líquida: {Fmt(_visaoFinanceira.ValorFaturaLiquida)}  •  " +
                      $"Diferença residual: {Fmt(_visaoFinanceira.DiferencaResidual)}"
                    : $"Status: {_resultado.ContextoTemporal.Status}  •  {_resultado.ContextoTemporal.Observacao}";
            ContextoDataGrid.ItemsSource = _resultado.ContextoTemporal.Evidencias.Select(x => new
            {
                CompetenciaFatura = x.CompetenciaFatura.ToString("MM/yyyy"),
                x.Arquivo,
                x.Subfatura,
                Pagina = x.PaginaPdf,
                x.Movimento,
                CompetenciaLancamento = x.CompetenciaLancamento.ToString("MM/yyyy"),
                Valor = x.Valor.ToString("N2"),
                x.Entidade
            }).ToList();
        }
    }

    private void CarregarLancamentos()
    {
        string status = _resultado.Status == AnaliseFinalStatus.Ambiguo
            ? "Divergência"
            : AnaliseFinalService.TraduzirStatus(_resultado.Status);

        var linhasFatura = (_resultado.LancamentosFaturaInvestigacao?.Count > 0
            ? _resultado.LancamentosFaturaInvestigacao.Select(x => new LinhaFaturaDetalhe
            {
                Fatura = x.CompetenciaFatura.ToString("MM/yyyy"),
                Arquivo = VazioComoTraco(x.Arquivo),
                PaginaPdf = x.PaginaPdf.ToString(),
                PaginaFatura = x.PaginaFatura?.ToString() ?? "—",
                Subfatura = x.Subfatura.ToString(),
                Entidade = VazioComoTraco(x.Entidade),
                Movimento = VazioComoTraco(x.Movimento),
                Competencia = x.CompetenciaLancamento.ToString("MM/yyyy"),
                Natureza = VazioComoTraco(x.Natureza),
                Plano = VazioComoTraco(x.Plano),
                Valor = x.Valor.ToString("N2"),
                Participacao = x.Participacao?.ToString("N2") ?? "—",
                Uso = VazioComoTraco(x.UsoComparacao),
                TextoOrigem = VazioComoTraco(x.TextoOrigem),
                ContextoTemporal = false,
                OrdemFatura = x.CompetenciaFatura,
                OrdemPagina = x.PaginaPdf
            })
            : _resultado.ComponentesFatura.Select(x => new LinhaFaturaDetalhe
            {
                Fatura = _resultado.Competencia.ToString("MM/yyyy"),
                Arquivo = ExtrairArquivoFaturaPrincipal(_resultado.OrigemFatura),
                PaginaPdf = x.PaginaPdf.ToString(),
                PaginaFatura = x.PaginaFatura?.ToString() ?? "—",
                Subfatura = x.Subfatura.ToString(),
                Entidade = VazioComoTraco(x.Entidade),
                Movimento = VazioComoTraco(x.Movimento),
                Competencia = x.Competencia.ToString("MM/yyyy"),
                Natureza = VazioComoTraco(x.Natureza),
                Plano = VazioComoTraco(x.Plano),
                Valor = x.Valor.ToString("N2"),
                Participacao = x.Participacao?.ToString("N2") ?? "—",
                Uso = VazioComoTraco(x.RegraComparacao),
                TextoOrigem = VazioComoTraco(x.TextoOrigem),
                ContextoTemporal = false,
                OrdemFatura = _resultado.Competencia,
                OrdemPagina = x.PaginaPdf
            }))
            .ToList();

        if (_resultado.ContextoTemporal != null)
        {
            foreach (ContextoTemporalEvidencia x in _resultado.ContextoTemporal.Evidencias)
            {
                linhasFatura.Add(new LinhaFaturaDetalhe
                {
                    Fatura = x.CompetenciaFatura.ToString("MM/yyyy"),
                    Arquivo = VazioComoTraco(x.Arquivo),
                    PaginaPdf = x.PaginaPdf.ToString(),
                    PaginaFatura = "—",
                    Subfatura = x.Subfatura.ToString(),
                    Entidade = VazioComoTraco(x.Entidade),
                    Movimento = VazioComoTraco(x.Movimento),
                    Competencia = x.CompetenciaLancamento.ToString("MM/yyyy"),
                    Natureza = "Contexto temporal",
                    Plano = "—",
                    Valor = x.Valor.ToString("N2"),
                    Participacao = "—",
                    Uso = AnaliseFaturasVisaoFinanceiraService.EhAjusteContextoAplicado(x, _resultado.Competencia)
                        ? "Contexto temporal — aplicado à diferença residual"
                        : "Contexto temporal — evidência, não aplicada à diferença residual",
                    TextoOrigem = $"{VazioComoTraco(x.Arquivo)} • pág. {x.PaginaPdf}",
                    ContextoTemporal = true,
                    OrdemFatura = x.CompetenciaFatura,
                    OrdemPagina = x.PaginaPdf
                });
            }
        }

        linhasFatura = linhasFatura
            .GroupBy(x => new
            {
                x.Fatura,
                x.Arquivo,
                x.PaginaPdf,
                x.Subfatura,
                x.Movimento,
                x.Competencia,
                x.Valor,
                x.ContextoTemporal
            })
            .Select(g => g.First())
            .OrderBy(x => x.OrdemFatura)
            .ThenBy(x => x.Arquivo, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.OrdemPagina)
            .ToList();

        var linhasOver = _resultado.ComponentesOver.Select(x => new LinhaOverDetalhe
        {
            Linha = x.NumeroLinha.ToString(),
            Evento = VazioComoTraco(x.Evento),
            Descricao = VazioComoTraco(x.Descricao),
            Competencia = x.Competencia?.ToString("MM/yyyy") ?? "—",
            Natureza = VazioComoTraco(x.Natureza),
            Uso = VazioComoTraco(x.RegraComparacao),
            PV = x.ValorPV?.ToString("N2") ?? "—",
            NET = x.ValorNET?.ToString("N2") ?? "—",
            Over = x.ValorOver?.ToString("N2") ?? "—",
            Entidade = VazioComoTraco(x.Entidade),
            Matricula = VazioComoTraco(x.Matricula),
            Cartao = VazioComoTraco(x.Cartao),
            IgnoradoNoComparavel = EhIgnoradoNoComparavel(x.RegraComparacao)
        }).ToList();

        FaturaDataGrid.ItemsSource = linhasFatura;
        OverDataGrid.ItemsSource = linhasOver;

        decimal brutoFatura = _resultado.LancamentosFaturaInvestigacao?.Count > 0
            ? _resultado.LancamentosFaturaInvestigacao.Sum(x => x.Valor)
            : _resultado.ComponentesFatura.Sum(x => x.Valor);
        decimal participacaoFatura = _resultado.LancamentosFaturaInvestigacao?.Count > 0
            ? _resultado.LancamentosFaturaInvestigacao.Sum(x => x.Participacao ?? 0m)
            : _resultado.ComponentesFatura.Sum(x => x.Participacao ?? 0m);
        decimal competenciasAnterioresIgnoradas = _resultado.LancamentosFaturaInvestigacao?.Count > 0
            ? _resultado.LancamentosFaturaInvestigacao
                .Where(x => x.UsoComparacao?.Contains("anterior", StringComparison.OrdinalIgnoreCase) == true &&
                            x.UsoComparacao.Contains("ignor", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Valor)
            : _resultado.ComponentesFatura
                .Where(x => x.RegraComparacao?.Contains("anterior", StringComparison.OrdinalIgnoreCase) == true &&
                            x.RegraComparacao.Contains("ignor", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Valor);
        int quantidadeContexto = linhasFatura.Count(x => x.ContextoTemporal);

        decimal netBruto = _resultado.ComponentesOver.Sum(x => x.ValorNET ?? 0m);
        decimal iofIgnorado = _resultado.ComponentesOver
            .Where(x => string.Equals(x.Evento, "9001", StringComparison.OrdinalIgnoreCase) ||
                        x.Descricao?.Contains("IOF", StringComparison.OrdinalIgnoreCase) == true)
            .Sum(x => x.ValorNET ?? 0m);
        decimal copartIgnorada = _resultado.ComponentesOver
            .Where(x => x.RegraComparacao?.Contains("copart", StringComparison.OrdinalIgnoreCase) == true &&
                        x.RegraComparacao.Contains("ignor", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.ValorNET ?? 0m);
        decimal pvBruto = _resultado.ComponentesOver.Sum(x => x.ValorPV ?? 0m);
        decimal overBruto = _resultado.ComponentesOver.Sum(x => x.ValorOver ?? 0m);

        LancamentosTituloText.Text = $"{_resultado.Beneficiario}  •  {_resultado.Certificado}";
        LancamentosResumoText.Text =
            $"Status: {status}  •  {linhasFatura.Count(x => !x.ContextoTemporal):N0} lançamento(s) na fatura analisada  •  " +
            $"{quantidadeContexto:N0} lançamento(s) nas faturas de contexto  •  {_resultado.ComponentesOver.Count:N0} componente(s) no Over.";

        var arquivosContexto = _resultado.ContextoTemporal?.Evidencias
            .Select(x => x.Arquivo)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList() ?? new System.Collections.Generic.List<string>();
        var arquivosPrincipais = linhasFatura
            .Where(x => !x.ContextoTemporal)
            .Select(x => x.Arquivo)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "—")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        string arquivosFatura = arquivosPrincipais.Count > 0
            ? string.Join("  |  ", arquivosPrincipais)
            : ExtrairArquivoFaturaPrincipal(_resultado.OrigemFatura);
        if (arquivosContexto.Count > 0)
            arquivosFatura += "  |  Contexto: " + string.Join("  |  ", arquivosContexto);

        LancamentosOrigemText.Text = $"Fatura(s): {VazioComoTraco(arquivosFatura)}   |   Over: {VazioComoTraco(_resultado.OrigemOver)}";

        LancamentosTotalFaturaText.Text = _visaoFinanceira.PossuiAjusteContexto
            ? $"Original: {Fmt(_resultado.ValorFatura)}  •  Ajustes: {_visaoFinanceira.AjusteContexto:N2}  •  " +
              $"Fatura líquida: {Fmt(_visaoFinanceira.ValorFaturaLiquida)}  •  Bruto: {brutoFatura:N2}"
            : $"Valor comparável: {Fmt(_resultado.ValorFatura)}  •  Bruto: {brutoFatura:N2}";
        LancamentosTotalFaturaAuxText.Text =
            $"Comp. anteriores ignoradas: {competenciasAnterioresIgnoradas:N2}  •  Participação: {participacaoFatura:N2}  •  Contexto: {quantidadeContexto:N0} lançamento(s)";

        LancamentosTotalOverText.Text = $"NET comparável (sem IOF): {Fmt(_resultado.ValorOver)}";
        LancamentosTotalOverAuxText.Text =
            $"NET bruto: {netBruto:N2}  •  IOF ignorado: {iofIgnorado:N2}  •  Copart ignorada: {copartIgnorada:N2}  •  PV: {pvBruto:N2}  •  Over: {overBruto:N2}";
    }

    private static bool EhIgnoradoNoComparavel(string? regra)
        => !string.IsNullOrWhiteSpace(regra) &&
           regra.Contains("ignor", StringComparison.OrdinalIgnoreCase);

    private static string ExtrairArquivoFaturaPrincipal(string origem)
    {
        if (string.IsNullOrWhiteSpace(origem))
            return "—";

        int indice = origem.IndexOf(" • Subf.", StringComparison.OrdinalIgnoreCase);
        if (indice > 0)
            return origem[..indice].Trim();

        indice = origem.IndexOf(" • Pág.", StringComparison.OrdinalIgnoreCase);
        if (indice > 0)
            return origem[..indice].Trim();

        return origem.Trim();
    }

    private static string VazioComoTraco(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? "—" : texto.Trim();

    private string CriarBreveExplicacao(bool possuiDevolucao)
    {
        if (_resultado.Status == AnaliseFinalStatus.Compativel)
            return "Os valores da fatura e do Over estão compatíveis.";

        if (_resultado.Status == AnaliseFinalStatus.Atencao && possuiDevolucao)
            return "Cancelamento no Over com devolução posterior da Bradesco; caso separado para conferência.";

        if (_resultado.Status == AnaliseFinalStatus.Atencao)
            return "O caso foi separado para conferência.";

        if (_resultado.Status == AnaliseFinalStatus.Ambiguo)
            return "O vínculo entre a fatura e o Over precisa de conferência manual.";

        decimal? diferenca = _visaoFinanceira.DiferencaResidual ?? _resultado.Diferenca;
        if (possuiDevolucao && diferenca > 0m)
            return "Mesmo após a devolução, permanece divergência porque a fatura líquida está acima do Over.";

        if (possuiDevolucao && diferenca < 0m)
            return "Mesmo após a devolução, permanece divergência porque o Over está acima da fatura líquida.";

        if (diferenca > 0m)
            return "Permanece divergência porque a fatura está acima do Over.";

        if (diferenca < 0m)
            return "Permanece divergência porque o Over está acima da fatura.";

        return "A ocorrência precisa de conferência manual.";
    }

    private bool TentarObterDevolucaoResumida(out decimal valor, out int dias)
    {
        RegraAnaliseResultado? estruturada = _resultado.RegrasTestadas.FirstOrDefault(x =>
            x.ValorDevolucao.HasValue && x.DiasEquivalentesDevolucao.HasValue);
        if (estruturada != null)
        {
            valor = estruturada.ValorDevolucao!.Value;
            dias = estruturada.DiasEquivalentesDevolucao!.Value;
            return true;
        }

        string textoLegado = string.Join(
            " ",
            _resultado.RegrasTestadas.Select(x => $"{x.DadosUtilizados} {x.Justificativa}")) +
            " " + (_resultado.JustificativaFinal ?? string.Empty);

        Match valorMatch = Regex.Match(
            textoLegado,
            @"(?:devolução\s+R\$|devolução\s+encontrada:\s*R\$)\s*(?<valor>[\d.,]+)",
            RegexOptions.IgnoreCase);
        Match diasMatch = Regex.Match(
            textoLegado,
            @"(?:dias\s+equivalentes\s+|equivale\s+financeiramente\s+a\s+)(?<dias>\d+)",
            RegexOptions.IgnoreCase);

        if (valorMatch.Success &&
            diasMatch.Success &&
            decimal.TryParse(
                valorMatch.Groups["valor"].Value,
                NumberStyles.Number,
                CultureInfo.GetCultureInfo("pt-BR"),
                out valor) &&
            int.TryParse(diasMatch.Groups["dias"].Value, out dias))
        {
            return true;
        }

        valor = 0m;
        dias = 0;
        return false;
    }

    private sealed class LinhaFaturaDetalhe
    {
        public string Fatura { get; init; } = string.Empty;
        public string Arquivo { get; init; } = string.Empty;
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
        public string Uso { get; init; } = string.Empty;
        public string TextoOrigem { get; init; } = string.Empty;
        public bool ContextoTemporal { get; init; }
        public DateTime OrdemFatura { get; init; }
        public int OrdemPagina { get; init; }
    }

    private sealed class LinhaOverDetalhe
    {
        public string Linha { get; init; } = string.Empty;
        public string Evento { get; init; } = string.Empty;
        public string Descricao { get; init; } = string.Empty;
        public string Competencia { get; init; } = string.Empty;
        public string Natureza { get; init; } = string.Empty;
        public string Uso { get; init; } = string.Empty;
        public string PV { get; init; } = string.Empty;
        public string NET { get; init; } = string.Empty;
        public string Over { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public string Matricula { get; init; } = string.Empty;
        public string Cartao { get; init; } = string.Empty;
        public bool IgnoradoNoComparavel { get; init; }
    }

    private static string TraduzirRegra(RegraAnaliseStatus status) => status switch
    {
        RegraAnaliseStatus.Explicada => "Explicada",
        RegraAnaliseStatus.RevisaoManual => "Revisão manual",
        RegraAnaliseStatus.EvidenciaEncontrada => "Evidência",
        _ => "Não aplicável"
    };

    private static string Fmt(decimal? valor) => valor.HasValue ? valor.Value.ToString("N2") : "—";
    private static string FmtData(DateTime? data) => data.HasValue ? data.Value.ToString("dd/MM/yyyy") : "—";
}
