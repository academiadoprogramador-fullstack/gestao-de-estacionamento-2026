using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class PersistenciaFase5Testes
{
    private const string MigracaoFase4 = "20261123160433_Fase4EntregaLiberacao";

    [TestMethod]
    public async Task MigracaoDaFase5EhAditivaENaoPreencheFotosRetroativas()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            await SqlTeste.AplicarMigrationsAsync(nomeBanco);

            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "FotosVistoria"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "ConsumosFotosMensais"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "TarefasFotos"));

            var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
                .UseSqlServer(SqlTeste.Conexao(nomeBanco))
                .Options;

            await using (var db = new EstacionamentoDbContext(options))
            {
                Assert.AreEqual(0, await db.FotosVistoria.CountAsync());
                Assert.AreEqual(0, await db.ConsumosFotosMensais.CountAsync());
                Assert.AreEqual(0, await db.TarefasFotos.CountAsync());

                await db.GetService<IMigrator>().MigrateAsync(MigracaoFase4);
            }

            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "FotosVistoria"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "ConsumosFotosMensais"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "TarefasFotos"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "EntregasVeiculo"));
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }
}
