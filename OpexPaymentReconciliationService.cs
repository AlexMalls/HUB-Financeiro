using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HubFinanceiro;

public enum OpexPaymentReconciliationIssueKind
{
    DadosBancoIndisponiveis,
    HubNoBancoAusenteNoSantander,
    SantanderAusenteNoHub,
    StatusHubDivergente
}

public sealed record OpexPaymentReconciliationIssue(
    OpexPaymentReconciliationIssueKind Tipo,
    string Empresa,
    string Titulo,
    string Descricao,
    decimal? Valor,
    string Fornecedor,
    string StatusHub,
    PrevisaoPagamento? PagamentoHub,
    SantanderCommitmentItem? PagamentoBanco);

public sealed record OpexPaymentReconciliationCompanyResult(
    string Empresa,
    string ContextoBanco,
    bool DadosBancoDisponiveis,
    int QuantidadeHubDia,
    int QuantidadeHubNoBanco,
    int QuantidadeBancoDia,
    int CorrespondenciasNoBanco,
    int CorrespondenciasOutrosStatus,
    SantanderCommitmentMemoryEntry? SnapshotSelecionado,
    IReadOnlyList<OpexPaymentReconciliationIssue> Divergencias)
{
    public bool SemDivergencias => DadosBancoDisponiveis && Divergencias.Count == 0;
}

public sealed record OpexPaymentReconciliationResult(
    DateTime Data,
    OpexPaymentReconciliationCompanyResult Administradora,
    OpexPaymentReconciliationCompanyResult Corretora)
{
    public IReadOnlyList<OpexPaymentReconciliationCompanyResult> Empresas =>
        new[] { Administradora, Corretora };

    public IReadOnlyList<OpexPaymentReconciliationIssue> Divergencias =>
        Empresas.SelectMany(empresa => empresa.Divergencias).ToList();

    public bool CoberturaBancoCompleta => Empresas.All(empresa => empresa.DadosBancoDisponiveis);
    public bool SemDivergencias => CoberturaBancoCompleta && Divergencias.Count == 0;

    public int TotalHubNoBanco => Empresas.Sum(empresa => empresa.QuantidadeHubNoBanco);
    public int TotalBanco => Empresas.Sum(empresa => empresa.QuantidadeBancoDia);
    public int TotalCorrespondenciasNoBanco => Empresas.Sum(empresa => empresa.CorrespondenciasNoBanco);
}

/// <summary>
/// Faz a conferência bidirecional entre os lançamentos do O.P.E.X. e os
/// compromissos Santander já capturados pelo monitor do HUB.
///
/// Regra de negócio:
/// 1) todo lançamento HUB com status "No Banco" precisa existir no Santander;
/// 2) pagamentos Santander já usados na etapa 1 são consumidos e não são
///    analisados novamente;
/// 3) somente os pagamentos Santander restantes são procurados no HUB inteiro;
/// 4) se não existirem no HUB, geram falta de lançamento;
/// 5) se existirem com status diferente de "No Banco", geram divergência de status.
///
/// O pareamento é multiconjunto (uma linha de banco só pode casar uma vez) e
/// usa valor como requisito. Nome do favorecido e código, quando disponíveis,
/// servem para decidir o melhor par entre valores repetidos.
/// </summary>
public static class OpexPaymentReconciliationService
{
    private const string StatusNoBanco = "No Banco";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex DigitsRegex = new(@"\D+", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumericRegex = new(@"[^A-Z0-9]+", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "LTDA", "LTD", "SA", "S", "A", "ME", "EPP", "EIRELI", "CIA", "COMPANHIA",
        "DE", "DA", "DO", "DAS", "DOS", "E", "PARA", "P", "PAGAMENTO", "PIX", "TED"
    };

    public static OpexPaymentReconciliationResult Conferir(
        DateTime data,
        IReadOnlyList<PrevisaoPagamento> pagamentosHub,
        IReadOnlyList<SantanderCommitmentMemoryEntry> memoriaSantander)
    {
        var dia = data.Date;

        var adm = ConferirEmpresa(
            dia,
            "ADM",
            "Administradora",
            pagamentosHub,
            memoriaSantander);

        var cor = ConferirEmpresa(
            dia,
            "COR",
            "Corretora",
            pagamentosHub,
            memoriaSantander);

        return new OpexPaymentReconciliationResult(dia, adm, cor);
    }

    private static OpexPaymentReconciliationCompanyResult ConferirEmpresa(
        DateTime data,
        string empresaHub,
        string contextoBanco,
        IReadOnlyList<PrevisaoPagamento> pagamentosHub,
        IReadOnlyList<SantanderCommitmentMemoryEntry> memoriaSantander)
    {
        var hubDia = pagamentosHub
            .Where(p => p.DataPagamento.Date == data &&
                        string.Equals(p.Empresa?.Trim(), empresaHub, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Valor)
            .ThenBy(p => p.NomeFornecedor)
            .ThenBy(p => p.Id)
            .ToList();

        var hubNoBanco = hubDia
            .Where(p => IsNoBanco(p.Status))
            .ToList();

        var snapshot = SelecionarSnapshot(memoriaSantander, contextoBanco, data);
        if (snapshot == null)
        {
            var issue = new OpexPaymentReconciliationIssue(
                OpexPaymentReconciliationIssueKind.DadosBancoIndisponiveis,
                empresaHub,
                $"Sem dados Santander para {contextoBanco}",
                $"Não há uma consulta Santander armazenada que cubra {data:dd/MM/yyyy}. Abra Consultar compromissos nesse contexto/data e execute a conferência novamente.",
                null,
                string.Empty,
                string.Empty,
                null,
                null);

            return new OpexPaymentReconciliationCompanyResult(
                empresaHub,
                contextoBanco,
                false,
                hubDia.Count,
                hubNoBanco.Count,
                0,
                0,
                0,
                null,
                new[] { issue });
        }

        var bancoDia = snapshot.Pagamentos
            .Where(item => TryParseDate(item.DataPagamento, out var pagamentoData) && pagamentoData.Date == data)
            .ToList();

        var issues = new List<OpexPaymentReconciliationIssue>();
        var usedHub = new bool[hubDia.Count];
        var usedBank = new bool[bancoDia.Count];

        // PASSAGEM 1: tudo que o HUB afirma estar "No Banco" precisa existir no Santander.
        var noBancoIndexes = Enumerable.Range(0, hubDia.Count)
            .Where(index => IsNoBanco(hubDia[index].Status))
            .ToList();

        var matchesNoBanco = PairByAmountAndAffinity(hubDia, noBancoIndexes, bancoDia, usedHub, usedBank);

        foreach (var hubIndex in noBancoIndexes.Where(index => !usedHub[index]))
        {
            var hub = hubDia[hubIndex];
            issues.Add(new OpexPaymentReconciliationIssue(
                OpexPaymentReconciliationIssueKind.HubNoBancoAusenteNoSantander,
                empresaHub,
                "Marcado como “No Banco” no HUB, mas não localizado no Santander",
                $"{hub.NomeFornecedor} • {FormatMoney(hub.Valor)} • Código {hub.CodigoFornecedor:D6}. O HUB indica que este pagamento já está no banco, porém ele não apareceu nos compromissos de {data:dd/MM/yyyy}.",
                hub.Valor,
                hub.NomeFornecedor,
                hub.Status,
                hub,
                null));
        }

        // PASSAGEM 2: só o que sobrou no banco é procurado no HUB.
        // Isso evita conferir novamente as linhas que já provaram os "No Banco".
        var remainingHubIndexes = Enumerable.Range(0, hubDia.Count)
            .Where(index => !usedHub[index])
            .ToList();

        var matchesRemaining = PairByAmountAndAffinity(hubDia, remainingHubIndexes, bancoDia, usedHub, usedBank);

        var otherStatusMatches = 0;
        foreach (var match in matchesRemaining)
        {
            var hub = hubDia[match.HubIndex];
            var bank = bancoDia[match.BankIndex];

            if (IsNoBanco(hub.Status))
                continue;

            otherStatusMatches++;
            var status = string.IsNullOrWhiteSpace(hub.Status) ? "sem status" : hub.Status.Trim();
            issues.Add(new OpexPaymentReconciliationIssue(
                OpexPaymentReconciliationIssueKind.StatusHubDivergente,
                empresaHub,
                $"Pagamento existe no HUB, mas está “{status}”",
                $"{hub.NomeFornecedor} • {FormatMoney(hub.Valor)}. O Santander possui este pagamento em {data:dd/MM/yyyy}, então o status do lançamento no HUB deve ser alterado para “No Banco”.",
                hub.Valor,
                string.IsNullOrWhiteSpace(bank.Favorecido) ? hub.NomeFornecedor : bank.Favorecido,
                status,
                hub,
                bank));
        }

        foreach (var bankIndex in Enumerable.Range(0, bancoDia.Count).Where(index => !usedBank[index]))
        {
            var bank = bancoDia[bankIndex];
            var valor = GetBankValue(bank);
            var favorecido = string.IsNullOrWhiteSpace(bank.Favorecido) ? "Favorecido não identificado" : bank.Favorecido.Trim();
            var numero = string.IsNullOrWhiteSpace(bank.NumeroPagamento) ? string.Empty : $" • Nº pagamento {bank.NumeroPagamento}";

            issues.Add(new OpexPaymentReconciliationIssue(
                OpexPaymentReconciliationIssueKind.SantanderAusenteNoHub,
                empresaHub,
                "Pagamento existe no Santander, mas falta no HUB",
                $"{favorecido} • {(valor.HasValue ? FormatMoney(valor.Value) : bank.Valor)}{numero}. Nenhum lançamento correspondente foi encontrado no O.P.E.X. para {data:dd/MM/yyyy}.",
                valor,
                favorecido,
                string.Empty,
                null,
                bank));
        }

        return new OpexPaymentReconciliationCompanyResult(
            empresaHub,
            contextoBanco,
            true,
            hubDia.Count,
            hubNoBanco.Count,
            bancoDia.Count,
            matchesNoBanco.Count,
            otherStatusMatches,
            snapshot,
            issues);
    }

    private static SantanderCommitmentMemoryEntry? SelecionarSnapshot(
        IReadOnlyList<SantanderCommitmentMemoryEntry> memoria,
        string contextoBanco,
        DateTime data)
    {
        return memoria
            .Where(entry => string.Equals(entry.Contexto, contextoBanco, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new
            {
                Entry = entry,
                Start = ParseDate(entry.DataInicial),
                End = ParseDate(entry.DataFinal)
            })
            .Where(item => item.Start.HasValue && item.End.HasValue &&
                           data >= item.Start.Value.Date && data <= item.End.Value.Date)
            .OrderBy(item => item.Start!.Value.Date == data && item.End!.Value.Date == data ? 0 : 1)
            .ThenBy(item => (item.End!.Value.Date - item.Start!.Value.Date).TotalDays)
            .ThenByDescending(item => item.Entry.AtualizadoEm)
            .Select(item => item.Entry)
            .FirstOrDefault();
    }

    private static List<PaymentMatch> PairByAmountAndAffinity(
        IReadOnlyList<PrevisaoPagamento> hub,
        IReadOnlyList<int> hubIndexes,
        IReadOnlyList<SantanderCommitmentItem> bank,
        bool[] usedHub,
        bool[] usedBank)
    {
        var matches = new List<PaymentMatch>();

        var amountGroups = hubIndexes
            .Where(index => !usedHub[index])
            .GroupBy(index => MoneyKey(hub[index].Valor))
            .OrderBy(group => group.Key)
            .ToList();

        foreach (var amountGroup in amountGroups)
        {
            var availableHub = amountGroup.Where(index => !usedHub[index]).ToList();
            var availableBank = Enumerable.Range(0, bank.Count)
                .Where(index => !usedBank[index])
                .Where(index => GetBankValue(bank[index]).HasValue && MoneyKey(GetBankValue(bank[index])!.Value) == amountGroup.Key)
                .ToList();

            if (availableHub.Count == 0 || availableBank.Count == 0)
                continue;

            // Primeiro escolhe os pares com mais evidência textual/código.
            var scoredPairs = (
                from hubIndex in availableHub
                from bankIndex in availableBank
                let score = AffinityScore(hub[hubIndex], bank[bankIndex])
                orderby score descending,
                        hub[hubIndex].Id,
                        bankIndex
                select new PaymentMatch(hubIndex, bankIndex, score))
                .ToList();

            foreach (var candidate in scoredPairs.Where(pair => pair.Score > 0))
            {
                if (usedHub[candidate.HubIndex] || usedBank[candidate.BankIndex])
                    continue;

                usedHub[candidate.HubIndex] = true;
                usedBank[candidate.BankIndex] = true;
                matches.Add(candidate);
            }

            // Valor sozinho não comprova identidade. Se os dois lados expõem nome
            // e não há qualquer afinidade entre eles, deixamos os itens sem casar
            // para que a divergência apareça ao usuário em vez de gerar falso OK.
            // O fallback só é permitido no caso único em que uma das pontas não
            // expõe nome suficiente para comparação.
            var remainingHub = availableHub.Where(index => !usedHub[index]).ToList();
            var remainingBank = availableBank.Where(index => !usedBank[index]).ToList();

            if (remainingHub.Count == 1 && remainingBank.Count == 1)
            {
                var hubIndex = remainingHub[0];
                var bankIndex = remainingBank[0];
                var hubName = NormalizeName(hub[hubIndex].NomeFornecedor);
                var bankName = NormalizeName(bank[bankIndex].Favorecido);

                if (string.IsNullOrWhiteSpace(hubName) || string.IsNullOrWhiteSpace(bankName))
                {
                    var match = new PaymentMatch(hubIndex, bankIndex, 0);
                    usedHub[match.HubIndex] = true;
                    usedBank[match.BankIndex] = true;
                    matches.Add(match);
                }
            }
        }

        return matches;
    }

    private static int AffinityScore(PrevisaoPagamento hub, SantanderCommitmentItem bank)
    {
        var score = 0;

        var hubCode = DigitsOnly(hub.CodigoFornecedor.ToString(CultureInfo.InvariantCulture));
        var bankClient = DigitsOnly(bank.NumeroCliente);
        if (!string.IsNullOrWhiteSpace(hubCode) && hub.CodigoFornecedor != 0 &&
            !string.IsNullOrWhiteSpace(bankClient) &&
            NumericIdentifiersEqual(hubCode, bankClient))
        {
            score += 1000;
        }

        var hubName = NormalizeName(hub.NomeFornecedor);
        var bankName = NormalizeName(bank.Favorecido);
        if (string.IsNullOrWhiteSpace(hubName) || string.IsNullOrWhiteSpace(bankName))
            return score;

        if (string.Equals(hubName, bankName, StringComparison.Ordinal))
            return score + 600;

        if ((hubName.Contains(bankName, StringComparison.Ordinal) || bankName.Contains(hubName, StringComparison.Ordinal)) &&
            Math.Min(hubName.Length, bankName.Length) >= 5)
        {
            score += 450;
        }

        var hubTokens = SignificantTokens(hubName);
        var bankTokens = SignificantTokens(bankName);
        if (hubTokens.Count > 0 && bankTokens.Count > 0)
        {
            var shared = hubTokens.Intersect(bankTokens, StringComparer.Ordinal).Count();
            var denominator = Math.Max(hubTokens.Count, bankTokens.Count);
            var ratio = denominator == 0 ? 0d : (double)shared / denominator;
            score += (int)Math.Round(ratio * 300d, MidpointRounding.AwayFromZero);
        }

        return score;
    }

    private static bool IsNoBanco(string? status) =>
        string.Equals(status?.Trim(), StatusNoBanco, StringComparison.OrdinalIgnoreCase);

    private static decimal MoneyKey(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal? GetBankValue(SantanderCommitmentItem item)
    {
        if (item.ValorNumerico.HasValue)
            return item.ValorNumerico.Value;

        var raw = item.Valor?.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (decimal.TryParse(raw, NumberStyles.Number, PtBr, out var parsed))
            return parsed;

        return null;
    }

    private static string FormatMoney(decimal value) => value.ToString("C2", PtBr);

    private static DateTime? ParseDate(string? value)
    {
        return TryParseDate(value, out var parsed) ? parsed : null;
    }

    private static bool TryParseDate(string? value, out DateTime parsed)
    {
        return DateTime.TryParseExact(
            value?.Trim(),
            "dd/MM/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed);
    }

    private static bool NumericIdentifiersEqual(string left, string right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        return left.Length > 0 && right.Length > 0 && string.Equals(left, right, StringComparison.Ordinal);
    }

    private static string DigitsOnly(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : DigitsRegex.Replace(value, string.Empty);

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return NonAlphaNumericRegex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    private static HashSet<string> SignificantTokens(string normalizedName)
    {
        return normalizedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed record PaymentMatch(int HubIndex, int BankIndex, int Score);
}
