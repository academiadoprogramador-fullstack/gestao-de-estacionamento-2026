namespace Estacionamento.Domain.Atendimentos;

/// <summary>Estados do atendimento previstos para a Fase 2.</summary>
public static class EstadoAtendimento
{
    /// <summary>Veículo na unidade, com vaga ocupada e tarifa capturada.</summary>
    public const string Ativo = "ativo";
}

/// <summary>
/// Tipos de evento append-only do histórico por atendimento. Nenhum valor
/// contém texto de documento ou vistoria.
/// </summary>
public static class TiposEventoAtendimento
{
    public const string EntradaRegistrada = "entrada_registrada";
    public const string VagaOcupada = "vaga_ocupada";
    public const string VistoriaEntradaLida = "vistoria_entrada_lida";
    public const string DocumentoLido = "documento_lido";

    /// <summary>Allowlist de eventos visíveis ao Manobrista.</summary>
    public static readonly IReadOnlySet<string> VisiveisAoManobrista =
        new HashSet<string>(StringComparer.Ordinal)
        {
            EntradaRegistrada,
            VagaOcupada,
            VistoriaEntradaLida
        };
}

/// <summary>
/// Propósitos de cifragem autenticada. Cada propósito vincula o envelope ao
/// campo e impede a troca de conteúdo entre campos.
/// </summary>
public static class PropositosCriptografia
{
    public const string Documento = "documento";
    public const string VistoriaEntrada = "vistoria-entrada";
}

/// <summary>
/// Tipos de auditoria de negócio append-only. Utilizados nos eventos gerais e
/// nos eventos de atendimento.
/// </summary>
public static class TiposAuditoriaNegocio
{
    public const string FuncionarioCriado = "funcionario_criado";
    public const string AcessoAlterado = "acesso_alterado";
    public const string VagaCadastrada = "vaga_cadastrada";
    public const string TarifaPublicada = "tarifa_publicada";
    public const string EntradaRegistrada = "entrada_registrada";
    public const string DocumentoLido = "documento_lido";
    public const string VistoriaLida = "vistoria_lida";
}
