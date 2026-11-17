using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Provisionamento;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Provisiona os perfis fixos e cria somente as contas sintéticas ausentes.
/// Contas existentes não têm e-mail, senha, perfis ou estado alterados — em
/// especial, uma conta desabilitada permanece desabilitada.
/// </summary>
public sealed class ProvisionadorContasSinteticas : IProvisionadorContasSinteticas
{
    private readonly UserManager<UsuarioAplicacao> _usuarios;
    private readonly RoleManager<IdentityRole<Guid>> _perfis;
    private readonly EstacionamentoDbContext _db;
    private readonly ILogger<ProvisionadorContasSinteticas> _logger;

    public ProvisionadorContasSinteticas(
        UserManager<UsuarioAplicacao> usuarios,
        RoleManager<IdentityRole<Guid>> perfis,
        EstacionamentoDbContext db,
        ILogger<ProvisionadorContasSinteticas> logger)
    {
        _usuarios = usuarios;
        _perfis = perfis;
        _db = db;
        _logger = logger;
    }

    public async Task<ResultadoProvisionamento> ProvisionarAsync(
        ContasSinteticasOptions contas,
        CancellationToken cancellationToken = default)
    {
        await GarantirPerfisAsync();

        var criadas = 0;
        var preservadas = 0;

        await using var transacao = await _db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var (perfil, conta) in contas.ObterTodas())
        {
            if (conta is null)
            {
                throw new ProvisionamentoException("conta_ausente", perfil);
            }

            var existente = await _usuarios.FindByEmailAsync(conta.Email.Trim());
            if (existente is not null)
            {
                preservadas++;
                continue;
            }

            var usuario = new UsuarioAplicacao
            {
                Id = Guid.NewGuid(),
                UserName = conta.Email.Trim(),
                Email = conta.Email.Trim(),
                EmailConfirmed = true,
                Nome = perfil,
                Ativo = true,
                VersaoAcesso = 1
            };

            var criacao = await _usuarios.CreateAsync(usuario, conta.Senha);
            if (!criacao.Succeeded)
            {
                throw new ProvisionamentoException("criacao_conta_falhou", perfil);
            }

            var atribuicao = await _usuarios.AddToRoleAsync(usuario, perfil);
            if (!atribuicao.Succeeded)
            {
                throw new ProvisionamentoException("atribuicao_perfil_falhou", perfil);
            }

            criadas++;
        }

        await transacao.CommitAsync(cancellationToken);
        _logger.LogInformation(
            "Provisionamento concluído: {ContasCriadas} conta(s) criada(s), {ContasPreservadas} preservada(s).",
            criadas,
            preservadas);

        return new ResultadoProvisionamento(criadas, preservadas);
    }

    private async Task GarantirPerfisAsync()
    {
        foreach (var perfil in Perfis.Sinteticos)
        {
            if (await _perfis.RoleExistsAsync(perfil))
            {
                continue;
            }

            var criacao = await _perfis.CreateAsync(new IdentityRole<Guid>(perfil));
            if (!criacao.Succeeded)
            {
                throw new ProvisionamentoException("criacao_perfil_falhou", perfil);
            }
        }
    }
}
