using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Application.Auditoria;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class RetiradaPedidoTestes
{
    [TestMethod]
    public async Task PedidoFixaCalculoMudaEstadoEAudita()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        Fase3Apoio.DeslocarEntrada(fabrica, atendimentoId, TimeSpan.FromHours(30));

        using var resposta = await Fase3Apoio.RegistrarPedidoAsync(caixa, atendimentoId);
        Assert.AreEqual(HttpStatusCode.Created, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());

        using var pedido = await Fase2Apoio.LerJsonAsync(resposta);
        var pedidoId = pedido.RootElement.GetProperty("pedidoId").GetGuid();
        Assert.AreEqual("aguardando", pedido.RootElement.GetProperty("estado").GetString());
        Assert.AreEqual(2, pedido.RootElement.GetProperty("diarias").GetInt32());
        Assert.AreEqual(40.00m, pedido.RootElement.GetProperty("totalOriginal").GetDecimal());
        Assert.AreEqual($"/api/v1/withdrawals/{pedidoId}", pedido.RootElement.GetProperty("location").GetString());

        using var detalheAtendimento = JsonDocument.Parse(
            await (await administrador.GetAsync($"/api/v1/attendances/{atendimentoId}"))
                .Content.ReadAsStringAsync());
        var retirada = detalheAtendimento.RootElement.GetProperty("retirada");
        Assert.AreEqual("aguardando", retirada.GetProperty("estado").GetString());
        Assert.AreEqual(2, retirada.GetProperty("diarias").GetInt32());
        Assert.AreEqual(40.00m, retirada.GetProperty("totalOriginal").GetDecimal());

        using var historico = JsonDocument.Parse(
            await (await administrador.GetAsync($"/api/v1/attendances/{atendimentoId}/history?page=1&pageSize=50"))
                .Content.ReadAsStringAsync());
        var tipos = historico.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("tipo").GetString())
            .ToArray();

        CollectionAssert.Contains(tipos, "pedido_retirada_registrado");
        CollectionAssert.Contains(tipos, "conferencia_retirada_registrada");
        CollectionAssert.Contains(tipos, "calculo_cobranca_fixado");

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        var atendimento = await db.Atendimentos.AsNoTracking().SingleAsync(registro => registro.Id == atendimentoId);
        Assert.AreEqual("aguardando", atendimento.Estado);
        Assert.AreEqual(1, await db.PedidosRetirada.CountAsync());
        Assert.AreEqual(1, await db.Cobrancas.CountAsync());
        Assert.AreEqual(1, await db.RazoesAtendimento.CountAsync());
        Assert.IsTrue(await db.EventosAuditoriaNegocio.AnyAsync(evento =>
            evento.Tipo == "pedido_retirada_registrado" && evento.AlvoId == pedidoId));
    }

    [TestMethod]
    public async Task SegundoPedidoEConflitoDeReplayPreservamOOriginal()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var chave = Guid.NewGuid();
        var primeiroPedido = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId, chave);

        using var segundo = await Fase3Apoio.RegistrarPedidoAsync(caixa, atendimentoId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.AreEqual("withdrawal_already_exists", await Fase3Apoio.CodigoAsync(segundo));

        using var replay = await Fase3Apoio.RegistrarPedidoAsync(caixa, atendimentoId, chave);
        Assert.AreEqual(HttpStatusCode.Created, replay.StatusCode, await replay.Content.ReadAsStringAsync());
        using var replayJson = await Fase2Apoio.LerJsonAsync(replay);
        Assert.AreEqual(primeiroPedido, replayJson.RootElement.GetProperty("pedidoId").GetGuid());

        using var divergente = await Fase3Apoio.RegistrarPedidoAsync(
            caixa,
            atendimentoId,
            chave,
            conferencia: "divergente");
        Assert.AreEqual(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.AreEqual("idempotency_conflict", await Fase3Apoio.CodigoAsync(divergente));

        using var semSessao = fabrica.CriarCliente();
        using var replaySemSessao = await Fase3Apoio.RegistrarPedidoAsync(semSessao, atendimentoId, chave);
        Assert.AreEqual(HttpStatusCode.Unauthorized, replaySemSessao.StatusCode);

        using var mesmoConteudoOutroAtor = await Fase3Apoio.RegistrarPedidoAsync(
            administrador,
            atendimentoId,
            chave);
        Assert.AreEqual(HttpStatusCode.Conflict, mesmoConteudoOutroAtor.StatusCode);
        Assert.AreEqual(
            "withdrawal_already_exists",
            await Fase3Apoio.CodigoAsync(mesmoConteudoOutroAtor));

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(1, await db.PedidosRetirada.CountAsync());
        Assert.AreEqual(1, await db.Cobrancas.CountAsync());

        var resultadoPersistido = await db.ResultadosIdempotentes
            .AsNoTracking()
            .SingleAsync(resultado => resultado.Operacao == "withdrawal.request");
        Assert.IsNotNull(resultadoPersistido.ResultadoJson);
        Assert.IsFalse(resultadoPersistido.ResultadoJson.Contains("conferencia", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(resultadoPersistido.ResultadoJson.Contains("000.000.000-00", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RecusaTerceiroPropriedadesInesperadasEPerfis()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);

        using var ausente = await Fase3Apoio.RegistrarPedidoAsync(
            caixa,
            atendimentoId,
            titularPresente: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, ausente.StatusCode);
        Assert.AreEqual("withdrawal_requires_holder", await Fase3Apoio.CodigoAsync(ausente));

        using var terceiro = await Fase3Apoio.RegistrarPedidoAsync(
            caixa,
            atendimentoId,
            extras: new { documento = "novo-documento", foto = "base64" });
        Assert.AreEqual(HttpStatusCode.BadRequest, terceiro.StatusCode);

        using var negado = await Fase3Apoio.RegistrarPedidoAsync(manobrista, atendimentoId);
        Assert.AreEqual(HttpStatusCode.Forbidden, negado.StatusCode);

        using var inexistente = await Fase3Apoio.RegistrarPedidoAsync(caixa, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.AreEqual("atendimento_nao_encontrado", await Fase3Apoio.CodigoAsync(inexistente));

        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"UPDATE [Atendimentos] SET [Estado] = 'entregue' WHERE [Id] = '{atendimentoId}';");

        using var inelegivel = await Fase3Apoio.RegistrarPedidoAsync(caixa, atendimentoId);
        Assert.AreEqual(HttpStatusCode.Conflict, inelegivel.StatusCode);
        Assert.AreEqual("attendance_not_active", await Fase3Apoio.CodigoAsync(inelegivel));
    }

    [TestMethod]
    public async Task DoisPedidosConcorrentesConfirmamUmUnicoRegistro()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixaA, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (caixaB, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixaA);

        var respostas = await Task.WhenAll(
            Fase3Apoio.RegistrarPedidoAsync(caixaA, atendimentoId, Guid.NewGuid()),
            Fase3Apoio.RegistrarPedidoAsync(caixaB, atendimentoId, Guid.NewGuid()));

        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));

        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(1, await db.PedidosRetirada.CountAsync());
        Assert.AreEqual(1, await db.Cobrancas.CountAsync());
    }

    [TestMethod]
    public async Task FalhaDeAuditoriaRevertePedidoCobrancaEEstado()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);

        using var fabricaComAuditoriaQueFalha = new FabricaApiTeste(
                nomeBancoExistente: fabrica.NomeBanco,
                criarBanco: false,
                removerBancoAoDispor: false)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAuditoriaNegocio>();
                    services.AddScoped<IAuditoriaNegocio, AuditoriaQueFalha>();
                }));

        using var cliente = fabricaComAuditoriaQueFalha.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost")
            });
        var login = await ClienteSessao.LoginAdministradorAsync(cliente);
        ClienteSessao.UsarBearer(cliente, login.TokenAcesso);

        using var resposta = await Fase3Apoio.RegistrarPedidoAsync(cliente, atendimentoId);
        Assert.AreEqual(HttpStatusCode.InternalServerError, resposta.StatusCode);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(0, await db.PedidosRetirada.CountAsync());
        Assert.AreEqual(0, await db.Cobrancas.CountAsync());
        Assert.AreEqual(0, await db.RazoesAtendimento.CountAsync());
        Assert.AreEqual(
            "ativo",
            (await db.Atendimentos.AsNoTracking().SingleAsync(registro => registro.Id == atendimentoId)).Estado);
        Assert.AreEqual(0, await db.ResultadosIdempotentes
            .CountAsync(resultado => resultado.Operacao == "withdrawal.request"));
    }

    private sealed class AuditoriaQueFalha : IAuditoriaNegocio
    {
        public void Adicionar(EventoAuditoriaNegocio evento) =>
            throw new InvalidOperationException("Falha de auditoria simulada.");

        public Task RegistrarAsync(
            EventoAuditoriaNegocio evento,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha de auditoria simulada.");
    }
}
