using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public enum RegraAnaliseStatus
{
    NaoAplicavel,
    EvidenciaEncontrada,
    RevisaoManual,
    Explicada
}

public sealed class RegraAnaliseResultado
{
    public string NomeDaRegra { get; init; } = string.Empty;
    public string Condicao { get; init; } = string.Empty;
    public string DadosUtilizados { get; init; } = string.Empty;
    public RegraAnaliseStatus Resultado { get; init; }
    public string Justificativa { get; init; } = string.Empty;
    public IReadOnlyList<string> Evidencias { get; init; } = Array.Empty<string>();
    public bool SinalizaAtencao { get; init; }
    public decimal? ValorDevolucao { get; init; }
    public int? DiasEquivalentesDevolucao { get; init; }

    public bool ExplicaDivergencia => Resultado == RegraAnaliseStatus.Explicada;
}

public sealed class DadosBeneficiarioFaturaAnalise
{
    public string Arquivo { get; init; } = string.Empty;
    public int Subfatura { get; init; }
    public string Entidade { get; init; } = string.Empty;
    public string Certificado { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public DateTime? DataNascimento { get; init; }
    public DateTime? DataInicio { get; init; }
    public string Plano { get; init; } = string.Empty;
}

public sealed class RegraAnaliseContexto
{
    public DateTime CompetenciaAnalisada { get; init; }
    public bool IgnorarClientesCancelados { get; init; }
    public ComparacaoPrincipalResultado Comparacao { get; init; } = new();
    public ComposicaoBeneficiario? Composicao { get; init; }
    public ContextoTemporalResultado? ContextoTemporal { get; init; }
    public DadosBeneficiarioFaturaAnalise? DadosFatura { get; init; }

    public IReadOnlyList<ComponenteFatura> TodosComponentesFatura =>
        Composicao?.ComponentesFatura ?? Array.Empty<ComponenteFatura>();

    public IReadOnlyList<ComponenteFatura> ComponentesFatura =>
        TodosComponentesFatura.Where(x => x.ConsiderarNoComparavel).ToList();

    public IReadOnlyList<ComponenteOver> ComponentesOver =>
        Composicao?.ComponentesOver ?? Array.Empty<ComponenteOver>();
}

public interface IRegraAnalise
{
    string NomeDaRegra { get; }
    RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto);
}

public sealed class RegrasAnaliseService
{
    private readonly IReadOnlyList<IRegraAnalise> _regras;

    public RegrasAnaliseService()
        : this(new IRegraAnalise[]
        {
            new RegraInclusaoProporcionalVigencia15(),
            new RegraDevolucaoProporcionalCancelamento(),
            new RegraMensalidadesDevolvidas(),
            new RegraInclusaoAlteracao(),
            new RegraCancelamentos(),
            new RegraRetroativos(),
            new RegraReativacaoTransferencia(),
            new RegraVigenciaPosterior(),
            new RegraRecemNascido()
        })
    {
    }

    public RegrasAnaliseService(IReadOnlyList<IRegraAnalise> regras)
        => _regras = regras ?? throw new ArgumentNullException(nameof(regras));

    public IReadOnlyList<RegraAnaliseResultado> Avaliar(RegraAnaliseContexto contexto)
    {
        if (contexto == null)
            throw new ArgumentNullException(nameof(contexto));

        if (contexto.Comparacao.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel)
            return Array.Empty<RegraAnaliseResultado>();

        return _regras.Select(x => x.Avaliar(contexto)).ToList();
    }
}

internal abstract class RegraAnaliseBase : IRegraAnalise
{
    public abstract string NomeDaRegra { get; }
    public abstract RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto);

    protected RegraAnaliseResultado NaoAplicavel(string condicao, string justificativa)
        => Resultado(RegraAnaliseStatus.NaoAplicavel, condicao, "—", justificativa, Array.Empty<string>());

    protected RegraAnaliseResultado Evidencia(string condicao, string dados, string justificativa, IEnumerable<string> evidencias)
        => Resultado(RegraAnaliseStatus.EvidenciaEncontrada, condicao, dados, justificativa, evidencias);

    protected RegraAnaliseResultado Revisao(
        string condicao,
        string dados,
        string justificativa,
        IEnumerable<string> evidencias,
        decimal? valorDevolucao = null,
        int? diasEquivalentesDevolucao = null)
        => Resultado(
            RegraAnaliseStatus.RevisaoManual,
            condicao,
            dados,
            justificativa,
            evidencias,
            valorDevolucao: valorDevolucao,
            diasEquivalentesDevolucao: diasEquivalentesDevolucao);

    protected RegraAnaliseResultado Explicada(string condicao, string dados, string justificativa, IEnumerable<string> evidencias)
        => Resultado(RegraAnaliseStatus.Explicada, condicao, dados, justificativa, evidencias);

    protected RegraAnaliseResultado Atencao(string condicao, string dados, string justificativa, IEnumerable<string> evidencias)
        => Resultado(RegraAnaliseStatus.RevisaoManual, condicao, dados, justificativa, evidencias, sinalizaAtencao: true);

    private RegraAnaliseResultado Resultado(
        RegraAnaliseStatus status,
        string condicao,
        string dados,
        string justificativa,
        IEnumerable<string> evidencias,
        bool sinalizaAtencao = false,
        decimal? valorDevolucao = null,
        int? diasEquivalentesDevolucao = null)
        => new()
        {
            NomeDaRegra = NomeDaRegra,
            Condicao = condicao,
            DadosUtilizados = dados,
            Resultado = status,
            Justificativa = justificativa,
            Evidencias = evidencias.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(),
            SinalizaAtencao = sinalizaAtencao,
            ValorDevolucao = valorDevolucao,
            DiasEquivalentesDevolucao = diasEquivalentesDevolucao
        };

    protected static decimal SomarMovimentos(RegraAnaliseContexto contexto, params string[] codigos)
    {
        var conjunto = new HashSet<string>(codigos, StringComparer.OrdinalIgnoreCase);
        return AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            contexto.ComponentesFatura
                .Where(x => conjunto.Contains((x.Movimento ?? string.Empty).Trim()))
                .Sum(x => x.Valor));
    }

    protected static List<ComponenteFatura> ObterMovimentos(RegraAnaliseContexto contexto, params string[] codigos)
    {
        var conjunto = new HashSet<string>(codigos, StringComparer.OrdinalIgnoreCase);
        return contexto.ComponentesFatura
            .Where(x => conjunto.Contains((x.Movimento ?? string.Empty).Trim()))
            .ToList();
    }

    protected static bool DiferencaIgual(RegraAnaliseContexto contexto, decimal valor)
        => contexto.Comparacao.DiferencaFaturaMenosOver.HasValue &&
           valor != 0m &&
           AnaliseFaturasRegrasComparacao.ValoresIguaisCentavo(
               contexto.Comparacao.DiferencaFaturaMenosOver.Value,
               valor);

    protected static string DescreverComponentes(IEnumerable<ComponenteFatura> componentes)
        => string.Join(" | ", componentes.Select(x =>
            $"{(string.IsNullOrWhiteSpace(x.Movimento) ? "mensalidade" : x.Movimento)} {x.Competencia:MM/yyyy} {x.Valor:N2} pág. {x.PaginaPdf}"));

    protected static IReadOnlyList<string> EvidenciasContexto(RegraAnaliseContexto contexto)
    {
        if (contexto.ContextoTemporal is null)
            return Array.Empty<string>();

        return contexto.ContextoTemporal.Evidencias
            .Select(x => $"{x.CompetenciaFatura:MM/yyyy} • {x.Movimento} {x.CompetenciaLancamento:MM/yyyy} • {x.Valor:N2} • {x.Arquivo} pág. {x.PaginaPdf}")
            .ToList();
    }
}

internal sealed class RegraInclusaoProporcionalVigencia15 : RegraAnaliseBase
{
    private const int DiaInicioElegivel = 15;
    private const int DiasCobrados = 16;

    public override string NomeDaRegra => "Inclusão proporcional por vigência 15";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao =
            "Inclusão com vigência no dia 15 da competência analisada e cobrança da Bradesco equivalente a 16/30 do NET mensal do Over.";

        if (contexto.Comparacao.Categoria != ComparacaoPrincipalCategoria.ValorMaiorNoOver)
        {
            return NaoAplicavel(
                condicao,
                "A regra se aplica somente quando a fatura está abaixo do NET comparável do Over.");
        }

        DateTime competencia = new(
            contexto.CompetenciaAnalisada.Year,
            contexto.CompetenciaAnalisada.Month,
            1);
        DateTime? dataInicio = contexto.DadosFatura?.DataInicio;

        if (!dataInicio.HasValue ||
            dataInicio.Value.Year != competencia.Year ||
            dataInicio.Value.Month != competencia.Month ||
            dataInicio.Value.Day != DiaInicioElegivel)
        {
            return NaoAplicavel(
                condicao,
                $"A data de início não é 15/{competencia:MM/yyyy}.");
        }

        if (!contexto.Comparacao.ValorFatura.HasValue ||
            !contexto.Comparacao.ValorOverComparavel.HasValue ||
            contexto.Comparacao.ValorFatura.Value <= 0m ||
            contexto.Comparacao.ValorOverComparavel.Value <= 0m)
        {
            return NaoAplicavel(
                condicao,
                "Não há valores positivos e comparáveis nos dois relatórios para calcular a proporcionalidade.");
        }

        decimal valorFatura = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            contexto.Comparacao.ValorFatura.Value);
        decimal mensalidadeOver = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            contexto.Comparacao.ValorOverComparavel.Value);
        decimal valorProporcional = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            mensalidadeOver / 30m * DiasCobrados);
        decimal diferencaProporcional = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            valorFatura - valorProporcional);
        bool dentroTolerancia =
            AnaliseFaturasRegrasComparacao.DentroToleranciaComparacaoPrincipal(diferencaProporcional);

        string dados =
            $"Início {dataInicio.Value:dd/MM/yyyy}; NET mensal do Over R$ {mensalidadeOver:N2} / 30 × {DiasCobrados} = R$ {valorProporcional:N2}; " +
            $"fatura R$ {valorFatura:N2}; diferença R$ {diferencaProporcional:N2}.";
        string evidencia =
            $"Vigência {dataInicio.Value:dd/MM/yyyy} • fatura {valorFatura:N2} • Over {mensalidadeOver:N2} • proporcional de {DiasCobrados} dias {valorProporcional:N2}";

        if (dentroTolerancia)
        {
            return Explicada(
                condicao,
                dados,
                $"A beneficiária iniciou a vigência em {dataInicio.Value:dd/MM/yyyy}. A cobrança de R$ {valorFatura:N2} corresponde a {DiasCobrados}/30 do NET mensal de R$ {mensalidadeOver:N2} do Over, dentro da tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}. A diferença decorre dos ciclos Bradesco de 1 a 30 e Over de 15 a 14.",
                new[] { evidencia });
        }

        return Evidencia(
            condicao,
            dados,
            $"A inclusão ocorreu no dia 15, mas a cobrança da fatura não corresponde a {DiasCobrados}/30 do NET mensal do Over dentro da tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}.",
            new[] { evidencia });
    }
}

internal sealed class RegraDevolucaoProporcionalCancelamento : RegraAnaliseBase
{
    private static readonly HashSet<string> MovimentosDevolucao = new(StringComparer.OrdinalIgnoreCase)
    {
        "AD", "CM", "CR", "DM", "DR"
    };

    public override string NomeDaRegra => "Devolução proporcional por cancelamento";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao =
            "Divergência com devolução/cancelamento negativo referente à competência analisada, para estimativa manual dos dias devolvidos.";

        bool categoriaPermiteEstimativa = contexto.Comparacao.Categoria is
            ComparacaoPrincipalCategoria.NaoEncontradoNoOver or
            ComparacaoPrincipalCategoria.ValorMaiorNaFatura;
        if (!categoriaPermiteEstimativa && !contexto.IgnorarClientesCancelados)
        {
            return NaoAplicavel(
                condicao,
                "A estimativa se aplica somente quando há valor excedente na fatura ou o beneficiário não foi encontrado no Over.");
        }

        DateTime competencia = new(
            contexto.CompetenciaAnalisada.Year,
            contexto.CompetenciaAnalisada.Month,
            1);

        var evidencias = new List<string>();
        var valoresDevolucao = new List<decimal>();
        bool bradescoDevolveuDepois = false;

        List<ComponenteOver> cancelamentosOver = contexto.ComponentesOver
            .Where(EhDevolucaoCancelamentoOver)
            .ToList();
        evidencias.AddRange(cancelamentosOver.Select(x =>
            $"Over • evento {VazioComoTraco(x.Evento)} • {VazioComoTraco(x.Descricao)} • NET {(x.ValorNET ?? 0m):N2} • linha {x.NumeroLinha}"));

        foreach (ComponenteFatura componente in contexto.TodosComponentesFatura)
        {
            if (!MesmoMes(componente.Competencia, competencia) ||
                componente.Valor >= 0m ||
                !EhDevolucao(componente.Movimento))
            {
                continue;
            }

            valoresDevolucao.Add(componente.Valor);
            evidencias.Add(
                $"Fatura analisada • {VazioMov(componente.Movimento)} {componente.Competencia:MM/yyyy} • {componente.Valor:N2} • pág. {componente.PaginaPdf}");
        }

        if (contexto.ContextoTemporal != null)
        {
            foreach (ContextoTemporalEvidencia evidencia in contexto.ContextoTemporal.Evidencias)
            {
                if (!MesmoMes(evidencia.CompetenciaLancamento, competencia) ||
                    evidencia.Valor >= 0m ||
                    !EhDevolucao(evidencia.Movimento))
                {
                    continue;
                }

                valoresDevolucao.Add(evidencia.Valor);
                if (PrimeiroDiaMes(evidencia.CompetenciaFatura) > competencia)
                    bradescoDevolveuDepois = true;
                evidencias.Add(
                    $"{evidencia.CompetenciaFatura:MM/yyyy} • {VazioMov(evidencia.Movimento)} {evidencia.CompetenciaLancamento:MM/yyyy} • {evidencia.Valor:N2} • {evidencia.Arquivo} pág. {evidencia.PaginaPdf}");
            }
        }

        if (valoresDevolucao.Count == 0)
        {
            return NaoAplicavel(
                condicao,
                $"Nenhuma devolução/cancelamento negativo referente a {competencia:MM/yyyy} foi localizada.");
        }

        decimal devolvido = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            Math.Abs(valoresDevolucao.Sum()));
        bool direcionarParaAtencao =
            contexto.IgnorarClientesCancelados &&
            bradescoDevolveuDepois;

        if (!categoriaPermiteEstimativa && !direcionarParaAtencao)
        {
            return NaoAplicavel(
                condicao,
                "A devolução encontrada não atende simultaneamente aos critérios da opção Ignorar clientes cancelados.");
        }

        decimal mensalidadeBase = ObterMensalidadeBase(contexto, competencia);
        if (mensalidadeBase <= 0m)
        {
            string justificativaSemBase = direcionarParaAtencao
                ? "Devolução posterior da competência analisada identificada na Bradesco. Como a opção Ignorar clientes cancelados está marcada, o caso foi considerado cancelado e direcionado para Atenção independentemente do valor devolvido."
                : $"Há devolução da Bradesco referente a {competencia:MM/yyyy}, mas não foi possível calcular os dias equivalentes porque não foi identificada uma mensalidade-base positiva. A ocorrência permanece como Divergência para conferência manual.";

            return direcionarParaAtencao
                ? Atencao(
                    condicao,
                    $"Devolução encontrada: R$ {devolvido:N2}. Mensalidade-base não identificada.",
                    justificativaSemBase,
                    evidencias)
                : Revisao(
                condicao,
                $"Devolução encontrada: R$ {devolvido:N2}. Mensalidade-base não identificada.",
                    justificativaSemBase,
                    evidencias);
        }

        decimal valorDia = mensalidadeBase / 30m;
        (int dias, decimal proporcional, decimal diferenca) = EncontrarDiasMaisProximos(
            mensalidadeBase,
            devolvido);
        bool dentroTolerancia =
            AnaliseFaturasRegrasComparacao.DentroToleranciaComparacaoPrincipal(diferenca);

        string plural = dias == 1 ? "dia" : "dias";
        string resultadoTolerancia = dentroTolerancia
            ? $"dentro da tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}"
            : $"fora da tolerância de ±R$ {AnaliseFaturasRegrasComparacao.ToleranciaComparacaoPrincipal:N2}";
        string justificativa = direcionarParaAtencao
            ? $"Devolução posterior de R$ {devolvido:N2} referente à competência analisada identificada na Bradesco. " +
              "Como a opção Ignorar clientes cancelados está marcada, o caso foi considerado cancelado e direcionado para Atenção independentemente do valor devolvido."
            : $"Estimativa: o valor devolvido pela Bradesco equivale financeiramente a {dias} {plural} em uma base fixa de 30 dias. " +
              $"Mensalidade-base: R$ {mensalidadeBase:N2}; valor diário em base de 30 dias: R$ {valorDia:N4}; " +
              $"{dias} {plural}: R$ {proporcional:N2}; devolução encontrada: R$ {devolvido:N2}; " +
              $"diferença: R$ {diferenca:N2}, {resultadoTolerancia}. " +
              "Como os relatórios importados não informam a data de cancelamento, não é possível confirmar automaticamente se essa quantidade de dias era realmente devida. " +
              "Por segurança, o caso permanece como Divergência para conferência manual da data de cancelamento.";

        string dados =
            $"Mensalidade-base R$ {mensalidadeBase:N2} / 30; devolução R$ {devolvido:N2}; dias equivalentes {dias}; valor proporcional R$ {proporcional:N2}; diferença R$ {diferenca:N2}; {resultadoTolerancia}.";

        if (direcionarParaAtencao)
        {
            return new RegraAnaliseResultado
            {
                NomeDaRegra = NomeDaRegra,
                Condicao = condicao,
                DadosUtilizados = dados,
                Resultado = RegraAnaliseStatus.RevisaoManual,
                Justificativa = justificativa,
                Evidencias = evidencias.Distinct().ToList(),
                SinalizaAtencao = true,
                ValorDevolucao = devolvido,
                DiasEquivalentesDevolucao = dias
            };
        }

        return Revisao(condicao, dados, justificativa, evidencias, devolvido, dias);
    }

    private static decimal ObterMensalidadeBase(RegraAnaliseContexto contexto, DateTime competencia)
    {
        decimal baseAtual = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
            contexto.TodosComponentesFatura
                .Where(x => MesmoMes(x.Competencia, competencia) && x.Valor > 0m)
                .Sum(x => x.Valor));

        if (baseAtual > 0m)
            return baseAtual;

        if (contexto.ContextoTemporal == null)
            return 0m;

        decimal? primeiraMensalidadePosterior = contexto.ContextoTemporal.Evidencias
            .Where(x =>
                x.Valor > 0m &&
                MesmoMes(x.CompetenciaLancamento, x.CompetenciaFatura))
            .GroupBy(x => new DateTime(x.CompetenciaFatura.Year, x.CompetenciaFatura.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => AnaliseFaturasRegrasComparacao.ArredondarCentavos(g.Sum(x => x.Valor)))
            .FirstOrDefault(x => x > 0m);

        return primeiraMensalidadePosterior ?? 0m;
    }

    private static (int Dias, decimal ValorProporcional, decimal Diferenca) EncontrarDiasMaisProximos(
        decimal mensalidadeBase,
        decimal valorDevolvido)
    {
        int melhorDia = 1;
        decimal melhorValor = AnaliseFaturasRegrasComparacao.ArredondarCentavos(mensalidadeBase / 30m);
        decimal melhorDiferenca = Math.Abs(melhorValor - valorDevolvido);

        for (int dias = 2; dias <= 30; dias++)
        {
            decimal proporcional = AnaliseFaturasRegrasComparacao.ArredondarCentavos(
                mensalidadeBase / 30m * dias);
            decimal diferenca = Math.Abs(proporcional - valorDevolvido);

            if (diferenca < melhorDiferenca)
            {
                melhorDia = dias;
                melhorValor = proporcional;
                melhorDiferenca = diferenca;
            }
        }

        return (
            melhorDia,
            melhorValor,
            AnaliseFaturasRegrasComparacao.ArredondarCentavos(melhorDiferenca));
    }

    private static bool EhDevolucao(string? movimento)
    {
        string mov = (movimento ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(mov) || MovimentosDevolucao.Contains(mov);
    }

    private static bool MesmoMes(DateTime a, DateTime b)
        => a.Year == b.Year && a.Month == b.Month;

    private static DateTime PrimeiroDiaMes(DateTime data)
        => new(data.Year, data.Month, 1);

    private static bool EhDevolucaoCancelamentoOver(ComponenteOver componente)
    {
        bool possuiValorNegativo =
            (componente.ValorNET ?? 0m) < 0m ||
            (componente.ValorPV ?? 0m) < 0m ||
            (componente.ValorOver ?? 0m) < 0m;
        if (!possuiValorNegativo)
            return false;

        string evento = (componente.Evento ?? string.Empty).Trim();
        string descricao = componente.Descricao ?? string.Empty;
        string natureza = componente.Natureza ?? string.Empty;
        return evento == "007" ||
               descricao.Contains("CANCELAMENTO", StringComparison.OrdinalIgnoreCase) ||
               natureza.Contains("CANCELAMENTO", StringComparison.OrdinalIgnoreCase);
    }

    private static string VazioComoTraco(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? "—" : texto.Trim();

    private static string VazioMov(string? movimento)
        => string.IsNullOrWhiteSpace(movimento) ? "sem MOV" : movimento.Trim().ToUpperInvariant();
}

internal sealed class RegraMensalidadesDevolvidas : RegraAnaliseBase
{
    public override string NomeDaRegra => "Mensalidades devolvidas";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao = "Movimentos de devolução (AD/DM/DR) ou devolução futura que expliquem exatamente a diferença.";

        if (contexto.ContextoTemporal?.Status == ContextoTemporalStatus.ExplicadaPorDevolucao)
        {
            return Explicada(
                condicao,
                $"Ajustes futuros: {contexto.ContextoTemporal.ValorAjustesContexto:N2}",
                contexto.ContextoTemporal.Observacao,
                EvidenciasContexto(contexto));
        }

        List<ComponenteFatura> componentes = contexto.ComponentesFatura
            .Where(x =>
                string.Equals((x.Movimento ?? string.Empty).Trim(), "AD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals((x.Movimento ?? string.Empty).Trim(), "DM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals((x.Movimento ?? string.Empty).Trim(), "DR", StringComparison.OrdinalIgnoreCase) ||
                (x.Valor < 0m && string.IsNullOrWhiteSpace(x.Movimento)))
            .ToList();
        decimal soma = AnaliseFaturasRegrasComparacao.ArredondarCentavos(componentes.Sum(x => x.Valor));

        if (componentes.Count == 0)
            return NaoAplicavel(condicao, "Nenhuma devolução AD/DM/DR nem mensalidade negativa sem MOV foi encontrada.");

        string dados = $"Devoluções na fatura: {soma:N2}";
        if (DiferencaIgual(contexto, soma))
        {
            return Explicada(
                condicao,
                dados,
                "O valor líquido dos movimentos de devolução é exatamente igual à diferença principal.",
                componentes.Select(x => DescreverComponentes(new[] { x })));
        }

        return Evidencia(
            condicao,
            dados,
            "Devolução encontrada, mas o valor não explica integralmente a diferença.",
            componentes.Select(x => DescreverComponentes(new[] { x })));
    }
}

internal sealed class RegraInclusaoAlteracao : RegraAnaliseBase
{
    public override string NomeDaRegra => "Inclusões e alterações";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao = "Movimentos IM/IR/AM/AR que expliquem exatamente a diferença ou justifiquem ausência temporal.";

        if (contexto.ContextoTemporal?.Status is ContextoTemporalStatus.ExplicadaPorInclusao or ContextoTemporalStatus.ExplicadaPorAlteracao)
        {
            return Explicada(
                condicao,
                $"Ajustes futuros: {contexto.ContextoTemporal.ValorAjustesContexto:N2}",
                contexto.ContextoTemporal.Observacao,
                EvidenciasContexto(contexto));
        }

        List<ComponenteFatura> componentes = ObterMovimentos(contexto, "IM", "IR", "AM", "AR");
        decimal soma = AnaliseFaturasRegrasComparacao.ArredondarCentavos(componentes.Sum(x => x.Valor));

        if (componentes.Count == 0)
            return NaoAplicavel(condicao, "Nenhum movimento IM/IR/AM/AR foi encontrado.");

        string dados = $"IM/IR/AM/AR na fatura: {soma:N2}";
        if (DiferencaIgual(contexto, soma))
        {
            return Explicada(
                condicao,
                dados,
                "Os movimentos de inclusão/alteração somam exatamente a diferença principal.",
                componentes.Select(x => DescreverComponentes(new[] { x })));
        }

        return Evidencia(condicao, dados,
            "Inclusão/alteração encontrada, mas o valor não explica integralmente a diferença.",
            componentes.Select(x => DescreverComponentes(new[] { x })));
    }
}

internal sealed class RegraCancelamentos : RegraAnaliseBase
{
    public override string NomeDaRegra => "Cancelamentos";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao = "Movimentos CM/CR que expliquem exatamente a diferença principal.";

        if (contexto.ContextoTemporal?.Status == ContextoTemporalStatus.ExplicadaPorCancelamento)
        {
            return Explicada(
                condicao,
                $"Ajustes futuros: {contexto.ContextoTemporal.ValorAjustesContexto:N2}",
                contexto.ContextoTemporal.Observacao,
                EvidenciasContexto(contexto));
        }

        List<ComponenteFatura> componentes = ObterMovimentos(contexto, "CM", "CR");
        decimal soma = AnaliseFaturasRegrasComparacao.ArredondarCentavos(componentes.Sum(x => x.Valor));

        if (componentes.Count == 0)
            return NaoAplicavel(condicao, "Nenhum movimento CM/CR foi encontrado.");

        string dados = $"Cancelamentos na fatura: {soma:N2}";
        if (DiferencaIgual(contexto, soma))
        {
            return Explicada(condicao, dados,
                "O valor dos cancelamentos é exatamente igual à diferença principal.",
                componentes.Select(x => DescreverComponentes(new[] { x })));
        }

        return Evidencia(condicao, dados,
            "Cancelamento encontrado, mas o valor não explica integralmente a diferença.",
            componentes.Select(x => DescreverComponentes(new[] { x })));
    }
}

internal sealed class RegraRetroativos : RegraAnaliseBase
{
    private static readonly string[] Retroativos = { "IR", "RR", "TR", "AR", "CR", "DR" };

    public override string NomeDaRegra => "Retroativos";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao = "Movimentos retroativos IR/RR/TR/AR/CR/DR que se refiram à competência analisada e reconciliem a diferença.";

        if (contexto.ContextoTemporal?.Status == ContextoTemporalStatus.ExplicadaPorRetroativo)
        {
            return Explicada(
                condicao,
                $"Ajustes futuros: {contexto.ContextoTemporal.ValorAjustesContexto:N2}",
                contexto.ContextoTemporal.Observacao,
                EvidenciasContexto(contexto));
        }

        List<ComponenteFatura> componentes = ObterMovimentos(contexto, Retroativos);
        decimal soma = AnaliseFaturasRegrasComparacao.ArredondarCentavos(componentes.Sum(x => x.Valor));

        if (componentes.Count == 0)
            return NaoAplicavel(condicao, "Nenhum movimento retroativo foi encontrado.");

        string dados = $"Retroativos na fatura: {soma:N2}";
        if (DiferencaIgual(contexto, soma))
        {
            return Explicada(condicao, dados,
                "Os movimentos retroativos somam exatamente a diferença principal.",
                componentes.Select(x => DescreverComponentes(new[] { x })));
        }

        return Evidencia(condicao, dados,
            "Lançamentos retroativos encontrados, mas o valor não explica integralmente a diferença.",
            componentes.Select(x => DescreverComponentes(new[] { x })));
    }
}

internal sealed class RegraReativacaoTransferencia : RegraAnaliseBase
{
    public override string NomeDaRegra => "Reativações e transferências";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao = "Movimentos RM/RR/TM/TR que expliquem exatamente a diferença principal.";

        if (contexto.ContextoTemporal?.Status is ContextoTemporalStatus.ExplicadaPorReativacao or ContextoTemporalStatus.ExplicadaPorTransferencia)
        {
            return Explicada(
                condicao,
                $"Ajustes futuros: {contexto.ContextoTemporal.ValorAjustesContexto:N2}",
                contexto.ContextoTemporal.Observacao,
                EvidenciasContexto(contexto));
        }

        List<ComponenteFatura> componentes = ObterMovimentos(contexto, "RM", "RR", "TM", "TR");
        decimal soma = AnaliseFaturasRegrasComparacao.ArredondarCentavos(componentes.Sum(x => x.Valor));

        if (componentes.Count == 0)
            return NaoAplicavel(condicao, "Nenhuma reativação/transferência foi encontrada.");

        string dados = $"RM/RR/TM/TR na fatura: {soma:N2}";
        if (DiferencaIgual(contexto, soma))
        {
            return Explicada(condicao, dados,
                "Os movimentos de reativação/transferência somam exatamente a diferença principal.",
                componentes.Select(x => DescreverComponentes(new[] { x })));
        }

        return Evidencia(condicao, dados,
            "Reativação/transferência encontrada, mas o valor não explica integralmente a diferença.",
            componentes.Select(x => DescreverComponentes(new[] { x })));
    }
}

internal sealed class RegraVigenciaPosterior : RegraAnaliseBase
{
    public override string NomeDaRegra => "Vigência posterior";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao = "Beneficiário ausente no mês analisado e com vigência comprovadamente posterior nas faturas de contexto.";

        if (contexto.ContextoTemporal?.Status == ContextoTemporalStatus.ExplicadaPorVigenciaPosterior)
        {
            return Explicada(
                condicao,
                "Data de início identificada nas faturas seguintes.",
                contexto.ContextoTemporal.Observacao,
                EvidenciasContexto(contexto));
        }

        return NaoAplicavel(condicao, "Não foi comprovada vigência posterior suficiente para explicar esta divergência.");
    }
}

internal sealed class RegraRecemNascido : RegraAnaliseBase
{
    public override string NomeDaRegra => "Recém-nascido";

    public override RegraAnaliseResultado Avaliar(RegraAnaliseContexto contexto)
    {
        const string condicao = "Beneficiário nascido dentro da competência analisada. A regra apenas sinaliza revisão; não encerra divergência automaticamente.";

        DateTime? nascimento = contexto.DadosFatura?.DataNascimento;
        if (!nascimento.HasValue)
            return NaoAplicavel(condicao, "Data de nascimento não está disponível na ocorrência da fatura.");

        DateTime inicio = new(contexto.CompetenciaAnalisada.Year, contexto.CompetenciaAnalisada.Month, 1);
        DateTime fim = inicio.AddMonths(1).AddDays(-1);
        if (nascimento.Value.Date < inicio || nascimento.Value.Date > fim)
            return NaoAplicavel(condicao, $"Nascimento em {nascimento.Value:dd/MM/yyyy}, fora da competência {inicio:MM/yyyy}.");

        var evidencias = new List<string>
        {
            $"Nascimento: {nascimento.Value:dd/MM/yyyy}",
            $"Certificado: {contexto.Comparacao.Certificado}",
            $"Beneficiário: {contexto.Comparacao.NomeReferencia}"
        };

        if (contexto.DadosFatura?.DataInicio is DateTime inicioVigencia)
            evidencias.Add($"Início de vigência: {inicioVigencia:dd/MM/yyyy}");

        return Revisao(
            condicao,
            $"Nascimento dentro de {inicio:MM/yyyy}.",
            "Recém-nascido identificado na competência analisada; requer revisão manual.",
            evidencias);
    }
}
