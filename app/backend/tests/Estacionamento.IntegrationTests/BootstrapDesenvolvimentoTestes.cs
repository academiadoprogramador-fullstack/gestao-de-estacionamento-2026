using System.Net;
using Estacionamento.Api.Infra;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class BootstrapDesenvolvimentoTestes
{
    [TestMethod]
    public async Task CatalogoVazioRecebeMigrationsContasEPerfisNaOrdemExigida()
    {
        using var fabrica = new FabricaApiTeste("Development", aplicarMigrationsAntesDoStartup: false);
        using var cliente = fabrica.CriarCliente();

        CollectionAssert.AreEqual(
            new[] { "validacao", "conexao", "migracoes", "provisionamento" },
            fabrica.EtapasBootstrap.ToArray());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var usuarios = await db.Users.AsNoTracking().ToListAsync();
            var perfis = await db.Roles.AsNoTracking().Select(r => r.Name).ToListAsync();

            Assert.AreEqual(3, usuarios.Count);
            Assert.IsTrue(usuarios.All(usuario => usuario.Ativo));
            CollectionAssert.AreEquivalent(Perfis.Todos.ToArray(), perfis.ToArray());
        }

        Assert.IsTrue(SqlTeste.TabelaExiste(fabrica.NomeBanco, "SessoesAutenticacao"));
        Assert.IsTrue(SqlTeste.TabelaExiste(fabrica.NomeBanco, "RefreshTokens"));
        Assert.IsTrue(SqlTeste.TabelaExiste(fabrica.NomeBanco, "EventosAuditoria"));
        Assert.IsFalse(SqlTeste.TabelaExiste(fabrica.NomeBanco, "Vagas"));
        Assert.IsFalse(SqlTeste.TabelaExiste(fabrica.NomeBanco, "Atendimentos"));
        Assert.IsFalse(SqlTeste.TabelaExiste(fabrica.NomeBanco, "Tarifas"));

        Assert.IsEmpty(await SqlTeste.MigrationsPendentesAsync(fabrica.NomeBanco));

        var saude = await cliente.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, saude.StatusCode);
    }

    [TestMethod]
    public async Task ReinicioPreservaContasExistentesSemReativarNemAlterar()
    {
        string nomeBanco;
        string emailAdmin;
        string hashSenha;

        using (var primeira = new FabricaApiTeste(
            "Development",
            aplicarMigrationsAntesDoStartup: false,
            removerBancoAoDispor: false))
        {
            using var cliente = primeira.CriarCliente();
            nomeBanco = primeira.NomeBanco;

            using var escopo = primeira.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var admin = await db.Users.SingleAsync(usuario => usuario.Ativo &&
                usuario.Nome == Perfis.Administrador);
            emailAdmin = admin.Email!;
            hashSenha = admin.PasswordHash!;
            admin.Ativo = false;
            admin.VersaoAcesso++;
            await db.SaveChangesAsync();
        }

        using var segunda = new FabricaApiTeste(
            "Development",
            aplicarMigrationsAntesDoStartup: false,
            nomeBancoExistente: nomeBanco);

        try
        {
            using var cliente = segunda.CriarCliente();

            CollectionAssert.AreEqual(
                new[] { "validacao", "conexao", "migracoes", "provisionamento" },
                segunda.EtapasBootstrap.ToArray());

            using var escopo = segunda.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var usuarios = await db.Users.AsNoTracking().ToListAsync();
            Assert.AreEqual(3, usuarios.Count);

            var admin = usuarios.Single(usuario => usuario.Email == emailAdmin);
            Assert.IsFalse(admin.Ativo, "Conta desabilitada não pode ser reativada pelo bootstrap.");
            Assert.AreEqual(hashSenha, admin.PasswordHash, "Senha existente não pode ser alterada.");
            Assert.AreEqual(2, admin.VersaoAcesso, "Versão de acesso existente não pode ser alterada.");
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }

    [TestMethod]
    public async Task MigrationInicialPendenteEhAplicadaAntesDoProvisionamento()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            var pendentesAntes = await SqlTeste.MigrationsPendentesAsync(nomeBanco);
            Assert.AreEqual(1, pendentesAntes.Count);

            using var fabrica = new FabricaApiTeste(
                "Development",
                aplicarMigrationsAntesDoStartup: false,
                nomeBancoExistente: nomeBanco);
            using var cliente = fabrica.CriarCliente();

            CollectionAssert.AreEqual(
                new[] { "validacao", "conexao", "migracoes", "provisionamento" },
                fabrica.EtapasBootstrap.ToArray());
            Assert.IsEmpty(await SqlTeste.MigrationsPendentesAsync(nomeBanco));
            Assert.AreEqual(3, await ContarUsuariosAsync(fabrica));
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }

    [TestMethod]
    public async Task TestingNaoExecutaMigrationNemSeedAutomatico()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            using var fabrica = new FabricaApiTeste(
                "Testing",
                aplicarMigrationsAntesDoStartup: false,
                nomeBancoExistente: nomeBanco);
            using var cliente = fabrica.CriarCliente();

            Assert.AreEqual(0, SqlTeste.ContarTabelas(nomeBanco));
            Assert.IsEmpty(fabrica.EtapasBootstrap);
            Assert.AreEqual(HttpStatusCode.OK, (await cliente.GetAsync("/health")).StatusCode);
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }

    [TestMethod]
    public async Task ProductionNaoExecutaMigrationNemSeedMesmoComConfiguracaoCompleta()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            using var fabrica = new FabricaApiTeste(
                "Production",
                aplicarMigrationsAntesDoStartup: false,
                configuracaoExtra: new Dictionary<string, string?>
                {
                    ["Bootstrap:Habilitar"] = "true",
                    ["Migracoes:AplicarNoStartup"] = "true"
                },
                nomeBancoExistente: nomeBanco);
            using var cliente = fabrica.CriarCliente();

            Assert.AreEqual(0, SqlTeste.ContarTabelas(nomeBanco));
            Assert.IsEmpty(fabrica.EtapasBootstrap);
            Assert.AreEqual(HttpStatusCode.NotFound, (await cliente.GetAsync("/openapi/v1.json")).StatusCode);
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }


    [TestMethod]
    public void SqlIndisponivelImpedeTrafegoEmDevelopment()
    {
        using var fabrica = new FabricaApiTeste(
            "Development",
            aplicarMigrationsAntesDoStartup: false,
            configuracaoExtra: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Estacionamento"] =
                    "Server=127.0.0.1,1;Database=Estacionamento;User Id=ninguem;Password=Invalida1;TrustServerCertificate=True;Connect Timeout=1"
            });

        AssertFalhaDeInicializacao(() => fabrica.CriarCliente());
        Assert.AreEqual(0, SqlTeste.ContarTabelas(fabrica.NomeBanco));
    }

    [TestMethod]
    public void ConfiguracaoSemChaveJwtFalhaAntesDeTocarNoBanco()
    {
        using var fabrica = new FabricaApiTeste(
            "Development",
            aplicarMigrationsAntesDoStartup: false,
            configuracaoExtra: new Dictionary<string, string?>
            {
                ["Autenticacao:Jwt:Chaves:v1"] = string.Empty
            });

        AssertFalhaDeInicializacao(() => fabrica.CriarCliente());
        Assert.AreEqual(0, SqlTeste.ContarTabelas(fabrica.NomeBanco));
    }

    [TestMethod]
    public void ContaSemSenhaFalhaAntesDeQualquerMigration()
    {
        using var fabrica = new FabricaApiTeste(
            "Development",
            aplicarMigrationsAntesDoStartup: false,
            configuracaoExtra: new Dictionary<string, string?>
            {
                ["ContasSinteticas:Caixa:Senha"] = string.Empty
            });

        AssertFalhaDeInicializacao(() => fabrica.CriarCliente());
        Assert.AreEqual(0, SqlTeste.ContarTabelas(fabrica.NomeBanco));
    }

    [TestMethod]
    public async Task FalhaDeMigrationBloqueiaProvisionamentoETrafego()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            SqlTeste.ExecutarEmMaster($"ALTER DATABASE [{nomeBanco}] SET READ_ONLY;");

            using (var fabrica = new FabricaApiTeste(
                "Development",
                aplicarMigrationsAntesDoStartup: false,
                nomeBancoExistente: nomeBanco,
                removerBancoAoDispor: false))
            {
                AssertFalhaDeInicializacao(() => fabrica.CriarCliente());
                Assert.AreEqual(0, SqlTeste.ContarTabelas(nomeBanco));
            }
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            SqlTeste.ExecutarEmMaster($"ALTER DATABASE [{nomeBanco}] SET READ_WRITE;");
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }

    [TestMethod]
    public async Task FalhaDeProvisionamentoBloqueiaTrafegoSemExporCredenciais()
    {
        var nomeBanco = SqlTeste.CriarBanco();
        await SqlTeste.AplicarMigrationsAsync(nomeBanco);

        var contas = ContasTeste.Contas();
        var emailAdmin = contas.Administrador!.Email;

        var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
            .UseSqlServer(SqlTeste.Conexao(nomeBanco))
            .Options;

        await using (var db = new EstacionamentoDbContext(options))
        {
            db.Users.Add(new UsuarioAplicacao
            {
                Id = Guid.NewGuid(),
                UserName = emailAdmin,
                NormalizedUserName = emailAdmin.ToUpperInvariant(),
                Email = "outro.usuario@example.test",
                NormalizedEmail = "OUTRO.USUARIO@EXAMPLE.TEST",
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                Nome = "Conflito",
                Ativo = true,
                VersaoAcesso = 1
            });
            await db.SaveChangesAsync();
        }

        try
        {
            using var fabrica = new FabricaApiTeste(
                "Development",
                aplicarMigrationsAntesDoStartup: false,
                nomeBancoExistente: nomeBanco,
                removerBancoAoDispor: false);

            var excecao = AssertFalhaDeInicializacao(() => fabrica.CriarCliente());

            Assert.IsFalse(excecao.ToString().Contains(ContasTeste.Senha, StringComparison.Ordinal));
            Assert.IsFalse(excecao.ToString().Contains(emailAdmin, StringComparison.Ordinal));
            Assert.IsTrue(SqlTeste.ContarTabelas(nomeBanco) > 0);
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }

    [TestMethod]
    public async Task OpenApiEmDevelopmentExigeAdministrador()
    {
        using var fabrica = new FabricaApiTeste("Development", aplicarMigrationsAntesDoStartup: false);
        using var cliente = fabrica.CriarCliente();

        var semToken = await cliente.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.Unauthorized, semToken.StatusCode);

        var caixa = await ClienteSessao.LoginCaixaAsync(cliente);
        ClienteSessao.UsarBearer(cliente, caixa.TokenAcesso);
        var negado = await cliente.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.Forbidden, negado.StatusCode);

        var admin = await ClienteSessao.LoginAdministradorAsync(cliente);
        ClienteSessao.UsarBearer(cliente, admin.TokenAcesso);
        var permitido = await cliente.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, permitido.StatusCode);
        Assert.AreEqual("no-store", permitido.Headers.CacheControl?.ToString().Split(',').First().Trim());
    }

    private static async Task<int> ContarUsuariosAsync(FabricaApiTeste fabrica)
    {
        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        return await db.Users.CountAsync();
    }

    private static Exception AssertFalhaDeInicializacao(Action acao)
    {
        try
        {
            acao();
        }
        catch (Exception excecao)
        {
            Assert.IsTrue(
                excecao is InicializacaoException ||
                excecao.InnerException is InicializacaoException ||
                excecao.ToString().Contains("Inicialização", StringComparison.Ordinal),
                $"Esperava falha controlada de inicialização, mas veio {excecao.GetType().Name}.");
            return excecao;
        }

        Assert.Fail("A inicialização deveria ter falhado de forma controlada.");
        return new InvalidOperationException();
    }
}
