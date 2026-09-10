using System.Security.Cryptography;

namespace HubFinanceiro;

public sealed class FornecedorIdentidadeRegistro
{
    public int IdInterno { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int Codigo { get; set; }
    public int Natureza { get; set; }
    public string Email { get; set; } = string.Empty;
    public int DiaPagamento { get; set; }
    public int TipoPagamento { get; set; }
    public bool Ativo { get; set; }
    public bool Administradora { get; set; }
    public bool Corretora { get; set; }

    public bool CorrespondeExatamente(Fornecedor fornecedor)
    {
        return string.Equals(Nome, fornecedor.Nome, StringComparison.Ordinal)
            && Codigo == fornecedor.Codigo
            && Natureza == fornecedor.Natureza
            && string.Equals(Email ?? string.Empty, fornecedor.Email ?? string.Empty, StringComparison.Ordinal)
            && DiaPagamento == fornecedor.DiaPagamento
            && TipoPagamento == fornecedor.TipoPagamento
            && Ativo == fornecedor.Ativo
            && Administradora == fornecedor.Administradora
            && Corretora == fornecedor.Corretora;
    }

    public void AtualizarSnapshot(Fornecedor fornecedor)
    {
        Nome = fornecedor.Nome;
        Codigo = fornecedor.Codigo;
        Natureza = fornecedor.Natureza;
        Email = fornecedor.Email ?? string.Empty;
        DiaPagamento = fornecedor.DiaPagamento;
        TipoPagamento = fornecedor.TipoPagamento;
        Ativo = fornecedor.Ativo;
        Administradora = fornecedor.Administradora;
        Corretora = fornecedor.Corretora;
    }
}

public static class FornecedorIdentidadeService
{
    public const int IdInternoMinimo = 100_000_000;
    public const int IdInternoMaximoExclusivo = 1_000_000_000;

    public static Dictionary<Fornecedor, int> Reconciliar(
        IReadOnlyList<Fornecedor> fornecedores,
        List<FornecedorIdentidadeRegistro> registros,
        out bool alterado)
    {
        alterado = false;
        var resultado = new Dictionary<Fornecedor, int>();
        var utilizados = new HashSet<FornecedorIdentidadeRegistro>();
        var idsUsados = new HashSet<int>();

        foreach (var fornecedor in fornecedores)
        {
            var registro = registros.FirstOrDefault(r =>
                !utilizados.Contains(r) && r.CorrespondeExatamente(fornecedor));

            if (registro == null && fornecedor.Codigo > 0)
            {
                var candidatosCodigo = registros
                    .Where(r => !utilizados.Contains(r) && r.Codigo == fornecedor.Codigo)
                    .Take(2)
                    .ToList();

                if (candidatosCodigo.Count == 1)
                    registro = candidatosCodigo[0];
            }

            if (registro == null)
            {
                var candidatosNome = registros
                    .Where(r => !utilizados.Contains(r)
                        && string.Equals(r.Nome, fornecedor.Nome, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();

                if (candidatosNome.Count == 1)
                    registro = candidatosNome[0];
            }

            if (registro == null)
            {
                registro = new FornecedorIdentidadeRegistro();
                registros.Add(registro);
                alterado = true;
            }

            if (!IdInternoValido(registro.IdInterno) || idsUsados.Contains(registro.IdInterno))
            {
                registro.IdInterno = GerarIdInterno(registros, fornecedores, idsUsados);
                alterado = true;
            }

            if (!registro.CorrespondeExatamente(fornecedor))
            {
                registro.AtualizarSnapshot(fornecedor);
                alterado = true;
            }

            utilizados.Add(registro);
            idsUsados.Add(registro.IdInterno);
            resultado[fornecedor] = registro.IdInterno;
        }

        if (registros.RemoveAll(r => !utilizados.Contains(r)) > 0)
            alterado = true;

        return resultado;
    }

    public static int GerarIdInterno(
        IEnumerable<FornecedorIdentidadeRegistro> registros,
        IEnumerable<Fornecedor> fornecedores,
        IEnumerable<int>? idsAdicionais = null)
    {
        var reservados = new HashSet<int>(
            registros.Where(r => IdInternoValido(r.IdInterno)).Select(r => r.IdInterno));

        foreach (var codigo in fornecedores.Select(f => f.Codigo).Where(IdInternoValido))
            reservados.Add(codigo);

        if (idsAdicionais != null)
        {
            foreach (var id in idsAdicionais.Where(IdInternoValido))
                reservados.Add(id);
        }

        for (var tentativa = 0; tentativa < 10_000; tentativa++)
        {
            var candidato = RandomNumberGenerator.GetInt32(IdInternoMinimo, IdInternoMaximoExclusivo);
            if (!reservados.Contains(candidato))
                return candidato;
        }

        throw new InvalidOperationException("Não foi possível gerar um ID interno único para o fornecedor.");
    }

    public static bool IdInternoValido(int id)
        => id >= IdInternoMinimo && id < IdInternoMaximoExclusivo;

    public static string CodigoVisivel(int codigo)
        => codigo > 0 ? codigo.ToString() : string.Empty;

    public static Fornecedor? LocalizarFornecedor(
        IEnumerable<Fornecedor> fornecedores,
        FornecedorIdentidadeRegistro registro)
    {
        var lista = fornecedores.ToList();

        var exatos = lista.Where(registro.CorrespondeExatamente).Take(2).ToList();
        if (exatos.Count == 1)
            return exatos[0];

        if (registro.Codigo > 0)
        {
            var porCodigo = lista.Where(f => f.Codigo == registro.Codigo).Take(2).ToList();
            if (porCodigo.Count == 1)
                return porCodigo[0];
        }

        var porNome = lista
            .Where(f => string.Equals(f.Nome, registro.Nome, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return porNome.Count == 1 ? porNome[0] : null;
    }
}
