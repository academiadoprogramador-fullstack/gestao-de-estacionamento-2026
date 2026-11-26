using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Fotos;
using Microsoft.Extensions.Options;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Adaptador S3 compatível com Cloudflare R2. O bucket é privado e apenas o
/// servidor recebe as credenciais; o navegador nunca vê chave de objeto ou URL
/// do provedor. A R2 não implementa Streaming SigV4, então o upload desabilita
/// a assinatura de payload e o checksum padrão do AWS SDK.
/// </summary>
public sealed class ArmazenamentoFotosR2 : IArmazenamentoFotos, IDisposable
{
    private readonly ArmazenamentoFotosOptions _opcoes;
    private readonly IAmazonS3 _cliente;

    public ArmazenamentoFotosR2(IOptions<ArmazenamentoFotosOptions> opcoes)
    {
        _opcoes = opcoes.Value;

        var configuracao = new AmazonS3Config
        {
            ServiceURL = _opcoes.R2.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = _opcoes.R2.RegiaoAssinatura,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        _cliente = new AmazonS3Client(
            new BasicAWSCredentials(_opcoes.R2.AccessKeyId, _opcoes.R2.SecretAccessKey),
            configuracao);
    }

    public async Task EnviarAsync(
        string chave,
        Stream conteudo,
        string tipoConteudo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _cliente.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _opcoes.R2.Bucket,
                    Key = chave,
                    InputStream = conteudo,
                    ContentType = tipoConteudo,
                    DisablePayloadSigning = true,
                    DisableDefaultChecksumValidation = true
                },
                cancellationToken);
        }
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == HttpStatusCode.NotFound)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O bucket privado de fotos não está disponível.");
        }
        catch (AmazonS3Exception)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
        catch (AmazonServiceException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
        catch (HttpRequestException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
    }

    public async Task<Stream> AbrirAsync(string chave, CancellationToken cancellationToken = default)
    {
        try
        {
            var resposta = await _cliente.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = _opcoes.R2.Bucket,
                    Key = chave
                },
                cancellationToken);

            return resposta.ResponseStream;
        }
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == HttpStatusCode.NotFound)
        {
            throw ExcecaoAplicacao.NaoEncontrado(
                "photo_content_unavailable",
                "O conteúdo da foto não está disponível.");
        }
        catch (AmazonServiceException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
        catch (HttpRequestException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
    }

    public async Task ExcluirAsync(string chave, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cliente.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _opcoes.R2.Bucket,
                    Key = chave
                },
                cancellationToken);
        }
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == HttpStatusCode.NotFound)
        {
            // Exclusão idempotente: o objeto já não existe.
        }
        catch (AmazonServiceException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
        catch (HttpRequestException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
    }

    public async Task<bool> ExisteAsync(string chave, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cliente.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = _opcoes.R2.Bucket,
                    Key = chave
                },
                cancellationToken);
            return true;
        }
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (AmazonServiceException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
        catch (HttpRequestException)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_unavailable",
                "O armazenamento privado de fotos está indisponível.");
        }
    }

    public void Dispose() => _cliente.Dispose();
}
