using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Tempo;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Api.Autenticacao;

/// <summary>
/// Verifica, em cada requisição protegida, se a conta está ativa, a sessão não
/// foi revogada ou expirada e a versão de acesso coincide com o JWT. Qualquer
/// indisponibilidade do SQL resulta em negação (falha fechada).
/// </summary>
public sealed class ValidadorSessaoJwt
{
    private readonly EstacionamentoDbContext _db;
    private readonly IRelogio _relogio;

    public ValidadorSessaoJwt(EstacionamentoDbContext db, IRelogio relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public async Task<bool> ValidarAsync(
        Guid usuarioId,
        Guid sessaoId,
        long versaoAcesso,
        CancellationToken cancellationToken)
    {
        var agora = _relogio.UtcNow;

        return await _db.SessoesAutenticacao
            .AsNoTracking()
            .Where(sessao =>
                sessao.Id == sessaoId &&
                sessao.UsuarioId == usuarioId &&
                sessao.VersaoAcesso == versaoAcesso &&
                sessao.RevogadaEmUtc == null &&
                sessao.ExpiraEmUtc > agora)
            .Join(
                _db.Users.AsNoTracking(),
                sessao => sessao.UsuarioId,
                usuario => usuario.Id,
                (_, usuario) => new { usuario.Ativo, usuario.VersaoAcesso })
            .AnyAsync(registro => registro.Ativo && registro.VersaoAcesso == versaoAcesso, cancellationToken);
    }
}
