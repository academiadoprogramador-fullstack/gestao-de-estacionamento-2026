using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Persistencia;

/// <summary>
/// Contexto único de identidade, sessão e auditoria de autenticação. Não contém
/// entidades de vagas, atendimento, tarifas ou financeiro nesta fase.
/// </summary>
public sealed class EstacionamentoDbContext : IdentityDbContext<UsuarioAplicacao, IdentityRole<Guid>, Guid>
{
    public const string NomeConexao = "Estacionamento";

    public EstacionamentoDbContext(DbContextOptions<EstacionamentoDbContext> options)
        : base(options)
    {
    }

    public DbSet<SessaoAutenticacao> SessoesAutenticacao => Set<SessaoAutenticacao>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<EventoAuditoria> EventosAuditoria => Set<EventoAuditoria>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UsuarioAplicacao>(entidade =>
        {
            entidade.Property(usuario => usuario.Nome).HasMaxLength(200).IsRequired();
            entidade.Property(usuario => usuario.Ativo).IsRequired();
            entidade.Property(usuario => usuario.VersaoAcesso).IsRequired();
        });

        builder.Entity<SessaoAutenticacao>(entidade =>
        {
            entidade.ToTable("SessoesAutenticacao");
            entidade.HasKey(sessao => sessao.Id);
            entidade.Property(sessao => sessao.CausaRevogacao).HasMaxLength(64);
            entidade.Property(sessao => sessao.RowVersion).IsRowVersion();
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(sessao => sessao.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
            entidade.HasIndex(sessao => new { sessao.UsuarioId, sessao.RevogadaEmUtc });
            entidade.HasIndex(sessao => sessao.ExpiraEmUtc);
        });

        builder.Entity<RefreshToken>(entidade =>
        {
            entidade.ToTable("RefreshTokens");
            entidade.HasKey(refresh => refresh.Id);
            entidade.Property(refresh => refresh.HashSha256).HasMaxLength(64).IsRequired();
            entidade.Property(refresh => refresh.RowVersion).IsRowVersion();
            entidade.HasOne<SessaoAutenticacao>()
                .WithMany()
                .HasForeignKey(refresh => refresh.SessaoId)
                .OnDelete(DeleteBehavior.Restrict);
            entidade.HasIndex(refresh => refresh.HashSha256).IsUnique();
            entidade.HasIndex(refresh => new { refresh.SessaoId, refresh.RevogadoEmUtc });
        });

        builder.Entity<EventoAuditoria>(entidade =>
        {
            entidade.ToTable("EventosAuditoria");
            entidade.HasKey(evento => evento.Id);
            entidade.Property(evento => evento.Id).ValueGeneratedOnAdd();
            entidade.Property(evento => evento.Tipo).HasMaxLength(64).IsRequired();
            entidade.Property(evento => evento.AtorOpaco).HasMaxLength(128);
            entidade.Property(evento => evento.AlvoOpaco).HasMaxLength(128);
            entidade.Property(evento => evento.Correlacao).HasMaxLength(128);
            entidade.Property(evento => evento.Detalhe).HasMaxLength(256);
            entidade.Property(evento => evento.OcorridoEmUtc).IsRequired();
            entidade.HasIndex(evento => new { evento.Tipo, evento.OcorridoEmUtc });
            entidade.HasIndex(evento => new { evento.AtorOpaco, evento.OcorridoEmUtc });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ImpedirAlteracaoDeAuditoria();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ImpedirAlteracaoDeAuditoria();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// A auditoria é append-only na aplicação: nenhuma operação normal pode
    /// alterar ou excluir eventos já gravados.
    /// </summary>
    private void ImpedirAlteracaoDeAuditoria()
    {
        var alteracoesProibidas = ChangeTracker.Entries<EventoAuditoria>()
            .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (alteracoesProibidas.Count > 0)
        {
            throw new InvalidOperationException("Eventos de auditoria são append-only e não podem ser alterados ou excluídos.");
        }
    }
}
