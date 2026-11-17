using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class AutenticacaoSessaoApiTestes
{
    private static FabricaApiTeste _fabrica = null!;
    private static HttpClient _cliente = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _fabrica = new FabricaApiTeste(configuracaoExtra: new Dictionary<string, string?>
        {
            // A política de 5/min é exercitada em teste dedicado; aqui o limite
            // alto evita que a matriz de autenticação colida com ela.
            ["Autenticacao:LimiteTentativasLoginPorMinuto"] = "1000"
        });
        _cliente = _fabrica.CriarCliente();
        await _fabrica.ProvisionarContasAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _cliente.Dispose();
        _fabrica.Dispose();
    }

    [TestCleanup]
    public void LimparAutenticacao()
    {
        ClienteSessao.UsarBearer(_cliente, null);
    }

    [TestMethod]
    public async Task LoginValidoEmiteTokenERefreshSemDadosSensiveis()
    {
        var resposta = await _cliente.PostAsJsonAsync(
            "/api/v1/session/login",
            new
            {
                email = ContasTeste.Contas().Administrador!.Email,
                senha = ContasTeste.Contas().Administrador!.Senha
            });

        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode);
        Assert.AreEqual("no-store", resposta.Headers.CacheControl?.ToString().Split(',').First().Trim());
        Assert.IsTrue(resposta.Headers.Contains("X-Correlation-Id"));

        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.IsFalse(corpo.Contains("senha", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(corpo.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(corpo.Contains("hash", StringComparison.OrdinalIgnoreCase));

        var sessao = await RespostaSessao.LerAsync(resposta);
        Assert.IsNotNull(sessao.TokenAcesso);
        Assert.IsNotNull(sessao.Refresh);
        CollectionAssert.Contains(sessao.Perfis.ToArray(), Perfis.Administrador);
    }

    [TestMethod]
    public async Task CredenciaisInvalidasSaoGenericasParaContaExistenteEInexistente()
    {
        var existente = await ClienteSessao.LoginAsync(
            _cliente,
            ContasTeste.Contas().Caixa!.Email,
            "SenhaErrada1");
        var inexistente = await ClienteSessao.LoginAsync(
            _cliente,
            "ninguem@example.test",
            "SenhaErrada1");

        Assert.AreEqual(HttpStatusCode.Unauthorized, (HttpStatusCode)existente.Status);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (HttpStatusCode)inexistente.Status);
        Assert.AreEqual(existente.Codigo, inexistente.Codigo);
        Assert.IsNull(existente.TokenAcesso);
        StringAssert.Contains(existente.Corpo!, "credenciais_invalidas");
    }

    [TestMethod]
    public async Task RequisicaoSemTokenRetornaProblemDetailsNaoCacheavel()
    {
        var resposta = await _cliente.GetAsync("/api/v1/session");
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.AreEqual("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("no-store", resposta.Headers.CacheControl?.ToString().Split(',').First().Trim());

        using var json = JsonDocument.Parse(corpo);
        var raiz = json.RootElement;
        Assert.AreEqual(401, raiz.GetProperty("status").GetInt32());
        Assert.IsTrue(raiz.TryGetProperty("title", out _));
        Assert.IsTrue(raiz.TryGetProperty("type", out _));
        Assert.IsTrue(raiz.TryGetProperty("instance", out _));
        Assert.AreEqual("nao_autenticado", raiz.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(raiz.GetProperty("correlationId").GetString()));
        Assert.IsFalse(corpo.Contains("stackTrace", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task RefreshRotativoInvalidaTokenAnteriorEReutilizacaoRevogaFamilia()
    {
        var login = await ClienteSessao.LoginAdministradorAsync(_cliente);
        Assert.AreEqual(HttpStatusCode.OK, (HttpStatusCode)login.Status);

        var renovacao = await _cliente.PostAsJsonAsync(
            "/api/v1/session/refresh",
            new { refresh = login.Refresh });
        var novaSessao = await RespostaSessao.LerAsync(renovacao);

        Assert.AreEqual(HttpStatusCode.OK, renovacao.StatusCode);
        Assert.AreNotEqual(login.Refresh, novaSessao.Refresh);

        var reuso = await _cliente.PostAsJsonAsync(
            "/api/v1/session/refresh",
            new { refresh = login.Refresh });

        Assert.AreEqual(HttpStatusCode.Unauthorized, reuso.StatusCode);

        ClienteSessao.UsarBearer(_cliente, novaSessao.TokenAcesso);
        var consulta = await _cliente.GetAsync("/api/v1/session");

        Assert.AreEqual(HttpStatusCode.Unauthorized, consulta.StatusCode);
    }

    [TestMethod]
    public async Task LogoutRevogaSessaoAntesDeResponder()
    {
        var login = await ClienteSessao.LoginCaixaAsync(_cliente);
        ClienteSessao.UsarBearer(_cliente, login.TokenAcesso);

        var logout = await _cliente.PostAsync("/api/v1/session/logout", null);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);

        var consulta = await _cliente.GetAsync("/api/v1/session");
        Assert.AreEqual(HttpStatusCode.Unauthorized, consulta.StatusCode);

        var refresh = await _cliente.PostAsJsonAsync(
            "/api/v1/session/refresh",
            new { refresh = login.Refresh });
        Assert.AreEqual(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [TestMethod]
    public async Task ContaDesativadaInvalidaProximaRequisicao()
    {
        var email = await CriarUsuarioAsync("desativado", Perfis.Caixa);
        var login = await ClienteSessao.LoginAsync(_cliente, email, ContasTeste.Senha);
        Assert.AreEqual(HttpStatusCode.OK, (HttpStatusCode)login.Status);

        await ExecutarNoBancoAsync(async db =>
        {
            var usuario = await db.Users.SingleAsync(u => u.Email == email);
            usuario.Ativo = false;
            await db.SaveChangesAsync();
        });

        ClienteSessao.UsarBearer(_cliente, login.TokenAcesso);
        var consulta = await _cliente.GetAsync("/api/v1/area-trabalho");

        Assert.AreEqual(HttpStatusCode.Unauthorized, consulta.StatusCode);
    }

    [TestMethod]
    public async Task AlteracaoDeVersaoDeAcessoInvalidaSessaoExistente()
    {
        var email = await CriarUsuarioAsync("versao", Perfis.Caixa);
        var login = await ClienteSessao.LoginAsync(_cliente, email, ContasTeste.Senha);

        await ExecutarNoBancoAsync(async db =>
        {
            var usuario = await db.Users.SingleAsync(u => u.Email == email);
            usuario.VersaoAcesso++;
            await db.SaveChangesAsync();
        });

        ClienteSessao.UsarBearer(_cliente, login.TokenAcesso);
        var consulta = await _cliente.GetAsync("/api/v1/area-trabalho");

        Assert.AreEqual(HttpStatusCode.Unauthorized, consulta.StatusCode);
    }

    [TestMethod]
    public async Task PerfilSemPermissaoRecebe403SemLogout()
    {
        var login = await ClienteSessao.LoginCaixaAsync(_cliente);
        ClienteSessao.UsarBearer(_cliente, login.TokenAcesso);

        var negado = await _cliente.GetAsync("/api/v1/administracao/verificacao");
        Assert.AreEqual(HttpStatusCode.Forbidden, negado.StatusCode);
        Assert.AreEqual("acesso_negado", await CodigoDoProblemaAsync(negado));

        var sessao = await _cliente.GetAsync("/api/v1/session");
        Assert.AreEqual(HttpStatusCode.OK, sessao.StatusCode);

        var admin = await ClienteSessao.LoginAdministradorAsync(_cliente);
        ClienteSessao.UsarBearer(_cliente, admin.TokenAcesso);
        var permitido = await _cliente.GetAsync("/api/v1/administracao/verificacao");
        Assert.AreEqual(HttpStatusCode.OK, permitido.StatusCode);
    }

    [TestMethod]
    public async Task CorsAceitaOrigemExataENegaOrigemDesconhecida()
    {
        var permitida = new HttpRequestMessage(HttpMethod.Options, "/api/v1/session/login");
        permitida.Headers.Add("Origin", "https://localhost:4200");
        permitida.Headers.Add("Access-Control-Request-Method", "POST");
        permitida.Headers.Add("Access-Control-Request-Headers", "content-type");

        var respostaPermitida = await _cliente.SendAsync(permitida);
        Assert.AreEqual(HttpStatusCode.NoContent, respostaPermitida.StatusCode);
        Assert.AreEqual(
            "https://localhost:4200",
            respostaPermitida.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.IsFalse(respostaPermitida.Headers.Contains("Access-Control-Allow-Credentials"));

        var negada = new HttpRequestMessage(HttpMethod.Options, "/api/v1/session/login");
        negada.Headers.Add("Origin", "https://evil.test");
        negada.Headers.Add("Access-Control-Request-Method", "POST");

        var respostaNegada = await _cliente.SendAsync(negada);
        Assert.IsFalse(respostaNegada.Headers.Contains("Access-Control-Allow-Origin"));
    }


    [TestMethod]
    public async Task CincoFalhasBloqueiamContaPorQuinzeMinutosComRespostaGenerica()
    {
        var email = await CriarUsuarioAsync("bloqueio", Perfis.Caixa);

        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            var falha = await ClienteSessao.LoginAsync(_cliente, email, "SenhaErrada1");
            Assert.AreEqual(HttpStatusCode.Unauthorized, (HttpStatusCode)falha.Status);
        }

        var bloqueada = await ClienteSessao.LoginAsync(_cliente, email, ContasTeste.Senha);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (HttpStatusCode)bloqueada.Status);
        Assert.AreEqual("credenciais_invalidas", bloqueada.Codigo);

        await ExecutarNoBancoAsync(async db =>
        {
            var usuario = await db.Users.SingleAsync(u => u.Email == email);
            Assert.IsTrue(usuario.LockoutEnd > DateTimeOffset.UtcNow);
            usuario.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        var liberada = await ClienteSessao.LoginAsync(_cliente, email, ContasTeste.Senha);
        Assert.AreEqual(HttpStatusCode.OK, (HttpStatusCode)liberada.Status);
    }

    [TestMethod]
    public async Task RefreshConcorrenteProduzUmaRenovacaoERevogaFamilia()
    {
        var login = await ClienteSessao.LoginAdministradorAsync(_cliente);

        var primeira = _cliente.PostAsJsonAsync(
            "/api/v1/session/refresh", new { refresh = login.Refresh });
        var segunda = _cliente.PostAsJsonAsync(
            "/api/v1/session/refresh", new { refresh = login.Refresh });

        var respostas = await Task.WhenAll(primeira, segunda);
        var sucessos = respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.OK);
        var falhas = respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Unauthorized);

        Assert.AreEqual(1, sucessos);
        Assert.AreEqual(1, falhas);

        var vencedora = respostas.Single(resposta => resposta.StatusCode == HttpStatusCode.OK);
        var nova = await RespostaSessao.LerAsync(vencedora);

        ClienteSessao.UsarBearer(_cliente, nova.TokenAcesso);
        var consulta = await _cliente.GetAsync("/api/v1/session");
        Assert.AreEqual(HttpStatusCode.Unauthorized, consulta.StatusCode);
    }

    [TestMethod]
    public async Task TokenAdulteradoEhRejeitadoSemConsultarChaveDoCliente()
    {
        var login = await ClienteSessao.LoginAdministradorAsync(_cliente);
        var adulterado = login.TokenAcesso![..^2] + "xy";

        ClienteSessao.UsarBearer(_cliente, adulterado);
        var resposta = await _cliente.GetAsync("/api/v1/session");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.AreEqual("nao_autenticado", await CodigoDoProblemaAsync(resposta));
    }

    [TestMethod]
    public async Task RefreshNaoEhAceitoPorUrlOuCookie()
    {
        var login = await ClienteSessao.LoginAdministradorAsync(_cliente);

        var porUrl = await _cliente.GetAsync($"/api/v1/session/refresh?refresh={login.Refresh}");
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, porUrl.StatusCode);

        var cookie = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session/refresh");
        cookie.Headers.Add("Cookie", $"refresh={login.Refresh}");
        var respostaCookie = await _cliente.SendAsync(cookie);
        Assert.IsTrue(
            respostaCookie.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType,
            "Cookie não pode autenticar nem substituir o refresh no corpo JSON.");
        Assert.IsNull(respostaCookie.Headers.Location);
    }

    [TestMethod]
    public async Task ChamadaAutenticadaEntreOrigensRecebeCabecalhoCors()
    {
        var login = await ClienteSessao.LoginCaixaAsync(_cliente);
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/v1/session");
        requisicao.Headers.Add("Origin", "https://localhost:4200");
        ClienteSessao.UsarBearer(_cliente, login.TokenAcesso);

        var resposta = await _cliente.SendAsync(requisicao);

        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode);
        Assert.AreEqual(
            "https://localhost:4200",
            resposta.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.IsFalse(resposta.Headers.Contains("Access-Control-Allow-Credentials"));
    }


    [TestMethod]
    public async Task LimiteAbsolutoDeOitoHorasImpedeNovaRenovacao()
    {
        var login = await ClienteSessao.LoginAdministradorAsync(_cliente);
        Assert.AreEqual(HttpStatusCode.OK, (HttpStatusCode)login.Status);

        await ExecutarNoBancoAsync(async db =>
        {
            var sessao = await db.SessoesAutenticacao
                .Where(s => s.UsuarioId == db.Users
                    .Single(u => u.Email == ContasTeste.Contas().Administrador!.Email).Id)
                .OrderByDescending(s => s.CriadaEmUtc)
                .FirstAsync();
            sessao.ExpiraEmUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        });

        var renovacao = await _cliente.PostAsJsonAsync(
            "/api/v1/session/refresh",
            new { refresh = login.Refresh });

        Assert.AreEqual(HttpStatusCode.Unauthorized, renovacao.StatusCode);
    }

    [TestMethod]
    public async Task HashDeRefreshNoSqlNaoRevelaOTokenPuro()
    {
        var login = await ClienteSessao.LoginAdministradorAsync(_cliente);

        await ExecutarNoBancoAsync(async db =>
        {
            var refresh = await db.RefreshTokens
                .AsNoTracking()
                .OrderByDescending(token => token.CriadoEmUtc)
                .FirstAsync();

            Assert.AreEqual(64, refresh.HashSha256.Length);
            Assert.AreNotEqual(login.Refresh, refresh.HashSha256);
            Assert.IsFalse(refresh.HashSha256.Contains(login.Refresh!, StringComparison.Ordinal));
            Assert.IsTrue(refresh.ExpiraEmUtc <= refresh.CriadoEmUtc.AddHours(8));
            Assert.IsTrue(refresh.RowVersion.Length > 0);
        });
    }

    [TestMethod]
    public async Task OpenApiNaoEhExpostoEmTesting()
    {
        var resposta = await _cliente.GetAsync("/openapi/v1.json");

        Assert.AreEqual(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [TestMethod]
    public async Task AuditoriaRegistraEventosSemSenhaTokenOuPii()
    {
        await ClienteSessao.LoginAsync(
            _cliente,
            ContasTeste.Contas().Caixa!.Email,
            "SenhaErrada1");
        var login = await ClienteSessao.LoginCaixaAsync(_cliente);
        ClienteSessao.UsarBearer(_cliente, login.TokenAcesso);
        await _cliente.PostAsync("/api/v1/session/logout", null);

        await ExecutarNoBancoAsync(async db =>
        {
            var eventos = await db.EventosAuditoria.AsNoTracking().ToListAsync();
            Assert.IsTrue(eventos.Count > 0);
            Assert.IsTrue(eventos.Any(evento => evento.Tipo == TiposEventoAuditoria.LoginFalha));
            Assert.IsTrue(eventos.Any(evento => evento.Tipo == TiposEventoAuditoria.LoginSucesso));
            Assert.IsTrue(eventos.Any(evento => evento.Tipo == TiposEventoAuditoria.Logout));

            foreach (var evento in eventos)
            {
                var texto = string.Join('|', evento.Tipo, evento.AtorOpaco, evento.AlvoOpaco,
                    evento.Correlacao, evento.Detalhe);
                Assert.IsFalse(texto.Contains(ContasTeste.Senha, StringComparison.Ordinal));
                Assert.IsFalse(texto.Contains(login.Refresh!, StringComparison.Ordinal));
                Assert.IsFalse(texto.Contains(login.TokenAcesso!, StringComparison.Ordinal));
                Assert.IsFalse(texto.Contains("@example.test", StringComparison.Ordinal));
            }
        });
    }

    [TestMethod]
    public async Task AuditoriaEhAppendOnly()
    {
        await ClienteSessao.LoginAsync(
            _cliente,
            ContasTeste.Contas().Caixa!.Email,
            ContasTeste.Contas().Caixa!.Senha);

        await ExecutarNoBancoAsync(async db =>
        {
            var evento = await db.EventosAuditoria.AsTracking().FirstAsync();
            evento.Detalhe = "tentativa de alteração";

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
    }

    private static async Task<string> CriarUsuarioAsync(string prefixo, string perfil)
    {
        var email = $"{prefixo}.{Guid.NewGuid():N}@example.test";

        using var escopo = _fabrica.Services.CreateScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<UsuarioAplicacao>>();
        var usuario = new UsuarioAplicacao
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Nome = prefixo,
            Ativo = true,
            VersaoAcesso = 1
        };

        var criacao = await usuarios.CreateAsync(usuario, ContasTeste.Senha);
        Assert.IsTrue(criacao.Succeeded, string.Join(";", criacao.Errors.Select(erro => erro.Code)));
        await usuarios.AddToRoleAsync(usuario, perfil);
        return email;
    }

    private static async Task ExecutarNoBancoAsync(Func<EstacionamentoDbContext, Task> acao)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        await acao(db);
    }

    private static async Task<string?> CodigoDoProblemaAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var codigo) ? codigo.GetString() : null;
    }
}

[TestClass]
public sealed class LimiteLoginApiTestes
{
    [TestMethod]
    public async Task SextaTentativaNoMesmoMinutoRecebe429ComCorrelacao()
    {
        using var fabrica = new FabricaApiTeste();
        using var cliente = fabrica.CriarCliente();

        HttpResponseMessage? ultima = null;
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            ultima = await cliente.PostAsJsonAsync(
                "/api/v1/session/login",
                new { email = $"tentativa{tentativa}@example.test", senha = "SenhaErrada1" });
            Assert.AreEqual(HttpStatusCode.Unauthorized, ultima.StatusCode);
        }

        var excedente = await cliente.PostAsJsonAsync(
            "/api/v1/session/login",
            new { email = "tentativa6@example.test", senha = "SenhaErrada1" });

        Assert.AreEqual(HttpStatusCode.TooManyRequests, excedente.StatusCode);
        Assert.AreEqual("60", excedente.Headers.RetryAfter?.ToString());
        Assert.IsTrue(excedente.Headers.Contains("X-Correlation-Id"));

        using var json = JsonDocument.Parse(await excedente.Content.ReadAsStringAsync());
        Assert.AreEqual("limite_login_excedido", json.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            json.RootElement.GetProperty("correlationId").GetString()));
    }
}

[TestClass]
public sealed class FalhaFechadaSqlApiTestes
{
    [TestMethod]
    public async Task SqlIndisponivelNaVerificacaoDeSessaoNegaRequisicao()
    {
        // Primeira fábrica cria uma sessão válida no SQL disponível.
        using var fabricaValida = new FabricaApiTeste();
        using var clienteValido = fabricaValida.CriarCliente();
        await fabricaValida.ProvisionarContasAsync();
        var login = await ClienteSessao.LoginAdministradorAsync(clienteValido);

        // Segunda fábrica usa as mesmas chaves/chaves de sessão, mas o SQL
        // aponta para uma porta sem serviço: a verificação falha fechada.
        var configuracao = fabricaValida.ConfiguracaoPadrao();
        configuracao["ConnectionStrings:Estacionamento"] =
            "Server=127.0.0.1,1;Database=Inexistente;User Id=ninguem;Password=Invalida1;TrustServerCertificate=True;Connect Timeout=1";

        using var fabricaSemSql = new FabricaApiTeste(
            ambiente: "Testing",
            aplicarMigrationsAntesDoStartup: false,
            configuracaoExtra: configuracao);

        using var clienteSemSql = fabricaSemSql.CriarCliente();
        ClienteSessao.UsarBearer(clienteSemSql, login.TokenAcesso);

        var resposta = await clienteSemSql.GetAsync("/api/v1/area-trabalho");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
