public class Fornecedor
{
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string CNPJ { get; set; } = string.Empty;
    public bool Ativo { get; set; }

    public override string ToString()
    {
        return Nome;
    }
}
