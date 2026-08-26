using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace HubFinanceiro;

public sealed class AnaliseFaturasEmailPreparacaoResultado
{
    public int QuantidadeEmails { get; init; }
    public int QuantidadeRegistros { get; init; }
    public bool RemetenteLocalizadoNasContas { get; init; }
}

/// <summary>
/// Monta e exibe rascunhos no Outlook Classic. Nenhuma mensagem é enviada pelo HUB.
/// </summary>
public sealed class AnaliseFaturasEmailService
{
    public const string Remetente = "conferencia.fatura@positiva.com.br";
    public const string Destinatario = "Faturamento@positiva.com.br";
    public const string Copia = "gliciele.silva@positiva.com.br";
    public const string NomeAssinatura = "Ale - Padrão Financeiro";

    private const string PropriedadeContentId = "http://schemas.microsoft.com/mapi/proptag/0x3712001F";
    private const string PropriedadeAnexoOculto = "http://schemas.microsoft.com/mapi/proptag/0x7FFE000B";
    private const int TipoItemEmail = 0;
    private const int FormatoCorpoHtml = 2;
    private const int TipoAnexoPorValor = 1;
    private const int FecharDescartando = 1;

    private static readonly Regex BodyRegex = new(
        @"<body\b[^>]*>(?<conteudo>[\s\S]*?)</body>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex StyleRegex = new(
        @"<style\b[^>]*>[\s\S]*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ImagemRegex = new(
        "(?<inicio>\\bsrc\\s*=\\s*[\"'])(?<caminho>[^\"']+)(?<fim>[\"'])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TagHtmlRegex = new(
        @"<[^>]+>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PrefixoContainersRegex = new(
        @"^(?:\s|<div\b[^>]*>|<!--[\s\S]*?-->)*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TermoOverRegex = new(
        @"\bOver\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<AnaliseFinalResultado> SelecionarDivergenciasSemExplicacao(
        IEnumerable<AnaliseFinalResultado> resultados)
    {
        if (resultados == null)
            throw new ArgumentNullException(nameof(resultados));

        return resultados
            .Where(x => x.Status == AnaliseFinalStatus.DivergenciaPendente || x.Status == AnaliseFinalStatus.Ambiguo)
            .Where(x => string.IsNullOrWhiteSpace(x.JustificativaManual))
            .ToList();
    }

    public AnaliseFaturasEmailPreparacaoResultado PrepararRascunhos(
        DateTime competencia,
        IReadOnlyList<AnaliseFaturasExcelRelatorio> relatorios,
        DateTime horarioAtual)
    {
        if (relatorios == null)
            throw new ArgumentNullException(nameof(relatorios));

        List<AnaliseFaturasExcelRelatorio> anexos = relatorios
            .Where(x => x != null)
            .OrderBy(x => x.TipoDivergencia, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (anexos.Count == 0)
            throw new InvalidOperationException("Não existem relatórios para preparar os e-mails.");

        foreach (AnaliseFaturasExcelRelatorio relatorio in anexos)
        {
            if (string.IsNullOrWhiteSpace(relatorio.CaminhoArquivo) || !File.Exists(relatorio.CaminhoArquivo))
                throw new FileNotFoundException("Um dos relatórios que seria anexado não foi encontrado.", relatorio.CaminhoArquivo);
        }

        // A assinatura é um acabamento opcional: nunca pode impedir a criação
        // dos rascunhos. Primeiro tenta a assinatura local pelo nome; se ela
        // não existir, o fluxo ainda poderá aproveitar a assinatura padrão que
        // o próprio Outlook inserir ao abrir a janela de composição.
        AssinaturaOutlook assinaturaLocal = CarregarAssinaturaOpcional(NomeAssinatura);
        var mensagensAbertas = new List<object>();
        object? outlook = null;
        object? sessao = null;
        object? contas = null;
        object? contaRemetente = null;
        string etapaAtual = "iniciar o Outlook Classic";
        string pastaAnexosOutlook = Path.Combine(
            AppContext.BaseDirectory,
            "temporarios",
            "AnexosOutlook",
            Guid.NewGuid().ToString("N"));
        int indiceAnexo = 0;

        try
        {
            Type? tipoOutlook = Type.GetTypeFromProgID("Outlook.Application");
            if (tipoOutlook == null)
            {
                throw new InvalidOperationException(
                    "O Outlook Classic não está instalado ou não está registrado no Windows.");
            }

            outlook = Activator.CreateInstance(tipoOutlook)
                ?? throw new InvalidOperationException("Não foi possível iniciar o Outlook Classic.");

            etapaAtual = "acessar o perfil e as contas do Outlook Classic";
            dynamic outlookCom = outlook;
            sessao = outlookCom.Session;
            dynamic sessaoCom = sessao;
            contas = sessaoCom.Accounts;
            contaRemetente = LocalizarConta(contas, Remetente);

            foreach (AnaliseFaturasExcelRelatorio relatorio in anexos)
            {
                etapaAtual = $"criar a mensagem de '{relatorio.TipoDivergencia}'";
                object email = outlookCom.CreateItem(TipoItemEmail);
                mensagensAbertas.Add(email);
                dynamic emailCom = email;

                etapaAtual = $"configurar remetente, destinatários e assunto de '{relatorio.TipoDivergencia}'";
                ConfigurarRemetente(email, contaRemetente);
                emailCom.To = Destinatario;
                emailCom.CC = Copia;
                emailCom.Subject = CriarAssunto(relatorio.TipoDivergencia, competencia);
                emailCom.BodyFormat = FormatoCorpoHtml;

                // Display cria a janela e permite que o Outlook insira sua assinatura
                // padrão. A assinatura local nomeada tem prioridade; quando não existe,
                // preservamos a que o próprio Outlook tiver inserido. Esta é a única
                // ação final sobre a mensagem: o HUB não chama Send nem Save.
                etapaAtual = $"abrir para revisão a mensagem de '{relatorio.TipoDivergencia}'";
                emailCom.Display(false);

                AssinaturaOutlook assinaturaEmail = assinaturaLocal;
                if (!assinaturaEmail.TemConteudo)
                {
                    string htmlInseridoPeloOutlook =
                        Convert.ToString(emailCom.HTMLBody) ?? string.Empty;
                    assinaturaEmail = ExtrairAssinaturaDoHtml(htmlInseridoPeloOutlook);
                }

                etapaAtual = $"montar o texto da mensagem de '{relatorio.TipoDivergencia}'";
                emailCom.HTMLBody = CriarCorpoHtml(
                    relatorio.TipoDivergencia,
                    competencia,
                    horarioAtual,
                    assinaturaEmail.HtmlCorpo,
                    assinaturaEmail.Estilos);

                etapaAtual = $"anexar o relatório de '{relatorio.TipoDivergencia}'";
                AnexarImagensDaAssinatura(email, assinaturaEmail.Imagens);
                string nomeExibicao = Path.GetFileName(relatorio.CaminhoArquivo);
                string caminhoCurto = CriarCopiaTemporariaParaOutlook(
                    relatorio.CaminhoArquivo,
                    pastaAnexosOutlook,
                    ++indiceAnexo);
                try
                {
                    AnexarArquivo(email, caminhoCurto, nomeExibicao);
                }
                finally
                {
                    ExcluirArquivoTemporario(caminhoCurto);
                }

                object? destinatarios = null;
                try
                {
                    etapaAtual = $"validar os destinatários da mensagem de '{relatorio.TipoDivergencia}'";
                    destinatarios = emailCom.Recipients;
                    dynamic destinatariosCom = destinatarios;
                    bool resolveuTodos = Convert.ToBoolean(destinatariosCom.ResolveAll());
                    if (!resolveuTodos)
                    {
                        string assunto = Convert.ToString(emailCom.Subject) ?? string.Empty;
                        throw new InvalidOperationException(
                            $"O Outlook não conseguiu resolver um ou mais destinatários do e-mail '{assunto}'.");
                    }
                }
                finally
                {
                    LiberarCom(destinatarios);
                }
            }

            return new AnaliseFaturasEmailPreparacaoResultado
            {
                QuantidadeEmails = anexos.Count,
                QuantidadeRegistros = anexos.Sum(x => x.QuantidadeRegistros),
                RemetenteLocalizadoNasContas = contaRemetente != null
            };
        }
        catch (Exception ex)
        {
            // Não deixa janelas incompletas misturadas com os rascunhos válidos.
            foreach (object email in mensagensAbertas)
            {
                try
                {
                    dynamic emailCom = email;
                    emailCom.Close(FecharDescartando);
                }
                catch { }
            }

            throw new InvalidOperationException(
                $"Falha ao {etapaAtual}.\n\nDetalhes: {ObterMensagemMaisInterna(ex)}",
                ex);
        }
        finally
        {
            foreach (object email in mensagensAbertas)
                LiberarCom(email);

            LiberarCom(contaRemetente);
            LiberarCom(contas);
            LiberarCom(sessao);
            LiberarCom(outlook);
            ExcluirPastaTemporaria(pastaAnexosOutlook);
        }
    }

    public static string CriarAssunto(string tipoDivergencia, DateTime competencia)
    {
        string tipo = string.IsNullOrWhiteSpace(tipoDivergencia)
            ? "Divergência não classificada"
            : ExibirOverComoProtheus(tipoDivergencia.Trim());

        return $"Divergências de faturas - {tipo} - {competencia:MM/yyyy}";
    }

    public static string ExibirOverComoProtheus(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto ?? string.Empty;

        return TermoOverRegex.Replace(texto, "Protheus");
    }

    public static string CriarCorpoHtml(
        string tipoDivergencia,
        DateTime competencia,
        DateTime horarioAtual,
        string htmlAssinatura,
        string estilosAssinatura = "")
    {
        string saudacao = horarioAtual.Hour < 12 ? "bom dia" : "boa tarde";
        string tipoOriginal = string.IsNullOrWhiteSpace(tipoDivergencia)
            ? "Divergência não classificada"
            : tipoDivergencia.Trim();
        string tipo = WebUtility.HtmlEncode(ExibirOverComoProtheus(tipoOriginal));
        string periodo = WebUtility.HtmlEncode(competencia.ToString("MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")));

        return "<html><head><meta charset=\"utf-8\">" + estilosAssinatura + "</head>" +
               "<body style=\"font-family:Aptos,Calibri,Arial,sans-serif;font-size:12pt;color:#153D64\">" +
               $"<p style=\"margin:0 0 12pt 0\">Prezados, {saudacao}.</p>" +
               $"<p style=\"margin:0 0 12pt 0\">Segue, em anexo, o relatório das divergências do tipo <strong>{tipo}</strong>, " +
               $"referentes à competência <strong>{periodo}</strong>.</p>" +
               "<p style=\"margin:0 0 6pt 0\">O arquivo contém somente os casos que permanecem sem explicação registrada no HUB Financeiro. " +
               "Solicito, por gentileza, a conferência e o retorno sobre as ocorrências apresentadas.</p>" +
               htmlAssinatura + "</body></html>";
    }

    private static void ConfigurarRemetente(object email, object? contaRemetente)
    {
        dynamic emailCom = email;
        if (contaRemetente != null)
            emailCom.SendUsingAccount = contaRemetente;
        else
            emailCom.SentOnBehalfOfName = Remetente;
    }

    private static object? LocalizarConta(object contas, string smtpProcurado)
    {
        dynamic contasCom = contas;
        int quantidade = Convert.ToInt32(contasCom.Count);
        for (int indice = 1; indice <= quantidade; indice++)
        {
            object? conta = null;
            try
            {
                conta = contasCom.Item(indice);
                dynamic contaCom = conta;
                string smtp = Convert.ToString(contaCom.SmtpAddress) ?? string.Empty;
                if (string.Equals(smtp, smtpProcurado, StringComparison.OrdinalIgnoreCase))
                    return conta;
            }
            catch
            {
                // Algumas contas locais não expõem SMTP. Elas apenas não servem como remetente.
            }

            LiberarCom(conta);
        }

        return null;
    }

    private static void AnexarArquivo(object email, string caminho, string nomeExibicao)
    {
        object? anexos = null;
        object? anexo = null;
        try
        {
            dynamic emailCom = email;
            anexos = emailCom.Attachments;
            dynamic anexosCom = anexos;
            anexo = anexosCom.Add(caminho, TipoAnexoPorValor, Type.Missing, nomeExibicao);
        }
        finally
        {
            LiberarCom(anexo);
            LiberarCom(anexos);
        }
    }

    private static string CriarCopiaTemporariaParaOutlook(
        string caminhoOriginal,
        string pastaDestino,
        int indice)
    {
        string caminhoAbsoluto = Path.GetFullPath(caminhoOriginal);
        if (!File.Exists(caminhoAbsoluto))
        {
            throw new FileNotFoundException(
                "O relatório deixou de estar disponível antes de ser anexado.",
                caminhoAbsoluto);
        }

        Directory.CreateDirectory(pastaDestino);

        string extensao = Path.GetExtension(caminhoAbsoluto);
        if (string.IsNullOrWhiteSpace(extensao))
            extensao = ".xlsx";

        // O Outlook Classic ainda pode rejeitar caminhos próximos do limite
        // legado de 260 caracteres, principalmente em pastas do OneDrive.
        // O nome curto é usado somente como origem do anexo; o nome exibido na
        // mensagem continua sendo o nome descritivo do relatório original.
        string caminhoTemporario = Path.Combine(pastaDestino, $"anexo-{indice:00}{extensao}");
        File.Copy(caminhoAbsoluto, caminhoTemporario, overwrite: true);

        if (!File.Exists(caminhoTemporario))
        {
            throw new FileNotFoundException(
                "Não foi possível preparar uma cópia temporária do relatório para o Outlook.",
                caminhoTemporario);
        }

        return caminhoTemporario;
    }

    private static void ExcluirArquivoTemporario(string caminho)
    {
        try
        {
            if (File.Exists(caminho))
                File.Delete(caminho);
        }
        catch
        {
            // A limpeza final fará uma segunda tentativa. Uma eventual trava do
            // Outlook não pode invalidar mensagens que já foram preparadas.
        }
    }

    private static void ExcluirPastaTemporaria(string pasta)
    {
        try
        {
            if (!Directory.Exists(pasta))
                return;

            foreach (string arquivo in Directory.EnumerateFiles(
                         pasta,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                ExcluirArquivoTemporario(arquivo);
            }

            if (!Directory.EnumerateFileSystemEntries(pasta).Any())
                Directory.Delete(pasta, recursive: false);

            string? pastaAnexos = Directory.GetParent(pasta)?.FullName;
            if (!string.IsNullOrWhiteSpace(pastaAnexos) &&
                Directory.Exists(pastaAnexos) &&
                !Directory.EnumerateFileSystemEntries(pastaAnexos).Any())
            {
                Directory.Delete(pastaAnexos, recursive: false);
            }

            string? pastaTemporarios = pastaAnexos == null
                ? null
                : Directory.GetParent(pastaAnexos)?.FullName;
            if (!string.IsNullOrWhiteSpace(pastaTemporarios) &&
                Directory.Exists(pastaTemporarios) &&
                !Directory.EnumerateFileSystemEntries(pastaTemporarios).Any())
            {
                Directory.Delete(pastaTemporarios, recursive: false);
            }
        }
        catch
        {
            // Somente arquivos temporários desta preparação são considerados.
            // Se algum deles estiver momentaneamente bloqueado, não afeta o e-mail.
        }
    }

    private static void AnexarImagensDaAssinatura(
        object email,
        IReadOnlyList<ImagemAssinaturaOutlook> imagens)
    {
        foreach (ImagemAssinaturaOutlook imagem in imagens)
        {
            object? anexos = null;
            object? anexo = null;
            object? propriedades = null;
            try
            {
                dynamic emailCom = email;
                anexos = emailCom.Attachments;
                dynamic anexosCom = anexos;
                anexo = anexosCom.Add(
                    imagem.CaminhoArquivo,
                    TipoAnexoPorValor,
                    Type.Missing,
                    Path.GetFileName(imagem.CaminhoArquivo));

                dynamic anexoCom = anexo;
                propriedades = anexoCom.PropertyAccessor;
                dynamic propriedadesCom = propriedades;
                propriedadesCom.SetProperty(PropriedadeContentId, imagem.ContentId);
                try { propriedadesCom.SetProperty(PropriedadeAnexoOculto, true); }
                catch { /* A ocultação é cosmética; o Content-ID é o vínculo essencial. */ }
            }
            finally
            {
                LiberarCom(propriedades);
                LiberarCom(anexo);
                LiberarCom(anexos);
            }
        }
    }

    private static AssinaturaOutlook CarregarAssinatura(string nomeAssinatura)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string pastaAssinaturas = Path.Combine(appData, "Microsoft", "Signatures");
        if (!Directory.Exists(pastaAssinaturas))
        {
            throw new DirectoryNotFoundException(
                $"A pasta de assinaturas do Outlook Classic não foi encontrada: {pastaAssinaturas}");
        }

        string? arquivoHtml = Directory.EnumerateFiles(pastaAssinaturas, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(x =>
                (string.Equals(Path.GetExtension(x), ".htm", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetExtension(x), ".html", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(Path.GetFileNameWithoutExtension(x), nomeAssinatura, StringComparison.OrdinalIgnoreCase));

        if (arquivoHtml == null)
        {
            throw new FileNotFoundException(
                $"A assinatura '{nomeAssinatura}' não foi encontrada nas assinaturas locais do Outlook Classic. " +
                "Abra o Outlook, confirme esse nome em Assinaturas e salve-a neste computador.");
        }

        string html = LerHtml(arquivoHtml);
        string estilos = string.Concat(StyleRegex.Matches(html).Cast<Match>().Select(x => x.Value));
        Match body = BodyRegex.Match(html);
        string corpo = RemoverParagrafosVaziosIniciais(
            body.Success ? body.Groups["conteudo"].Value : html);
        string pastaHtml = Path.GetDirectoryName(arquivoHtml) ?? pastaAssinaturas;

        var imagens = new List<ImagemAssinaturaOutlook>();
        var imagensPorCaminho = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        corpo = ImagemRegex.Replace(corpo, match =>
        {
            string valor = WebUtility.HtmlDecode(match.Groups["caminho"].Value).Trim();
            if (valor.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            string caminho;
            try
            {
                caminho = valor.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(valor).LocalPath
                    : Path.GetFullPath(Path.Combine(
                        pastaHtml,
                        Uri.UnescapeDataString(valor).Replace('/', Path.DirectorySeparatorChar)));
            }
            catch
            {
                return match.Value;
            }

            if (!File.Exists(caminho))
                return match.Value;

            if (!imagensPorCaminho.TryGetValue(caminho, out string? contentId))
            {
                contentId = $"assinatura-{Guid.NewGuid():N}@hubfinanceiro";
                imagensPorCaminho[caminho] = contentId;
                imagens.Add(new ImagemAssinaturaOutlook(caminho, contentId));
            }

            return match.Groups["inicio"].Value + "cid:" + contentId + match.Groups["fim"].Value;
        });

        return new AssinaturaOutlook(corpo, estilos, imagens);
    }

    private static AssinaturaOutlook CarregarAssinaturaOpcional(string nomeAssinatura)
    {
        try
        {
            return CarregarAssinatura(nomeAssinatura);
        }
        catch
        {
            return AssinaturaOutlook.Vazia;
        }
    }

    private static AssinaturaOutlook ExtrairAssinaturaDoHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return AssinaturaOutlook.Vazia;

        string estilos = string.Concat(StyleRegex.Matches(html).Cast<Match>().Select(x => x.Value));
        Match body = BodyRegex.Match(html);
        string corpo = RemoverParagrafosVaziosIniciais(
            body.Success ? body.Groups["conteudo"].Value : html);
        return new AssinaturaOutlook(corpo, estilos, Array.Empty<ImagemAssinaturaOutlook>());
    }

    private static string RemoverParagrafosVaziosIniciais(string htmlCorpo)
    {
        string resultado = htmlCorpo.Trim();

        while (true)
        {
            int inicioParagrafo = resultado.IndexOf("<p", StringComparison.OrdinalIgnoreCase);
            if (inicioParagrafo < 0)
                break;

            string prefixo = resultado[..inicioParagrafo];
            if (!PrefixoContainersRegex.IsMatch(prefixo))
                break;

            int fimAbertura = resultado.IndexOf('>', inicioParagrafo);
            if (fimAbertura < 0)
                break;

            int fimParagrafo = resultado.IndexOf("</p>", fimAbertura + 1, StringComparison.OrdinalIgnoreCase);
            if (fimParagrafo < 0)
                break;

            string conteudo = resultado[(fimAbertura + 1)..fimParagrafo];
            string textoVisivel = WebUtility.HtmlDecode(TagHtmlRegex.Replace(conteudo, string.Empty))
                .Replace("\u200B", string.Empty, StringComparison.Ordinal)
                .Trim();

            if (!string.IsNullOrWhiteSpace(textoVisivel))
                break;

            resultado = resultado.Remove(
                inicioParagrafo,
                fimParagrafo + "</p>".Length - inicioParagrafo);
        }

        return resultado.Trim();
    }

    private static string LerHtml(string caminho)
    {
        byte[] bytes = File.ReadAllBytes(caminho);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        string preliminar = Encoding.Latin1.GetString(bytes);
        if (preliminar.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase) ||
            preliminar.Contains("charset=\"utf-8\"", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return DecodificarWindows1252(bytes);
    }

    private static string DecodificarWindows1252(byte[] bytes)
    {
        const string tabelaEspecial = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
        var resultado = new StringBuilder(bytes.Length);
        foreach (byte valor in bytes)
        {
            resultado.Append(valor is >= 0x80 and <= 0x9F
                ? tabelaEspecial[valor - 0x80]
                : (char)valor);
        }

        return resultado.ToString();
    }

    private static void LiberarCom(object? objeto)
    {
        if (objeto != null && Marshal.IsComObject(objeto))
        {
            try { Marshal.FinalReleaseComObject(objeto); }
            catch { }
        }
    }

    private static string ObterMensagemMaisInterna(Exception excecao)
    {
        Exception atual = excecao;
        while (atual.InnerException != null)
            atual = atual.InnerException;

        return string.IsNullOrWhiteSpace(atual.Message)
            ? atual.GetType().Name
            : atual.Message;
    }

    private sealed record AssinaturaOutlook(
        string HtmlCorpo,
        string Estilos,
        IReadOnlyList<ImagemAssinaturaOutlook> Imagens)
    {
        public static AssinaturaOutlook Vazia { get; } =
            new(string.Empty, string.Empty, Array.Empty<ImagemAssinaturaOutlook>());

        public bool TemConteudo => !string.IsNullOrWhiteSpace(HtmlCorpo);
    }

    private sealed record ImagemAssinaturaOutlook(string CaminhoArquivo, string ContentId);
}
