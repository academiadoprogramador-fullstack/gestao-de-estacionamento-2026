namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Estados do atendimento. A Fase 3 produz apenas <c>aguardando</c> e
/// <c>em busca</c>; <c>entregue</c> é reservado ao contrato da Fase 4 e nenhum
/// caso de uso desta fase o atribui.
/// </summary>
public static class EstadoAtendimento
{
    /// <summary>Veículo na unidade, com vaga ocupada e tarifa capturada.</summary>
    public const string Ativo = "ativo";

    /// <summary>Pedido de retirada registrado e cobrança fixada.</summary>
    public const string Aguardando = "aguardando";

    /// <summary>Busca do veículo iniciada e auditada.</summary>
    public const string EmBusca = "em busca";

    /// <summary>Reservado à Fase 4; não é produzido nesta fase.</summary>
    public const string Entregue = "entregue";

    public static readonly IReadOnlySet<string> ProduzidosNaFase3 =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Aguardando,
            EmBusca
        };
}

/// <summary>
/// Tipos de evento append-only do histórico por atendimento. Nenhum valor
/// contém texto de documento, vistoria ou justificativa.
/// </summary>
public static class TiposEventoAtendimento
{
    public const string EntradaRegistrada = "entrada_registrada";
    public const string VagaOcupada = "vaga_ocupada";
    public const string VistoriaEntradaLida = "vistoria_entrada_lida";
    public const string DocumentoLido = "documento_lido";
    public const string PedidoRetiradaRegistrado = "pedido_retirada_registrado";
    public const string ConferenciaRetiradaRegistrada = "conferencia_retirada_registrada";
    public const string CalculoCobrancaFixado = "calculo_cobranca_fixado";
    public const string BuscaIniciada = "busca_iniciada";
    public const string PagamentoRegistrado = "pagamento_registrado";
    public const string DescontoConcedido = "desconto_concedido";
    public const string EstornoRegistrado = "estorno_registrado";
    public const string AutorizacaoConcedida = "autorizacao_concedida";
    public const string AutorizacaoRevogada = "autorizacao_revogada";
    public const string JustificativaAdministrativaLida = "justificativa_administrativa_lida";

    /// <summary>
    /// Allowlist de eventos visíveis ao Manobrista. Exclui conferência,
    /// finanças, autorizações e leituras de justificativa.
    /// </summary>
    public static readonly IReadOnlySet<string> VisiveisAoManobrista =
        new HashSet<string>(StringComparer.Ordinal)
        {
            EntradaRegistrada,
            VagaOcupada,
            VistoriaEntradaLida,
            PedidoRetiradaRegistrado,
            BuscaIniciada
        };
}

/// <summary>
/// Propósitos de cifragem autenticada. Cada propósito vincula o envelope ao
/// campo e impede a troca de conteúdo entre campos e entre registros.
/// </summary>
public static class PropositosCriptografia
{
    public const string Documento = "documento";
    public const string VistoriaEntrada = "vistoria-entrada";
    public const string JustificativaDesconto = "justificativa-desconto";
    public const string JustificativaEstorno = "justificativa-estorno";
    public const string JustificativaAutorizacaoDocumental = "justificativa-autorizacao-documental";
    public const string JustificativaAutorizacaoFinanceira = "justificativa-autorizacao-financeira";
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
    public const string PedidoRetiradaRegistrado = "pedido_retirada_registrado";
    public const string BuscaIniciada = "busca_iniciada";
    public const string PagamentoRegistrado = "pagamento_registrado";
    public const string DescontoConcedido = "desconto_concedido";
    public const string EstornoRegistrado = "estorno_registrado";
    public const string AutorizacaoConcedida = "autorizacao_concedida";
    public const string AutorizacaoRevogada = "autorizacao_revogada";
    public const string JustificativaLida = "justificativa_lida";
}
