using Estacionamento.Application.Comum;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Seguranca;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Tarifas;
using Estacionamento.Domain.Vagas;
using Estacionamento.Infrastructure.Seguranca;
using Microsoft.Extensions.Options;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class DominioFase2Testes
{
    [TestMethod]
    public void PlacaEhNormalizadaSemEspacosHifensECaixa()
    {
        Assert.AreEqual("ABC1D23", NormalizadorPlaca.Normalizar(" abc-1d2 3 "));
        Assert.IsTrue(NormalizadorPlaca.EhValido("ABC1D23"));
        Assert.IsFalse(NormalizadorPlaca.EhValido(string.Empty));
        Assert.IsFalse(NormalizadorPlaca.EhValido("ABC 123"));
        Assert.IsFalse(NormalizadorPlaca.EhValido(new string('A', 17)));
        Assert.IsFalse(NormalizadorPlaca.EhValido("ABC_123"));
    }

    [TestMethod]
    public void CodigoDeVagaEhNormalizadoComTrimEMaiusculas()
    {
        Assert.AreEqual("A-01", CodigoVaga.Normalizar("  a-01 "));
        Assert.IsTrue(CodigoVaga.EhValido("A"));
        Assert.IsTrue(CodigoVaga.EhValido(new string('X', 32)));
        Assert.IsFalse(CodigoVaga.EhValido(string.Empty));
        Assert.IsFalse(CodigoVaga.EhValido(new string('X', 33)));
    }

    [TestMethod]
    public void ValorDeDiariaRejeitaZeroNegativoEscalaEExcesso()
    {
        Assert.IsTrue(ValorDiaria.TentarCriar(20.00m, out var valido));
        Assert.AreEqual(20.00m, valido.Valor);

        Assert.IsFalse(ValorDiaria.TentarCriar(0m, out _));
        Assert.IsFalse(ValorDiaria.TentarCriar(-1m, out _));
        Assert.IsFalse(ValorDiaria.TentarCriar(20.001m, out _));
        Assert.IsFalse(ValorDiaria.TentarCriar(ValorDiaria.Maximo + 1m, out _));
        Assert.IsFalse(ValorDiaria.TentarCriar(1_000_000_000_000_000_000m, out _));
    }

    [TestMethod]
    public void IdentificadorPublicoEhLegivelEValidado()
    {
        var identificador = IdentificadorAtendimento.Formatar(2026, 1);
        Assert.AreEqual("AT-2026-000001", identificador);
        Assert.IsTrue(IdentificadorAtendimento.EhValido(identificador));
        Assert.IsFalse(IdentificadorAtendimento.EhValido("at-2026-000001"));
        Assert.IsFalse(IdentificadorAtendimento.EhValido("AT-2026-1"));
        Assert.IsFalse(IdentificadorAtendimento.EhValido("AT-2026-000001-extra"));
    }

    [TestMethod]
    public void CanonicalizacaoEhDeterministicaEIndependeDaOrdemDosCampos()
    {
        var ator = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var vaga = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var primeiro = CanonicalizadorIdempotencia.CalcularHashEntradaV1(
            ator, vaga, "ABC1D23", "Modelo", "Titular", "123", "Vistoria");

        var segundo = CanonicalizadorIdempotencia.CalcularHashEntradaV1(
            ator, vaga, "ABC1D23", "Modelo", "Titular", "123", "Vistoria");

        CollectionAssert.AreEqual(primeiro, segundo);
        Assert.AreEqual(32, primeiro.Length);

        var divergente = CanonicalizadorIdempotencia.CalcularHashEntradaV1(
            ator, vaga, "ABC1D23", "Modelo", "Titular", "124", "Vistoria");

        CollectionAssert.AreNotEqual(primeiro, divergente);
    }

    [TestMethod]
    public void CanonicalizacaoDistingueVersoesEConteudoAmbiguo()
    {
        var ator = Guid.NewGuid();
        var campos = new[]
        {
            new KeyValuePair<string, string>("a", "1"),
            new KeyValuePair<string, string>("b", "2")
        };

        var v1 = CanonicalizadorIdempotencia.CalcularHash(1, "op", ator, campos);
        var v2 = CanonicalizadorIdempotencia.CalcularHash(2, "op", ator, campos);
        CollectionAssert.AreNotEqual(v1, v2);

        var ambiguo = new[]
        {
            new KeyValuePair<string, string>("a", "1b:2"),
            new KeyValuePair<string, string>("b", string.Empty)
        };
        var outro = CanonicalizadorIdempotencia.CalcularHash(1, "op", ator, ambiguo);
        CollectionAssert.AreNotEqual(v1, outro);
    }

    [TestMethod]
    public void TokenDeConcorrenciaVaiEVoltaEmBase64()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var token = Estacionamento.Domain.Acesso.TokenConcorrencia.Serializar(bytes);

        Assert.IsTrue(Estacionamento.Domain.Acesso.TokenConcorrencia.TentarDesserializar(token, out var volta));
        CollectionAssert.AreEqual(bytes, volta);
        Assert.IsFalse(Estacionamento.Domain.Acesso.TokenConcorrencia.TentarDesserializar("nao-base64!", out _));
        Assert.IsFalse(Estacionamento.Domain.Acesso.TokenConcorrencia.TentarDesserializar(string.Empty, out _));
    }
}

[TestClass]
public sealed class CriptografiaCamposTestes
{
    private static readonly Guid AtendimentoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static (CriptografiaCamposAesGcm Criptografia, string ChaveV1) Criar(
        string? versaoAtiva = "v1",
        params (string Versao, string Chave)[] chavesExtras)
    {
        var chaveV1 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal) { ["v1"] = chaveV1 };

        foreach (var (versao, chave) in chavesExtras)
        {
            mapa[versao] = chave;
        }

        var options = Options.Create(new ProtecaoDadosOptions
        {
            VersaoAtiva = versaoAtiva ?? "v1",
            Chaves = mapa
        });

        return (new CriptografiaCamposAesGcm(options, new ContextoAplicacao("Testing")), chaveV1);
    }

    [TestMethod]
    public void IdaEVoltaUsaEnvelopeComVersaoKidNonceETag()
    {
        var (criptografia, _) = Criar();
        var texto = "Observação de vistoria com acentuação e 123.";

        var envelope = criptografia.Cifrar(PropositosCriptografia.VistoriaEntrada, AtendimentoId, texto);

        Assert.AreEqual(1, envelope[0]);
        Assert.IsTrue(envelope[1] > 0);
        Assert.AreEqual(texto, criptografia.Decifrar(PropositosCriptografia.VistoriaEntrada, AtendimentoId, envelope));
    }

    [TestMethod]
    public void NonceEhNovoACadaCifragem()
    {
        var (criptografia, _) = Criar();

        var primeiro = criptografia.Cifrar(PropositosCriptografia.Documento, AtendimentoId, "123");
        var segundo = criptografia.Cifrar(PropositosCriptografia.Documento, AtendimentoId, "123");

        var noncePrimeiro = primeiro.AsSpan(2 + primeiro[1], 12).ToArray();
        var nonceSegundo = segundo.AsSpan(2 + segundo[1], 12).ToArray();
        CollectionAssert.AreNotEqual(noncePrimeiro, nonceSegundo);
        CollectionAssert.AreNotEqual(primeiro, segundo);
    }

    [TestMethod]
    public void VetorConhecidoComAadDoServidorEhDecifrado()
    {
        var chave = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var options = Options.Create(new ProtecaoDadosOptions
        {
            VersaoAtiva = "v1",
            Chaves = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["v1"] = Convert.ToBase64String(chave)
            }
        });

        const string texto = "Documento sintético 000.000.000-00";
        var kid = System.Text.Encoding.UTF8.GetBytes("v1");
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plaintext = System.Text.Encoding.UTF8.GetBytes(texto);
        var ciphertext = new byte[plaintext.Length];
        var aad = System.Text.Encoding.UTF8.GetBytes(
            $"v1|kid:v1|ambiente:Testing|app:Estacionamento|proposito:documento|atendimento:{AtendimentoId:N}");

        using (var aes = new System.Security.Cryptography.AesGcm(chave, 16))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        var envelope = new byte[2 + kid.Length + nonce.Length + tag.Length + ciphertext.Length];
        envelope[0] = 1;
        envelope[1] = (byte)kid.Length;
        kid.CopyTo(envelope, 2);
        nonce.CopyTo(envelope, 2 + kid.Length);
        tag.CopyTo(envelope, 2 + kid.Length + nonce.Length);
        ciphertext.CopyTo(envelope, 2 + kid.Length + nonce.Length + tag.Length);

        var criptografia = new CriptografiaCamposAesGcm(options, new ContextoAplicacao("Testing"));
        Assert.AreEqual(texto, criptografia.Decifrar(PropositosCriptografia.Documento, AtendimentoId, envelope));
    }

    [TestMethod]
    public void TrocaDePropositoRegistroOuAdulteracaoFalhaFechado()
    {
        var (criptografia, _) = Criar();
        var envelope = criptografia.Cifrar(PropositosCriptografia.Documento, AtendimentoId, "123456");

        AssertarProtecao(() => criptografia.Decifrar(
            PropositosCriptografia.VistoriaEntrada, AtendimentoId, envelope));
        AssertarProtecao(() => criptografia.Decifrar(
            PropositosCriptografia.Documento, Guid.NewGuid(), envelope));

        var adulterado = envelope.ToArray();
        adulterado[^1] ^= 0xFF;
        AssertarProtecao(() => criptografia.Decifrar(
            PropositosCriptografia.Documento, AtendimentoId, adulterado));

        var tagAdulterada = envelope.ToArray();
        tagAdulterada[2 + envelope[1] + 12] ^= 0xFF;
        AssertarProtecao(() => criptografia.Decifrar(
            PropositosCriptografia.Documento, AtendimentoId, tagAdulterada));

        var versaoDesconhecida = envelope.ToArray();
        versaoDesconhecida[0] = 9;
        AssertarProtecao(() => criptografia.Decifrar(
            PropositosCriptografia.Documento, AtendimentoId, versaoDesconhecida));
    }

    [TestMethod]
    public void RotacaoMantemLeituraDeEnvelopesAnterioresSemRecifrar()
    {
        var chaveV1 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var chaveV2 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var antes = new CriptografiaCamposAesGcm(
            Options.Create(new ProtecaoDadosOptions
            {
                VersaoAtiva = "v1",
                Chaves = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["v1"] = chaveV1
                }
            }),
            new ContextoAplicacao("Testing"));

        var envelope = antes.Cifrar(PropositosCriptografia.Documento, AtendimentoId, "documento-antigo");

        var depois = new CriptografiaCamposAesGcm(
            Options.Create(new ProtecaoDadosOptions
            {
                VersaoAtiva = "v2",
                Chaves = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["v1"] = chaveV1,
                    ["v2"] = chaveV2
                }
            }),
            new ContextoAplicacao("Testing"));

        Assert.AreEqual(
            "documento-antigo",
            depois.Decifrar(PropositosCriptografia.Documento, AtendimentoId, envelope));

        var novo = depois.Cifrar(PropositosCriptografia.Documento, AtendimentoId, "documento-novo");
        Assert.AreEqual("v2", System.Text.Encoding.UTF8.GetString(novo, 2, novo[1]));
    }

    [TestMethod]
    public void ChaveAusenteFalhaFechadoSemFallback()
    {
        var (criptografia, _) = Criar();
        var envelope = criptografia.Cifrar(PropositosCriptografia.Documento, AtendimentoId, "123");

        var semChave = new CriptografiaCamposAesGcm(
            Options.Create(new ProtecaoDadosOptions
            {
                VersaoAtiva = "v9",
                Chaves = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["v9"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                }
            }),
            new ContextoAplicacao("Testing"));

        AssertarProtecao(() => semChave.Decifrar(
            PropositosCriptografia.Documento, AtendimentoId, envelope));
        AssertarProtecao(() => criptografia.Decifrar(
            PropositosCriptografia.Documento, AtendimentoId, []));
    }

    private static void AssertarProtecao(Action acao)
    {
        var excecao = Assert.ThrowsExactly<ExcecaoAplicacao>(acao);
        Assert.AreEqual("field_protection_failure", excecao.Codigo);
        Assert.AreEqual(TipoFalha.ProtecaoCampo, excecao.Tipo);
    }
}
