using Estacionamento.Application.Comum;

namespace Estacionamento.Application.Fotos;

/// <summary>
/// Arquivo recebido do cliente. O nome original existe apenas durante a
/// requisição e nunca é persistido ou devolvido.
/// </summary>
public sealed record ArquivoFoto(
    string? NomeOriginal,
    string? TipoConteudo,
    long TamanhoDeclarado,
    Stream Conteudo);

/// <summary>Resultado mínimo da foto após validação e normalização.</summary>
public sealed record ImagemProcessada(
    byte[] Conteudo,
    string Mime,
    int Largura,
    int Altura,
    byte[] HashSha256);

/// <summary>Metadados públicos de uma foto confirmada, sem chave do objeto.</summary>
public sealed record FotoVistoriaResumo(
    Guid FotoId,
    string Tipo,
    string Mime,
    long TamanhoBytes,
    int Largura,
    int Altura,
    DateTimeOffset CriadoEmUtc);

/// <summary>Recibo mínimo do envio, reutilizado no replay idempotente.</summary>
public sealed record ResultadoFoto(
    Guid FotoId,
    Guid AtendimentoId,
    string Tipo,
    string Estado,
    string Mime,
    long TamanhoBytes,
    int Largura,
    int Altura,
    DateTimeOffset CriadoEmUtc,
    string Location);

/// <summary>Situação consolidada do orçamento compartilhado de fotos.</summary>
public sealed record OrcamentoFotosResumo(
    int PercentualBytes,
    int PercentualOperacoesClasseA,
    int PercentualOperacoesClasseB,
    bool Aviso,
    bool Suspenso,
    string? Motivo,
    int LimiteFotosPorVistoria);

/// <summary>Lista de fotos disponíveis de uma vistoria e o estado da janela.</summary>
public sealed record ListaFotosVistoria(
    IReadOnlyList<FotoVistoriaResumo> Itens,
    int Quantidade,
    string Janela,
    bool Vencido,
    OrcamentoFotosResumo Orcamento);

/// <summary>
/// Resumo sem conteúdo usado no detalhe do atendimento e na prontidão de
/// entrega. Não inclui chave, binário ou nome original.
/// </summary>
public sealed record ResumoFotosVistoria(
    int QuantidadeEntrada,
    int QuantidadeSaida,
    string JanelaEntrada,
    string JanelaSaida,
    OrcamentoFotosResumo Orcamento);

/// <summary>Conteúdo privado transmitido somente depois da auditoria.</summary>
public sealed record ConteudoFoto(
    Stream Conteudo,
    string Mime,
    long TamanhoBytes,
    string NomeArquivo);

/// <summary>Porta de armazenamento privado de objetos.</summary>
public interface IArmazenamentoFotos
{
    Task EnviarAsync(
        string chave,
        Stream conteudo,
        string tipoConteudo,
        CancellationToken cancellationToken = default);

    Task<Stream> AbrirAsync(string chave, CancellationToken cancellationToken = default);

    Task ExcluirAsync(string chave, CancellationToken cancellationToken = default);

    Task<bool> ExisteAsync(string chave, CancellationToken cancellationToken = default);
}

/// <summary>Validação, orientação, redução e regravação sem metadados.</summary>
public interface IProcessadorImagens
{
    Task<ImagemProcessada> ProcessarAsync(
        Stream conteudo,
        string? tipoConteudoDeclarado,
        CancellationToken cancellationToken = default);
}

/// <summary>Envio, consulta e leitura auditada de fotos das vistorias.</summary>
public interface IServicoFotosVistoria
{
    Task<ResultadoFoto> EnviarAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        ArquivoFoto arquivo,
        byte[] versaoAtendimentoEsperada,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<ListaFotosVistoria> ListarAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        CancellationToken cancellationToken = default);

    Task<ConteudoFoto> AbrirConteudoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        Guid fotoId,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<ResumoFotosVistoria> ObterResumoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        CancellationToken cancellationToken = default);
}

/// <summary>Rotina retomável de confirmação, limpeza e retenção.</summary>
public interface IServicoManutencaoFotos
{
    Task<int> ExecutarLoteAsync(CancellationToken cancellationToken = default);
}
