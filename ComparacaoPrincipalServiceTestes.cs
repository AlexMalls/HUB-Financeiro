using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public sealed class ComparacaoPrincipalTesteResultado
{
    public string Nome { get; init; } = string.Empty;
    public bool Sucesso { get; init; }
    public string Detalhe { get; init; } = string.Empty;
}

public static class ComparacaoPrincipalServiceTestes
{
    public static IReadOnlyList<ComparacaoPrincipalTesteResultado> Executar()
    {
        var testes = new List<ComparacaoPrincipalTesteResultado>();

        Executar(testes, "valor compatível exato", () =>
        {
            ComparacaoPrincipalResultado r = Rodar(100m, 100m, 0m, 0m).Resultados.Single();
            return r.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                   !r.CompativelPorTolerancia &&
                   r.DiferencaFaturaMenosOver == 0m;
        });

        Executar(testes, "diferença +0,30 é compatível por tolerância", () =>
        {
            ComparacaoPrincipalResultado r = Rodar(100.30m, 100m, 0m, 0m).Resultados.Single();
            return r.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                   r.CompativelPorTolerancia &&
                   r.DiferencaFaturaMenosOver == 0.30m;
        });

        Executar(testes, "diferença -0,30 é compatível por tolerância", () =>
        {
            ComparacaoPrincipalResultado r = Rodar(99.70m, 100m, 0m, 0m).Resultados.Single();
            return r.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                   r.CompativelPorTolerancia &&
                   r.DiferencaFaturaMenosOver == -0.30m;
        });

        Executar(testes, "diferença 0,31 continua divergência", () =>
            Rodar(100.31m, 100m, 0m, 0m).Resultados.Single().Categoria ==
                ComparacaoPrincipalCategoria.ValorMaiorNaFatura);

        Executar(testes, "IOF não entra no NET comparável", () =>
        {
            ComparacaoPrincipalResultado r = Rodar(100m, 100m, 2.50m, 0m).Resultados.Single();
            return r.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                   r.ValorOverComparavel == 100m;
        });

        Executar(testes, "coparticipação é ignorada por padrão", () =>
        {
            ComparacaoPrincipalResultado r = Rodar(100m, 100m, 0m, 25m).Resultados.Single();
            return r.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                   r.ValorOverComparavel == 100m;
        });

        Executar(testes, "coparticipação pode ser incluída pela opção", () =>
        {
            var service = new ComparacaoPrincipalService();
            ComparacaoPrincipalResultado r = service.Comparar(
                new[] { CriarFatura(125m) },
                CriarOver(100m, 0m, 25m),
                ignorarCoparticipacao: false).Resultados.Single();

            return r.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                   r.ValorOverComparavel == 125m;
        });

        Executar(testes, "competência anterior da fatura é ignorada por padrão", () =>
        {
            FaturaBradescoArquivo fatura = CriarFatura(100m);
            FaturaBradescoBeneficiario ben = fatura.Subfaturas[0].Beneficiarios[0];
            ben.Lancamentos.Add(new FaturaBradescoLancamento
            {
                PaginaPdf = 6,
                Competencia = new DateTime(2026, 6, 1),
                Valor = 50m,
                Movimento = "IR",
                Plano = "TQSD"
            });

            ComparacaoPrincipalResultado r = new ComparacaoPrincipalService()
                .Comparar(new[] { fatura }, CriarOver(100m, 0m, 0m))
                .Resultados.Single();

            return r.Categoria == ComparacaoPrincipalCategoria.EncontradoValorCompativel &&
                   r.ValorFatura == 100m;
        });

        Executar(testes, "competência anterior pode ser incluída pela opção", () =>
        {
            FaturaBradescoArquivo fatura = CriarFatura(100m);
            FaturaBradescoBeneficiario ben = fatura.Subfaturas[0].Beneficiarios[0];
            ben.Lancamentos.Add(new FaturaBradescoLancamento
            {
                PaginaPdf = 6,
                Competencia = new DateTime(2026, 6, 1),
                Valor = 50m,
                Movimento = "IR",
                Plano = "TQSD"
            });

            ComparacaoPrincipalResultado r = new ComparacaoPrincipalService()
                .Comparar(
                    new[] { fatura },
                    CriarOver(100m, 0m, 0m),
                    ignorarCoparticipacao: true,
                    ignorarCompetenciasAnteriores: false)
                .Resultados.Single();

            return r.Categoria == ComparacaoPrincipalCategoria.ValorMaiorNaFatura &&
                   r.ValorFatura == 150m;
        });

        Executar(testes, "valor maior na fatura fora da tolerância", () =>
            Rodar(110m, 100m, 0m, 0m).Resultados.Single().Categoria == ComparacaoPrincipalCategoria.ValorMaiorNaFatura);

        Executar(testes, "valor maior no Over fora da tolerância", () =>
            Rodar(90m, 100m, 0m, 0m).Resultados.Single().Categoria == ComparacaoPrincipalCategoria.ValorMaiorNoOver);

        Executar(testes, "não encontrado na fatura", () =>
        {
            var faturaOutro = CriarFatura(50m, "OUTRA PESSOA");
            var sub = faturaOutro.Subfaturas[0];
            var antigo = sub.Beneficiarios[0];
            sub.Beneficiarios.Clear();
            var outro = new FaturaBradescoBeneficiario { Certificado = "0000099/00", Nome = antigo.Nome, Plano = antigo.Plano, DataInicio = antigo.DataInicio };
            outro.Lancamentos.Add(new FaturaBradescoLancamento { PaginaPdf = 5, Competencia = new DateTime(2026, 7, 1), Valor = 50m });
            sub.Beneficiarios.Add(outro);
            var over = CriarOver(100m, 0m, 0m);
            var d = new ComparacaoPrincipalService().Comparar(new[] { faturaOutro }, over);
            return d.Resultados.Any(x => x.Categoria == ComparacaoPrincipalCategoria.NaoEncontradoNaFatura);
        });

        Executar(testes, "não encontrado no Over", () =>
        {
            var fatura = CriarFatura(100m);
            var over = CriarOverVazio();
            var d = new ComparacaoPrincipalService().Comparar(new[] { fatura }, over);
            return d.Resultados.Any(x => x.Categoria == ComparacaoPrincipalCategoria.NaoEncontradoNoOver);
        });

        Executar(testes, "certificado duplicado permanece ambíguo", () =>
        {
            var arquivo = CriarFatura(100m, "ANA UM");
            FaturaBradescoSubfatura sub = arquivo.Subfaturas[0];
            var b2 = new FaturaBradescoBeneficiario { Certificado = "0000004/00", Nome = "ANA DOIS", Plano = "X", DataInicio = new DateTime(2024, 1, 1) };
            b2.Lancamentos.Add(new FaturaBradescoLancamento { PaginaPdf = 6, Competencia = new DateTime(2026, 7, 1), Valor = 100m });
            sub.Beneficiarios.Add(b2);
            var over = CriarOver(100m, 0m, 0m, "NOME QUE NAO BATE");
            var d = new ComparacaoPrincipalService().Comparar(new[] { arquivo }, over);
            return d.Resultados.Any(x => x.Categoria == ComparacaoPrincipalCategoria.Ambiguo);
        });

        return testes;
    }

    private static void Executar(List<ComparacaoPrincipalTesteResultado> testes, string nome, Func<bool> acao)
    {
        try
        {
            bool ok = acao();
            testes.Add(new ComparacaoPrincipalTesteResultado { Nome = nome, Sucesso = ok, Detalhe = ok ? "OK" : "resultado inesperado" });
        }
        catch (Exception ex)
        {
            testes.Add(new ComparacaoPrincipalTesteResultado { Nome = nome, Sucesso = false, Detalhe = ex.Message });
        }
    }

    private static ComparacaoPrincipalDiagnostico Rodar(decimal valorFatura, decimal netOver, decimal iof, decimal copart)
        => new ComparacaoPrincipalService().Comparar(
            new[] { CriarFatura(valorFatura) },
            CriarOver(netOver, iof, copart));

    private static FaturaBradescoArquivo CriarFatura(decimal valor, string nome = "CRISTINA FREIRE WEFFORT")
    {
        var arquivo = new FaturaBradescoArquivo { NomeArquivo = "fatura.pdf", Competencia = new DateTime(2026, 7, 1), Apolice = "0005249" };
        var sub = new FaturaBradescoSubfatura { Numero = 1, Entidade = "TESTE" };
        var ben = new FaturaBradescoBeneficiario { Certificado = "0000004/00", Nome = nome, Plano = "TQSD", DataInicio = new DateTime(2024, 1, 1) };
        ben.Lancamentos.Add(new FaturaBradescoLancamento { PaginaPdf = 5, Competencia = new DateTime(2026, 7, 1), Valor = valor, Plano = "TQSD" });
        sub.Beneficiarios.Add(ben);
        arquivo.Subfaturas.Add(sub);
        return arquivo;
    }

    private static OverArquivo CriarOver(decimal net, decimal iof, decimal copart, string nome = "CRISTINA FREIRE WEFFORT")
    {
        var lancamentos = new List<OverLancamento>
        {
            new() { NumeroLinha = 3, Competencia = new DateTime(2026, 7, 1), Beneficiario = nome, Entidade = "TESTE", Apolice = "5249", Matricula = "1", Evento = "0027", Descricao = "PLANO", ValorNET = net, Cartao = "952490000004001" }
        };

        if (iof != 0m)
            lancamentos.Add(new OverLancamento { NumeroLinha = 4, Competencia = new DateTime(2026, 7, 1), Beneficiario = nome, Entidade = "TESTE", Apolice = "5249", Matricula = "1", Evento = "9001", Descricao = "IOF SEGURADORA", ValorNET = iof, Cartao = "952490000004001" });

        if (copart != 0m)
            lancamentos.Add(new OverLancamento { NumeroLinha = 5, Competencia = new DateTime(2026, 7, 1), Beneficiario = nome, Entidade = "TESTE", Apolice = "5249", Matricula = "1", Evento = "116", Descricao = "Ft Moderador/Co-Participacao", ValorNET = copart, Cartao = "952490000004001" });

        return new OverArquivo { NomeArquivo = "over.xlsx", Competencia = new DateTime(2026, 7, 1), Lancamentos = lancamentos };
    }

    private static OverArquivo CriarOverVazio()
        => new() { NomeArquivo = "over.xlsx", Competencia = new DateTime(2026, 7, 1), Lancamentos = Array.Empty<OverLancamento>() };
}
