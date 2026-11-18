using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class AdministracaoAcessosTestes
{
    [TestMethod]
    public async Task CriacaoListagemEAlteracaoRevogamSessaoNaProximaRequisicao()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);

        const string email = "caixa.novo@example.test";
        const string senha = "SenhaTeste1";

        var criacao = await administrador.PostAsJsonAsync("/api/v1/employees", new
        {
            nome = "Caixa Novo",
            email,
            senhaInicial = senha,
            perfis = new[] { Perfis.Caixa }
        });

        Assert.AreEqual(HttpStatusCode.Created, criacao.StatusCode, await criacao.Content.ReadAsStringAsync());
        var corpoCriacao = await criacao.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoCriacao.Contains("senha", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(corpoCriacao.Contains("hash", StringComparison.OrdinalIgnoreCase));

        using var jsonCriacao = JsonDocument.Parse(corpoCriacao);
        var id = jsonCriacao.RootElement.GetProperty("id").GetGuid();
        Assert.AreEqual(Perfis.Caixa, jsonCriacao.RootElement.GetProperty("perfis")[0].GetString());

        var (funcionario, _) = await Fase2Apoio.EntrarAsync(fabrica, email, senha);
        Assert.AreEqual(
            HttpStatusCode.OK,
            (await funcionario.GetAsync("/api/v1/area-trabalho")).StatusCode);

        var listagem = await administrador.GetAsync("/api/v1/employees?page=1&pageSize=100");
        Assert.AreEqual(HttpStatusCode.OK, listagem.StatusCode);
        var corpoListagem = await listagem.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoListagem.Contains("senha", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(corpoListagem.Contains("hash", StringComparison.OrdinalIgnoreCase));
        using var jsonListagem = JsonDocument.Parse(corpoListagem);
        Assert.IsTrue(jsonListagem.RootElement.GetProperty("total").GetInt32() >= 4);

        var token = await Fase2Apoio.ObterTokenFuncionarioAsync(administrador, email);

        var alteracao = await administrador.PutAsJsonAsync($"/api/v1/employees/{id}/access", new
        {
            ativo = true,
            perfis = new[] { Perfis.Manobrista },
            tokenConcorrencia = token
        });

        Assert.AreEqual(HttpStatusCode.OK, alteracao.StatusCode, await alteracao.Content.ReadAsStringAsync());

        var aposAlteracao = await funcionario.GetAsync("/api/v1/area-trabalho");
        Assert.AreEqual(HttpStatusCode.Unauthorized, aposAlteracao.StatusCode);

        var novoToken = await Fase2Apoio.ObterTokenFuncionarioAsync(administrador, email);
        Assert.AreNotEqual(token, novoToken, "O token de concorrência deve mudar após a alteração de acesso.");

        using var clienteComTokenComoCredencial = fabrica.CriarCliente();
        clienteComTokenComoCredencial.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", novoToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            (await clienteComTokenComoCredencial.GetAsync("/api/v1/area-trabalho")).StatusCode);

        // Reutilizar explicitamente o bearer antigo também é recusado antes de
        // qualquer resultado de negócio.
        using var clienteAntigo = fabrica.CriarCliente();
        clienteAntigo.DefaultRequestHeaders.Authorization = funcionario.DefaultRequestHeaders.Authorization;
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            (await clienteAntigo.GetAsync("/api/v1/area-trabalho")).StatusCode);
    }

    [TestMethod]
    public async Task DuasAlteracoesComMesmoTokenProduzemUmUnicoConflito()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);

        const string email = "concorrente@example.test";
        await administrador.PostAsJsonAsync("/api/v1/employees", new
        {
            nome = "Concorrente",
            email,
            senhaInicial = "SenhaTeste1",
            perfis = new[] { Perfis.Caixa }
        });

        var id = await Fase2Apoio.ObterIdFuncionarioAsync(administrador, email);
        var token = await Fase2Apoio.ObterTokenFuncionarioAsync(administrador, email);

        var corpo = new
        {
            ativo = true,
            perfis = new[] { Perfis.Manobrista },
            tokenConcorrencia = token
        };

        var primeira = administrador.PutAsJsonAsync($"/api/v1/employees/{id}/access", corpo);
        var segunda = administrador.PutAsJsonAsync($"/api/v1/employees/{id}/access", corpo);

        var respostas = await Task.WhenAll(primeira, segunda);
        var status = respostas.Select(resposta => resposta.StatusCode).OrderBy(valor => valor).ToArray();

        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(
            1,
            respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));
        Assert.AreEqual(2, status.Length);

        var conflito = respostas.Single(resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.AreEqual("stale_access_version", await Fase2Apoio.CodigoDoErroAsync(conflito));
    }

    [TestMethod]
    public async Task UltimoAdministradorAtivoNaoPodeSerRemovido()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);

        var id = await Fase2Apoio.ObterIdFuncionarioAsync(
            administrador,
            Fase2Apoio.EmailAdministrador);
        var token = await Fase2Apoio.ObterTokenFuncionarioAsync(
            administrador,
            Fase2Apoio.EmailAdministrador);

        var resposta = await administrador.PutAsJsonAsync($"/api/v1/employees/{id}/access", new
        {
            ativo = false,
            perfis = new[] { Perfis.Caixa },
            tokenConcorrencia = token
        });

        Assert.AreEqual(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.AreEqual("last_active_administrator", await Fase2Apoio.CodigoDoErroAsync(resposta));
    }

    [TestMethod]
    public async Task ConcorrenciaEntreAdministradoresPreservaAoMenosUmAtivo()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();

        var (administradorA, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);

        const string emailB = "admin.b@example.test";
        var criacao = await administradorA.PostAsJsonAsync("/api/v1/employees", new
        {
            nome = "Administrador B",
            email = emailB,
            senhaInicial = "SenhaTeste1",
            perfis = new[] { Perfis.Administrador }
        });
        Assert.AreEqual(HttpStatusCode.Created, criacao.StatusCode);

        var (administradorB, _) = await Fase2Apoio.EntrarAsync(fabrica, emailB, "SenhaTeste1");

        var idA = await Fase2Apoio.ObterIdFuncionarioAsync(administradorA, Fase2Apoio.EmailAdministrador);
        var idB = await Fase2Apoio.ObterIdFuncionarioAsync(administradorA, emailB);
        var tokenA = await Fase2Apoio.ObterTokenFuncionarioAsync(administradorA, Fase2Apoio.EmailAdministrador);
        var tokenB = await Fase2Apoio.ObterTokenFuncionarioAsync(administradorA, emailB);

        var removerAdminDeB = administradorA.PutAsJsonAsync($"/api/v1/employees/{idB}/access", new
        {
            ativo = true,
            perfis = new[] { Perfis.Caixa },
            tokenConcorrencia = tokenB
        });

        var removerAdminDeA = administradorB.PutAsJsonAsync($"/api/v1/employees/{idA}/access", new
        {
            ativo = true,
            perfis = new[] { Perfis.Caixa },
            tokenConcorrencia = tokenA
        });

        var respostas = await Task.WhenAll(removerAdminDeB, removerAdminDeA);

        Assert.AreEqual(
            1,
            respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.OK),
            "Apenas uma remoção concorrente deve ser confirmada.");
        Assert.AreEqual(
            1,
            respostas.Count(resposta => resposta.StatusCode != HttpStatusCode.OK),
            "A operação concorrente deve ser recusada (conflito ou sessão revogada).");

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        var administradoresAtivos = await (
                from usuario in db.Users.AsNoTracking()
                join vinculo in db.UserRoles.AsNoTracking() on usuario.Id equals vinculo.UserId
                join perfil in db.Roles.AsNoTracking() on vinculo.RoleId equals perfil.Id
                where usuario.Ativo && perfil.Name == Perfis.Administrador
                select usuario.Id)
            .CountAsync();

        Assert.IsTrue(administradoresAtivos >= 1);
    }

    [TestMethod]
    public async Task CriacaoInvalidaDuplicadaENaoAdministrativaSaoRecusadas()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var semPerfis = await administrador.PostAsJsonAsync("/api/v1/employees", new
        {
            nome = "Sem Perfil",
            email = "sem.perfil@example.test",
            senhaInicial = "SenhaTeste1",
            perfis = Array.Empty<string>()
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, semPerfis.StatusCode);
        Assert.AreEqual("perfis_obrigatorios", await Fase2Apoio.CodigoDoErroAsync(semPerfis));

        var senhaCurta = await administrador.PostAsJsonAsync("/api/v1/employees", new
        {
            nome = "Senha Curta",
            email = "senha.curta@example.test",
            senhaInicial = "1234567",
            perfis = new[] { Perfis.Caixa }
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, senhaCurta.StatusCode);
        Assert.AreEqual("senha_invalida", await Fase2Apoio.CodigoDoErroAsync(senhaCurta));

        var duplicada = await administrador.PostAsJsonAsync("/api/v1/employees", new
        {
            nome = "Duplicada",
            email = Fase2Apoio.EmailCaixa.ToUpperInvariant(),
            senhaInicial = "SenhaTeste1",
            perfis = new[] { Perfis.Caixa }
        });
        Assert.AreEqual(HttpStatusCode.Conflict, duplicada.StatusCode);
        Assert.AreEqual("email_em_uso", await Fase2Apoio.CodigoDoErroAsync(duplicada));

        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            (await caixa.GetAsync("/api/v1/employees")).StatusCode);

        var criacaoPeloCaixa = await caixa.PostAsJsonAsync("/api/v1/employees", new
        {
            nome = "Proibido",
            email = "proibido@example.test",
            senhaInicial = "SenhaTeste1",
            perfis = new[] { Perfis.Caixa }
        });
        Assert.AreEqual(HttpStatusCode.Forbidden, criacaoPeloCaixa.StatusCode);
    }
}
