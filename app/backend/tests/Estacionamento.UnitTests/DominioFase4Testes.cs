using Estacionamento.Domain.Entrega;
using Estacionamento.Domain.Retiradas;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class DominioFase4Testes
{
    [TestMethod]
    public void EntregaNormalExigeVistoriaConferenciaAprovadaSaldoZeroEOcupacaoPropria()
    {
        var avaliacao = AvaliadorEntrega.Avaliar(
            vistoriaRegistrada: true,
            conferencia: ConferenciaRetirada.Aprovada,
            saldo: 0,
            revisaoRazao: 1,
            documental: null,
            financeira: null,
            ocupacaoPropriaAberta: true);

        Assert.IsTrue(avaliacao.Pronta);
        Assert.AreEqual(0, avaliacao.Bloqueios.Count);
        Assert.IsNull(avaliacao.DocumentalUsada);
        Assert.IsNull(avaliacao.FinanceiraUsada);
        Assert.IsTrue(avaliacao.Condicoes.All(condicao => condicao.Atendida));
    }

    [TestMethod]
    public void SemVistoriaBloqueiaComCodigoProprio()
    {
        var avaliacao = AvaliadorEntrega.Avaliar(
            false,
            ConferenciaRetirada.Aprovada,
            0,
            1,
            null,
            null,
            true);

        Assert.IsFalse(avaliacao.Pronta);
        CollectionAssert.Contains(
            avaliacao.Bloqueios.ToArray(),
            CodigosBloqueioEntrega.RequerVistoria);
    }

    [TestMethod]
    public void DivergenciaSemAutorizacaoDocumentalBloqueia()
    {
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Divergente,
            0,
            1,
            null,
            null,
            true);

        Assert.IsFalse(avaliacao.Pronta);
        CollectionAssert.Contains(
            avaliacao.Bloqueios.ToArray(),
            CodigosBloqueioEntrega.DocumentoNaoAprovado);
    }

    [TestMethod]
    public void DivergenciaComConcessaoDocumentalVigentePermiteEntregaEConsomeTipo()
    {
        var concessao = Guid.NewGuid();
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Divergente,
            0,
            1,
            new ControleAutorizacao(concessao, 1, Revogada: false),
            null,
            true);

        Assert.IsTrue(avaliacao.Pronta);
        Assert.AreEqual(concessao, avaliacao.DocumentalUsada?.Id);
        Assert.IsNull(avaliacao.FinanceiraUsada);
    }

    [TestMethod]
    public void SaldoPositivoSemAutorizacaoFinanceiraBloqueia()
    {
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Aprovada,
            40,
            1,
            null,
            null,
            true);

        Assert.IsFalse(avaliacao.Pronta);
        CollectionAssert.Contains(
            avaliacao.Bloqueios.ToArray(),
            CodigosBloqueioEntrega.SaldoPendente);
    }

    [TestMethod]
    public void SaldoComConcessaoFinanceiraNaRevisaoCorrentePermiteEntrega()
    {
        var concessao = Guid.NewGuid();
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Aprovada,
            40,
            2,
            null,
            new ControleAutorizacao(concessao, 2, Revogada: false),
            true);

        Assert.IsTrue(avaliacao.Pronta);
        Assert.AreEqual(concessao, avaliacao.FinanceiraUsada?.Id);
        Assert.IsNull(avaliacao.DocumentalUsada);
    }

    [TestMethod]
    public void ConcessaoFinanceiraDeRevisaoAnteriorFicaObsoleta()
    {
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Aprovada,
            40,
            2,
            null,
            new ControleAutorizacao(Guid.NewGuid(), 1, Revogada: false),
            true);

        Assert.IsFalse(avaliacao.Pronta);
        CollectionAssert.Contains(
            avaliacao.Bloqueios.ToArray(),
            CodigosBloqueioEntrega.AutorizacaoObsoleta);
    }

    [TestMethod]
    public void ConcessaoRevogadaBloqueiaMesmoComRevisaoCorrente()
    {
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Aprovada,
            40,
            1,
            new ControleAutorizacao(Guid.NewGuid(), 1, Revogada: true),
            new ControleAutorizacao(Guid.NewGuid(), 1, Revogada: true),
            true);

        Assert.IsFalse(avaliacao.Pronta);
        CollectionAssert.Contains(
            avaliacao.Bloqueios.ToArray(),
            CodigosBloqueioEntrega.AutorizacaoObsoleta);
    }

    [TestMethod]
    public void ExcecoesDocumentalEFinanceiraSaoIndependentes()
    {
        var documental = Guid.NewGuid();
        var financeira = Guid.NewGuid();
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Divergente,
            40,
            1,
            new ControleAutorizacao(documental, 1, Revogada: false),
            new ControleAutorizacao(financeira, 1, Revogada: false),
            true);

        Assert.IsTrue(avaliacao.Pronta);
        Assert.AreEqual(documental, avaliacao.DocumentalUsada?.Id);
        Assert.AreEqual(financeira, avaliacao.FinanceiraUsada?.Id);
    }

    [TestMethod]
    public void OcupacaoAusenteBloqueiaSemInferirDisponibilidade()
    {
        var avaliacao = AvaliadorEntrega.Avaliar(
            true,
            ConferenciaRetirada.Aprovada,
            0,
            1,
            null,
            null,
            ocupacaoPropriaAberta: false);

        Assert.IsFalse(avaliacao.Pronta);
        CollectionAssert.Contains(
            avaliacao.Bloqueios.ToArray(),
            CodigosBloqueioEntrega.RequerOcupacao);
    }

    [TestMethod]
    public void VersaoDeControleRepresentaAusenciaERevogacao()
    {
        var concessao = Guid.NewGuid();

        Assert.AreEqual(
            VersaoControleAutorizacao.Ausente,
            VersaoControleAutorizacao.Calcular("documental", null, null, false));

        var vigente = VersaoControleAutorizacao.Calcular("documental", concessao, 1, false);
        var revogada = VersaoControleAutorizacao.Calcular("documental", concessao, 1, true);

        Assert.AreNotEqual(vigente, revogada);
        Assert.IsTrue(VersaoControleAutorizacao.Corresponde(
            vigente,
            "documental",
            concessao,
            1,
            false));
        Assert.IsFalse(VersaoControleAutorizacao.Corresponde(
            vigente,
            "documental",
            concessao,
            1,
            true));
    }
}
