using Estacionamento.Application.Comum;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Seguranca;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Retiradas;
using Estacionamento.Infrastructure.Seguranca;
using Microsoft.Extensions.Options;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class DominioFase3Testes
{
    private static readonly DateTimeOffset Entrada =
        new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid AtendimentoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void CalculoContaPeriodosIniciadosDeVinteQuatroHorasComMinimoDeUm()
    {
        var imediato = CalculadoraCobranca.Calcular(Entrada, Entrada, 20.00m);
        Assert.AreEqual(1, imediato.Diarias);
        Assert.AreEqual(0, imediato.DuracaoTicks);

        var vinteQuatroHoras = CalculadoraCobranca.Calcular(Entrada, Entrada.AddHours(24), 20.00m);
        Assert.AreEqual(1, vinteQuatroHoras.Diarias);
        Assert.AreEqual(TimeSpan.TicksPerDay, vinteQuatroHoras.DuracaoTicks);

        var umTickDepois = CalculadoraCobranca.Calcular(
            Entrada,
            Entrada.AddHours(24).AddTicks(1),
            20.00m);
        Assert.AreEqual(2, umTickDepois.Diarias);

        var quarentaEOitoHoras = CalculadoraCobranca.Calcular(
            Entrada,
            Entrada.AddHours(48),
            20.00m);
        Assert.AreEqual(2, quarentaEOitoHoras.Diarias);

        var quarentaENoveHoras = CalculadoraCobranca.Calcular(
            Entrada,
            Entrada.AddHours(49),
            20.00m);
        Assert.AreEqual(3, quarentaENoveHoras.Diarias);
    }

    [TestMethod]
    public void CalculoRejeitaInstanteAnteriorEUsaTarifaSnapshotComDuasCasas()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CalculadoraCobranca.Calcular(Entrada, Entrada.AddTicks(-1), 20.00m));

        var resultado = CalculadoraCobranca.Calcular(Entrada, Entrada.AddHours(30), 12.34m);
        Assert.AreEqual(2, resultado.Diarias);
        Assert.AreEqual(24.68m, resultado.TotalOriginal);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CalculadoraCobranca.Calcular(
                Entrada,
                Entrada.AddHours(30),
                9_999_999_999_999_999.99m));
    }

    [TestMethod]
    public void ComposicaoDerivaCobradoPagoLiquidoESaldoDosLancamentos()
    {
        const decimal total = 60.00m;

        var inicial = ComposicaoSaldo.Calcular(total, []);
        Assert.AreEqual(60.00m, inicial.Cobrado);
        Assert.AreEqual(0m, inicial.PagoLiquido);
        Assert.AreEqual(60.00m, inicial.Saldo);

        var desconto = Lancamento(TipoLancamentoFinanceiro.Desconto, 10.00m);
        var comDesconto = ComposicaoSaldo.Calcular(total, [desconto]);
        Assert.AreEqual(50.00m, comDesconto.Cobrado);
        Assert.AreEqual(50.00m, comDesconto.Saldo);

        var pagamento = Lancamento(TipoLancamentoFinanceiro.Pagamento, 50.00m);
        var pago = ComposicaoSaldo.Calcular(total, [desconto, pagamento]);
        Assert.AreEqual(50.00m, pago.PagoLiquido);
        Assert.AreEqual(0m, pago.Saldo);

        var estorno = Lancamento(TipoLancamentoFinanceiro.Estorno, 50.00m);
        var estornado = ComposicaoSaldo.Calcular(total, [desconto, pagamento, estorno]);
        Assert.AreEqual(0m, estornado.PagoLiquido);
        Assert.AreEqual(50.00m, estornado.Saldo);
        Assert.AreEqual(50.00m, estornado.Cobrado);

        var zerado = ComposicaoSaldo.Calcular(total, [Lancamento(TipoLancamentoFinanceiro.Desconto, 60.00m)]);
        Assert.AreEqual(0m, zerado.Cobrado);
        Assert.AreEqual(0m, zerado.Saldo);
    }

    [TestMethod]
    public void ComposicaoRecusaSaldoNegativoECobrancaZeradaComPagamento()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ComposicaoSaldo.Calcular(50.00m, [Lancamento(TipoLancamentoFinanceiro.Desconto, 60.00m)]));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ComposicaoSaldo.Calcular(
                50.00m,
                [
                    Lancamento(TipoLancamentoFinanceiro.Pagamento, 60.00m),
                    Lancamento(TipoLancamentoFinanceiro.Estorno, 70.00m)
                ]));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ComposicaoSaldo.Calcular(0m, [Lancamento(TipoLancamentoFinanceiro.Pagamento, 10.00m)]));
    }

    [TestMethod]
    public void VigenciaDerivadaAplicaPrecedenciaRevogadaSubstituidaObsoletaVigente()
    {
        var documental = new ConcessaoAutorizacao
        {
            Id = Guid.NewGuid(),
            Tipo = TipoAutorizacaoRetirada.Documental,
            RevisaoReferencia = 1
        };

        Assert.AreEqual(
            VigenciaAutorizacao.Revogada,
            ComposicaoSaldo.DerivarVigencia(documental, false, true, 5));
        Assert.AreEqual(
            VigenciaAutorizacao.Substituida,
            ComposicaoSaldo.DerivarVigencia(documental, false, false, 5));
        Assert.AreEqual(
            VigenciaAutorizacao.Vigente,
            ComposicaoSaldo.DerivarVigencia(documental, true, false, 5));

        var financeira = new ConcessaoAutorizacao
        {
            Id = Guid.NewGuid(),
            Tipo = TipoAutorizacaoRetirada.Financeira,
            RevisaoReferencia = 2
        };

        Assert.AreEqual(
            VigenciaAutorizacao.Obsoleta,
            ComposicaoSaldo.DerivarVigencia(financeira, true, false, 3));
        Assert.AreEqual(
            VigenciaAutorizacao.Vigente,
            ComposicaoSaldo.DerivarVigencia(financeira, true, false, 2));
    }

    [TestMethod]
    public void CanonicalizacaoEhDeterministicaEPorOperacao()
    {
        var ator = Guid.NewGuid();
        var pedido = Guid.NewGuid();

        var primeiro = CanonicalizadorIdempotencia.CalcularHashPagamentoV1(
            ator,
            pedido,
            60.00m,
            FormaPagamento.Pix,
            2);
        var segundo = CanonicalizadorIdempotencia.CalcularHashPagamentoV1(
            ator,
            pedido,
            60.00m,
            FormaPagamento.Pix,
            2);

        Assert.AreEqual(32, primeiro.Length);
        CollectionAssert.AreEqual(primeiro, segundo);

        var outroValor = CanonicalizadorIdempotencia.CalcularHashPagamentoV1(
            ator,
            pedido,
            61.00m,
            FormaPagamento.Pix,
            2);
        CollectionAssert.AreNotEqual(primeiro, outroValor);

        var retirada = CanonicalizadorIdempotencia.CalcularHashSolicitacaoRetiradaV1(
            ator,
            pedido,
            true,
            ConferenciaRetirada.Aprovada);
        CollectionAssert.AreNotEqual(primeiro, retirada);
    }

    [TestMethod]
    public void CriptografiaVinculadaAoTrocaRegistroOuPropositoEFalhaFechada()
    {
        var (criptografia, _) = Criar();
        var registro = Guid.NewGuid();
        var outroRegistro = Guid.NewGuid();

        var envelope = criptografia.CifrarComRegistro(
            PropositosCriptografia.JustificativaDesconto,
            AtendimentoId,
            registro,
            "Justificativa sintética.");

        Assert.AreEqual(
            "Justificativa sintética.",
            criptografia.DecifrarComRegistro(
                PropositosCriptografia.JustificativaDesconto,
                AtendimentoId,
                registro,
                envelope));

        AssertarProtecao(() => criptografia.DecifrarComRegistro(
            PropositosCriptografia.JustificativaDesconto,
            AtendimentoId,
            outroRegistro,
            envelope));

        AssertarProtecao(() => criptografia.DecifrarComRegistro(
            PropositosCriptografia.JustificativaEstorno,
            AtendimentoId,
            registro,
            envelope));

        AssertarProtecao(() => criptografia.Decifrar(
            PropositosCriptografia.JustificativaDesconto,
            AtendimentoId,
            envelope));
    }

    [TestMethod]
    public void EnvelopesAntigosContinuamLegiveisPelaOperacaoDaFase2()
    {
        var (criptografia, _) = Criar();
        var antigo = criptografia.Cifrar(
            PropositosCriptografia.Documento,
            AtendimentoId,
            "documento da fase 2");

        Assert.AreEqual(
            "documento da fase 2",
            criptografia.Decifrar(PropositosCriptografia.Documento, AtendimentoId, antigo));

        AssertarProtecao(() => criptografia.DecifrarComRegistro(
            PropositosCriptografia.Documento,
            AtendimentoId,
            Guid.NewGuid(),
            antigo));
    }

    private static LancamentoFinanceiro Lancamento(string tipo, decimal valor) =>
        new()
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Valor = valor
        };

    private static (ICriptografiaCampos Criptografia, string Chave) Criar()
    {
        var chaveV1 = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var options = Options.Create(new ProtecaoDadosOptions
        {
            VersaoAtiva = "v1",
            Chaves = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["v1"] = chaveV1
            }
        });

        return (new CriptografiaCamposAesGcm(options, new ContextoAplicacao("Testing")), chaveV1);
    }

    private static void AssertarProtecao(Action acao)
    {
        try
        {
            acao();
            Assert.Fail("A operação deveria falhar de forma fechada.");
        }
        catch (ExcecaoAplicacao excecao)
        {
            Assert.AreEqual(TipoFalha.ProtecaoCampo, excecao.Tipo);
            Assert.AreEqual("field_protection_failure", excecao.Codigo);
        }
    }
}
