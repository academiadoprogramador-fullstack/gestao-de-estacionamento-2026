using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Estacionamento.Application.Autenticacao;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Tempo;
using Estacionamento.Api.Autenticacao;
using Estacionamento.Infrastructure.Servicos;
using Microsoft.IdentityModel.Tokens;

namespace Estacionamento.UnitTests;

/// <summary>
/// Relógio determinístico para expiração e auditoria.
/// </summary>
internal sealed class RelogioFixo : IRelogio
{
    public RelogioFixo(DateTimeOffset agora)
    {
        UtcNow = agora;
    }

    public DateTimeOffset UtcNow { get; set; }
}

/// <summary>
/// Construtores de material de teste. As chaves são aleatórias por execução e
/// não são reutilizadas fora dos testes.
/// </summary>
internal static class MaterialTeste
{
    public static string ChaveBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static AutenticacaoJwtOptions JwtValido(
        string? versaoAtiva = "v2",
        Dictionary<string, string>? chaves = null)
    {
        return new AutenticacaoJwtOptions
        {
            Emissor = "emissor-teste",
            Audiencia = "audiencia-teste",
            VersaoAtiva = versaoAtiva ?? "v2",
            DuracaoAcessoMinutos = 15,
            DuracaoSessaoHoras = 8,
            ToleranciaRelogioSegundos = 30,
            RotacaoChaveDias = 90,
            Chaves = chaves ?? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["v1"] = ChaveBase64(),
                ["v2"] = ChaveBase64()
            }
        };
    }
}

[TestClass]
public sealed class AutenticacaoJwtOptionsValidatorTestes
{
    private readonly AutenticacaoJwtOptionsValidator _validador = new();

    [TestMethod]
    public void ConfiguracaoValidaComVersaoAtivaExistente()
    {
        var resultado = _validador.Validate(null, MaterialTeste.JwtValido());

        Assert.IsTrue(resultado.Succeeded);
    }

    [TestMethod]
    public void RejeitaChaveComBase64Invalido()
    {
        var opcoes = MaterialTeste.JwtValido(chaves: new Dictionary<string, string>
        {
            ["v1"] = "isso-nao-e-base64"
        });

        var resultado = _validador.Validate(null, opcoes);

        Assert.IsFalse(resultado.Succeeded);
        StringAssert.Contains(resultado.FailureMessage, "32 bytes");
    }

    [TestMethod]
    public void RejeitaChaveComTamanhoDiferenteDe32Bytes()
    {
        var opcoes = MaterialTeste.JwtValido(chaves: new Dictionary<string, string>
        {
            ["v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
        });

        var resultado = _validador.Validate(null, opcoes);

        Assert.IsFalse(resultado.Succeeded);
        StringAssert.Contains(resultado.FailureMessage, "32 bytes");
    }

    [TestMethod]
    public void RejeitaVersaoAtivaAusente()
    {
        var opcoes = MaterialTeste.JwtValido(
            versaoAtiva: "v9",
            chaves: new Dictionary<string, string> { ["v1"] = MaterialTeste.ChaveBase64() });

        var resultado = _validador.Validate(null, opcoes);

        Assert.IsFalse(resultado.Succeeded);
        StringAssert.Contains(resultado.FailureMessage, "VersaoAtiva");
    }

    [TestMethod]
    public void RejeitaMapaVazio()
    {
        var opcoes = MaterialTeste.JwtValido(
            chaves: new Dictionary<string, string>(StringComparer.Ordinal));

        var resultado = _validador.Validate(null, opcoes);

        Assert.IsFalse(resultado.Succeeded);
    }

    [TestMethod]
    public void MensagemDeErroNaoExpoeOValorDaChave()
    {
        var chaveInvalida = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var opcoes = MaterialTeste.JwtValido(chaves: new Dictionary<string, string>
        {
            ["v1"] = chaveInvalida
        });

        var resultado = _validador.Validate(null, opcoes);

        Assert.IsFalse(resultado.Succeeded);
        Assert.IsFalse(resultado.FailureMessage!.Contains(chaveInvalida, StringComparison.Ordinal));
    }
}

[TestClass]
public sealed class ProtecaoDadosOptionsValidatorTestes
{
    [TestMethod]
    public void ConfiguracaoValidaComMapaIndependente()
    {
        var jwt = MaterialTeste.JwtValido();
        var validador = new ProtecaoDadosOptionsValidator(Microsoft.Extensions.Options.Options.Create(jwt));
        var protecao = new ProtecaoDadosOptions
        {
            VersaoAtiva = "aes-v1",
            Chaves = new Dictionary<string, string> { ["aes-v1"] = MaterialTeste.ChaveBase64() }
        };

        Assert.IsTrue(validador.Validate(null, protecao).Succeeded);
    }

    [TestMethod]
    public void RejeitaReutilizacaoDeMaterialDoMapaJwt()
    {
        var chaveCompartilhada = MaterialTeste.ChaveBase64();
        var jwt = MaterialTeste.JwtValido(chaves: new Dictionary<string, string>
        {
            ["v1"] = chaveCompartilhada
        });
        var validador = new ProtecaoDadosOptionsValidator(Microsoft.Extensions.Options.Options.Create(jwt));
        var protecao = new ProtecaoDadosOptions
        {
            VersaoAtiva = "aes-v1",
            Chaves = new Dictionary<string, string> { ["aes-v1"] = chaveCompartilhada }
        };

        var resultado = validador.Validate(null, protecao);

        Assert.IsFalse(resultado.Succeeded);
        StringAssert.Contains(resultado.FailureMessage, "reutiliza material");
    }

    [TestMethod]
    public void RejeitaVersaoAtivaAusenteNoMapaAes()
    {
        var validador = new ProtecaoDadosOptionsValidator(
            Microsoft.Extensions.Options.Options.Create(MaterialTeste.JwtValido()));
        var protecao = new ProtecaoDadosOptions
        {
            VersaoAtiva = "aes-v2",
            Chaves = new Dictionary<string, string> { ["aes-v1"] = MaterialTeste.ChaveBase64() }
        };

        Assert.IsFalse(validador.Validate(null, protecao).Succeeded);
    }
}

[TestClass]
public sealed class CorsOptionsValidatorTestes
{
    private readonly CorsOptionsValidator _validador = new();

    [TestMethod]
    public void AceitaOrigensHttpsExatas()
    {
        var opcoes = new CorsOptions
        {
            OrigensPermitidas = ["https://localhost:4200", "https://localhost:4300"]
        };

        Assert.IsTrue(_validador.Validate(null, opcoes).Succeeded);
    }

    [TestMethod]
    public void RejeitaCuringa()
    {
        var opcoes = new CorsOptions { OrigensPermitidas = ["https://*.example.test"] };

        Assert.IsFalse(_validador.Validate(null, opcoes).Succeeded);
    }

    [TestMethod]
    public void RejeitaHttpSemTls()
    {
        var opcoes = new CorsOptions { OrigensPermitidas = ["http://localhost:4200"] };

        Assert.IsFalse(_validador.Validate(null, opcoes).Succeeded);
    }

    [TestMethod]
    public void RejeitaListaVazia()
    {
        Assert.IsFalse(_validador.Validate(null, new CorsOptions()).Succeeded);
    }
}

[TestClass]
public sealed class ContasSinteticasValidatorTestes
{
    private readonly ValidadorContasSinteticas _validador = new();

    private static ContasSinteticasOptions ContasValidas() => new()
    {
        Administrador = new ContaSinteticaOptions
        {
            Email = "admin.sintetico@example.test",
            Senha = "SenhaAdmin1"
        },
        Caixa = new ContaSinteticaOptions
        {
            Email = "caixa.sintetico@example.test",
            Senha = "SenhaCaixa1"
        },
        Manobrista = new ContaSinteticaOptions
        {
            Email = "manobrista.sintetico@example.test",
            Senha = "SenhaManob1"
        }
    };

    [TestMethod]
    public void AceitaTresContasCompletasEUnicas()
    {
        var resultado = _validador.Validar(ContasValidas());

        Assert.IsTrue(resultado.Valido);
        Assert.IsEmpty(resultado.Erros);
    }

    [TestMethod]
    public void RejeitaContaAusente()
    {
        var contas = ContasValidas();
        contas.Manobrista = null;

        var resultado = _validador.Validar(contas);

        Assert.IsFalse(resultado.Valido);
        Assert.IsTrue(resultado.Erros.Any(erro => erro.Contains("Manobrista", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RejeitaEmailInvalido()
    {
        var contas = ContasValidas();
        contas.Caixa!.Email = "caixa-invalido";

        Assert.IsFalse(_validador.Validar(contas).Valido);
    }

    [TestMethod]
    public void RejeitaEmailsDuplicadosAposNormalizacao()
    {
        var contas = ContasValidas();
        contas.Caixa!.Email = "ADMIN.SINTETICO@EXAMPLE.TEST";

        var resultado = _validador.Validar(contas);

        Assert.IsFalse(resultado.Valido);
        Assert.IsTrue(resultado.Erros.Any(erro => erro.Contains("repete o e-mail", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RejeitaSenhaMenorQueOitoCaracteres()
    {
        var contas = ContasValidas();
        contas.Administrador!.Senha = "curta";

        Assert.IsFalse(_validador.Validar(contas).Valido);
    }

    [TestMethod]
    public void ErrosNaoExpoemEmailsOuSenhas()
    {
        var contas = ContasValidas();
        contas.Caixa!.Email = "ADMIN.SINTETICO@EXAMPLE.TEST";

        var resultado = _validador.Validar(contas);

        Assert.IsFalse(resultado.Valido);
        Assert.IsFalse(resultado.Erros.Any(erro => erro.Contains("@", StringComparison.Ordinal)));
        Assert.IsFalse(resultado.Erros.Any(erro => erro.Contains("SenhaCaixa1", StringComparison.Ordinal)));
    }
}

[TestClass]
public sealed class PoliticaSessaoTestes
{
    [TestMethod]
    public void FimDaSessaoEhOitoHorasSemExtensaoDeslizante()
    {
        var inicio = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

        var fim = PoliticaSessao.CalcularFimSessao(inicio, TimeSpan.FromHours(8));

        Assert.AreEqual(inicio.AddHours(8), fim);
    }

    [TestMethod]
    public void SessaoRevogadaNaoEhRenovavel()
    {
        var agora = DateTimeOffset.UtcNow;
        Assert.IsFalse(PoliticaSessao.SessaoRenovavel(agora, agora.AddHours(1), revogada: true));
    }

    [TestMethod]
    public void SessaoNoLimiteAbsolutoNaoEhRenovavel()
    {
        var agora = DateTimeOffset.UtcNow;
        Assert.IsFalse(PoliticaSessao.SessaoRenovavel(agora, agora, revogada: false));
    }

    [TestMethod]
    public void AtorOpacoNaoContemOIdentificadorOriginal()
    {
        var ator = PoliticaSessao.GerarAtorOpaco("pessoa@example.test");

        Assert.AreEqual(64, ator.Length);
        Assert.IsFalse(ator.Contains("pessoa", StringComparison.Ordinal));
        Assert.AreEqual(ator, PoliticaSessao.GerarAtorOpaco("pessoa@example.test"));
    }

    [TestMethod]
    public void NormalizacaoRemoveEspacosEConverteCaixa()
    {
        Assert.AreEqual("pessoa@example.test", NormalizadorEmail.Normalizar("  PESSOA@Example.Test "));
        Assert.AreEqual(string.Empty, NormalizadorEmail.Normalizar(null));
    }
}

[TestClass]
public sealed class GeradorRefreshTestes
{
    [TestMethod]
    public void GeraTokensOpacosDistintosCom48CaracteresBase64Url()
    {
        var gerador = new GeradorRefresh();

        var primeiro = gerador.GerarTokenOpaco();
        var segundo = gerador.GerarTokenOpaco();

        Assert.AreNotEqual(primeiro, segundo);
        Assert.IsTrue(primeiro.Length >= 43);
        Assert.IsFalse(primeiro.Contains('+', StringComparison.Ordinal));
        Assert.IsFalse(primeiro.Contains('/', StringComparison.Ordinal));
    }

    [TestMethod]
    public void HashEhSha256Deterministico()
    {
        var gerador = new GeradorRefresh();
        var token = gerador.GerarTokenOpaco();

        Assert.AreEqual(gerador.CalcularHash(token), gerador.CalcularHash(token));
        Assert.AreEqual(64, gerador.CalcularHash(token).Length);
    }
}

[TestClass]
public sealed class GeradorTokenJwtTestes
{
    private static TokenValidationParameters Parametros(AutenticacaoJwtOptions opcoes) =>
        ConfiguracaoJwt.CriarParametros(opcoes);

    [TestMethod]
    public void EmiteTokenValidoComClaimsMinimosEPerfis()
    {
        var opcoes = MaterialTeste.JwtValido();
        var relogio = new RelogioFixo(DateTimeOffset.UtcNow);
        var gerador = new GeradorTokenJwt(
            Microsoft.Extensions.Options.Options.Create(opcoes), relogio);

        var usuarioId = Guid.NewGuid();
        var sessaoId = Guid.NewGuid();
        var token = gerador.Emitir(usuarioId, sessaoId, 7, ["Administrador", "Caixa"]);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token.Valor, Parametros(opcoes), out var validado);

        Assert.AreEqual(usuarioId.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.AreEqual(sessaoId.ToString(), principal.FindFirstValue(GeradorTokenJwt.ClaimSessao));
        Assert.AreEqual("7", principal.FindFirstValue(GeradorTokenJwt.ClaimVersaoAcesso));
        Assert.IsTrue(principal.IsInRole("Administrador"));
        Assert.IsTrue(principal.IsInRole("Caixa"));
        Assert.IsFalse(principal.HasClaim(claim => claim.Type.Contains("email", StringComparison.OrdinalIgnoreCase)));

        var cabecalho = ((JwtSecurityToken)validado).Header;
        Assert.AreEqual("v2", cabecalho["kid"]);
        Assert.AreEqual(SecurityAlgorithms.HmacSha256, cabecalho.Alg);
    }

    [TestMethod]
    public void RejeitaTokenSemAssinatura()
    {
        var opcoes = MaterialTeste.JwtValido();
        var handler = new JwtSecurityTokenHandler();
        var cabecalho = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var carga = Base64Url(
            $"{{\"sub\":\"{Guid.NewGuid()}\",\"iss\":\"{opcoes.Emissor}\",\"aud\":\"{opcoes.Audiencia}\"}}");

        Assert.ThrowsExactly<SecurityTokenInvalidSignatureException>(() =>
            handler.ValidateToken($"{cabecalho}.{carga}.", Parametros(opcoes), out _));
    }

    [TestMethod]
    public void RejeitaAlgoritmoAlternativo()
    {
        var opcoes = MaterialTeste.JwtValido();
        // Chave de 64 bytes apenas para produzir assinatura HS512 válida; o
        // validador deve recusar o algoritmo antes de aceitar o token.
        var chave = RandomNumberGenerator.GetBytes(64);
        var cabecalho = new JwtHeader(
            new SigningCredentials(new SymmetricSecurityKey(chave), SecurityAlgorithms.HmacSha512));
        cabecalho["kid"] = "v1";
        var carga = new JwtPayload
        {
            [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iss] = opcoes.Emissor,
            [JwtRegisteredClaimNames.Aud] = opcoes.Audiencia,
            [JwtRegisteredClaimNames.Exp] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        };

        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(cabecalho, carga));

        Assert.ThrowsExactly<SecurityTokenSignatureKeyNotFoundException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, Parametros(opcoes), out _));

        var parametros = Parametros(opcoes);
        CollectionAssert.AreEqual(
            new[] { SecurityAlgorithms.HmacSha256 },
            parametros.ValidAlgorithms!.ToArray());
        Assert.IsTrue(parametros.RequireSignedTokens);
    }

    [TestMethod]
    public void RejeitaKidDesconhecido()
    {
        var opcoes = MaterialTeste.JwtValido();
        var chave = ValidadorChaveSimetrica.Decodificar(opcoes.Chaves["v1"]);
        var cabecalho = new JwtHeader(
            new SigningCredentials(new SymmetricSecurityKey(chave), SecurityAlgorithms.HmacSha256));
        cabecalho["kid"] = "v9";
        var carga = new JwtPayload
        {
            [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iss] = opcoes.Emissor,
            [JwtRegisteredClaimNames.Aud] = opcoes.Audiencia,
            [JwtRegisteredClaimNames.Exp] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        };

        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(cabecalho, carga));

        Assert.ThrowsExactly<SecurityTokenSignatureKeyNotFoundException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, Parametros(opcoes), out _));
    }

    [TestMethod]
    public void RejeitaEmissorOuAudienciaIncorretos()
    {
        var opcoes = MaterialTeste.JwtValido();
        var opcoesErradas = MaterialTeste.JwtValido();
        opcoesErradas.Emissor = "outro-emissor";
        opcoesErradas.Audiencia = "outra-audiencia";
        opcoesErradas.Chaves = new Dictionary<string, string>(opcoes.Chaves, StringComparer.Ordinal);

        var gerador = new GeradorTokenJwt(
            Microsoft.Extensions.Options.Options.Create(opcoesErradas), new RelogioFixo(DateTimeOffset.UtcNow));
        var token = gerador.Emitir(Guid.NewGuid(), Guid.NewGuid(), 1, ["Caixa"]);

        Assert.ThrowsExactly<SecurityTokenInvalidAudienceException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token.Valor, Parametros(opcoes), out _));
    }

    [TestMethod]
    public void TokenRespeitaLimiteAbsolutoDaSessao()
    {
        var opcoes = MaterialTeste.JwtValido();
        var agora = DateTimeOffset.UtcNow;
        var gerador = new GeradorTokenJwt(
            Microsoft.Extensions.Options.Options.Create(opcoes), new RelogioFixo(agora));
        var limite = agora.AddMinutes(5);

        var token = gerador.Emitir(Guid.NewGuid(), Guid.NewGuid(), 1, ["Caixa"], limite);

        Assert.AreEqual(limite, token.ExpiraEmUtc);
    }

    [TestMethod]
    public void RejeitaTokenExpirado()
    {
        var opcoes = MaterialTeste.JwtValido();
        var relogio = new RelogioFixo(DateTimeOffset.UtcNow.AddMinutes(-20));
        var gerador = new GeradorTokenJwt(
            Microsoft.Extensions.Options.Options.Create(opcoes), relogio);
        var token = gerador.Emitir(Guid.NewGuid(), Guid.NewGuid(), 1, ["Caixa"]);

        Assert.ThrowsExactly<SecurityTokenExpiredException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token.Valor, Parametros(opcoes), out _));
    }

    private static string Base64Url(string texto) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texto))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
