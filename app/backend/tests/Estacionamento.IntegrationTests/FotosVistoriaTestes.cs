using System.Net;
using System.Text.Json;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class FotosVistoriaTestes
{
    [TestMethod]
    public async Task FotosEntradaSaoOpcionaisLimitadasPorPerfilEJanela()
    {
        using var fabrica = Fase5Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var versao = await Fase4Apoio.VersaoAtendimentoAsync(caixa, atendimentoId);

        using (var vazio = await Fase5Apoio.ListarFotosAsync(caixa, atendimentoId, "entrada"))
        {
            Assert.AreEqual(0, vazio.RootElement.GetProperty("quantidade").GetInt32());
            Assert.AreEqual("aberta", vazio.RootElement.GetProperty("janela").GetString());
            Assert.IsFalse(vazio.RootElement.GetProperty("vencido").GetBoolean());
        }

        using var criada = await Fase5Apoio.EnviarFotoConfirmadaAsync(
            caixa,
            atendimentoId,
            "entrada",
            versao);
        var fotoId = criada.RootElement.GetProperty("fotoId").GetGuid();

        using var lista = await Fase5Apoio.ListarFotosAsync(manobrista, atendimentoId, "entrada");
        Assert.AreEqual(1, lista.RootElement.GetProperty("quantidade").GetInt32());
        var item = lista.RootElement.GetProperty("itens")[0];
        Assert.AreEqual(fotoId, item.GetProperty("fotoId").GetGuid());
        Assert.AreEqual("image/webp", item.GetProperty("mime").GetString());
        Assert.IsFalse(item.TryGetProperty("chaveObjeto", out _));
        Assert.IsFalse(lista.RootElement.GetRawText().Contains("fotos/", StringComparison.Ordinal));

        using var manobristaTentandoEnviar = await Fase5Apoio.EnviarFotoAsync(
            manobrista,
            atendimentoId,
            "entrada",
            Fase5Apoio.GerarImagem(),
            versaoAtendimento: versao);
        Assert.AreEqual(HttpStatusCode.Forbidden, manobristaTentandoEnviar.StatusCode);

        using var conteudo = await Fase5Apoio.LerConteudoAsync(manobrista, atendimentoId, "entrada", fotoId);
        Assert.AreEqual(HttpStatusCode.OK, conteudo.StatusCode, await conteudo.Content.ReadAsStringAsync());
        Assert.AreEqual("image/webp", conteudo.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("nosniff", conteudo.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("no-store", conteudo.Headers.CacheControl?.ToString().Split(',').First().Trim());
        var bytes = await conteudo.Content.ReadAsByteArrayAsync();
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.AreEqual("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

        await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        var versaoAposPedido = await Fase4Apoio.VersaoAtendimentoAsync(caixa, atendimentoId);
        using var janelaFechada = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            Fase5Apoio.GerarImagem(),
            versaoAtendimento: versaoAposPedido);
        Assert.AreEqual(HttpStatusCode.Conflict, janelaFechada.StatusCode);
        Assert.AreEqual("photo_window_closed", await Fase4Apoio.CodigoAsync(janelaFechada));
    }

    [TestMethod]
    public async Task FotosSaidaAceitamTresPerfisIdempotenciaELimiteSobConcorrencia()
    {
        using var fabrica = Fase5Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(manobrista, atendimentoId);

        var conteudo = Fase5Apoio.GerarImagem();
        var versao = await Fase4Apoio.VersaoAtendimentoAsync(manobrista, atendimentoId);
        var chave = Guid.NewGuid();

        using var primeira = await Fase5Apoio.EnviarFotoConfirmadaAsync(
            manobrista,
            atendimentoId,
            "saida",
            versao,
            conteudo,
            chave: chave);
        var fotoId = primeira.RootElement.GetProperty("fotoId").GetGuid();

        using var repeticao = await Fase5Apoio.EnviarFotoAsync(
            manobrista,
            atendimentoId,
            "saida",
            conteudo,
            "image/jpeg",
            versao,
            chave);
        Assert.AreEqual(HttpStatusCode.Created, repeticao.StatusCode);
        using var repetida = JsonDocument.Parse(await repeticao.Content.ReadAsStringAsync());
        Assert.AreEqual(fotoId, repetida.RootElement.GetProperty("fotoId").GetGuid());

        using var conflito = await Fase5Apoio.EnviarFotoAsync(
            manobrista,
            atendimentoId,
            "saida",
            Fase5Apoio.GerarImagem(320, 240),
            "image/jpeg",
            versao,
            chave);
        Assert.AreEqual(HttpStatusCode.Conflict, conflito.StatusCode);
        Assert.AreEqual("photo_idempotency_conflict", await Fase4Apoio.CodigoAsync(conflito));

        var envios = Enumerable.Range(0, 4)
            .Select(_ => Fase5Apoio.EnviarFotoAsync(
                manobrista,
                atendimentoId,
                "saida",
                conteudo,
                versaoAtendimento: versao))
            .ToArray();

        var respostas = await Task.WhenAll(envios);
        foreach (var resposta in respostas)
        {
            Assert.AreEqual(HttpStatusCode.Created, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
            resposta.Dispose();
        }

        using var sexta = await Fase5Apoio.EnviarFotoAsync(
            manobrista,
            atendimentoId,
            "saida",
            conteudo,
            versaoAtendimento: versao);
        Assert.AreEqual(HttpStatusCode.Conflict, sexta.StatusCode);
        Assert.AreEqual("photo_limit_reached", await Fase4Apoio.CodigoAsync(sexta));

        using var lista = await Fase5Apoio.ListarFotosAsync(caixa, atendimentoId, "saida");
        Assert.AreEqual(5, lista.RootElement.GetProperty("quantidade").GetInt32());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(5, await db.FotosVistoria.CountAsync(
                foto => foto.AtendimentoId == atendimentoId &&
                        foto.Tipo == "saida" &&
                        foto.Estado == "disponivel"));
            Assert.IsTrue(await db.EventosAuditoriaNegocio.AnyAsync(evento => evento.Tipo == "foto_anexada"));
        }

        var caixaDetalhe = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        await Fase3Apoio.PagarAsync(
            caixa,
            pedidoId,
            20m,
            "dinheiro",
            caixaDetalhe.RootElement.GetProperty("revisaoRazao").GetInt32());
        using var prontidao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        using var entrega = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao);
        Assert.AreEqual(HttpStatusCode.Created, entrega.StatusCode, await entrega.Content.ReadAsStringAsync());

        var versaoEntregue = await Fase4Apoio.VersaoAtendimentoAsync(caixa, atendimentoId);
        using var aposEntrega = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "saida",
            conteudo,
            versaoAtendimento: versaoEntregue);
        Assert.AreEqual(HttpStatusCode.Conflict, aposEntrega.StatusCode);
        Assert.AreEqual("photo_window_closed", await Fase4Apoio.CodigoAsync(aposEntrega));

        using var listaEntregue = await Fase5Apoio.ListarFotosAsync(manobrista, atendimentoId, "saida");
        Assert.AreEqual(5, listaEntregue.RootElement.GetProperty("quantidade").GetInt32());
        Assert.AreEqual("encerrada", listaEntregue.RootElement.GetProperty("janela").GetString());
    }

    [TestMethod]
    public async Task ContratoDistingueValidacaoFormatoTamanhoEInspectcaoAusente()
    {
        using var fabrica = Fase5Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var versao = await Fase4Apoio.VersaoAtendimentoAsync(caixa, atendimentoId);

        using var semVersao = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            Fase5Apoio.GerarImagem());
        Assert.AreEqual(HttpStatusCode.BadRequest, semVersao.StatusCode);

        using var tipoInvalido = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "outro",
            Fase5Apoio.GerarImagem(),
            versaoAtendimento: versao);
        Assert.AreEqual(HttpStatusCode.BadRequest, tipoInvalido.StatusCode);

        using var formato = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            System.Text.Encoding.UTF8.GetBytes("<svg></svg>"),
            "image/svg+xml",
            versao);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, formato.StatusCode);
        Assert.AreEqual("photo_unsupported_format", await Fase4Apoio.CodigoAsync(formato));

        using var excessiva = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            new byte[5 * 1024 * 1024 + 1],
            "image/jpeg",
            versao);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, excessiva.StatusCode);
        Assert.AreEqual("photo_too_large", await Fase4Apoio.CodigoAsync(excessiva));

        await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        var versaoPedido = await Fase4Apoio.VersaoAtendimentoAsync(caixa, atendimentoId);
        using var semVistoriaSaida = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "saida",
            Fase5Apoio.GerarImagem(),
            versaoAtendimento: versaoPedido);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, semVistoriaSaida.StatusCode);
        Assert.AreEqual("photo_requires_inspection", await Fase4Apoio.CodigoAsync(semVistoriaSaida));
    }

    [TestMethod]
    public async Task FalhaDoArmazenamentoECotaNaoBloqueiamFluxoTextual()
    {
        using var fabrica = Fase5Apoio.NovaFabrica(
            new Dictionary<string, string?>
            {
                ["ArmazenamentoFotos:Orcamento:FranquiaBytesMensal"] = "1000000",
                ["ArmazenamentoFotos:Orcamento:FranquiaOperacoesClasseAMensal"] = "1000000",
                ["ArmazenamentoFotos:Orcamento:FranquiaOperacoesClasseBMensal"] = "1000000"
            });
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var versao = await Fase4Apoio.VersaoAtendimentoAsync(caixa, atendimentoId);

        fabrica.ArmazenamentoFotos.FalharProximoEnvio = true;
        using var falha = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            Fase5Apoio.GerarImagem(),
            versaoAtendimento: versao);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, falha.StatusCode);
        Assert.AreEqual("photo_storage_unavailable", await Fase4Apoio.CodigoAsync(falha));

        using var listaAposFalha = await Fase5Apoio.ListarFotosAsync(caixa, atendimentoId, "entrada");
        Assert.AreEqual(0, listaAposFalha.RootElement.GetProperty("quantidade").GetInt32());

        using var recuperada = await Fase5Apoio.EnviarFotoConfirmadaAsync(
            caixa,
            atendimentoId,
            "entrada",
            versao);
        Assert.AreEqual("disponivel", recuperada.RootElement.GetProperty("estado").GetString());

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "UPDATE [ConsumosFotosMensais] SET [BytesArmazenados] = 750000;");

        using var aviso = await Fase5Apoio.ListarFotosAsync(caixa, atendimentoId, "entrada");
        Assert.IsTrue(aviso.RootElement.GetProperty("orcamento").GetProperty("aviso").GetBoolean());
        Assert.IsFalse(aviso.RootElement.GetProperty("orcamento").GetProperty("suspenso").GetBoolean());

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "UPDATE [ConsumosFotosMensais] SET [BytesArmazenados] = 850000;");

        using var suspenso = await Fase5Apoio.ListarFotosAsync(caixa, atendimentoId, "entrada");
        Assert.IsTrue(suspenso.RootElement.GetProperty("orcamento").GetProperty("suspenso").GetBoolean());
        Assert.AreEqual("limite_orcamento", suspenso.RootElement.GetProperty("orcamento").GetProperty("motivo").GetString());

        using var bloqueada = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            Fase5Apoio.GerarImagem(320, 240),
            versaoAtendimento: versao);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, bloqueada.StatusCode);
        Assert.AreEqual("photo_quota_exceeded", await Fase4Apoio.CodigoAsync(bloqueada));

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "UPDATE [ConsumosFotosMensais] SET [BytesArmazenados] = 0, [EstadoReconciliacao] = 'desconhecido';");

        using var desconhecido = await Fase5Apoio.ListarFotosAsync(caixa, atendimentoId, "entrada");
        Assert.IsTrue(desconhecido.RootElement.GetProperty("orcamento").GetProperty("suspenso").GetBoolean());
        Assert.AreEqual(
            "consumo_desconhecido",
            desconhecido.RootElement.GetProperty("orcamento").GetProperty("motivo").GetString());

        using var suspensaPorDesconhecido = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            Fase5Apoio.GerarImagem(320, 240),
            versaoAtendimento: versao);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, suspensaPorDesconhecido.StatusCode);
        Assert.AreEqual("photo_quota_unknown", await Fase4Apoio.CodigoAsync(suspensaPorDesconhecido));
    }
}
