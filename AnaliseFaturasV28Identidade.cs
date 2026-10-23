using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace HubFinanceiro;

/// <summary>
/// Reconciliação conservadora de beneficiários sem cartão válido no Over.
/// Nunca altera o cartão original, valores financeiros nem as faturas.
/// A trilha de decisões é copiada para RegrasTestadas no resultado final, sendo
/// assim preservada nos snapshots do histórico de análises.
/// </summary>
public static class AnaliseFaturasV28Identidade
{
    private sealed class Marcacao
    {
        public string Certificado { get; set; } = string.Empty;
        public IReadOnlyList<RegraAnaliseResultado> Trilhas { get; set; } = Array.Empty<RegraAnaliseResultado>();
        // Um cartao estruturalmente valido pode apontar para OUTRA pessoa na fatura.
        // Nesse caso nao usar o certificado original sem uma conciliacao segura.
        public bool CartaoOriginalConflitante { get; set; }
    }

    private sealed class GrupoOver
    {
        public string Apolice { get; init; } = string.Empty;
        public string Nome { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public string Matricula { get; init; } = string.Empty;
        public List<OverLancamento> Lancamentos { get; init; } = new();
        public string CertificadoProposto { get; set; } = string.Empty;
        public string IdProposto { get; set; } = string.Empty;
        public bool CartaoOriginalConflitante { get; init; }
        public List<RegraAnaliseResultado> Log { get; } = new();
    }

    private sealed class Candidato
    {
        public string Id { get; init; } = string.Empty;
        public string Apolice { get; init; } = string.Empty;
        public string Certificado { get; init; } = string.Empty;
        public string Nome { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public decimal Valor { get; init; }
    }

    private sealed class Avaliacao
    {
        public Candidato Pessoa { get; init; } = new();
        public bool NomeLiteral { get; init; }
        public bool NomeCompativel { get; init; }
        public bool Familiar { get; init; }
        public bool Entidade { get; init; }
        public bool Valor { get; init; }
        public bool Aceitavel { get; init; }
        public int Evidencias { get; init; }
        public string Motivo { get; init; } = string.Empty;
    }

    private static readonly ConditionalWeakTable<OverLancamento, Marcacao> Marcacoes = new();

    public static string ObterCertificado(OverLancamento lancamento)
    {
        if (lancamento == null) return string.Empty;
        // O bloqueio DEVE preceder o atalho do cartao valido.
        // Caso contrario um cartao trocado nunca pode ser corrigido.
        if (Marcacoes.TryGetValue(lancamento, out Marcacao? marcacao) &&
            marcacao.CartaoOriginalConflitante)
            return marcacao.Certificado;

        string original = AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(lancamento.Cartao) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(original)) return original;
        return marcacao?.Certificado ?? string.Empty;
    }

    public static IReadOnlyList<RegraAnaliseResultado> ObterAuditoria(
        OverArquivo over, ComparacaoPrincipalResultado comparacao)
    {
        if (over == null || comparacao == null ||
            string.IsNullOrWhiteSpace(comparacao.OverOcorrenciaId) ||
            !comparacao.OverOcorrenciaId.StartsWith("O|", StringComparison.Ordinal))
            return Array.Empty<RegraAnaliseResultado>();

        var linhas = new HashSet<int>();
        foreach (string trecho in comparacao.OverOcorrenciaId.Substring(2).Split(','))
            if (int.TryParse(trecho.Trim(), out int numero)) linhas.Add(numero);

        return over.Lancamentos
            .Where(x => linhas.Contains(x.NumeroLinha))
            .SelectMany(x => Marcacoes.TryGetValue(x, out Marcacao? m) ? m.Trilhas : Array.Empty<RegraAnaliseResultado>())
            .GroupBy(x => $"{x.NomeDaRegra}|{x.DadosUtilizados}|{x.Justificativa}", StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
    }

    public static void Conciliar(
        IReadOnlyList<FaturaBradescoArquivo> faturas,
        OverArquivo over,
        bool ignorarCoparticipacao = true,
        bool ignorarCompetenciasAnteriores = true)
    {
        if (faturas == null) throw new ArgumentNullException(nameof(faturas));
        if (over == null) throw new ArgumentNullException(nameof(over));

        // Garante que reanálises não herdem decisões de execuções anteriores.
        foreach (OverLancamento lancamento in over.Lancamentos)
        {
            Marcacao marca = Marcacoes.GetValue(lancamento, _ => new Marcacao());
            marca.Certificado = string.Empty;
            marca.CartaoOriginalConflitante = false;
            marca.Trilhas = Array.Empty<RegraAnaliseResultado>();
        }

        DateTime competencia = over.Competencia ?? faturas.FirstOrDefault(x => x.Competencia.HasValue)?.Competencia ?? DateTime.MinValue;
        List<Candidato> candidatos = faturas.SelectMany(a => a.Subfaturas.SelectMany(s => s.Beneficiarios.Select(b =>
            new Candidato
            {
                Id = $"{a.NomeArquivo}|{s.Numero}|{b.Certificado}|{b.Nome}",
                Apolice = Codigo(a.Apolice),
                Certificado = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(b.Certificado) ?? string.Empty,
                Nome = b.Nome,
                Entidade = s.Entidade,
                Valor = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
                    b.Lancamentos.Where(l => AnaliseFaturasRegrasComparacao.ConsiderarCompetenciaFatura(
                            l.Competencia, competencia, ignorarCompetenciasAnteriores)).Sum(l => l.Valor))
            }))).Where(x => !string.IsNullOrEmpty(x.Certificado)).ToList();

        // V30: verifica cartoes validos contra a identidade da fatura, ANTES de
        // reservar certificados. Nao presumir que 15 digitos significam pessoa correta.
        VerificarIntegridadeCartoesValidos(candidatos, over, ignorarCoparticipacao);

        // Apenas cartoes validados reservam certificados. Cartoes em conflito
        // ficam em quarentena e passam pela verificacao alternativa auditada.
        var certificadosOcupados = new HashSet<string>(over.Lancamentos
            .Select(l => $"{Codigo(l.Apolice)}|{ObterCertificado(l)}")
            .Where(x => !x.EndsWith("|", StringComparison.Ordinal)), StringComparer.OrdinalIgnoreCase);

        var grupos = over.Lancamentos
            .GroupBy(x => new { Apolice = Codigo(x.Apolice), Nome = Normalizar(x.Beneficiario),
                Entidade = Normalizar(x.Entidade), Matricula = Digitos(x.Matricula) })
            .Where(g => g.All(x => string.IsNullOrWhiteSpace(ObterCertificado(x))))
            .Select(g => new GrupoOver
            {
                Apolice = g.Key.Apolice,
                Nome = g.First().Beneficiario,
                Entidade = g.First().Entidade,
                Matricula = g.Key.Matricula,
                CartaoOriginalConflitante = g.Any(x => Marcacoes.TryGetValue(x, out Marcacao? m) && m.CartaoOriginalConflitante),
                Lancamentos = g.OrderBy(x => x.NumeroLinha).ToList()
            }).ToList();

        foreach (GrupoOver grupo in grupos)
        {
            string cartoes = string.Join(", ", grupo.Lancamentos.Select(x => x.Cartao ?? string.Empty).Distinct());
            Adicionar(grupo, "01 • Cartão/certificado",
                grupo.CartaoOriginalConflitante ? RegraAnaliseStatus.RevisaoManual : RegraAnaliseStatus.NaoAplicavel,
                "Validar integridade da identidade MESMO com cartão de 15 dígitos",
                $"Cartão informado: {cartoes}",
                grupo.CartaoOriginalConflitante
                    ? "ALERTA: certificado do cartão aponta para outro beneficiário da fatura; cartão isolado foi bloqueado e a identidade será reavaliada."
                    : "Cartão ausente ou inválido; iniciada identificação alternativa.");

            List<Candidato> mesmaApolice = candidatos.Where(x =>
                !string.IsNullOrWhiteSpace(grupo.Apolice) && x.Apolice == grupo.Apolice).ToList();
            Adicionar(grupo, "02 • Apólice", mesmaApolice.Count > 0 ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                "Restringir candidatos à mesma apólice",
                $"Over: {grupo.Apolice}; candidatos na fatura: {mesmaApolice.Count}",
                mesmaApolice.Count == 0 ? "Nenhum candidato na apólice. Não haverá associação." : "Escopo limitado à mesma apólice.");
            if (mesmaApolice.Count == 0) { Finalizar(grupo); continue; }

            string prefixoFamilia = PrefixoFamiliar(grupo.Matricula);
            List<string> basesFamilia = over.Lancamentos.Where(l =>
                    Codigo(l.Apolice) == grupo.Apolice &&
                    PrefixoFamiliar(Digitos(l.Matricula)) == prefixoFamilia &&
                    !string.IsNullOrWhiteSpace(prefixoFamilia))
                .Select(l => ObterCertificado(l))
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string baseFamilia = basesFamilia.Count == 1 ? basesFamilia[0] : string.Empty;
            Adicionar(grupo, "03 • Matrícula familiar", baseFamilia.Length > 0 ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                "Prefixo familiar de matrícula validado por cartão de outro integrante",
                $"Prefixo {prefixoFamilia}; bases de certificado: {(basesFamilia.Count == 0 ? "nenhuma" : string.Join(",", basesFamilia))}",
                basesFamilia.Count > 1 ? "Família ambígua: certificados-base divergentes. Evidência familiar desconsiderada."
                : baseFamilia.Length > 0 ? "Família corroborada por cartão válido." : "Sem vínculo familiar verificável.");

            string nomeOver = Normalizar(grupo.Nome);
            var literais = mesmaApolice.Where(x => Normalizar(x.Nome) == nomeOver).ToList();
            Adicionar(grupo, "04 • Nome literal", literais.Count > 0 ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                "Nome completo normalizado, sem aproximação",
                $"Correspondências literais: {literais.Count}",
                literais.Count == 1 ? $"Nome literal: {literais[0].Nome}. A pesquisa continua para validar outras evidências."
                    : "Resultado não é suficiente sozinho; avaliação continua.");

            List<Avaliacao> avaliacoes = mesmaApolice.Select(c => Avaliar(grupo, c, baseFamilia,
                    AnaliseFaturasRegrasComparacao.SomarNetComparavel(grupo.Lancamentos, ignorarCoparticipacao),
                    certificadosOcupados)).ToList();
            List<Avaliacao> nomesCompativeis = avaliacoes.Where(x => x.NomeCompativel).ToList();
            Adicionar(grupo, "05 • Nome inteligente", nomesCompativeis.Count > 0 ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                "Nome completo, partículas e abreviações; rejeitar sobrenomes explícitos conflitantes",
                $"Compatíveis: {nomesCompativeis.Count}; avaliados: {avaliacoes.Count}",
                nomesCompativeis.Count == 0 ? "Nenhum nome integralmente compatível." : "Todos os candidatos compatíveis seguem para conferências independentes.");

            string[] termos = Termos(grupo.Nome);
            if (termos.Length >= 2)
            {
                for (int qtd = 2; qtd <= termos.Length; qtd++)
                {
                    int matches = mesmaApolice.Count(x => PrefixoIgual(termos, Termos(x.Nome), qtd));
                    Adicionar(grupo, "06 • Nome progressivo", matches > 0 ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                        "Comparar nomes em ordem, começando com dois e acrescentando palavras",
                        $"{string.Join(" ", termos.Take(qtd))}: {matches} candidato(s)",
                        matches == 1 ? "Candidato inicial único, mas a análise NÃO foi interrompida." :
                            matches > 1 ? "Múltiplos candidatos; pesquisa refinada com a próxima palavra." : "Sem candidato com esse prefixo completo.");
                }
            }

            Adicionar(grupo, "07 • Entidade", avaliacoes.Any(x => x.NomeCompativel && x.Entidade) ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                "Confrontar entidade com mapeamentos conhecidos, sem bloquear famílias provadas",
                $"Entidade Over: {grupo.Entidade}; compatíveis: {avaliacoes.Count(x => x.NomeCompativel && x.Entidade)}",
                "Entidade é apoio: contratante de subfatura pode diferir da entidade do Over.");
            Adicionar(grupo, "08 • Valor NET", avaliacoes.Any(x => x.NomeCompativel && x.Valor) ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                "Conferir NET comparável sem torná-lo prova isolada ou exigência absoluta",
                $"NET Over: {AnaliseFaturasRegrasComparacao.SomarNetComparavel(grupo.Lancamentos, ignorarCoparticipacao):N2}; valores iguais entre nomes compatíveis: {avaliacoes.Count(x => x.NomeCompativel && x.Valor)}",
                "Valores iguais reforçam a hipótese; divergência financeira não inviabiliza uma identidade já confirmada.");

            foreach (Avaliacao avaliacao in avaliacoes.Where(x => x.NomeCompativel || PrefixoIgual(termos, Termos(x.Pessoa.Nome), 2)))
            {
                Adicionar(grupo, "09 • Candidato", avaliacao.Aceitavel ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.NaoAplicavel,
                    "Avaliar candidato sem confirmação antecipada",
                    $"{avaliacao.Pessoa.Nome} | cert. {avaliacao.Pessoa.Certificado} | {avaliacao.Pessoa.Entidade}",
                    $"{avaliacao.Motivo} Evidências: nome={(avaliacao.NomeCompativel ? "sim" : "não")}, família={(avaliacao.Familiar ? "sim" : "não")}, entidade={(avaliacao.Entidade ? "sim" : "não")}, valor={(avaliacao.Valor ? "sim" : "não")}. Total independente: {avaliacao.Evidencias}.");
            }

            List<Avaliacao> elegiveis = avaliacoes.Where(x => x.Aceitavel).ToList();
            // Se há vários nomes integralmente compatíveis, valor/entidade isolados
            // não autorizam escolher um entre pessoas homônimas. A família validada
            // é a única forma de desempate automático nesse cenário.
            if (elegiveis.Count == 1 && nomesCompativeis.Count > 1 &&
                (!elegiveis[0].Familiar || avaliacoes.Count(x => x.NomeCompativel && x.Familiar) != 1))
                elegiveis.Clear();

            Adicionar(grupo, "10 • Unicidade", elegiveis.Count == 1 ? RegraAnaliseStatus.EvidenciaEncontrada : RegraAnaliseStatus.RevisaoManual,
                "Exigir pelo menos duas evidências independentes, ausência de conflito e candidato único",
                $"Elegíveis: {elegiveis.Count}; compatíveis pelo nome: {nomesCompativeis.Count}",
                elegiveis.Count == 1 ? "Um candidato qualificado; aguardando verificação recíproca entre ocorrências Over." :
                    "Nenhuma associação automática. Candidatos insuficientes ou concorrentes.");
            if (elegiveis.Count == 1)
            {
                grupo.CertificadoProposto = elegiveis[0].Pessoa.Certificado;
                grupo.IdProposto = elegiveis[0].Pessoa.Id;
            }
        }

        // Resolução um-para-um em ambos os sentidos: nunca dar a mesma ocorrência
        // de fatura para duas famílias/beneficiários diferentes do Over.
        var contagens = grupos.Where(g => !string.IsNullOrEmpty(g.IdProposto))
            .GroupBy(g => g.IdProposto, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (GrupoOver grupo in grupos)
        {
            if (grupo.Log.Any(x => x.NomeDaRegra == "Identificação • 11 • Decisão final"))
                continue;
            bool unico = !string.IsNullOrEmpty(grupo.IdProposto) && contagens[grupo.IdProposto] == 1;
            if (unico)
            {
                Adicionar(grupo, "11 • Decisão final", RegraAnaliseStatus.EvidenciaEncontrada,
                    "Unicidade recíproca entre grupos Over e candidatos de fatura",
                    $"Certificado confirmado: {grupo.CertificadoProposto}",
                    grupo.CartaoOriginalConflitante
                        ? "Cartão original conflitante corrigido por identidade, família e valor com vínculo único; conferir esta correção na auditoria."
                        : "Vínculo alternativo confirmado; divergência financeira continuará sendo calculada normalmente.");
            }
            else if (!string.IsNullOrEmpty(grupo.IdProposto))
            {
                Adicionar(grupo, "11 • Decisão final", RegraAnaliseStatus.RevisaoManual,
                    "Unicidade recíproca obrigatória", "Candidato disputado por mais de uma ocorrência Over",
                    "Associação bloqueada para evitar falso positivo.");
            }
            else
            {
                Adicionar(grupo, "11 • Decisão final", RegraAnaliseStatus.RevisaoManual,
                    "Unicidade e evidências independentes obrigatórias", "Sem certificado alternativo",
                    "Identificação inconclusiva; manter divergência para revisão.");
            }
            foreach (OverLancamento linha in grupo.Lancamentos)
            {
                Marcacao m = Marcacoes.GetValue(linha, _ => new Marcacao());
                m.Certificado = unico ? grupo.CertificadoProposto : string.Empty;
                m.Trilhas = grupo.Log.ToList();
            }
        }
    }

    /// <summary>
    /// V30 - DETECCAO DE CARTOES TROCADOS.
    /// Nao corrige por suposicao: so bloqueia um cartao valido se ele contradiz
    /// simultaneamente NOME e VALOR da pessoa que representa no PDF e existe
    /// alternativa forte na mesma apolice. A conciliacao normal exige depois
    /// nova unicidade, identificacao familiar e verificacao financeira.
    /// </summary>
    private static void VerificarIntegridadeCartoesValidos(
        IReadOnlyList<Candidato> candidatos,
        OverArquivo over,
        bool ignorarCoparticipacao)
    {
        var gruposComCartao = over.Lancamentos
            .Where(x => !string.IsNullOrWhiteSpace(
                AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(x.Cartao)))
            .GroupBy(x => new
            {
                Apolice = Codigo(x.Apolice),
                Nome = Normalizar(x.Beneficiario),
                Matricula = Digitos(x.Matricula),
                Certificado = AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(x.Cartao)!
            });

        foreach (var grupo in gruposComCartao)
        {
            List<Candidato> naApolice = candidatos.Where(x =>
                x.Apolice == grupo.Key.Apolice && !string.IsNullOrWhiteSpace(x.Apolice)).ToList();
            List<Candidato> originais = naApolice.Where(x =>
                string.Equals(x.Certificado, grupo.Key.Certificado, StringComparison.OrdinalIgnoreCase)).ToList();
            if (originais.Count == 0)
                continue; // Ausencia de certificado no PDF nao prova cartao trocado.

            string nomeOver = grupo.First().Beneficiario;
            // Nomes abreviados legitimos seguem no vinculo normal.
            if (originais.Any(x => NomesCompativeis(nomeOver, x.Nome)))
                continue;

            decimal net = AnaliseFaturasRegrasComparacao.SomarNetComparavel(grupo, ignorarCoparticipacao);
            if (originais.Any(x => AnaliseFaturasRegrasComparacao.ValoresIguaisCentavo(x.Valor, net)))
                continue; // Divergencia apenas de nome nao basta para mudar identidade.

            string prefixo = PrefixoFamiliar(grupo.Key.Matricula);
            List<string> bases = over.Lancamentos.Where(l =>
                    Codigo(l.Apolice) == grupo.Key.Apolice &&
                    PrefixoFamiliar(Digitos(l.Matricula)) == prefixo &&
                    !string.IsNullOrWhiteSpace(prefixo))
                .Select(l => AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(l.Cartao) ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string familia = bases.Count == 1 ? bases[0] : string.Empty;

            List<Candidato> alternativas = naApolice.Where(x =>
                !string.Equals(x.Certificado, grupo.Key.Certificado, StringComparison.OrdinalIgnoreCase) &&
                AnaliseFaturasRegrasComparacao.ValoresIguaisCentavo(x.Valor, net) &&
                (Normalizar(nomeOver) == Normalizar(x.Nome) ||
                 (!string.IsNullOrWhiteSpace(familia) &&
                  x.Certificado.Split('/')[0] == familia &&
                  NomesCompativeis(nomeOver, x.Nome)))).ToList();

            if (alternativas.Count == 0)
                continue;

            // Nao utiliza ainda o candidato alternativo. Marca o cartao como
            // contestado, e a rotina principal o revalida com os demais grupos.
            foreach (OverLancamento linha in grupo)
                Marcacoes.GetValue(linha, _ => new Marcacao()).CartaoOriginalConflitante = true;
        }
    }

    private static Avaliacao Avaliar(GrupoOver grupo, Candidato candidato, string baseFamilia,
        decimal valorOver, HashSet<string> ocupados)
    {
        bool literal = Normalizar(grupo.Nome) == Normalizar(candidato.Nome);
        bool nome = NomesCompativeis(grupo.Nome, candidato.Nome);
        bool familiar = !string.IsNullOrEmpty(baseFamilia) &&
            candidato.Certificado.Split('/')[0] == baseFamilia;
        bool entidade = EntidadesCompativeis(grupo.Entidade, candidato.Entidade);
        bool valor = AnaliseFaturasRegrasComparacao.ValoresIguaisCentavo(candidato.Valor, valorOver);
        bool ocupado = ocupados.Contains($"{grupo.Apolice}|{candidato.Certificado}");
        int evidencias = (nome ? 1 : 0) + (familiar ? 1 : 0) + (entidade ? 1 : 0) + (valor ? 1 : 0);

        // Valor é apenas corroborativo. Não converter diferença financeira legítima
        // em "não encontrado" quando nome+família ou nome literal+entidade bastarem.
        bool baseSegura = (familiar && nome) || (literal && entidade) || (literal && valor) ||
            (nome && entidade && valor && Termos(grupo.Nome).Length >= 3);
        bool elegivel = nome && baseSegura && evidencias >= 2 && !ocupado;
        string motivo = !nome ? "Sobrenome/nome explícito incompatível; rejeitado." :
            ocupado ? "Certificado já ocupado por cartão válido; rejeitado." :
            !baseSegura ? "Evidências independentes insuficientes; sem confirmação." :
            "Candidato compatível, sujeito à unicidade.";
        return new Avaliacao { Pessoa = candidato, NomeLiteral = literal, NomeCompativel = nome,
            Familiar = familiar, Entidade = entidade, Valor = valor, Evidencias = evidencias,
            Aceitavel = elegivel, Motivo = motivo };
    }

    private static bool NomesCompativeis(string origem, string destino)
    {
        string[] a = Termos(origem);
        string[] b = Termos(destino);
        if (a.Length < 2 || b.Length < 2 || a[0] != b[0]) return false;
        int i = 0, j = 0, completos = 0;
        while (i < a.Length && j < b.Length)
        {
            string x = a[i], y = b[j];
            if (x == y) { completos++; i++; j++; continue; }
            if (x.Length == 1 && y.StartsWith(x, StringComparison.Ordinal)) { i++; j++; continue; }
            if (y.Length == 1 && x.StartsWith(y, StringComparison.Ordinal)) { i++; j++; continue; }
            // Abreviação final da Bradesco (ex.: ROD. para RODRIGUES), nunca aceitar
            // uma palavra completa incompatível (ROCHEDO != SOUZA).
            if (x.Length >= 2 && x.Length <= 3 && y.StartsWith(x, StringComparison.Ordinal)) { i++; j++; continue; }
            if (y.Length >= 2 && y.Length <= 3 && x.StartsWith(y, StringComparison.Ordinal)) { i++; j++; continue; }
            return false;
        }
        // Só tolerar tokens finais omitidos se a parte presente for suficientemente
        // informativa; uma ausência final sozinha não é identidade segura.
        if (Math.Max(a.Length - i, b.Length - j) > 1 || completos < 2) return false;
        return true;
    }

    private static bool PrefixoIgual(string[] a, string[] b, int quantidade)
    {
        if (quantidade < 2 || a.Length < quantidade || b.Length < quantidade) return false;
        return Enumerable.Range(0, quantidade).All(i => a[i] == b[i] ||
            (a[i].Length == 1 && b[i].StartsWith(a[i], StringComparison.Ordinal)) ||
            (b[i].Length == 1 && a[i].StartsWith(b[i], StringComparison.Ordinal)));
    }

    private static readonly HashSet<string> Particulas = new(StringComparer.Ordinal)
    { "DE", "DA", "DO", "DAS", "DOS", "E" };

    private static string[] Termos(string? nome) => Normalizar(nome)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(x => !Particulas.Contains(x)).ToArray();

    private static string Normalizar(string? texto) => AnaliseFaturasNormalizador.NormalizarNome(texto);
    private static string Digitos(string? valor) => new((valor ?? "").Where(char.IsDigit).ToArray());
    private static string Codigo(string? valor) => Digitos(valor).TrimStart('0');
    private static string PrefixoFamiliar(string? matricula)
    {
        string d = Digitos(matricula);
        return d.Length == 17 ? d[..14] : string.Empty;
    }

    private static bool EntidadesCompativeis(string over, string fatura)
    {
        string a = Normalizar(over), b = Normalizar(fatura);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (a == b) return true;
        if ((a == "CFM" && b.Contains("CONSELHO FEDERAL DE MEDICINA", StringComparison.Ordinal)) ||
            (b == "CFM" && a.Contains("CONSELHO FEDERAL DE MEDICINA", StringComparison.Ordinal))) return true;
        if ((a == "CREMESP" && b.Contains("CONSELHO REGIONAL DE MEDICINA", StringComparison.Ordinal) && b.Contains("SAO PAULO", StringComparison.Ordinal)) ||
            (b == "CREMESP" && a.Contains("CONSELHO REGIONAL DE MEDICINA", StringComparison.Ordinal) && a.Contains("SAO PAULO", StringComparison.Ordinal))) return true;
        if ((a == "CREMERJ" && b.Contains("CONSELHO REGIONAL DE MEDICINA", StringComparison.Ordinal) && b.Contains("RIO DE JANEIRO", StringComparison.Ordinal)) ||
            (b == "CREMERJ" && a.Contains("CONSELHO REGIONAL DE MEDICINA", StringComparison.Ordinal) && a.Contains("RIO DE JANEIRO", StringComparison.Ordinal))) return true;
        return false;
    }

    private static void Adicionar(GrupoOver grupo, string nome, RegraAnaliseStatus resultado,
        string condicao, string dados, string justificativa)
    {
        grupo.Log.Add(new RegraAnaliseResultado
        {
            NomeDaRegra = "Identificação • " + nome,
            Resultado = resultado,
            Condicao = condicao,
            DadosUtilizados = dados,
            Justificativa = justificativa,
            SinalizaAtencao = false
        });
    }

    private static void Finalizar(GrupoOver grupo)
    {
        Adicionar(grupo, "11 • Decisão final", RegraAnaliseStatus.RevisaoManual,
            "Apólice compatível obrigatória", "Sem candidatos da apólice",
            "Associação bloqueada: apólice não encontrada nas faturas selecionadas.");
        foreach (OverLancamento linha in grupo.Lancamentos)
            Marcacoes.GetValue(linha, _ => new Marcacao()).Trilhas = grupo.Log.ToList();
    }
}