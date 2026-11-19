using System.Text.Json;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Retiradas;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Domain.Retiradas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Apoio comum de validação e replay para os comandos da Fase 3. O resultado
/// persistido é um JSON mínimo, sem documento, vistoria, justificativa ou corpo
/// da requisição.
/// </summary>
internal static class ApoioIdempotenciaFase3
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    public static void GarantirChave(Guid chaveIdempotente)
    {
        if (chaveIdempotente == Guid.Empty)
        {
            throw ExcecaoAplicacao.Validacao(
                "idempotency_key_invalida",
                "O cabeçalho Idempotency-Key deve ser um UUID válido.");
        }
    }

    public static string Serializar<T>(T resultado) =>
        JsonSerializer.Serialize(resultado, OpcoesJson);

    /// <summary>
    /// Lê o resultado confirmado de um replay. Uma reserva sem resultado indica
    /// que a tentativa anterior ainda não foi confirmada.
    /// </summary>
    public static T Ler<T>(ResultadoIdempotente resultado)
    {
        if (resultado.ResultadoJson is null)
        {
            throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A tentativa anterior ainda não possui resultado confirmado.");
        }

        return JsonSerializer.Deserialize<T>(resultado.ResultadoJson, OpcoesJson)
            ?? throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A tentativa anterior ainda não possui resultado confirmado.");
    }

    public static void VerificarHash(ResultadoIdempotente resultado, byte[] hashAtual)
    {
        if (!resultado.HashSha256.SequenceEqual(hashAtual))
        {
            throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A mesma chave idempotente foi usada com conteúdo diferente.");
        }
    }

    public static void GarantirValorMonetario(decimal valor, string codigo, string mensagem)
    {
        if (valor <= 0 || valor != decimal.Round(valor, 2, MidpointRounding.AwayFromZero))
        {
            throw ExcecaoAplicacao.Validacao(codigo, mensagem);
        }
    }

    public static string NormalizarTextoObrigatorio(
        string? texto,
        int tamanhoMaximo,
        string codigo,
        string mensagem)
    {
        var normalizado = texto?.Trim() ?? string.Empty;
        if (normalizado.Length == 0 || normalizado.Length > tamanhoMaximo)
        {
            throw ExcecaoAplicacao.Validacao(codigo, mensagem);
        }

        return normalizado;
    }
}

/// <summary>
/// Fila paginada e detalhe do pedido de retirada, com projeções explícitas por
/// perfil. Listagens nunca incluem conferência, finanças ou autorizações ao
/// Manobrista, e justificativas nunca aparecem em DTOs comuns.
/// </summary>
public sealed class ServicoConsultaRetiradas : IServicoConsultaRetiradas
{
    private readonly EstacionamentoDbContext _db;

    public ServicoConsultaRetiradas(EstacionamentoDbContext db)
    {
        _db = db;
    }

    public async Task<object> ListarAsync(
        AtorAtual ator,
        string? estado,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        var filtro = NormalizarFiltro(estado);

        var consulta =
            from pedido in _db.PedidosRetirada.AsNoTracking()
            join atendimento in _db.Atendimentos.AsNoTracking()
                on pedido.AtendimentoId equals atendimento.Id
            join ocupacao in _db.Ocupacoes.AsNoTracking()
                on atendimento.Id equals ocupacao.AtendimentoId into ocupacoes
            from ocupacao in ocupacoes.Where(registro => registro.FimUtc == null).DefaultIfEmpty()
            join vaga in _db.Vagas.AsNoTracking()
                on ocupacao.VagaId equals vaga.Id into vagas
            from vaga in vagas.DefaultIfEmpty()
            join usuario in _db.Users.AsNoTracking()
                on pedido.BuscaPorId equals usuario.Id into usuarios
            from usuario in usuarios.DefaultIfEmpty()
            where filtro == null || pedido.Estado == filtro
            orderby pedido.SolicitadoEmUtc descending, pedido.Id descending
            select new LinhaRetirada(
                pedido.Id,
                pedido.AtendimentoId,
                atendimento.IdentificadorPublico ?? string.Empty,
                atendimento.Placa,
                atendimento.Modelo,
                vaga == null ? string.Empty : vaga.Codigo,
                pedido.Estado,
                atendimento.EntradaUtc,
                pedido.SolicitadoEmUtc,
                pedido.BuscaIniciadaEmUtc,
                usuario == null ? null : usuario.Nome,
                pedido.RowVersion,
                pedido.Conferencia);

        var total = await consulta.CountAsync(cancellationToken);
        var linhas = await consulta
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(cancellationToken);

        if (!ator.EhOperador)
        {
            return new Pagina<RetiradaFilaOperacional>(
                linhas.Select(ProjetarOperacional).ToList(),
                pagina,
                tamanho,
                total);
        }

        var financeiro = await MontarFinanceiroAsync(linhas, cancellationToken);
        return new Pagina<RetiradaFilaFinanceira>(
            linhas.Select(linha =>
            {
                var dados = financeiro[linha.PedidoId];
                return new RetiradaFilaFinanceira(
                    linha.PedidoId,
                    linha.AtendimentoId,
                    linha.Identificador,
                    linha.Placa,
                    linha.Modelo,
                    linha.VagaCodigo,
                    linha.Estado,
                    linha.EntradaUtc,
                    linha.SolicitadoEmUtc,
                    linha.BuscaIniciadaEmUtc,
                    linha.ResponsavelBusca,
                    linha.VersaoPedido,
                    dados.Cobranca.TotalOriginal,
                    dados.Composicao.PagoLiquido,
                    dados.Composicao.Saldo,
                    dados.Documental,
                    dados.Financeira);
            }).ToList(),
            pagina,
            tamanho,
            total);
    }

    public async Task<object> ObterAsync(
        AtorAtual ator,
        Guid pedidoId,
        CancellationToken cancellationToken = default)
    {
        var linha = await CarregarLinhaAsync(pedidoId, cancellationToken);

        if (!ator.EhOperador)
        {
            return ProjetarOperacional(linha);
        }

        var financeiro = await MontarFinanceiroAsync([linha], cancellationToken);
        var dados = financeiro[pedidoId];
        var lancamentos = dados.Lancamentos;

        return new RetiradaDetalheFinanceira(
            linha.PedidoId,
            linha.AtendimentoId,
            linha.Identificador,
            linha.Placa,
            linha.Modelo,
            linha.VagaCodigo,
            linha.Estado,
            linha.EntradaUtc,
            linha.SolicitadoEmUtc,
            linha.BuscaIniciadaEmUtc,
            linha.ResponsavelBusca,
            linha.VersaoPedido,
            linha.Conferencia,
            dados.Cobranca.DuracaoTicks,
            dados.Cobranca.Diarias,
            dados.TarifaVersao,
            dados.Cobranca.TarifaValorSnapshot,
            dados.Cobranca.TotalOriginal,
            dados.Composicao.Descontos,
            dados.Composicao.Pagamentos,
            dados.Composicao.Estornos,
            dados.Composicao.Cobrado,
            dados.Composicao.PagoLiquido,
            dados.Composicao.Saldo,
            dados.RevisaoRazao,
            lancamentos,
            dados.DocumentalResumo,
            dados.FinanceiraResumo);
    }

    public async Task<object?> ObterResumoPorAtendimentoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        CancellationToken cancellationToken = default)
    {
        var pedidoId = await _db.PedidosRetirada
            .AsNoTracking()
            .Where(pedido => pedido.AtendimentoId == atendimentoId)
            .Select(pedido => (Guid?)pedido.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (pedidoId is null)
        {
            return null;
        }

        var detalhe = await ObterAsync(ator, pedidoId.Value, cancellationToken);

        if (detalhe is RetiradaDetalheFinanceira financeiro)
        {
            return new Estacionamento.Application.Atendimentos.ResumoRetiradaFinanceiro(
                financeiro.Id,
                financeiro.Estado,
                financeiro.SolicitadoEmUtc,
                financeiro.BuscaIniciadaEmUtc,
                financeiro.ResponsavelBusca,
                financeiro.Conferencia,
                financeiro.Diarias,
                financeiro.TotalOriginal,
                financeiro.Cobrado,
                financeiro.PagoLiquido,
                financeiro.Saldo,
                financeiro.RevisaoRazao,
                financeiro.AutorizacaoDocumental.Vigencia,
                financeiro.AutorizacaoFinanceira.Vigencia);
        }

        var operacional = (RetiradaDetalheOperacional)detalhe;
        return new Estacionamento.Application.Atendimentos.ResumoRetiradaOperacional(
            operacional.Id,
            operacional.Estado,
            operacional.SolicitadoEmUtc,
            operacional.BuscaIniciadaEmUtc,
            operacional.ResponsavelBusca);
    }

    private async Task<LinhaRetirada> CarregarLinhaAsync(
        Guid pedidoId,
        CancellationToken cancellationToken)
    {
        var linha = await (
                from pedido in _db.PedidosRetirada.AsNoTracking()
                join atendimento in _db.Atendimentos.AsNoTracking()
                    on pedido.AtendimentoId equals atendimento.Id
                join ocupacao in _db.Ocupacoes.AsNoTracking()
                    on atendimento.Id equals ocupacao.AtendimentoId into ocupacoes
                from ocupacao in ocupacoes.Where(registro => registro.FimUtc == null).DefaultIfEmpty()
                join vaga in _db.Vagas.AsNoTracking()
                    on ocupacao.VagaId equals vaga.Id into vagas
                from vaga in vagas.DefaultIfEmpty()
                join usuario in _db.Users.AsNoTracking()
                    on pedido.BuscaPorId equals usuario.Id into usuarios
                from usuario in usuarios.DefaultIfEmpty()
                where pedido.Id == pedidoId
                select new LinhaRetirada(
                    pedido.Id,
                    pedido.AtendimentoId,
                    atendimento.IdentificadorPublico ?? string.Empty,
                    atendimento.Placa,
                    atendimento.Modelo,
                    vaga == null ? string.Empty : vaga.Codigo,
                    pedido.Estado,
                    atendimento.EntradaUtc,
                    pedido.SolicitadoEmUtc,
                    pedido.BuscaIniciadaEmUtc,
                    usuario == null ? null : usuario.Nome,
                    pedido.RowVersion,
                    pedido.Conferencia))
            .SingleOrDefaultAsync(cancellationToken);

        return linha ?? throw ExcecaoAplicacao.NaoEncontrado(
            "withdrawal_not_found",
            "Pedido de retirada não encontrado.");
    }

    private async Task<Dictionary<Guid, DadosFinanceiros>> MontarFinanceiroAsync(
        IReadOnlyList<LinhaRetirada> linhas,
        CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<Guid, DadosFinanceiros>();
        if (linhas.Count == 0)
        {
            return resultado;
        }

        var pedidoIds = linhas.Select(linha => linha.PedidoId).ToArray();
        var atendimentoIds = linhas.Select(linha => linha.AtendimentoId).Distinct().ToArray();

        var cobrancas = await _db.Cobrancas
            .AsNoTracking()
            .Where(cobranca => pedidoIds.Contains(cobranca.PedidoId))
            .ToDictionaryAsync(cobranca => cobranca.PedidoId, cancellationToken);

        var tarifas = await _db.TarifasDiarias
            .AsNoTracking()
            .Where(tarifa => cobrancas.Values.Select(cobranca => cobranca.TarifaVersaoId).Contains(tarifa.Id))
            .ToDictionaryAsync(tarifa => tarifa.Id, tarifa => tarifa.Versao, cancellationToken);

        var razoes = await _db.RazoesAtendimento
            .AsNoTracking()
            .Where(razao => atendimentoIds.Contains(razao.AtendimentoId))
            .ToDictionaryAsync(razao => razao.AtendimentoId, cancellationToken);

        var lancamentos = await _db.LancamentosFinanceiros
            .AsNoTracking()
            .Where(lancamento => atendimentoIds.Contains(lancamento.AtendimentoId))
            .ToListAsync(cancellationToken);

        var nomes = await _db.Users
            .AsNoTracking()
            .Where(usuario => lancamentos.Select(lancamento => lancamento.AtorId).Contains(usuario.Id))
            .ToDictionaryAsync(usuario => usuario.Id, usuario => usuario.Nome, cancellationToken);

        var concessoes = await _db.ConcessoesAutorizacao
            .AsNoTracking()
            .Where(concessao => pedidoIds.Contains(concessao.PedidoId))
            .ToListAsync(cancellationToken);

        var autoresConcessao = await _db.Users
            .AsNoTracking()
            .Where(usuario => concessoes.Select(concessao => concessao.ConcedidaPorId).Contains(usuario.Id))
            .ToDictionaryAsync(usuario => usuario.Id, usuario => usuario.Nome, cancellationToken);

        var revogacoes = await _db.RevogacoesAutorizacao
            .AsNoTracking()
            .Where(revogacao => concessoes.Select(concessao => concessao.Id).Contains(revogacao.ConcessaoId))
            .ToDictionaryAsync(revogacao => revogacao.ConcessaoId, cancellationToken);

        foreach (var linha in linhas)
        {
            var cobranca = cobrancas[linha.PedidoId];
            var lancamentosAtendimento = lancamentos
                .Where(lancamento => lancamento.AtendimentoId == linha.AtendimentoId)
                .OrderByDescending(lancamento => lancamento.OcorridoEmUtc)
                .ThenByDescending(lancamento => lancamento.Id)
                .ToArray();

            var composicao = ComposicaoSaldo.Calcular(
                cobranca.TotalOriginal,
                lancamentosAtendimento);

            var revisaoRazao = razoes.TryGetValue(linha.AtendimentoId, out var razao)
                ? razao.Revisao
                : 1;

            var porTipo = concessoes
                .Where(concessao => concessao.PedidoId == linha.PedidoId)
                .GroupBy(concessao => concessao.Tipo)
                .ToDictionary(grupo => grupo.Key, grupo => grupo
                    .OrderBy(concessao => concessao.ConcedidaEmUtc)
                    .ThenBy(concessao => concessao.Id)
                    .ToArray());

            var documental = MontarAutorizacao(
                TipoAutorizacaoRetirada.Documental,
                porTipo,
                revogacoes,
                revisaoRazao,
                autoresConcessao);
            var financeira = MontarAutorizacao(
                TipoAutorizacaoRetirada.Financeira,
                porTipo,
                revogacoes,
                revisaoRazao,
                autoresConcessao);

            resultado[linha.PedidoId] = new DadosFinanceiros(
                cobranca,
                composicao,
                revisaoRazao,
                tarifas.TryGetValue(cobranca.TarifaVersaoId, out var versao) ? versao : 0,
                lancamentosAtendimento
                    .Select(lancamento => new LancamentoResumo(
                        lancamento.Id,
                        lancamento.Tipo,
                        lancamento.Valor,
                        lancamento.FormaPagamento,
                        lancamento.PagamentoOriginalId,
                        nomes.TryGetValue(lancamento.AtorId, out var nome) ? nome : "—",
                        lancamento.OcorridoEmUtc,
                        lancamento.JustificativaCifrada is not null))
                    .ToList(),
                documental.Vigencia,
                financeira.Vigencia,
                documental,
                financeira);
        }

        return resultado;
    }

    private static AutorizacaoResumo MontarAutorizacao(
        string tipo,
        IReadOnlyDictionary<string, ConcessaoAutorizacao[]> porTipo,
        IReadOnlyDictionary<Guid, RevogacaoAutorizacao> revogacoes,
        int revisaoRazao,
        IReadOnlyDictionary<Guid, string> autores)
    {
        if (!porTipo.TryGetValue(tipo, out var concessoes) || concessoes.Length == 0)
        {
            return new AutorizacaoResumo(tipo, null, VigenciaAutorizacao.NaoConcedida, null, null, null, false);
        }

        var ultima = concessoes[^1];
        var vigencia = ComposicaoSaldo.DerivarVigencia(
            ultima,
            ehUltimaDoTipo: true,
            revogacoes.ContainsKey(ultima.Id),
            revisaoRazao);

        return new AutorizacaoResumo(
            tipo,
            ultima.Id,
            vigencia,
            ultima.RevisaoReferencia,
            autores.TryGetValue(ultima.ConcedidaPorId, out var autor) ? autor : "—",
            ultima.ConcedidaEmUtc,
            ultima.JustificativaCifrada.Length > 0);
    }

    private static RetiradaFilaOperacional ProjetarOperacional(LinhaRetirada linha) =>
        new(
            linha.PedidoId,
            linha.AtendimentoId,
            linha.Identificador,
            linha.Placa,
            linha.Modelo,
            linha.VagaCodigo,
            linha.Estado,
            linha.EntradaUtc,
            linha.SolicitadoEmUtc,
            linha.BuscaIniciadaEmUtc,
            linha.ResponsavelBusca,
            linha.VersaoPedido);

    private static string? NormalizarFiltro(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado) || estado.Equals("todos", StringComparison.Ordinal))
        {
            return null;
        }

        if (!EstadoAtendimento.ProduzidosNaFase3.Contains(estado))
        {
            throw ExcecaoAplicacao.Validacao(
                "filtro_invalido",
                "O filtro de estado deve ser aguardando ou em busca.");
        }

        return estado;
    }

    private sealed record LinhaRetirada(
        Guid PedidoId,
        Guid AtendimentoId,
        string Identificador,
        string Placa,
        string Modelo,
        string VagaCodigo,
        string Estado,
        DateTimeOffset EntradaUtc,
        DateTimeOffset SolicitadoEmUtc,
        DateTimeOffset? BuscaIniciadaEmUtc,
        string? ResponsavelBusca,
        byte[] VersaoPedido,
        string Conferencia);

    private sealed record DadosFinanceiros(
        Cobranca Cobranca,
        ResultadoComposicaoSaldo Composicao,
        int RevisaoRazao,
        int TarifaVersao,
        IReadOnlyList<LancamentoResumo> Lancamentos,
        string Documental,
        string Financeira,
        AutorizacaoResumo DocumentalResumo,
        AutorizacaoResumo FinanceiraResumo);
}
