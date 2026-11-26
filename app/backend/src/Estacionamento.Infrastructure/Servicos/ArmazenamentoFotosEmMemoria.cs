using System.Collections.Concurrent;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Fotos;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Dublê em memória para testes e para o modo sem R2. Não persiste entre
/// processos e nunca é registrado fora de testes/manutenção explícita.
/// </summary>
public sealed class ArmazenamentoFotosEmMemoria : IArmazenamentoFotos
{
    private readonly ConcurrentDictionary<string, byte[]> _objetos = new(StringComparer.Ordinal);

    public bool FalharProximoEnvio { get; set; }
    public bool FalharProximaLeitura { get; set; }
    public bool FalharProximaExclusao { get; set; }

    public IReadOnlyDictionary<string, byte[]> Objetos => _objetos;

    public async Task EnviarAsync(
        string chave,
        Stream conteudo,
        string tipoConteudo,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        if (FalharProximoEnvio)
        {
            FalharProximoEnvio = false;
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "Falha simulada do armazenamento de fotos.");
        }

        using var memoria = new MemoryStream();
        await conteudo.CopyToAsync(memoria, cancellationToken);
        _objetos[chave] = memoria.ToArray();
    }

    public Task<Stream> AbrirAsync(string chave, CancellationToken cancellationToken = default)
    {
        if (FalharProximaLeitura)
        {
            FalharProximaLeitura = false;
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "Falha simulada de leitura do armazenamento de fotos.");
        }

        if (!_objetos.TryGetValue(chave, out var conteudo))
        {
            throw ExcecaoAplicacao.NaoEncontrado(
                "photo_content_unavailable",
                "O conteúdo da foto não está disponível.");
        }

        return Task.FromResult<Stream>(new MemoryStream(conteudo, writable: false));
    }

    public Task ExcluirAsync(string chave, CancellationToken cancellationToken = default)
    {
        if (FalharProximaExclusao)
        {
            FalharProximaExclusao = false;
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "Falha simulada de exclusão do armazenamento de fotos.");
        }

        _objetos.TryRemove(chave, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExisteAsync(string chave, CancellationToken cancellationToken = default) =>
        Task.FromResult(_objetos.ContainsKey(chave));
}
