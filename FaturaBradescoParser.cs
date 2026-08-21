using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HubFinanceiro;

/// <summary>
/// Leitor estrutural das faturas Bradesco Saúde utilizadas na análise de faturas.
/// Esta classe NÃO compara com Over e NÃO aplica regras financeiras de divergência.
/// Responsabilidade exclusiva: transformar o PDF em objetos estruturados e rastreáveis.
/// </summary>
public sealed class FaturaBradescoParser
{
    private static readonly Regex RegexCabecalho = new(
        @"\b(?<apolice>\d{7})\s+M[ÉE]DICA\s+(?<competencia>0[1-9]|1[0-2])/((?<ano>20\d{2}))\s+\d+\s+Subfatura\s+(?<numero>\d+)\s*-\s*(?<entidade>.*?)\s+878\s*-\s*MULTI\s+SAUDE\s+EMPRESA\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RegexCertificado = new(
        @"^\d{7}/\d{2}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RegexCompetencia = new(
        @"^(?<mes>0[1-9]|1[0-2])/(?<ano>20\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RegexValor = new(
        @"^-?(?:\d{1,3}(?:\.\d{3})*|\d+),\d{2}-?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const float ToleranciaLinha = 1.15f;
    private const float ToleranciaEspaco = 1.10f;

    public FaturaBradescoArquivo Ler(string arquivo)
    {
        if (string.IsNullOrWhiteSpace(arquivo))
            throw new ArgumentException("O caminho da fatura não foi informado.", nameof(arquivo));

        if (!File.Exists(arquivo))
            throw new FileNotFoundException("A fatura selecionada não foi encontrada.", arquivo);

        if (!IOPath.GetExtension(arquivo).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O arquivo informado não é um PDF.");

        var resultado = new FaturaBradescoArquivo
        {
            Arquivo = arquivo,
            NomeArquivo = IOPath.GetFileName(arquivo)
        };

        using var reader = new PdfReader(arquivo);
        using var pdf = new PdfDocument(reader);

        resultado.TotalPaginasPdf = pdf.GetNumberOfPages();
        if (resultado.TotalPaginasPdf <= 0)
            throw new InvalidDataException("O PDF não possui páginas.");

        var subfaturasPorChave = new Dictionary<string, FaturaBradescoSubfatura>(StringComparer.OrdinalIgnoreCase);
        var beneficiarioAtualPorSubfatura = new Dictionary<string, FaturaBradescoBeneficiario?>(StringComparer.OrdinalIgnoreCase);

        for (int paginaPdf = 1; paginaPdf <= pdf.GetNumberOfPages(); paginaPdf++)
        {
            PdfPage pagina = pdf.GetPage(paginaPdf);
            string textoPagina = PdfTextExtractor.GetTextFromPage(pagina) ?? string.Empty;
            string textoCompacto = CompactarEspacos(textoPagina);

            CabecalhoPagina? cabecalho = ExtrairCabecalho(textoCompacto);
            if (cabecalho == null)
                continue;

            if (!resultado.Competencia.HasValue)
                resultado.Competencia = cabecalho.Competencia;
            else if (resultado.Competencia.Value != cabecalho.Competencia)
                resultado.Avisos.Add($"Página {paginaPdf}: competência {cabecalho.Competencia:MM/yyyy} difere da competência principal {resultado.Competencia.Value:MM/yyyy}.");

            if (string.IsNullOrWhiteSpace(resultado.Apolice))
                resultado.Apolice = cabecalho.Apolice;
            else if (!string.Equals(resultado.Apolice, cabecalho.Apolice, StringComparison.OrdinalIgnoreCase))
                resultado.Avisos.Add($"Página {paginaPdf}: apólice {cabecalho.Apolice} difere da apólice principal {resultado.Apolice}.");

            if (cabecalho.NumeroSubfatura == 999)
            {
                resultado.PaginasSubfatura999Ignoradas.Add(paginaPdf);
                continue;
            }

            string chaveSubfatura = CriarChaveSubfatura(cabecalho.NumeroSubfatura, cabecalho.Entidade);
            if (!subfaturasPorChave.TryGetValue(chaveSubfatura, out FaturaBradescoSubfatura? subfatura))
            {
                subfatura = new FaturaBradescoSubfatura
                {
                    Numero = cabecalho.NumeroSubfatura,
                    Entidade = cabecalho.Entidade
                };
                subfaturasPorChave[chaveSubfatura] = subfatura;
                beneficiarioAtualPorSubfatura[chaveSubfatura] = null;
            }

            if (!subfatura.PaginasPdf.Contains(paginaPdf))
                subfatura.PaginasPdf.Add(paginaPdf);

            bool paginaDetalhe = EhPaginaDetalhe(textoPagina);
            if (!paginaDetalhe)
                continue;

            if (!subfatura.PaginasDetalhe.Contains(paginaPdf))
                subfatura.PaginasDetalhe.Add(paginaPdf);

            FaturaBradescoBeneficiario? beneficiarioAtual = beneficiarioAtualPorSubfatura[chaveSubfatura];
            bool contextoParticipacaoAtivo = beneficiarioAtual == null;

            IReadOnlyList<LinhaTabela> linhas = ExtrairLinhasTabela(pagina);
            foreach (LinhaTabela linha in linhas)
            {
                CamposLinha campos = ExtrairCampos(linha, pagina.GetPageSize().GetWidth());

                bool linhaContextoExplicita = EhLinhaContexto(campos);
                bool possuiCertificado = RegexCertificado.IsMatch(RemoverEspacos(campos.Certificado));
                DateTime? competenciaLancamento = InterpretarCompetencia(campos.CompetenciaLancamento);
                decimal? valor = InterpretarValor(campos.Valor);
                decimal? participacao = InterpretarValor(campos.Participacao);

                if (linhaContextoExplicita)
                {
                    contextoParticipacaoAtivo = true;
                    if (competenciaLancamento.HasValue && valor.HasValue)
                    {
                        resultado.LinhasContextoIgnoradas.Add(new FaturaBradescoLinhaContextoIgnorada
                        {
                            PaginaPdf = paginaPdf,
                            NumeroSubfatura = subfatura.Numero,
                            Entidade = subfatura.Entidade,
                            Movimento = campos.Movimento,
                            Competencia = competenciaLancamento,
                            Valor = valor,
                            Participacao = participacao,
                            Texto = campos.TextoReconstruido
                        });
                    }
                    continue;
                }

                if (!possuiCertificado && contextoParticipacaoAtivo)
                {
                    // Algumas faturas trazem mais de uma linha de COB REF PARTICIPAC.,
                    // mas só a primeira contém o texto descritivo. As linhas seguintes
                    // aparecem apenas com MOV/competência/valor e não pertencem a um beneficiário.
                    if (competenciaLancamento.HasValue && valor.HasValue)
                    {
                        resultado.LinhasContextoIgnoradas.Add(new FaturaBradescoLinhaContextoIgnorada
                        {
                            PaginaPdf = paginaPdf,
                            NumeroSubfatura = subfatura.Numero,
                            Entidade = subfatura.Entidade,
                            Movimento = campos.Movimento,
                            Competencia = competenciaLancamento,
                            Valor = valor,
                            Participacao = participacao,
                            Texto = campos.TextoReconstruido
                        });
                    }
                    continue;
                }

                if (possuiCertificado)
                {
                    contextoParticipacaoAtivo = false;

                    string certificado = RemoverEspacos(campos.Certificado);
                    string nome = CompactarEspacos(campos.NomeBeneficiario);
                    if (string.IsNullOrWhiteSpace(nome))
                    {
                        resultado.Avisos.Add($"Página {paginaPdf}: certificado {certificado} encontrado sem nome legível.");
                        continue;
                    }

                    string chaveBeneficiario = CriarChaveBeneficiario(certificado, nome);
                    if (!subfatura.BeneficiariosPorChave.TryGetValue(chaveBeneficiario, out beneficiarioAtual))
                    {
                        beneficiarioAtual = new FaturaBradescoBeneficiario
                        {
                            Certificado = certificado,
                            Nome = nome,
                            DataNascimento = InterpretarData(campos.DataNascimento),
                            Sexo = CompactarEspacos(campos.Sexo),
                            EstadoCivil = CompactarEspacos(campos.EstadoCivil),
                            Parentesco = CompactarEspacos(campos.Parentesco),
                            Plano = CompactarEspacos(campos.Plano),
                            DataInicio = InterpretarData(campos.DataInicio)
                        };

                        subfatura.BeneficiariosPorChave[chaveBeneficiario] = beneficiarioAtual;
                    }
                    else
                    {
                        CompletarMetadadosBeneficiario(beneficiarioAtual, campos);
                    }

                    beneficiarioAtualPorSubfatura[chaveSubfatura] = beneficiarioAtual;
                }

                if (beneficiarioAtual == null)
                {
                    if (competenciaLancamento.HasValue && valor.HasValue)
                    {
                        resultado.LinhasSemBeneficiario.Add(new FaturaBradescoLinhaSemBeneficiario
                        {
                            PaginaPdf = paginaPdf,
                            NumeroSubfatura = subfatura.Numero,
                            Entidade = subfatura.Entidade,
                            Texto = campos.TextoReconstruido
                        });
                    }
                    continue;
                }

                CompletarMetadadosBeneficiario(beneficiarioAtual, campos);

                if (competenciaLancamento.HasValue && valor.HasValue)
                {
                    beneficiarioAtual.Lancamentos.Add(new FaturaBradescoLancamento
                    {
                        PaginaPdf = paginaPdf,
                        PaginaFatura = cabecalho.PaginaImpressa,
                        Movimento = CompactarEspacos(campos.Movimento),
                        Plano = string.IsNullOrWhiteSpace(campos.Plano)
                            ? beneficiarioAtual.Plano
                            : CompactarEspacos(campos.Plano),
                        DataInicio = InterpretarData(campos.DataInicio) ?? beneficiarioAtual.DataInicio,
                        Competencia = competenciaLancamento.Value,
                        Valor = valor.Value,
                        Participacao = participacao,
                        TextoOrigem = campos.TextoReconstruido
                    });
                }
            }
        }

        resultado.Subfaturas.AddRange(
            subfaturasPorChave.Values
                .OrderBy(x => x.Numero)
                .ThenBy(x => x.Entidade, StringComparer.CurrentCultureIgnoreCase));

        foreach (FaturaBradescoSubfatura subfatura in resultado.Subfaturas)
        {
            subfatura.Beneficiarios.AddRange(
                subfatura.BeneficiariosPorChave.Values
                    .OrderBy(x => x.Certificado, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase));
        }

        if (!resultado.Competencia.HasValue)
            throw new InvalidDataException("Não foi possível identificar a competência da fatura.");

        if (string.IsNullOrWhiteSpace(resultado.Apolice))
            throw new InvalidDataException("Não foi possível identificar a apólice da fatura.");

        if (resultado.Subfaturas.Count == 0)
            throw new InvalidDataException("Nenhuma subfatura utilizável foi identificada. A Subfatura 999 é ignorada por ser totalizadora.");

        if (resultado.TotalPaginasDetalhe == 0)
            throw new InvalidDataException("Nenhuma página de detalhe de beneficiários foi identificada.");

        if (resultado.TotalBeneficiarios == 0)
            throw new InvalidDataException("Nenhum beneficiário foi identificado nas páginas de detalhe.");

        if (resultado.LinhasSemBeneficiario.Count > 0)
        {
            resultado.Avisos.Add(
                $"{resultado.LinhasSemBeneficiario.Count} linha(s) com competência/valor não puderam ser vinculadas a um beneficiário. Consulte o diagnóstico antes de avançar.");
        }

        return resultado;
    }

    private static CabecalhoPagina? ExtrairCabecalho(string textoCompacto)
    {
        Match match = RegexCabecalho.Match(textoCompacto);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups["numero"].Value, out int numeroSubfatura))
            return null;

        if (!int.TryParse(match.Groups["competencia"].Value, out int mes))
            return null;

        if (!int.TryParse(match.Groups["ano"].Value, out int ano))
            return null;

        int? paginaImpressa = null;
        Match pagMatch = Regex.Match(
            textoCompacto,
            @"878\s*-\s*MULTI\s+SAUDE\s+EMPRESA\s+\d{2}/\d{2}/\d{4}\s+(?<pagina>\d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (pagMatch.Success && int.TryParse(pagMatch.Groups["pagina"].Value, out int paginaLida))
            paginaImpressa = paginaLida;

        return new CabecalhoPagina(
            match.Groups["apolice"].Value,
            new DateTime(ano, mes, 1),
            numeroSubfatura,
            CompactarEspacos(match.Groups["entidade"].Value),
            paginaImpressa);
    }

    private static bool EhPaginaDetalhe(string texto)
    {
        string normalizado = RemoverAcentos(texto).ToUpperInvariant();
        return normalizado.Contains("CERTIF.", StringComparison.Ordinal) &&
               normalizado.Contains("NOME DO BENEFICIARIO", StringComparison.Ordinal) &&
               normalizado.Contains("LANCAMENTO", StringComparison.Ordinal) &&
               normalizado.Contains("MES/ANO", StringComparison.Ordinal);
    }

    private static IReadOnlyList<LinhaTabela> ExtrairLinhasTabela(PdfPage pagina)
    {
        var coletor = new ColetorGlifos();
        var processor = new PdfCanvasProcessor(new GlyphEventListener(coletor));
        processor.ProcessPageContent(pagina);

        var glifos = coletor.Glifos
            .Where(x => !string.IsNullOrEmpty(x.Texto))
            .OrderByDescending(x => x.Y)
            .ThenBy(x => x.XInicial)
            .ToList();

        var linhas = new List<LinhaTabela>();

        foreach (GlifoInfo glifo in glifos)
        {
            LinhaTabela? linha = linhas.Count > 0 ? linhas[^1] : null;
            if (linha == null || Math.Abs(linha.YReferencia - glifo.Y) > ToleranciaLinha)
            {
                linha = new LinhaTabela(glifo.Y);
                linhas.Add(linha);
            }

            linha.Glifos.Add(glifo);
        }

        return linhas;
    }

    private static CamposLinha ExtrairCampos(LinhaTabela linha, float larguraPagina)
    {
        if (larguraPagina <= 0)
            larguraPagina = 595f;

        string cert = MontarCampo(linha.Glifos, larguraPagina, 0.000f, 0.102f);
        string nome = MontarCampo(linha.Glifos, larguraPagina, 0.102f, 0.405f);
        string nascimento = MontarCampo(linha.Glifos, larguraPagina, 0.405f, 0.479f);
        string sexo = MontarCampo(linha.Glifos, larguraPagina, 0.479f, 0.514f);
        string civil = MontarCampo(linha.Glifos, larguraPagina, 0.514f, 0.556f);
        string parentesco = MontarCampo(linha.Glifos, larguraPagina, 0.556f, 0.596f);
        string plano = MontarCampo(linha.Glifos, larguraPagina, 0.596f, 0.640f);
        string inicio = MontarCampo(linha.Glifos, larguraPagina, 0.640f, 0.714f);
        string movimento = MontarCampo(linha.Glifos, larguraPagina, 0.714f, 0.741f);
        string competencia = MontarCampo(linha.Glifos, larguraPagina, 0.741f, 0.820f);
        string valor = MontarCampo(linha.Glifos, larguraPagina, 0.820f, 0.892f);
        string participacao = MontarCampo(linha.Glifos, larguraPagina, 0.892f, 1.010f);

        string reconstruido = string.Join(" | ", new[]
        {
            cert, nome, nascimento, sexo, civil, parentesco, plano, inicio,
            movimento, competencia, valor, participacao
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return new CamposLinha(
            cert,
            nome,
            nascimento,
            sexo,
            civil,
            parentesco,
            plano,
            inicio,
            movimento,
            competencia,
            valor,
            participacao,
            reconstruido);
    }

    private static string MontarCampo(
        IEnumerable<GlifoInfo> glifos,
        float larguraPagina,
        float inicioRazao,
        float fimRazao)
    {
        float xMin = larguraPagina * inicioRazao;
        float xMax = larguraPagina * fimRazao;

        var selecionados = glifos
            .Where(g => g.XCentro >= xMin && g.XCentro < xMax)
            .OrderBy(g => g.XInicial)
            .ToList();

        if (selecionados.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        float? fimAnterior = null;

        foreach (GlifoInfo glifo in selecionados)
        {
            string texto = glifo.Texto;
            if (string.IsNullOrEmpty(texto))
                continue;

            bool textoEhEspaco = texto.All(char.IsWhiteSpace);
            if (!textoEhEspaco && fimAnterior.HasValue &&
                glifo.XInicial - fimAnterior.Value > ToleranciaEspaco &&
                sb.Length > 0 && !char.IsWhiteSpace(sb[^1]))
            {
                sb.Append(' ');
            }

            sb.Append(texto);
            fimAnterior = Math.Max(fimAnterior ?? glifo.XFinal, glifo.XFinal);
        }

        return CompactarEspacos(sb.ToString());
    }

    private static bool EhLinhaContexto(CamposLinha campos)
    {
        string nome = RemoverAcentos(campos.NomeBeneficiario).ToUpperInvariant();
        return nome.StartsWith("COB REF PARTICIPAC", StringComparison.Ordinal) ||
               nome.StartsWith("COB. REF. PARTICIPAC", StringComparison.Ordinal);
    }

    private static void CompletarMetadadosBeneficiario(FaturaBradescoBeneficiario beneficiario, CamposLinha campos)
    {
        beneficiario.DataNascimento ??= InterpretarData(campos.DataNascimento);
        beneficiario.DataInicio ??= InterpretarData(campos.DataInicio);

        if (string.IsNullOrWhiteSpace(beneficiario.Sexo) && !string.IsNullOrWhiteSpace(campos.Sexo))
            beneficiario.Sexo = CompactarEspacos(campos.Sexo);
        if (string.IsNullOrWhiteSpace(beneficiario.EstadoCivil) && !string.IsNullOrWhiteSpace(campos.EstadoCivil))
            beneficiario.EstadoCivil = CompactarEspacos(campos.EstadoCivil);
        if (string.IsNullOrWhiteSpace(beneficiario.Parentesco) && !string.IsNullOrWhiteSpace(campos.Parentesco))
            beneficiario.Parentesco = CompactarEspacos(campos.Parentesco);
        if (string.IsNullOrWhiteSpace(beneficiario.Plano) && !string.IsNullOrWhiteSpace(campos.Plano))
            beneficiario.Plano = CompactarEspacos(campos.Plano);
    }

    private static DateTime? InterpretarData(string texto)
    {
        if (DateTime.TryParseExact(
            texto.Trim(),
            "dd/MM/yyyy",
            CultureInfo.GetCultureInfo("pt-BR"),
            DateTimeStyles.None,
            out DateTime data))
        {
            return data.Date;
        }

        return null;
    }

    private static DateTime? InterpretarCompetencia(string texto)
    {
        Match match = RegexCompetencia.Match(texto.Trim());
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups["mes"].Value, out int mes) ||
            !int.TryParse(match.Groups["ano"].Value, out int ano))
            return null;

        return new DateTime(ano, mes, 1);
    }

    private static decimal? InterpretarValor(string texto)
    {
        string valor = texto.Trim();
        if (string.IsNullOrWhiteSpace(valor) || !RegexValor.IsMatch(valor))
            return null;

        bool negativo = valor.EndsWith("-", StringComparison.Ordinal) ||
                        valor.StartsWith("-", StringComparison.Ordinal);

        valor = valor.Trim('-');
        valor = valor.Replace(".", string.Empty, StringComparison.Ordinal)
                     .Replace(',', '.');

        if (!decimal.TryParse(
            valor,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out decimal resultado))
        {
            return null;
        }

        return negativo ? -resultado : resultado;
    }

    private static string CriarChaveSubfatura(int numero, string entidade)
        => $"{numero}|{RemoverAcentos(CompactarEspacos(entidade)).ToUpperInvariant()}";

    private static string CriarChaveBeneficiario(string certificado, string nome)
        => $"{certificado}|{RemoverAcentos(CompactarEspacos(nome)).ToUpperInvariant()}";

    private static string RemoverEspacos(string texto)
        => Regex.Replace(texto ?? string.Empty, @"\s+", string.Empty);

    private static string CompactarEspacos(string texto)
        => Regex.Replace(texto ?? string.Empty, @"\s+", " ").Trim();

    private static string RemoverAcentos(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        string normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);

        foreach (char c in normalizado)
        {
            UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed class ColetorGlifos : IEventListener
    {
        public List<GlifoInfo> Glifos { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT || data is not TextRenderInfo info)
                return;

            string texto = info.GetText() ?? string.Empty;
            if (string.IsNullOrEmpty(texto))
                return;

            LineSegment baseline = info.GetBaseline();
            Vector inicio = baseline.GetStartPoint();
            Vector fim = baseline.GetEndPoint();

            float xInicial = Math.Min(inicio.Get(0), fim.Get(0));
            float xFinal = Math.Max(inicio.Get(0), fim.Get(0));
            float y = (inicio.Get(1) + fim.Get(1)) / 2f;

            Glifos.Add(new GlifoInfo(texto, xInicial, xFinal, y));
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    private sealed record CabecalhoPagina(
        string Apolice,
        DateTime Competencia,
        int NumeroSubfatura,
        string Entidade,
        int? PaginaImpressa);

    private sealed record GlifoInfo(string Texto, float XInicial, float XFinal, float Y)
    {
        public float XCentro => (XInicial + XFinal) / 2f;
    }

    private sealed class LinhaTabela
    {
        public LinhaTabela(float yReferencia) => YReferencia = yReferencia;
        public float YReferencia { get; }
        public List<GlifoInfo> Glifos { get; } = new();
    }

    private sealed record CamposLinha(
        string Certificado,
        string NomeBeneficiario,
        string DataNascimento,
        string Sexo,
        string EstadoCivil,
        string Parentesco,
        string Plano,
        string DataInicio,
        string Movimento,
        string CompetenciaLancamento,
        string Valor,
        string Participacao,
        string TextoReconstruido);
}

public sealed class FaturaBradescoArquivo
{
    public string Arquivo { get; init; } = string.Empty;
    public string NomeArquivo { get; init; } = string.Empty;
    public DateTime? Competencia { get; set; }
    public string Apolice { get; set; } = string.Empty;
    public int TotalPaginasPdf { get; set; }
    public List<FaturaBradescoSubfatura> Subfaturas { get; } = new();
    public List<int> PaginasSubfatura999Ignoradas { get; } = new();
    public List<FaturaBradescoLinhaContextoIgnorada> LinhasContextoIgnoradas { get; } = new();
    public List<FaturaBradescoLinhaSemBeneficiario> LinhasSemBeneficiario { get; } = new();
    public List<string> Avisos { get; } = new();

    public int TotalPaginasDetalhe => Subfaturas.Sum(x => x.PaginasDetalhe.Count);
    public int TotalBeneficiarios => Subfaturas.Sum(x => x.Beneficiarios.Count);
    public int TotalLancamentos => Subfaturas.Sum(x => x.Beneficiarios.Sum(b => b.Lancamentos.Count));
}

public sealed class FaturaBradescoSubfatura
{
    public int Numero { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public List<int> PaginasPdf { get; } = new();
    public List<int> PaginasDetalhe { get; } = new();
    public List<FaturaBradescoBeneficiario> Beneficiarios { get; } = new();

    internal Dictionary<string, FaturaBradescoBeneficiario> BeneficiariosPorChave { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FaturaBradescoBeneficiario
{
    public string Certificado { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public DateTime? DataNascimento { get; set; }
    public string Sexo { get; set; } = string.Empty;
    public string EstadoCivil { get; set; } = string.Empty;
    public string Parentesco { get; set; } = string.Empty;
    public string Plano { get; set; } = string.Empty;
    public DateTime? DataInicio { get; set; }
    public List<FaturaBradescoLancamento> Lancamentos { get; } = new();
}

public sealed class FaturaBradescoLancamento
{
    public int PaginaPdf { get; init; }
    public int? PaginaFatura { get; init; }
    public string Movimento { get; init; } = string.Empty;
    public string Plano { get; init; } = string.Empty;
    public DateTime? DataInicio { get; init; }
    public DateTime Competencia { get; init; }
    public decimal Valor { get; init; }
    public decimal? Participacao { get; init; }
    public string TextoOrigem { get; init; } = string.Empty;
}

public sealed class FaturaBradescoLinhaContextoIgnorada
{
    public int PaginaPdf { get; init; }
    public int NumeroSubfatura { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public string Movimento { get; init; } = string.Empty;
    public DateTime? Competencia { get; init; }
    public decimal? Valor { get; init; }
    public decimal? Participacao { get; init; }
    public string Texto { get; init; } = string.Empty;
}

public sealed class FaturaBradescoLinhaSemBeneficiario
{
    public int PaginaPdf { get; init; }
    public int NumeroSubfatura { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public string Texto { get; init; } = string.Empty;
}
