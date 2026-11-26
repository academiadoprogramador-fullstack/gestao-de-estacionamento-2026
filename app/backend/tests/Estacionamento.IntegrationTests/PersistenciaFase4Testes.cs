using System.Net;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class PersistenciaFase4Testes
{
    private const string MigracaoFase3 = "20261119160017_Fase3RetiradaCaixa";
    private const string MigracaoFase5 = "20261126151516_Fase5FotosVistoria";

    [TestMethod]
    public async Task MigracaoDaFase4EhAditivaNaoPreencheHistoricoEPodeSerRevertidaEmCatalogoDescartavel()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            await SqlTeste.AplicarMigrationsAsync(nomeBanco);

            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "VistoriasSaida"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "EntregasVeiculo"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "ConsumosAutorizacao"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "FotosVistoria"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "ConsumosFotosMensais"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "TarefasFotos"));

            var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
                .UseSqlServer(SqlTeste.Conexao(nomeBanco))
                .Options;

            // Catálogo vazio: a migração não cria vistoria, entrega ou consumo.
            await using (var db = new EstacionamentoDbContext(options))
            {
                Assert.AreEqual(0, await db.VistoriasSaida.CountAsync());
                Assert.AreEqual(0, await db.EntregasVeiculo.CountAsync());
                Assert.AreEqual(0, await db.ConsumosAutorizacao.CountAsync());

                // Reverte apenas os objetos aditivos para a Fase 3. Nenhuma
                // entrega real foi gravada neste catálogo descartável.
                await db.GetService<IMigrator>().MigrateAsync(MigracaoFase3);
            }

            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "VistoriasSaida"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "EntregasVeiculo"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "ConsumosAutorizacao"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "PedidosRetirada"));

            await SqlTeste.AplicarMigrationsAsync(nomeBanco);

            Guid atendimentoId;
            Guid pedidoId;
            using (var fabrica = new FabricaApiTeste(
                       nomeBancoExistente: nomeBanco,
                       criarBanco: false,
                       removerBancoAoDispor: false,
                       aplicarMigrationsAntesDoStartup: false))
            {
                await fabrica.ProvisionarContasAsync();
                var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
                var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
                atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
                pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
            }

            await using (var db = new EstacionamentoDbContext(options))
            {
                var pendentes = await db.Database.GetPendingMigrationsAsync();
                Assert.AreEqual(0, pendentes.Count());
                Assert.AreEqual(1, await db.Atendimentos.CountAsync());
                Assert.AreEqual(1, await db.PedidosRetirada.CountAsync());
                Assert.AreEqual(0, await db.VistoriasSaida.CountAsync());
                Assert.AreEqual(0, await db.EntregasVeiculo.CountAsync());
                Assert.AreEqual(0, await db.ConsumosAutorizacao.CountAsync());
                Assert.AreEqual(
                    atendimentoId,
                    (await db.Atendimentos.AsNoTracking().SingleAsync()).Id);
                Assert.AreEqual(
                    pedidoId,
                    (await db.PedidosRetirada.AsNoTracking().SingleAsync()).Id);
            }

            Assert.AreEqual(MigracaoFase5, await UltimaMigracaoAsync(nomeBanco));
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }

    [TestMethod]
    public async Task RestricoesDeUnicidadeEProtecaoAppendOnlyCobremEntregaEVistoria()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);
        using (var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            using var pagamento = await Fase3Apoio.PagarAsync(
                caixa,
                pedidoId,
                Fase3Apoio.Saldo(caixaInicial),
                "dinheiro",
                Fase3Apoio.RevisaoDaRazao(caixaInicial));
            Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());
        }

        await Fase4Apoio.ConfirmarEntregaConfirmadaAsync(caixa, pedidoId);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();

        var vistoria = await db.VistoriasSaida.AsNoTracking().SingleAsync();
        vistoria.AtorId = Guid.NewGuid();
        db.VistoriasSaida.Update(vistoria);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var entrega = await db.EntregasVeiculo.AsNoTracking().SingleAsync();
        entrega.VagaId = Guid.NewGuid();
        db.EntregasVeiculo.Update(entrega);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        db.VistoriasSaida.Add(new Estacionamento.Domain.Entrega.VistoriaSaida
        {
            Id = Guid.NewGuid(),
            AtendimentoId = atendimentoId,
            PedidoId = pedidoId,
            TextoCifrado = [1, 2, 3],
            AtorId = entrega.AtorId,
            CriadoEmUtc = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static async Task<string?> UltimaMigracaoAsync(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
            .UseSqlServer(SqlTeste.Conexao(nomeBanco))
            .Options;

        await using var db = new EstacionamentoDbContext(options);
        return (await db.Database.GetAppliedMigrationsAsync()).LastOrDefault();
    }
}
