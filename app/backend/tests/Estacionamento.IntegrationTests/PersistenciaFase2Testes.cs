using System.Net;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia;
using Estacionamento.Infrastructure.Persistencia.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class PersistenciaFase2Testes
{
    [TestMethod]
    public async Task IndicesFiltradosImpedemDuasOcupacoesAbertasNaMesmaVaga()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
        var entrada = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Created, entrada.StatusCode);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();

        var aberta = await db.Ocupacoes.SingleAsync();
        var atendimentoId = aberta.AtendimentoId;
        var responsavelId = aberta.ResponsavelId;

        // Encerrar a ocupação libera os índices filtrados e permite uma nova
        // ocupação do mesmo atendimento (histórico), mas não duas abertas.
        aberta.FimUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var nova = new Ocupacao
        {
            Id = Guid.NewGuid(),
            VagaId = vagaId,
            AtendimentoId = atendimentoId,
            InicioUtc = DateTimeOffset.UtcNow,
            ResponsavelId = responsavelId
        };
        db.Ocupacoes.Add(nova);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var duplicada = new Ocupacao
        {
            Id = Guid.NewGuid(),
            VagaId = vagaId,
            AtendimentoId = atendimentoId,
            InicioUtc = DateTimeOffset.UtcNow,
            ResponsavelId = responsavelId
        };
        db.Ocupacoes.Add(duplicada);

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task MigracaoDaFase2EhAditivaSobreDadosDaFase1()
    {
        var nomeBanco = SqlTeste.CriarBanco();

        try
        {
            var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
                .UseSqlServer(SqlTeste.Conexao(nomeBanco))
                .Options;

            Guid usuarioId;
            Guid sessaoId;

            await using (var db = new EstacionamentoDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20261117145708_MigracaoInicial");

                usuarioId = Guid.NewGuid();
                sessaoId = Guid.NewGuid();

                // Insere dados com o esquema da Fase 1 por SQL direto: o modelo
                // atual já possui RowVersion e não pode ser usado antes da
                // migration aditiva ser aplicada.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [AspNetUsers]
                        ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail],
                         [EmailConfirmed], [PasswordHash], [SecurityStamp], [ConcurrencyStamp],
                         [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnd],
                         [LockoutEnabled], [AccessFailedCount], [Nome], [Ativo], [VersaoAcesso])
                    VALUES
                        ({usuarioId}, {"fase1@example.test"}, {"FASE1@EXAMPLE.TEST"},
                         {"fase1@example.test"}, {"FASE1@EXAMPLE.TEST"}, {true}, {null},
                         {Guid.NewGuid().ToString()}, {Guid.NewGuid().ToString()}, {null},
                         {false}, {false}, {null}, {true}, {0}, {"Conta da Fase 1"}, {true}, {1L});
                    """);

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [SessoesAutenticacao]
                        ([Id], [UsuarioId], [VersaoAcesso], [CriadaEmUtc], [ExpiraEmUtc],
                         [RevogadaEmUtc], [CausaRevogacao])
                    VALUES
                        ({sessaoId}, {usuarioId}, {1L}, {DateTimeOffset.UtcNow},
                         {DateTimeOffset.UtcNow.AddHours(8)}, {null}, {null});
                    """);

                var colunaRowVersion = await db.Database
                    .SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'RowVersion'")
                    .SingleAsync();
                Assert.AreEqual(0, colunaRowVersion);
            }

            await SqlTeste.AplicarMigrationsAsync(nomeBanco);

            await using (var db = new EstacionamentoDbContext(options))
            {
                var usuario = await db.Users.AsNoTracking().SingleAsync(registro => registro.Id == usuarioId);
                var sessao = await db.SessoesAutenticacao.AsNoTracking().SingleAsync(registro => registro.Id == sessaoId);

                Assert.AreEqual("Conta da Fase 1", usuario.Nome);
                Assert.IsTrue(usuario.Ativo);
                Assert.AreEqual(1, usuario.VersaoAcesso);
                Assert.IsTrue(usuario.RowVersion.Length > 0, "A coluna RowVersion deve ser acrescentada pela Fase 2.");
                Assert.AreEqual(usuarioId, sessao.UsuarioId);

                Assert.IsTrue(await db.Vagas.CountAsync() == 0);
                Assert.IsTrue(await db.Atendimentos.CountAsync() == 0);
                Assert.IsTrue(await db.ResultadosIdempotentes.CountAsync() == 0);
                Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "VistoriasEntrada"));
                Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "Ocupacoes"));
                Assert.IsTrue(SqlTeste.TabelaExiste(nomeBanco, "EventosAtendimento"));
            }
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }
}
