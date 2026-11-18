namespace Estacionamento.Application.Comum;

/// <summary>Tipo de falha de negócio, mapeado para o status HTTP na API.</summary>
public enum TipoFalha
{
    Validacao,
    AcessoNegado,
    NaoEncontrado,
    Conflito,
    RegraNaoSatisfeita,
    ProtecaoCampo
}

/// <summary>
/// Falha controlada de caso de uso. O código é estável e não contém PII; as
/// mensagens e o detalhe nunca carregam documento, vistoria ou segredo.
/// </summary>
public sealed class ExcecaoAplicacao : Exception
{
    public ExcecaoAplicacao(
        TipoFalha tipo,
        string codigo,
        string mensagem,
        IReadOnlyDictionary<string, string[]>? erros = null)
        : base(mensagem)
    {
        Tipo = tipo;
        Codigo = codigo;
        Erros = erros;
    }

    public TipoFalha Tipo { get; }

    public string Codigo { get; }

    public IReadOnlyDictionary<string, string[]>? Erros { get; }

    public static ExcecaoAplicacao Validacao(string codigo, string mensagem) =>
        new(TipoFalha.Validacao, codigo, mensagem);

    public static ExcecaoAplicacao AcessoNegado(string codigo = "acesso_negado") =>
        new(TipoFalha.AcessoNegado, codigo, "Acesso negado para o perfil autenticado.");

    public static ExcecaoAplicacao NaoEncontrado(string codigo, string mensagem) =>
        new(TipoFalha.NaoEncontrado, codigo, mensagem);

    public static ExcecaoAplicacao Conflito(string codigo, string mensagem) =>
        new(TipoFalha.Conflito, codigo, mensagem);

    public static ExcecaoAplicacao RegraNaoSatisfeita(string codigo, string mensagem) =>
        new(TipoFalha.RegraNaoSatisfeita, codigo, mensagem);

    public static ExcecaoAplicacao ProtecaoCampo() =>
        new(
            TipoFalha.ProtecaoCampo,
            "field_protection_failure",
            "Não foi possível proteger ou ler o campo sensível.");
}
