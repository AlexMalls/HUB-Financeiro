using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class CnabDadosBancariosWindow : Window
{
    private readonly CnabDadosBancariosFuncionario _dados;
    private readonly bool _editarIdentificacao;

    public CnabDadosBancariosFuncionario DadosSalvos => _dados;

    public CnabDadosBancariosWindow(CnabDadosBancariosFuncionario dados, bool editarIdentificacao = false)
    {
        InitializeComponent();
        _dados = dados.Clone();
        _editarIdentificacao = editarIdentificacao;

        NomeTextBlock.Text = _dados.Nome;
        CodigoTextBlock.Text = _dados.CodigoFuncionario > 0
            ? $"Matrícula/código: {_dados.CodigoFuncionario}"
            : "Matrícula/código não identificado";

        IdentificacaoSomenteLeituraPanel.Visibility = editarIdentificacao ? Visibility.Collapsed : Visibility.Visible;
        IdentificacaoEdicaoPanel.Visibility = editarIdentificacao ? Visibility.Visible : Visibility.Collapsed;
        NomeTextBox.Text = _dados.Nome;
        CodigoFuncionarioTextBox.Text = _dados.CodigoFuncionario > 0 ? _dados.CodigoFuncionario.ToString() : string.Empty;

        DocumentoTextBox.Text = _dados.Documento;
        BancoTextBox.Text = string.IsNullOrWhiteSpace(_dados.BancoCodigo) ? "033" : _dados.BancoCodigo;
        AgenciaTextBox.Text = _dados.Agencia;
        AgenciaDvTextBox.Text = _dados.AgenciaDv;
        ContaTextBox.Text = _dados.Conta;
        ContaDvTextBox.Text = _dados.ContaDv;
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        if (_editarIdentificacao)
        {
            string nome = NomeTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(nome))
            {
                CustomMessageBox.ShowWarning("Informe o nome do colaborador.");
                return;
            }

            if (!int.TryParse(CodigoFuncionarioTextBox.Text.Trim(), out int codigo) || codigo <= 0)
            {
                CustomMessageBox.ShowWarning("Informe uma matrícula/código numérico válido.");
                return;
            }

            _dados.Nome = nome;
            _dados.CodigoFuncionario = codigo;
        }

        string documento = CnabDadosBancariosFuncionario.SomenteDigitos(DocumentoTextBox.Text);
        string banco = CnabDadosBancariosFuncionario.SomenteDigitos(BancoTextBox.Text);
        string agencia = CnabDadosBancariosFuncionario.SomenteDigitos(AgenciaTextBox.Text);
        string conta = CnabDadosBancariosFuncionario.SomenteDigitos(ContaTextBox.Text);
        string agenciaDv = CnabDadosBancariosFuncionario.SomenteAlfanumerico(AgenciaDvTextBox.Text).ToUpperInvariant();
        string contaDv = CnabDadosBancariosFuncionario.SomenteAlfanumerico(ContaDvTextBox.Text).ToUpperInvariant();

        if (documento.Length != 11)
        {
            CustomMessageBox.ShowWarning("Informe um CPF válido com 11 dígitos.");
            return;
        }
        if (banco.Length != 3)
        {
            CustomMessageBox.ShowWarning("Informe o código do banco com 3 dígitos.");
            return;
        }
        if (agencia.Length == 0 || agencia.Length > 5)
        {
            CustomMessageBox.ShowWarning("Informe uma agência válida com até 5 dígitos.");
            return;
        }
        if (conta.Length == 0 || conta.Length > 12 || contaDv.Length != 1)
        {
            CustomMessageBox.ShowWarning("Informe a conta e o respectivo DV.");
            return;
        }

        _dados.Documento = documento;
        _dados.BancoCodigo = banco;
        _dados.Agencia = agencia;
        _dados.AgenciaDv = agenciaDv;
        _dados.Conta = conta;
        _dados.ContaDv = contaDv;
        _dados.UltimaAtualizacao = DateTime.Now;
        _dados.OrigemDados = "Cadastro manual";

        DialogResult = true;
        Close();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
