using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Retiradas;

namespace Estacionamento.Domain.Entrega;

/// <summary>Códigos estáveis dos bloqueios de entrega (D4-T06).</summary>
public static class CodigosBloqueioEntrega
{
    public const string RequerVistoria = "delivery_requires_exit_inspection";
    public const string DocumentoNaoAprovado = "delivery_document_not_approved";
    public const string SaldoPendente = "delivery_balance_outstanding";
    public const string AutorizacaoObsoleta = "delivery_authorization_stale";
    public const string RequerOcupacao = "delivery_requires_open_occupancy";
    public const string JaConcluida = "delivery_already_completed";
}

/// <summary>
/// Estado derivado de uma autorização para a avaliação de prontidão. A
/// vigência nunca é persistida: é calculada a partir da concessão corrente, da
/// revogação e da revisão referenciada.
/// </summary>
public sealed record ControleAutorizacao(Guid? Id, int? RevisaoReferencia, bool Revogada);

/// <summary>
/// Condição avaliada para a entrega. <see cref="Codigo"/> identifica a
/// verificação e <see cref="CodigoBloqueio"/> o motivo estável quando não
/// atendida; o texto permite apresentar o bloqueio antes da confirmação.
/// </summary>
public sealed record CondicaoEntrega(
    string Codigo,
    string Descricao,
    string Situacao,
    bool Atendida,
    string? CodigoBloqueio,
    Guid? ConcessaoId,
    int? RevisaoReferencia);

/// <summary>Resultado da avaliação de prontidão, sem reservar a entrega.</summary>
public sealed record AvaliacaoEntrega(
    IReadOnlyList<CondicaoEntrega> Condicoes,
    IReadOnlyList<string> Bloqueios,
    ControleAutorizacao? DocumentalUsada,
    ControleAutorizacao? FinanceiraUsada)
{
    public bool Pronta => Bloqueios.Count == 0;
}

/// <summary>
/// Regras puras de prontidão da entrega. A conferência aprovada e o saldo zero
/// são as condições normais; uma divergência documental exige a concessão
/// documental corrente e um saldo positivo exige a concessão financeira
/// vinculada à revisão corrente da razão. As exceções são independentes.
/// </summary>
public static class AvaliadorEntrega
{
    public const string CodigoVistoria = "vistoria_saida";
    public const string CodigoConferencia = "conferencia_documental";
    public const string CodigoSaldo = "saldo_exigivel";
    public const string CodigoOcupacao = "ocupacao_aberta";

    public static AvaliacaoEntrega Avaliar(
        bool vistoriaRegistrada,
        string conferencia,
        decimal saldo,
        int revisaoRazao,
        ControleAutorizacao? documental,
        ControleAutorizacao? financeira,
        bool ocupacaoPropriaAberta)
    {
        var condicoes = new List<CondicaoEntrega>
        {
            AvaliarVistoria(vistoriaRegistrada),
            AvaliarConferencia(conferencia, documental),
            AvaliarSaldo(saldo, revisaoRazao, financeira),
            AvaliarOcupacao(ocupacaoPropriaAberta)
        };

        var bloqueios = condicoes
            .Where(condicao => !condicao.Atendida && condicao.CodigoBloqueio is not null)
            .Select(condicao => condicao.CodigoBloqueio!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var documentalUsada = condicoes[1] is { Atendida: true, ConcessaoId: not null }
            ? documental
            : null;
        var financeiraUsada = condicoes[2] is { Atendida: true, ConcessaoId: not null }
            ? financeira
            : null;

        return new AvaliacaoEntrega(condicoes, bloqueios, documentalUsada, financeiraUsada);
    }

    private static CondicaoEntrega AvaliarVistoria(bool registrada) =>
        new(
            CodigoVistoria,
            "Vistoria de saída",
            registrada ? "Registrada" : "Não registrada",
            registrada,
            registrada ? null : CodigosBloqueioEntrega.RequerVistoria,
            null,
            null);

    private static CondicaoEntrega AvaliarConferencia(
        string conferencia,
        ControleAutorizacao? documental)
    {
        if (string.Equals(conferencia, ConferenciaRetirada.Aprovada, StringComparison.Ordinal))
        {
            return new CondicaoEntrega(
                CodigoConferencia,
                "Conferência documental",
                "Aprovada",
                true,
                null,
                null,
                null);
        }

        if (documental is { Id: not null, Revogada: false })
        {
            return new CondicaoEntrega(
                CodigoConferencia,
                "Conferência documental",
                "Divergente · exceção documental vigente",
                true,
                null,
                documental.Id,
                documental.RevisaoReferencia);
        }

        var situacao = documental is { Id: not null }
            ? "Divergente · autorização documental revogada"
            : "Divergente sem autorização documental vigente";

        var bloqueio = documental is { Id: not null }
            ? CodigosBloqueioEntrega.AutorizacaoObsoleta
            : CodigosBloqueioEntrega.DocumentoNaoAprovado;

        return new CondicaoEntrega(
            CodigoConferencia,
            "Conferência documental",
            situacao,
            false,
            bloqueio,
            null,
            null);
    }

    private static CondicaoEntrega AvaliarSaldo(
        decimal saldo,
        int revisaoRazao,
        ControleAutorizacao? financeira)
    {
        if (saldo == 0)
        {
            return new CondicaoEntrega(
                CodigoSaldo,
                "Saldo exigível",
                "Saldo zerado",
                true,
                null,
                null,
                null);
        }

        var vigente = financeira is { Id: not null, Revogada: false } &&
            financeira.RevisaoReferencia == revisaoRazao;

        if (vigente)
        {
            return new CondicaoEntrega(
                CodigoSaldo,
                "Saldo exigível",
                "Saldo positivo · exceção financeira vigente; o saldo permanece exigível",
                true,
                null,
                financeira!.Id,
                financeira.RevisaoReferencia);
        }

        var situacao = financeira is { Id: not null }
            ? "Saldo positivo · autorização financeira revogada ou obsoleta"
            : "Saldo positivo sem autorização financeira vigente";

        var bloqueio = financeira is { Id: not null }
            ? CodigosBloqueioEntrega.AutorizacaoObsoleta
            : CodigosBloqueioEntrega.SaldoPendente;

        return new CondicaoEntrega(
            CodigoSaldo,
            "Saldo exigível",
            situacao,
            false,
            bloqueio,
            null,
            null);
    }

    private static CondicaoEntrega AvaliarOcupacao(bool propriaAberta) =>
        new(
            CodigoOcupacao,
            "Ocupação da visita",
            propriaAberta ? "Ocupação aberta do próprio atendimento" : "Não há ocupação aberta do atendimento",
            propriaAberta,
            propriaAberta ? null : CodigosBloqueioEntrega.RequerOcupacao,
            null,
            null);
}

/// <summary>
/// Deriva a representação opaca das versões de controle de autorização usadas
/// na prontidão e na confirmação. A ausência é explícita e o valor muda quando
/// uma concessão é substituída ou revogada.
/// </summary>
public static class VersaoControleAutorizacao
{
    public const string Ausente = "ausente";

    public static string Calcular(
        string tipo,
        Guid? concessaoId,
        int? revisaoReferencia,
        bool revogada) =>
        concessaoId is null
            ? Ausente
            : $"{tipo}:{concessaoId.Value:N}:{revisaoReferencia?.ToString() ?? "0"}:{(revogada ? "revogada" : "vigente")}";

    public static bool Corresponde(
        string? informada,
        string tipo,
        Guid? concessaoId,
        int? revisaoReferencia,
        bool revogada) =>
        string.Equals(
            informada,
            Calcular(tipo, concessaoId, revisaoReferencia, revogada),
            StringComparison.Ordinal);
}
