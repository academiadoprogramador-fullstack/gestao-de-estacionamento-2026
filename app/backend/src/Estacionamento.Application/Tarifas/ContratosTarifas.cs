using Estacionamento.Application.Comum;

namespace Estacionamento.Application.Tarifas;

/// <summary>
/// Publicação de versão imutável. A versão-base é a versão corrente conhecida
/// pelo cliente ou nula quando ele afirma que ainda não há tarifa.
/// </summary>
public sealed record RequisicaoPublicarTarifa(decimal Valor, int? VersaoBaseEsperada);

/// <summary>Configuração corrente, inclusive quando ainda não há tarifa.</summary>
public sealed record TarifaCorrente(
    bool TemTarifa,
    int? Versao,
    decimal? Valor,
    Guid? PublicadoPorId,
    DateTimeOffset? PublicadoEmUtc,
    int? VersaoBase);

/// <summary>Versão publicada no histórico administrativo.</summary>
public sealed record TarifaResumo(
    int Versao,
    decimal Valor,
    Guid PublicadoPorId,
    string PublicadoPorNome,
    DateTimeOffset PublicadoEmUtc);

/// <summary>Publicação, consulta corrente e histórico de tarifas.</summary>
public interface IServicoTarifas
{
    Task<TarifaResumo> PublicarAsync(
        AtorAtual ator,
        RequisicaoPublicarTarifa requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<TarifaCorrente> ObterCorrenteAsync(CancellationToken cancellationToken = default);

    Task<Pagina<TarifaResumo>> ListarAsync(
        AtorAtual ator,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default);
}
