using Estacionamento.Infrastructure.Persistencia;
using Estacionamento.Infrastructure.Persistencia.Entidades;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class PersistenciaRestricoesTestes
{
    [TestMethod]
    public async Task RestricoesDeUnicidadeEChaveEstrangeiraSaoAplicadasPeloSqlServer()
    {
        using var fabrica = new FabricaApiTeste();
        using var cliente = fabrica.CriarCliente();
        await fabrica.ProvisionarContasAsync();
        var login = await ClienteSessao.LoginAdministradorAsync(cliente);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, (System.Net.HttpStatusCode)login.Status);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();

        var sessao = await db.SessoesAutenticacao.AsNoTracking().FirstAsync();
        var refresh = await db.RefreshTokens.AsNoTracking().FirstAsync();

        Assert.IsTrue(sessao.RowVersion.Length > 0);
        Assert.IsTrue(refresh.RowVersion.Length > 0);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessaoId = refresh.SessaoId,
            HashSha256 = refresh.HashSha256,
            CriadoEmUtc = DateTimeOffset.UtcNow,
            ExpiraEmUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [AspNetUsers] WHERE [Id] = {0}",
                sessao.UsuarioId);
            Assert.Fail("A FK restrita deveria impedir a exclusão do usuário com sessão.");
        }
        catch (SqlException excecao)
        {
            Assert.AreEqual(547, excecao.Number, "Esperava violação de FK (547).");
        }
    }
}
