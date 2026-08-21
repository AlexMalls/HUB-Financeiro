using System;
using System.Collections.Generic;

namespace HubFinanceiro;

/// <summary>
/// Casos determinísticos da camada de normalização.
/// Não acessa arquivos ou interface; pode ser chamado por diagnóstico ou testes futuros.
/// </summary>
public static class AnaliseFaturasNormalizadorTestes
{
    public static IReadOnlyList<NormalizacaoTesteResultado> Executar()
    {
        var testes = new List<NormalizacaoTesteResultado>();

        Testar(tar => AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(tar), "952490000004001", "0000004/00", "Cartão titular /00", testes);
        Testar(tar => AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(tar), "952490000030014", "0000030/01", "Cartão dependente /01", testes);
        Testar(tar => AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(tar), "952490004956008", "0004956/00", "Família 4956 titular /00", testes);
        Testar(tar => AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(tar), "952490004956016", "0004956/01", "Família 4956 dependente /01", testes);
        Testar(tar => AnaliseFaturasNormalizador.ExtrairCertificadoDoCartaoOver(tar), "952490004956024", "0004956/02", "Família 4956 dependente /02", testes);
        Testar(tar => AnaliseFaturasNormalizador.NormalizarCertificadoFatura(tar), "4/0", "0000004/00", "Certificado com zeros ausentes", testes);
        Testar(tar => AnaliseFaturasNormalizador.NormalizarCertificadoFatura(tar), "000495602", "0004956/02", "Certificado concatenado", testes);
        Testar(tar => AnaliseFaturasNormalizador.NormalizarNome(tar), "  João   D'Ávila-Souza  ", "JOAO D AVILA SOUZA", "Nome com acento/pontuação/espaços", testes);
        TestarDecimal("2.140,98-", -2140.98m, "Valor negativo com sinal no final", testes);
        TestarDecimal("-2.140,98", -2140.98m, "Valor negativo com sinal no início", testes);
        TestarCompetencia("09/2026", new DateTime(2026, 9, 1), "Competência MM/yyyy", testes);
        TestarCompetencia("202609", new DateTime(2026, 9, 1), "Competência yyyyMM", testes);
        TestarCompetencia("2026-09", new DateTime(2026, 9, 1), "Competência yyyy-MM", testes);
        TestarCompetencia("18/09/2026", new DateTime(2026, 9, 1), "Data completa convertida para competência", testes);

        return testes;
    }

    private static void Testar(
        Func<string, string?> funcao,
        string entrada,
        string esperado,
        string nome,
        List<NormalizacaoTesteResultado> resultados)
    {
        try
        {
            string? obtido = funcao(entrada);
            resultados.Add(new NormalizacaoTesteResultado(nome, entrada, esperado, obtido ?? "<null>", string.Equals(obtido, esperado, StringComparison.Ordinal)));
        }
        catch (Exception ex)
        {
            resultados.Add(new NormalizacaoTesteResultado(nome, entrada, esperado, ex.GetType().Name + ": " + ex.Message, false));
        }
    }

    private static void TestarDecimal(
        string entrada,
        decimal esperado,
        string nome,
        List<NormalizacaoTesteResultado> resultados)
    {
        try
        {
            decimal? obtido = AnaliseFaturasNormalizador.NormalizarValor(entrada);
            bool ok = obtido.HasValue && obtido.Value == esperado;
            resultados.Add(new NormalizacaoTesteResultado(nome, entrada, esperado.ToString(), obtido?.ToString() ?? "<null>", ok));
        }
        catch (Exception ex)
        {
            resultados.Add(new NormalizacaoTesteResultado(nome, entrada, esperado.ToString(), ex.GetType().Name + ": " + ex.Message, false));
        }
    }

    private static void TestarCompetencia(
        string entrada,
        DateTime esperado,
        string nome,
        List<NormalizacaoTesteResultado> resultados)
    {
        try
        {
            DateTime? obtido = AnaliseFaturasNormalizador.NormalizarCompetencia(entrada);
            bool ok = obtido.HasValue && obtido.Value == esperado;
            resultados.Add(new NormalizacaoTesteResultado(nome, entrada, esperado.ToString("yyyy-MM-dd"), obtido?.ToString("yyyy-MM-dd") ?? "<null>", ok));
        }
        catch (Exception ex)
        {
            resultados.Add(new NormalizacaoTesteResultado(nome, entrada, esperado.ToString("yyyy-MM-dd"), ex.GetType().Name + ": " + ex.Message, false));
        }
    }
}

public sealed record NormalizacaoTesteResultado(
    string Nome,
    string Entrada,
    string Esperado,
    string Obtido,
    bool Sucesso);
