using System.Security.Cryptography;
using System.Text;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Seguranca;
using Microsoft.Extensions.Options;

namespace Estacionamento.Infrastructure.Seguranca;

/// <summary>
/// Cifragem de campos com AES-256-GCM e chave versionada.
///
/// Envelope persistido:
/// <c>[versão:1][tamanhoKid:1][kid UTF-8][nonce:12][tag:16][ciphertext]</c>.
///
/// O AAD é determinístico e reconstruído no servidor com versão do formato,
/// <c>kid</c>, ambiente, aplicação, propósito e identificador do atendimento;
/// ele nunca inclui valores mutáveis nem contexto fornecido pelo cliente.
/// </summary>
public sealed class CriptografiaCamposAesGcm : ICriptografiaCampos
{
    private const byte VersaoFormato = 1;
    private const int TamanhoNonce = 12;
    private const int TamanhoTag = 16;
    private const int DeslocamentoConteudo = 2;

    private readonly ProtecaoDadosOptions _options;
    private readonly IContextoAplicacao _contexto;

    public CriptografiaCamposAesGcm(
        IOptions<ProtecaoDadosOptions> options,
        IContextoAplicacao contexto)
    {
        _options = options.Value;
        _contexto = contexto;
    }

    public byte[] Cifrar(string proposito, Guid atendimentoId, string texto)
    {
        var kid = _options.VersaoAtiva;
        if (string.IsNullOrWhiteSpace(kid) ||
            !_options.Chaves.TryGetValue(kid, out var chaveBase64) ||
            !ValidadorChaveSimetrica.PossuiFormatoValido(chaveBase64))
        {
            throw ExcecaoAplicacao.ProtecaoCampo();
        }

        var kidBytes = Encoding.UTF8.GetBytes(kid);
        if (kidBytes.Length is 0 or > byte.MaxValue)
        {
            throw ExcecaoAplicacao.ProtecaoCampo();
        }

        var textoBytes = Encoding.UTF8.GetBytes(texto);
        var nonce = RandomNumberGenerator.GetBytes(TamanhoNonce);
        var tag = new byte[TamanhoTag];
        var ciphertext = new byte[textoBytes.Length];
        var aad = ConstruirAad(VersaoFormato, kid, proposito, atendimentoId);

        try
        {
            using var aes = new AesGcm(ValidadorChaveSimetrica.Decodificar(chaveBase64), TamanhoTag);
            aes.Encrypt(nonce, textoBytes, ciphertext, tag, aad);
        }
        catch (CryptographicException)
        {
            throw ExcecaoAplicacao.ProtecaoCampo();
        }

        var envelope = new byte[DeslocamentoConteudo + kidBytes.Length + TamanhoNonce + TamanhoTag + ciphertext.Length];
        envelope[0] = VersaoFormato;
        envelope[1] = (byte)kidBytes.Length;
        kidBytes.CopyTo(envelope, DeslocamentoConteudo);

        var posicao = DeslocamentoConteudo + kidBytes.Length;
        nonce.CopyTo(envelope, posicao);
        posicao += TamanhoNonce;
        tag.CopyTo(envelope, posicao);
        posicao += TamanhoTag;
        ciphertext.CopyTo(envelope, posicao);

        return envelope;
    }

    public string Decifrar(string proposito, Guid atendimentoId, byte[] envelope)
    {
        if (envelope.Length < DeslocamentoConteudo + TamanhoNonce + TamanhoTag)
        {
            throw ExcecaoAplicacao.ProtecaoCampo();
        }

        if (envelope[0] != VersaoFormato)
        {
            throw ExcecaoAplicacao.ProtecaoCampo();
        }

        var tamanhoKid = envelope[1];
        if (tamanhoKid == 0 ||
            envelope.Length < DeslocamentoConteudo + tamanhoKid + TamanhoNonce + TamanhoTag)
        {
            throw ExcecaoAplicacao.ProtecaoCampo();
        }

        var kid = Encoding.UTF8.GetString(envelope, DeslocamentoConteudo, tamanhoKid);
        if (!_options.Chaves.TryGetValue(kid, out var chaveBase64) ||
            !ValidadorChaveSimetrica.PossuiFormatoValido(chaveBase64))
        {
            throw ExcecaoAplicacao.ProtecaoCampo();
        }

        var posicao = DeslocamentoConteudo + tamanhoKid;
        var nonce = envelope.AsSpan(posicao, TamanhoNonce);
        posicao += TamanhoNonce;
        var tag = envelope.AsSpan(posicao, TamanhoTag);
        posicao += TamanhoTag;
        var ciphertext = envelope.AsSpan(posicao);
        var texto = new byte[ciphertext.Length];
        var aad = ConstruirAad(VersaoFormato, kid, proposito, atendimentoId);

        try
        {
            using var aes = new AesGcm(ValidadorChaveSimetrica.Decodificar(chaveBase64), TamanhoTag);
            aes.Decrypt(nonce, ciphertext, tag, texto, aad);
            return Encoding.UTF8.GetString(texto);
        }
        catch (CryptographicException)
        {
            // Não tenta outras chaves, não retorna vazio e não expõe conteúdo.
            throw ExcecaoAplicacao.ProtecaoCampo();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(texto);
        }
    }

    private byte[] ConstruirAad(
        byte versaoFormato,
        string kid,
        string proposito,
        Guid atendimentoId) =>
        Encoding.UTF8.GetBytes(
            $"v{versaoFormato}|kid:{kid}|ambiente:{_contexto.NomeAmbiente}|app:{_contexto.NomeAplicacao}" +
            $"|proposito:{proposito}|atendimento:{atendimentoId:N}");
}
