using System;
using System.Collections.Generic;
using System.Linq;

namespace HubFinanceiro;

public sealed record VinculoTesteResultado(string Nome, bool Sucesso, string Esperado, string Obtido);

public static class BeneficiarioVinculoServiceTestes
{
    public static IReadOnlyList<VinculoTesteResultado> Executar()
    {
        var testes = new List<VinculoTesteResultado>();

        Testar(testes, "certificado único", VinculoBeneficiarioStatus.EncontradoUnico,
            CriarDiagnostico(
                new[] { Fatura("0000004/00", "CRISTINA FREIRE WEFFORT", 1) },
                new[] { Over("952490000004001", "CRISTINA FREIRE WEFFORT", 3) })
            .Resultados.Single(x => x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura).Status);

        Testar(testes, "certificado duplicado nomes diferentes", VinculoBeneficiarioStatus.EncontradoPorNome,
            CriarDiagnostico(
                new[]
                {
                    Fatura("0000100/00", "JOAO DA SILVA", 1),
                    Fatura("0000100/00", "MARIA SOUZA", 2)
                },
                new[] { Over(Cartao("0000100/00"), "MARIA SOUZA", 10) })
            .Resultados.Single(x => x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura).Status);

        Testar(testes, "mesmo certificado mesmo nome duplicado", VinculoBeneficiarioStatus.Ambiguo,
            CriarDiagnostico(
                new[]
                {
                    Fatura("0000200/00", "ANA LIMA", 1),
                    Fatura("0000200/00", "ANA LIMA", 2)
                },
                new[] { Over(Cartao("0000200/00"), "ANA LIMA", 20) })
            .Resultados.Single(x => x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura).Status);

        Testar(testes, "nome com acento no desempate", VinculoBeneficiarioStatus.EncontradoPorNome,
            CriarDiagnostico(
                new[]
                {
                    Fatura("0000300/00", "JOÃO D'ÁVILA", 1),
                    Fatura("0000300/00", "JOSE SILVA", 2)
                },
                new[] { Over(Cartao("0000300/00"), "Joao D Avila", 30) })
            .Resultados.Single(x => x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura).Status);

        Testar(testes, "ausência na fatura", VinculoBeneficiarioStatus.NaoEncontrado,
            CriarDiagnostico(
                Array.Empty<FaturaBradescoArquivo>(),
                new[] { Over(Cartao("0000400/00"), "PESSOA OVER", 40) })
            .Resultados.Single(x => x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura).Status);

        Testar(testes, "ausência no Over", VinculoBeneficiarioStatus.NaoEncontrado,
            CriarDiagnostico(
                new[] { Fatura("0000500/00", "PESSOA FATURA", 1) },
                Array.Empty<OverLancamento>())
            .Resultados.Single(x => x.Direcao == VinculoBeneficiarioDirecao.FaturaParaOver).Status);

        VinculoBeneficiariosDiagnostico multiplosEventos = CriarDiagnostico(
            new[] { Fatura("0000600/00", "MULTIPLOS EVENTOS", 1) },
            new[]
            {
                Over(Cartao("0000600/00"), "MULTIPLOS EVENTOS", 60, evento: "0027"),
                Over(Cartao("0000600/00"), "MULTIPLOS EVENTOS", 61, evento: "9001"),
                Over(Cartao("0000600/00"), "MULTIPLOS EVENTOS", 62, evento: "116")
            });
        Testar(testes, "múltiplos eventos não viram duplicidade", 1, multiplosEventos.TotalOverOcorrencias);

        Testar(testes, "cartão inválido não usa nome sozinho", VinculoBeneficiarioStatus.NaoEncontrado,
            CriarDiagnostico(
                new[] { Fatura("0000700/00", "MESMO NOME", 1) },
                new[] { Over("CARTAO INVALIDO", "MESMO NOME", 70) })
            .Resultados.Single(x => x.Direcao == VinculoBeneficiarioDirecao.OverParaFatura).Status);

        return testes;
    }

    private static VinculoBeneficiariosDiagnostico CriarDiagnostico(
        IReadOnlyList<FaturaBradescoArquivo> faturas,
        IReadOnlyList<OverLancamento> lancamentos)
    {
        var over = new OverArquivo
        {
            NomeArquivo = "teste.xlsx",
            Lancamentos = lancamentos
        };

        return new BeneficiarioVinculoService().CriarDiagnostico(faturas, over);
    }

    private static FaturaBradescoArquivo Fatura(string certificado, string nome, int subfatura)
    {
        var arquivo = new FaturaBradescoArquivo { NomeArquivo = $"fatura{subfatura}.pdf" };
        var sf = new FaturaBradescoSubfatura { Numero = subfatura, Entidade = $"ENTIDADE {subfatura}" };
        sf.Beneficiarios.Add(new FaturaBradescoBeneficiario
        {
            Certificado = certificado,
            Nome = nome
        });
        arquivo.Subfaturas.Add(sf);
        return arquivo;
    }

    private static OverLancamento Over(string cartao, string nome, int linha, string evento = "0027")
        => new()
        {
            NumeroLinha = linha,
            Beneficiario = nome,
            Cartao = cartao,
            Matricula = "MAT1",
            Entidade = "ENTIDADE",
            Apolice = "5249",
            Evento = evento
        };

    private static string Cartao(string certificado)
    {
        string cert = AnaliseFaturasNormalizador.NormalizarCertificadoFatura(certificado)
            ?? throw new InvalidOperationException("Certificado de teste inválido.");
        string baseCert = cert[..7];
        string dep = cert.Substring(8, 2);
        return "95249" + baseCert + dep + "0";
    }

    private static void Testar<T>(List<VinculoTesteResultado> lista, string nome, T esperado, T obtido)
    {
        bool ok = EqualityComparer<T>.Default.Equals(esperado, obtido);
        lista.Add(new VinculoTesteResultado(nome, ok, esperado?.ToString() ?? "null", obtido?.ToString() ?? "null"));
    }
}
