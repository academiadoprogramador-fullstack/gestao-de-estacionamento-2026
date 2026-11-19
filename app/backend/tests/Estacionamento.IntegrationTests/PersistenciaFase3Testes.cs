using System.Net;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class PersistenciaFase3Testes
{
    private const string MigracaoFase2 = "20261118174120_Fase2PreparacaoEntrada";
    private const string MigracaoFase3 = "20261119160017_Fase3RetiradaCaixa";

    [TestMethod]
    public async Task MigracaoDaFase3EhAditivaEPreservaDadosDaFase2()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            await SqlTeste.AplicarMigrationsAsync(nomeBanco);
            Guid atendimentoId;

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
                await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
            }

            var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
                .UseSqlServer(SqlTeste.Conexao(nomeBanco))
                .Options;

            int usuarios;
            int sessoes;
            int atendimentos;
            int vistorias;
            int ocupacoes;

            await using (var db = new EstacionamentoDbContext(options))
            {
                usuarios = await db.Users.CountAsync();
                sessoes = await db.SessoesAutenticacao.CountAsync();
                atendimentos = await db.Atendimentos.CountAsync();
                vistorias = await db.VistoriasEntrada.CountAsync();
                ocupacoes = await db.Ocupacoes.CountAsync();

                Assert.AreEqual(1, atendimentos);
                Assert.AreEqual(1, vistorias);
                Assert.AreEqual(1, ocupacoes);

                await db.GetService<IMigrator>().MigrateAsync(MigracaoFase2);
            }

            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "PedidosRetirada"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "Cobrancas"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "RazoesAtendimento"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "LancamentosFinanceiros"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "ConcessoesAutorizacao"));
            Assert.IsFalse(SqlTeste.TabelaExiste(nomeBanco, "RevogacoesAutorizacao"));

            await SqlTeste.AplicarMigrationsAsync(nomeBanco);

            await using (var db = new EstacionamentoDbContext(options))
            {
                Assert.AreEqual(usuarios, await db.Users.CountAsync());
                Assert.AreEqual(sessoes, await db.SessoesAutenticacao.CountAsync());
                Assert.AreEqual(atendimentos, await db.Atendimentos.CountAsync());
                Assert.AreEqual(vistorias, await db.VistoriasEntrada.CountAsync());
                Assert.AreEqual(ocupacoes, await db.Ocupacoes.CountAsync());
                Assert.AreEqual(atendimentoId, (await db.Atendimentos.AsNoTracking().SingleAsync()).Id);
                Assert.AreEqual(
                    "2026",
                    (await db.Atendimentos.AsNoTracking().SingleAsync())
                        .IdentificadorPublico!
                        .Split('-')[1]);
            }

            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "PedidosRetirada"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "Cobrancas"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "RazoesAtendimento"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "LancamentosFinanceiros"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "ConcessoesAutorizacao"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "RevogacoesAutorizacao"));
            Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "ResultadosIdempotentes"));
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }

    [TestMethod]
    public async Task ProtecaoAppendOnlyRecusaEdicaoDeCobrancaLancamentoEAutorizacao()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(
            caixa,
            atendimentoId,
            conferencia: "divergente");

        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        var versao = Fase3Apoio.VersaoDoPedido(detalhe);

        using var desconto = await Fase3Apoio.DescontarAsync(
            administrador,
            pedidoId,
            10.00m,
            "Justificativa sintética.",
            1);
        Assert.AreEqual(HttpStatusCode.Created, desconto.StatusCode);

        using var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "Justificativa sintética.",
            versao);
        Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();

        var cobranca = await db.Cobrancas.SingleAsync();
        cobranca.TotalOriginal = 1.00m;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var lancamento = await db.LancamentosFinanceiros.SingleAsync();
        lancamento.Valor = 1.00m;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var autorizacao = await db.ConcessoesAutorizacao.SingleAsync();
        autorizacao.RevisaoReferencia = 99;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var pedido = await db.PedidosRetirada.SingleAsync();
        var razao = await db.RazoesAtendimento.SingleAsync();
        razao.Revisao = 2;
        pedido.BuscaIniciadaEmUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.AreEqual(2, (await db.RazoesAtendimento.AsNoTracking().SingleAsync()).Revisao);
    }

    [TestMethod]
    public async Task UnicidadeDePedidoPorAtendimentoEhAplicadaPeloSqlServer()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        var original = await db.PedidosRetirada.AsNoTracking().SingleAsync();

        db.PedidosRetirada.Add(new Estacionamento.Domain.Retiradas.PedidoRetirada
        {
            Id = Guid.NewGuid(),
            AtendimentoId = original.AtendimentoId,
            Conferencia = original.Conferencia,
            TitularPresente = true,
            Estado = original.Estado,
            SolicitadoEmUtc = DateTimeOffset.UtcNow,
            SolicitadoPorId = original.SolicitadoPorId
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
