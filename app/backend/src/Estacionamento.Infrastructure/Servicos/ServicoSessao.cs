using System.Data;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Autenticacao;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia;
using Estacionamento.Infrastructure.Persistencia.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Casos de uso de login, renovação, consulta e logout com sessão revogável no
/// SQL. Falhas de credencial são sempre genéricas e não enumeram contas.
/// </summary>
public sealed class ServicoSessao : IServicoSessao
{
    private readonly UserManager<UsuarioAplicacao> _usuarios;
    private readonly EstacionamentoDbContext _db;
    private readonly IGeradorTokenJwt _geradorJwt;
    private readonly IGeradorRefresh _geradorRefresh;
    private readonly IServicoAuditoria _auditoria;
    private readonly IRelogio _relogio;
    private readonly AutenticacaoJwtOptions _options;

    public ServicoSessao(
        UserManager<UsuarioAplicacao> usuarios,
        EstacionamentoDbContext db,
        IGeradorTokenJwt geradorJwt,
        IGeradorRefresh geradorRefresh,
        IServicoAuditoria auditoria,
        IRelogio relogio,
        IOptions<AutenticacaoJwtOptions> options)
    {
        _usuarios = usuarios;
        _db = db;
        _geradorJwt = geradorJwt;
        _geradorRefresh = geradorRefresh;
        _auditoria = auditoria;
        _relogio = relogio;
        _options = options.Value;
    }

    public async Task<ResultadoLogin> LoginAsync(
        RequisicaoLogin requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        var agora = _relogio.UtcNow;
        var email = requisicao.Email?.Trim() ?? string.Empty;
        var senha = requisicao.Senha ?? string.Empty;
        var atorConfigurado = PoliticaSessao.GerarAtorOpaco(NormalizadorEmail.Normalizar(email));

        UsuarioAplicacao? usuario = null;
        if (email.Length > 0)
        {
            usuario = await _usuarios.FindByEmailAsync(email);
        }

        if (usuario is null)
        {
            // Consome tempo equivalente para reduzir a diferença observável entre
            // conta inexistente e senha incorreta.
            _usuarios.PasswordHasher.HashPassword(new UsuarioAplicacao(), senha);
            await RegistrarFalhaAsync(TiposEventoAuditoria.LoginFalha, atorConfigurado, null, correlacao,
                "credenciais_invalidas", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        var ator = usuario.Id.ToString();

        if (!usuario.Ativo)
        {
            await RegistrarFalhaAsync(TiposEventoAuditoria.LoginFalha, ator, null, correlacao,
                CausasRevogacao.ContaDesativada, agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        if (await _usuarios.IsLockedOutAsync(usuario))
        {
            await RegistrarFalhaAsync(TiposEventoAuditoria.LoginBloqueado, ator, null, correlacao,
                "conta_bloqueada", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        if (!await _usuarios.CheckPasswordAsync(usuario, senha))
        {
            await _usuarios.AccessFailedAsync(usuario);
            var bloqueada = await _usuarios.IsLockedOutAsync(usuario);
            await RegistrarFalhaAsync(
                bloqueada ? TiposEventoAuditoria.LoginBloqueado : TiposEventoAuditoria.LoginFalha,
                ator, null, correlacao, "credenciais_invalidas", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        await _usuarios.ResetAccessFailedCountAsync(usuario);

        var sessao = new SessaoAutenticacao
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            VersaoAcesso = usuario.VersaoAcesso,
            CriadaEmUtc = agora,
            ExpiraEmUtc = PoliticaSessao.CalcularFimSessao(agora, TimeSpan.FromHours(_options.DuracaoSessaoHoras))
        };

        var refreshPuro = _geradorRefresh.GerarTokenOpaco();
        var refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessaoId = sessao.Id,
            HashSha256 = _geradorRefresh.CalcularHash(refreshPuro),
            CriadoEmUtc = agora,
            ExpiraEmUtc = sessao.ExpiraEmUtc
        };

        var perfis = await _usuarios.GetRolesAsync(usuario);
        var token = _geradorJwt.Emitir(usuario.Id, sessao.Id, usuario.VersaoAcesso, perfis, sessao.ExpiraEmUtc);

        _db.SessoesAutenticacao.Add(sessao);
        _db.RefreshTokens.Add(refresh);
        _auditoria.Adicionar(new EventoAuditoriaAutenticacao(
            TiposEventoAuditoria.LoginSucesso, ator, sessao.Id.ToString(), correlacao, null, agora));
        await _db.SaveChangesAsync(cancellationToken);

        return ResultadoLogin.CriarSucesso(
            token.Valor,
            refreshPuro,
            token.ExpiraEmUtc,
            sessao.ExpiraEmUtc,
            new FuncionarioSessao(usuario.Id, usuario.Nome, usuario.Email ?? string.Empty, perfis.ToArray()));
    }

    public async Task<ResultadoLogin> RenovarAsync(
        string refresh,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refresh))
        {
            return ResultadoLogin.Falha;
        }

        var agora = _relogio.UtcNow;
        var hash = _geradorRefresh.CalcularHash(refresh);
        var tokenAtual = await _db.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.HashSha256 == hash, cancellationToken);

        if (tokenAtual is null)
        {
            await RegistrarFalhaAsync(TiposEventoAuditoria.LoginFalha, null, null, correlacao,
                "refresh_invalido", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        var sessao = await _db.SessoesAutenticacao
            .SingleOrDefaultAsync(s => s.Id == tokenAtual.SessaoId, cancellationToken);

        if (sessao is null)
        {
            return ResultadoLogin.Falha;
        }

        if (tokenAtual.ConsumidoEmUtc.HasValue)
        {
            await RevogarFamiliaAsync(sessao, CausasRevogacao.ReusoRefresh, agora, cancellationToken);
            await RegistrarFalhaAsync(TiposEventoAuditoria.ReusoRefresh, sessao.UsuarioId.ToString(),
                sessao.Id.ToString(), correlacao, "refresh_reutilizado", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        if (tokenAtual.RevogadoEmUtc.HasValue ||
            tokenAtual.ExpiraEmUtc <= agora ||
            !PoliticaSessao.SessaoRenovavel(agora, sessao.ExpiraEmUtc, sessao.RevogadaEmUtc.HasValue))
        {
            await RegistrarFalhaAsync(TiposEventoAuditoria.LoginFalha, sessao.UsuarioId.ToString(),
                sessao.Id.ToString(), correlacao, "refresh_invalido", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        var usuario = await _usuarios.FindByIdAsync(sessao.UsuarioId.ToString());
        if (usuario is null || !usuario.Ativo || usuario.VersaoAcesso != sessao.VersaoAcesso)
        {
            await RevogarFamiliaAsync(sessao, CausasRevogacao.AlteracaoAcesso, agora, cancellationToken);
            await RegistrarFalhaAsync(TiposEventoAuditoria.RevogacaoSessao, sessao.UsuarioId.ToString(),
                sessao.Id.ToString(), correlacao, "versao_acesso_invalida", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        var perfis = await _usuarios.GetRolesAsync(usuario);
        var novoTokemId = Guid.NewGuid();
        var novoRefreshPuro = _geradorRefresh.GerarTokenOpaco();

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // Consumo atômico: somente uma requisição consegue marcar o refresh
        // como consumido. Qualquer outra observa zero linhas afetadas.
        var afetados = await _db.RefreshTokens
            .Where(t => t.Id == tokenAtual.Id && t.ConsumidoEmUtc == null && t.RevogadoEmUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.ConsumidoEmUtc, agora)
                    .SetProperty(t => t.SubstituidoPorId, novoTokemId),
                cancellationToken);

        if (afetados == 0)
        {
            await transacao.RollbackAsync(cancellationToken);
            await RevogarFamiliaAsync(sessao, CausasRevogacao.ReusoRefresh, agora, cancellationToken);
            await RegistrarFalhaAsync(TiposEventoAuditoria.ReusoRefresh, sessao.UsuarioId.ToString(),
                sessao.Id.ToString(), correlacao, "refresh_reutilizado", agora, cancellationToken);
            return ResultadoLogin.Falha;
        }

        var novoRefresh = new RefreshToken
        {
            Id = novoTokemId,
            SessaoId = sessao.Id,
            HashSha256 = _geradorRefresh.CalcularHash(novoRefreshPuro),
            CriadoEmUtc = agora,
            ExpiraEmUtc = sessao.ExpiraEmUtc
        };

        var token = _geradorJwt.Emitir(usuario.Id, sessao.Id, usuario.VersaoAcesso, perfis, sessao.ExpiraEmUtc);

        _db.RefreshTokens.Add(novoRefresh);
        _auditoria.Adicionar(new EventoAuditoriaAutenticacao(
            TiposEventoAuditoria.RenovacaoSessao, usuario.Id.ToString(), sessao.Id.ToString(), correlacao, null, agora));
        await _db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);

        return ResultadoLogin.CriarSucesso(
            token.Valor,
            novoRefreshPuro,
            token.ExpiraEmUtc,
            sessao.ExpiraEmUtc,
            new FuncionarioSessao(usuario.Id, usuario.Nome, usuario.Email ?? string.Empty, perfis.ToArray()));
    }

    public async Task<SessaoAtual?> ObterSessaoAsync(Guid sessaoId, CancellationToken cancellationToken = default)
    {
        var agora = _relogio.UtcNow;
        var sessao = await _db.SessoesAutenticacao
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessaoId, cancellationToken);

        if (sessao is null || sessao.RevogadaEmUtc.HasValue || sessao.ExpiraEmUtc <= agora)
        {
            return null;
        }

        var usuario = await _usuarios.FindByIdAsync(sessao.UsuarioId.ToString());
        if (usuario is null || !usuario.Ativo || usuario.VersaoAcesso != sessao.VersaoAcesso)
        {
            return null;
        }

        var perfis = await _usuarios.GetRolesAsync(usuario);
        return new SessaoAtual(
            sessao.Id,
            new FuncionarioSessao(usuario.Id, usuario.Nome, usuario.Email ?? string.Empty, perfis.ToArray()),
            sessao.VersaoAcesso,
            sessao.CriadaEmUtc,
            sessao.ExpiraEmUtc);
    }

    public async Task<bool> LogoutAsync(Guid sessaoId, string? correlacao, CancellationToken cancellationToken = default)
    {
        var agora = _relogio.UtcNow;
        var sessao = await _db.SessoesAutenticacao
            .SingleOrDefaultAsync(s => s.Id == sessaoId, cancellationToken);

        if (sessao is null)
        {
            return false;
        }

        if (!sessao.RevogadaEmUtc.HasValue)
        {
            sessao.RevogadaEmUtc = agora;
            sessao.CausaRevogacao = CausasRevogacao.Logout;
        }

        await _db.RefreshTokens
            .Where(t => t.SessaoId == sessaoId && t.RevogadoEmUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevogadoEmUtc, agora),
                cancellationToken);

        _auditoria.Adicionar(new EventoAuditoriaAutenticacao(
            TiposEventoAuditoria.Logout, sessao.UsuarioId.ToString(), sessao.Id.ToString(), correlacao, null, agora));
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task RevogarFamiliaAsync(
        SessaoAutenticacao sessao,
        string causa,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        if (!sessao.RevogadaEmUtc.HasValue)
        {
            sessao.RevogadaEmUtc = agora;
            sessao.CausaRevogacao = causa;
        }

        await _db.RefreshTokens
            .Where(t => t.SessaoId == sessao.Id && t.RevogadoEmUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevogadoEmUtc, agora),
                cancellationToken);

        _auditoria.Adicionar(new EventoAuditoriaAutenticacao(
            TiposEventoAuditoria.RevogacaoSessao, sessao.UsuarioId.ToString(), sessao.Id.ToString(),
            null, causa, agora));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RegistrarFalhaAsync(
        string tipo,
        string? atorOpaco,
        string? alvoOpaco,
        string? correlacao,
        string detalhe,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        await _auditoria.RegistrarAsync(
            new EventoAuditoriaAutenticacao(tipo, atorOpaco, alvoOpaco, correlacao, detalhe, agora),
            cancellationToken);
    }
}
