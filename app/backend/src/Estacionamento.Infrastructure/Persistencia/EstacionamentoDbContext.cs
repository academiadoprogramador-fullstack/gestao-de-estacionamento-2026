using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Auditoria;
using Estacionamento.Domain.Entrega;
using Estacionamento.Domain.Fotos;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Domain.Retiradas;
using Estacionamento.Domain.Tarifas;
using Estacionamento.Domain.Vagas;

namespace Estacionamento.Infrastructure.Persistencia;

/// <summary>
/// Contexto único de identidade, sessão, auditoria e domínio operacional da
/// Fase 2. Consultas de lista usam projeções explícitas; comandos usam
/// rastreamento e transação na mesma fronteira relacional.
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

    public DbSet<Vaga> Vagas => Set<Vaga>();

    public DbSet<TarifaDiaria> TarifasDiarias => Set<TarifaDiaria>();

    public DbSet<ConfiguracaoTarifa> ConfiguracaoTarifa => Set<ConfiguracaoTarifa>();

    public DbSet<Atendimento> Atendimentos => Set<Atendimento>();

    public DbSet<VistoriaEntrada> VistoriasEntrada => Set<VistoriaEntrada>();

    public DbSet<Ocupacao> Ocupacoes => Set<Ocupacao>();

    public DbSet<EventoAtendimento> EventosAtendimento => Set<EventoAtendimento>();

    public DbSet<ResultadoIdempotente> ResultadosIdempotentes => Set<ResultadoIdempotente>();

    public DbSet<EventoAuditoriaNegocio> EventosAuditoriaNegocio => Set<EventoAuditoriaNegocio>();

    public DbSet<PedidoRetirada> PedidosRetirada => Set<PedidoRetirada>();

    public DbSet<Cobranca> Cobrancas => Set<Cobranca>();

    public DbSet<RazaoAtendimento> RazoesAtendimento => Set<RazaoAtendimento>();

    public DbSet<LancamentoFinanceiro> LancamentosFinanceiros => Set<LancamentoFinanceiro>();

    public DbSet<ConcessaoAutorizacao> ConcessoesAutorizacao => Set<ConcessaoAutorizacao>();

    public DbSet<RevogacaoAutorizacao> RevogacoesAutorizacao => Set<RevogacaoAutorizacao>();

    public DbSet<VistoriaSaida> VistoriasSaida => Set<VistoriaSaida>();

    public DbSet<EntregaVeiculo> EntregasVeiculo => Set<EntregaVeiculo>();

    public DbSet<ConsumoAutorizacao> ConsumosAutorizacao => Set<ConsumoAutorizacao>();

    public DbSet<FotoVistoria> FotosVistoria => Set<FotoVistoria>();

    public DbSet<ConsumoFotosMensal> ConsumosFotosMensais => Set<ConsumoFotosMensal>();

    public DbSet<TarefaFoto> TarefasFotos => Set<TarefaFoto>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UsuarioAplicacao>(entidade =>
        {
            entidade.Property(usuario => usuario.Nome).HasMaxLength(200).IsRequired();
            entidade.Property(usuario => usuario.Ativo).IsRequired();
            entidade.Property(usuario => usuario.VersaoAcesso).IsRequired();
            entidade.Property(usuario => usuario.RowVersion).IsRowVersion();
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

        ConfigurarVagas(builder);
        ConfigurarTarifas(builder);
        ConfigurarAtendimentos(builder);
        ConfigurarIdempotencia(builder);
        ConfigurarAuditoriaNegocio(builder);
        ConfigurarRetiradas(builder);
        ConfigurarEntrega(builder);
        ConfigurarFotos(builder);

        // Sequência usada exclusivamente pelo identificador público do
        // atendimento; não é chave primária nem expõe a chave interna.
        builder.HasSequence<long>("SequenciaIdentificadorAtendimento")
            .StartsAt(1)
            .IncrementsBy(1);
    }

    private static void ConfigurarVagas(ModelBuilder builder)
    {
        builder.Entity<Vaga>(entidade =>
        {
            entidade.ToTable("Vagas");
            entidade.HasKey(vaga => vaga.Id);
            entidade.Property(vaga => vaga.Codigo).HasMaxLength(CodigoVaga.TamanhoMaximo).IsRequired();
            entidade.Property(vaga => vaga.CodigoNormalizado).HasMaxLength(CodigoVaga.TamanhoMaximo).IsRequired();
            entidade.Property(vaga => vaga.CriadoEmUtc).IsRequired();
            entidade.Property(vaga => vaga.RowVersion).IsRowVersion();
            entidade.HasIndex(vaga => vaga.CodigoNormalizado).IsUnique();
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(vaga => vaga.CriadoPorId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigurarTarifas(ModelBuilder builder)
    {
        builder.Entity<TarifaDiaria>(entidade =>
        {
            entidade.ToTable("TarifasDiarias", tabela =>
                tabela.HasCheckConstraint("CK_TarifasDiarias_ValorPositivo", "[Valor] > 0"));
            entidade.HasKey(tarifa => tarifa.Id);
            entidade.Property(tarifa => tarifa.Valor).HasColumnType("decimal(18,2)").IsRequired();
            entidade.Property(tarifa => tarifa.Versao).IsRequired();
            entidade.Property(tarifa => tarifa.PublicadoEmUtc).IsRequired();
            entidade.HasIndex(tarifa => tarifa.Versao).IsUnique();
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(tarifa => tarifa.PublicadoPorId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<ConfiguracaoTarifa>(entidade =>
        {
            entidade.ToTable("ConfiguracaoTarifa");
            entidade.HasKey(configuracao => configuracao.Id);
            entidade.Property(configuracao => configuracao.Id).ValueGeneratedNever();
            entidade.Property(configuracao => configuracao.RowVersion).IsRowVersion();
            entidade.HasOne<TarifaDiaria>()
                .WithMany()
                .HasForeignKey(configuracao => configuracao.VersaoCorrenteId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasData(new Estacionamento.Domain.Tarifas.ConfiguracaoTarifa
            {
                Id = Estacionamento.Domain.Tarifas.ConfiguracaoTarifa.IdSingleton
            });
        });
    }

    private static void ConfigurarAtendimentos(ModelBuilder builder)
    {
        builder.Entity<Atendimento>(entidade =>
        {
            entidade.ToTable("Atendimentos");
            entidade.HasKey(atendimento => atendimento.Id);
            entidade.Property(atendimento => atendimento.IdentificadorPublico).HasMaxLength(32);
            entidade.Property(atendimento => atendimento.Placa).HasMaxLength(NormalizadorPlaca.TamanhoMaximo).IsRequired();
            entidade.Property(atendimento => atendimento.Modelo).HasMaxLength(LimitesTextoAtendimento.Modelo).IsRequired();
            entidade.Property(atendimento => atendimento.Titular).HasMaxLength(LimitesTextoAtendimento.Titular).IsRequired();
            entidade.Property(atendimento => atendimento.DocumentoTitularCifrado).HasColumnType("varbinary(max)").IsRequired();
            entidade.Property(atendimento => atendimento.TarifaValorSnapshot).HasColumnType("decimal(18,2)").IsRequired();
            entidade.Property(atendimento => atendimento.Estado).HasMaxLength(32).IsRequired();
            entidade.Property(atendimento => atendimento.RowVersion).IsRowVersion();
            entidade.HasIndex(atendimento => atendimento.IdentificadorPublico)
                .IsUnique()
                .HasFilter("[IdentificadorPublico] IS NOT NULL");
            entidade.HasIndex(atendimento => new { atendimento.Placa, atendimento.EntradaUtc });
            entidade.HasIndex(atendimento => atendimento.EntradaUtc);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(atendimento => atendimento.OperadorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<TarifaDiaria>()
                .WithMany()
                .HasForeignKey(atendimento => atendimento.TarifaVersaoId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<VistoriaEntrada>(entidade =>
        {
            entidade.ToTable("VistoriasEntrada");
            entidade.HasKey(vistoria => vistoria.AtendimentoId);
            entidade.Property(vistoria => vistoria.TextoCifrado).HasColumnType("varbinary(max)").IsRequired();
            entidade.Property(vistoria => vistoria.CriadoEmUtc).IsRequired();
            entidade.HasOne<Atendimento>()
                .WithOne()
                .HasForeignKey<VistoriaEntrada>(vistoria => vistoria.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(vistoria => vistoria.AtorId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<Ocupacao>(entidade =>
        {
            entidade.ToTable("Ocupacoes");
            entidade.HasKey(ocupacao => ocupacao.Id);
            entidade.Property(ocupacao => ocupacao.InicioUtc).IsRequired();
            entidade.HasOne<Vaga>()
                .WithMany()
                .HasForeignKey(ocupacao => ocupacao.VagaId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(ocupacao => ocupacao.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(ocupacao => ocupacao.ResponsavelId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(ocupacao => ocupacao.VagaId)
                .IsUnique()
                .HasFilter("[FimUtc] IS NULL");
            entidade.HasIndex(ocupacao => ocupacao.AtendimentoId)
                .IsUnique()
                .HasFilter("[FimUtc] IS NULL");
        });

        builder.Entity<EventoAtendimento>(entidade =>
        {
            entidade.ToTable("EventosAtendimento");
            entidade.HasKey(evento => evento.Id);
            entidade.Property(evento => evento.Id).ValueGeneratedOnAdd();
            entidade.Property(evento => evento.Tipo).HasMaxLength(64).IsRequired();
            entidade.Property(evento => evento.AtorOpaco).HasMaxLength(64).IsRequired();
            entidade.Property(evento => evento.Correlacao).HasMaxLength(128);
            entidade.Property(evento => evento.DetalheOpaco).HasMaxLength(256);
            entidade.Property(evento => evento.OcorridoEmUtc).IsRequired();
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(evento => evento.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(evento => new { evento.AtendimentoId, evento.OcorridoEmUtc });
        });
    }

    private static void ConfigurarIdempotencia(ModelBuilder builder)
    {
        builder.Entity<ResultadoIdempotente>(entidade =>
        {
            entidade.ToTable("ResultadosIdempotentes");
            entidade.HasKey(resultado => resultado.Id);
            entidade.Property(resultado => resultado.Operacao).HasMaxLength(64).IsRequired();
            entidade.Property(resultado => resultado.HashSha256).HasColumnType("binary(32)").IsRequired();
            entidade.Property(resultado => resultado.CriadoEmUtc).IsRequired();
            entidade.Property(resultado => resultado.IdentificadorPublico).HasMaxLength(32);
            entidade.Property(resultado => resultado.Estado).HasMaxLength(32);
            entidade.Property(resultado => resultado.Location).HasMaxLength(256);
            entidade.Property(resultado => resultado.ResultadoJson).HasColumnType("nvarchar(max)");
            entidade.Property(resultado => resultado.CodigoResposta);
            entidade.HasIndex(resultado => new
            {
                resultado.AtorId,
                resultado.Operacao,
                resultado.Chave
            }).IsUnique();
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(resultado => resultado.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<Vaga>()
                .WithMany()
                .HasForeignKey(resultado => resultado.VagaId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigurarAuditoriaNegocio(ModelBuilder builder)
    {
        builder.Entity<EventoAuditoriaNegocio>(entidade =>
        {
            entidade.ToTable("EventosAuditoriaNegocio");
            entidade.HasKey(evento => evento.Id);
            entidade.Property(evento => evento.Id).ValueGeneratedOnAdd();
            entidade.Property(evento => evento.Tipo).HasMaxLength(64).IsRequired();
            entidade.Property(evento => evento.AlvoTipo).HasMaxLength(32).IsRequired();
            entidade.Property(evento => evento.OcorridoEmUtc).IsRequired();
            entidade.Property(evento => evento.Correlacao).HasMaxLength(128);
            entidade.Property(evento => evento.Detalhe).HasMaxLength(256);
            entidade.HasIndex(evento => new { evento.AlvoTipo, evento.AlvoId, evento.OcorridoEmUtc });
        });
    }

    private static void ConfigurarRetiradas(ModelBuilder builder)
    {
        builder.Entity<PedidoRetirada>(entidade =>
        {
            entidade.ToTable("PedidosRetirada");
            entidade.HasKey(pedido => pedido.Id);
            entidade.Property(pedido => pedido.Conferencia).HasMaxLength(16).IsRequired();
            entidade.Property(pedido => pedido.Estado).HasMaxLength(32).IsRequired();
            entidade.Property(pedido => pedido.TitularPresente).IsRequired();
            entidade.Property(pedido => pedido.SolicitadoEmUtc).IsRequired();
            entidade.Property(pedido => pedido.RowVersion).IsRowVersion();
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(pedido => pedido.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(pedido => pedido.SolicitadoPorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(pedido => pedido.BuscaPorId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(pedido => pedido.AtendimentoId).IsUnique();
            entidade.HasIndex(pedido => new { pedido.Estado, pedido.SolicitadoEmUtc, pedido.Id });
        });

        builder.Entity<Cobranca>(entidade =>
        {
            entidade.ToTable("Cobrancas", tabela =>
            {
                tabela.HasCheckConstraint("CK_Cobrancas_DiariasMinimas", "[Diarias] >= 1");
                tabela.HasCheckConstraint("CK_Cobrancas_TotalNaoNegativo", "[TotalOriginal] >= 0");
                tabela.HasCheckConstraint("CK_Cobrancas_TarifaNaoNegativa", "[TarifaValorSnapshot] >= 0");
            });
            entidade.HasKey(cobranca => cobranca.Id);
            entidade.Property(cobranca => cobranca.DuracaoTicks).IsRequired();
            entidade.Property(cobranca => cobranca.Diarias).IsRequired();
            entidade.Property(cobranca => cobranca.TarifaValorSnapshot).HasColumnType("decimal(18,2)").IsRequired();
            entidade.Property(cobranca => cobranca.TotalOriginal).HasColumnType("decimal(18,2)").IsRequired();
            entidade.Property(cobranca => cobranca.CriadaEmUtc).IsRequired();
            entidade.HasOne<PedidoRetirada>()
                .WithMany()
                .HasForeignKey(cobranca => cobranca.PedidoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<TarifaDiaria>()
                .WithMany()
                .HasForeignKey(cobranca => cobranca.TarifaVersaoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(cobranca => cobranca.PedidoId).IsUnique();
        });

        builder.Entity<RazaoAtendimento>(entidade =>
        {
            entidade.ToTable("RazoesAtendimento", tabela =>
                tabela.HasCheckConstraint("CK_RazoesAtendimento_RevisaoPositiva", "[Revisao] >= 1"));
            entidade.HasKey(razao => razao.Id);
            entidade.Property(razao => razao.Revisao).IsRequired();
            entidade.Property(razao => razao.RowVersion).IsRowVersion();
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(razao => razao.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(razao => razao.AtendimentoId).IsUnique();
        });

        builder.Entity<LancamentoFinanceiro>(entidade =>
        {
            entidade.ToTable("LancamentosFinanceiros", tabela =>
            {
                tabela.HasCheckConstraint("CK_LancamentosFinanceiros_ValorPositivo", "[Valor] > 0");
                tabela.HasCheckConstraint(
                    "CK_LancamentosFinanceiros_Tipo",
                    "[Tipo] IN ('desconto', 'pagamento', 'estorno')");
                tabela.HasCheckConstraint(
                    "CK_LancamentosFinanceiros_FormaVinculoJustificativa",
                    "([Tipo] = 'pagamento' AND [FormaPagamento] IS NOT NULL AND [PagamentoOriginalId] IS NULL AND [JustificativaCifrada] IS NULL) OR " +
                    "([Tipo] = 'estorno' AND [FormaPagamento] IS NULL AND [PagamentoOriginalId] IS NOT NULL AND [JustificativaCifrada] IS NOT NULL) OR " +
                    "([Tipo] = 'desconto' AND [FormaPagamento] IS NULL AND [PagamentoOriginalId] IS NULL AND [JustificativaCifrada] IS NOT NULL)");
            });
            entidade.HasKey(lancamento => lancamento.Id);
            entidade.Property(lancamento => lancamento.Tipo).HasMaxLength(16).IsRequired();
            entidade.Property(lancamento => lancamento.Valor).HasColumnType("decimal(18,2)").IsRequired();
            entidade.Property(lancamento => lancamento.FormaPagamento).HasMaxLength(16);
            entidade.Property(lancamento => lancamento.JustificativaCifrada).HasColumnType("varbinary(max)");
            entidade.Property(lancamento => lancamento.OcorridoEmUtc).IsRequired();
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(lancamento => lancamento.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<LancamentoFinanceiro>()
                .WithMany()
                .HasForeignKey(lancamento => lancamento.PagamentoOriginalId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(lancamento => lancamento.AtorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(lancamento => new { lancamento.AtendimentoId, lancamento.OcorridoEmUtc });
            entidade.HasIndex(lancamento => lancamento.PagamentoOriginalId)
                .IsUnique()
                .HasFilter("[Tipo] = 'estorno'");
        });

        builder.Entity<ConcessaoAutorizacao>(entidade =>
        {
            entidade.ToTable("ConcessoesAutorizacao", tabela =>
            {
                tabela.HasCheckConstraint(
                    "CK_ConcessoesAutorizacao_Tipo",
                    "[Tipo] IN ('documental', 'financeira')");
                tabela.HasCheckConstraint(
                    "CK_ConcessoesAutorizacao_RevisaoPositiva",
                    "[RevisaoReferencia] >= 1");
            });
            entidade.HasKey(concessao => concessao.Id);
            entidade.Property(concessao => concessao.Tipo).HasMaxLength(16).IsRequired();
            entidade.Property(concessao => concessao.RevisaoReferencia).IsRequired();
            entidade.Property(concessao => concessao.JustificativaCifrada).HasColumnType("varbinary(max)").IsRequired();
            entidade.Property(concessao => concessao.ConcedidaEmUtc).IsRequired();
            entidade.HasOne<PedidoRetirada>()
                .WithMany()
                .HasForeignKey(concessao => concessao.PedidoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(concessao => concessao.ConcedidaPorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(concessao => new { concessao.PedidoId, concessao.Tipo, concessao.ConcedidaEmUtc });
        });

        builder.Entity<RevogacaoAutorizacao>(entidade =>
        {
            entidade.ToTable("RevogacoesAutorizacao");
            entidade.HasKey(revogacao => revogacao.Id);
            entidade.Property(revogacao => revogacao.JustificativaCifrada).HasColumnType("varbinary(max)").IsRequired();
            entidade.Property(revogacao => revogacao.RevogadaEmUtc).IsRequired();
            entidade.HasOne<ConcessaoAutorizacao>()
                .WithMany()
                .HasForeignKey(revogacao => revogacao.ConcessaoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(revogacao => revogacao.RevogadaPorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(revogacao => revogacao.ConcessaoId).IsUnique();
        });
    }

    private static void ConfigurarEntrega(ModelBuilder builder)
    {
        builder.Entity<VistoriaSaida>(entidade =>
        {
            entidade.ToTable("VistoriasSaida");
            entidade.HasKey(vistoria => vistoria.Id);
            entidade.Property(vistoria => vistoria.TextoCifrado).HasColumnType("varbinary(max)").IsRequired();
            entidade.Property(vistoria => vistoria.CriadoEmUtc).IsRequired();
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(vistoria => vistoria.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<PedidoRetirada>()
                .WithMany()
                .HasForeignKey(vistoria => vistoria.PedidoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(vistoria => vistoria.AtorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(vistoria => vistoria.AtendimentoId).IsUnique();
            entidade.HasIndex(vistoria => vistoria.PedidoId).IsUnique();
        });

        builder.Entity<EntregaVeiculo>(entidade =>
        {
            entidade.ToTable("EntregasVeiculo");
            entidade.HasKey(entrega => entrega.Id);
            entidade.Property(entrega => entrega.EntregueEmUtc).IsRequired();
            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(entrega => entrega.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<PedidoRetirada>()
                .WithMany()
                .HasForeignKey(entrega => entrega.PedidoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<VistoriaSaida>()
                .WithMany()
                .HasForeignKey(entrega => entrega.VistoriaSaidaId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<Ocupacao>()
                .WithMany()
                .HasForeignKey(entrega => entrega.OcupacaoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<Vaga>()
                .WithMany()
                .HasForeignKey(entrega => entrega.VagaId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(entrega => entrega.AtorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(entrega => entrega.AtendimentoId).IsUnique();
            entidade.HasIndex(entrega => entrega.PedidoId).IsUnique();
            entidade.HasIndex(entrega => entrega.EntregueEmUtc);
        });

        builder.Entity<ConsumoAutorizacao>(entidade =>
        {
            entidade.ToTable("ConsumosAutorizacao", tabela =>
                tabela.HasCheckConstraint(
                    "CK_ConsumosAutorizacao_Tipo",
                    "[Tipo] IN ('documental', 'financeira')"));
            entidade.HasKey(consumo => consumo.Id);
            entidade.Property(consumo => consumo.Tipo).HasMaxLength(16).IsRequired();
            entidade.Property(consumo => consumo.RevisaoReferencia).IsRequired();
            entidade.Property(consumo => consumo.ConsumidaEmUtc).IsRequired();
            entidade.HasOne<EntregaVeiculo>()
                .WithMany()
                .HasForeignKey(consumo => consumo.EntregaId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<ConcessaoAutorizacao>()
                .WithMany()
                .HasForeignKey(consumo => consumo.ConcessaoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(consumo => new { consumo.EntregaId, consumo.Tipo }).IsUnique();
            entidade.HasIndex(consumo => consumo.ConcessaoId).IsUnique();
        });
    }

    private static void ConfigurarFotos(ModelBuilder builder)
    {
        builder.Entity<FotoVistoria>(entidade =>
        {
            entidade.ToTable("FotosVistoria", tabela =>
            {
                tabela.HasCheckConstraint(
                    "CK_FotosVistoria_Tipo",
                    "[Tipo] IN ('entrada', 'saida')");
                tabela.HasCheckConstraint(
                    "CK_FotosVistoria_Estado",
                    "[Estado] IN ('pendente', 'disponivel', 'limpeza_pendente', 'descartada')");
                tabela.HasCheckConstraint(
                    "CK_FotosVistoria_Vinculo",
                    "([Tipo] = 'entrada' AND [VistoriaEntradaId] IS NOT NULL AND [VistoriaSaidaId] IS NULL) OR " +
                    "([Tipo] = 'saida' AND [VistoriaSaidaId] IS NOT NULL AND [VistoriaEntradaId] IS NULL)");
                tabela.HasCheckConstraint(
                    "CK_FotosVistoria_Dimensoes",
                    "[Largura] > 0 AND [Altura] > 0 AND [TamanhoBytes] > 0");
            });

            entidade.HasKey(foto => foto.Id);
            entidade.Property(foto => foto.Tipo).HasMaxLength(16).IsRequired();
            entidade.Property(foto => foto.Estado).HasMaxLength(24).IsRequired();
            entidade.Property(foto => foto.ChaveObjeto).HasMaxLength(200).IsRequired();
            entidade.Property(foto => foto.Mime).HasMaxLength(64).IsRequired();
            entidade.Property(foto => foto.HashSha256).HasColumnType("binary(32)").IsRequired();
            entidade.Property(foto => foto.HashEntradaSha256).HasColumnType("binary(32)").IsRequired();
            entidade.Property(foto => foto.VersaoAtendimentoBase).HasColumnType("varbinary(max)").IsRequired();
            entidade.Property(foto => foto.RowVersion).IsRowVersion();

            entidade.HasOne<Atendimento>()
                .WithMany()
                .HasForeignKey(foto => foto.AtendimentoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<VistoriaEntrada>()
                .WithMany()
                .HasForeignKey(foto => foto.VistoriaEntradaId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<VistoriaSaida>()
                .WithMany()
                .HasForeignKey(foto => foto.VistoriaSaidaId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<UsuarioAplicacao>()
                .WithMany()
                .HasForeignKey(foto => foto.AtorId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasOne<ResultadoIdempotente>()
                .WithOne()
                .HasForeignKey<FotoVistoria>(foto => foto.ResultadoIdempotenteId)
                .OnDelete(DeleteBehavior.NoAction);

            entidade.HasIndex(foto => foto.ChaveObjeto).IsUnique();
            entidade.HasIndex(foto => foto.ResultadoIdempotenteId).IsUnique();
            entidade.HasIndex(foto => new { foto.AtendimentoId, foto.Tipo, foto.Estado });
            entidade.HasIndex(foto => new { foto.Estado, foto.ReservadoAteUtc });
            entidade.HasIndex(foto => new { foto.Estado, foto.ConfirmadoEmUtc });
        });

        builder.Entity<ConsumoFotosMensal>(entidade =>
        {
            entidade.ToTable("ConsumosFotosMensais", tabela =>
            {
                tabela.HasCheckConstraint(
                    "CK_ConsumosFotosMensais_Estado",
                    "[EstadoReconciliacao] IN ('conhecido', 'desconhecido')");
                tabela.HasCheckConstraint(
                    "CK_ConsumosFotosMensais_NaoNegativos",
                    "[BytesArmazenados] >= 0 AND [BytesTransferidos] >= 0 AND " +
                    "[OperacoesClasseA] >= 0 AND [OperacoesClasseB] >= 0 AND [BytesReservados] >= 0");
            });
            entidade.HasKey(consumo => consumo.Id);
            entidade.Property(consumo => consumo.Competencia).HasMaxLength(7).IsRequired();
            entidade.Property(consumo => consumo.Ambiente).HasMaxLength(64).IsRequired();
            entidade.Property(consumo => consumo.EstadoReconciliacao).HasMaxLength(24).IsRequired();
            entidade.Property(consumo => consumo.RowVersion).IsRowVersion();
            entidade.HasIndex(consumo => new { consumo.Competencia, consumo.Ambiente }).IsUnique();
        });

        builder.Entity<TarefaFoto>(entidade =>
        {
            entidade.ToTable("TarefasFotos", tabela =>
            {
                tabela.HasCheckConstraint(
                    "CK_TarefasFotos_Tipo",
                    "[Tipo] IN ('confirmacao', 'limpeza', 'retencao')");
                tabela.HasCheckConstraint(
                    "CK_TarefasFotos_Estado",
                    "[Estado] IN ('pendente', 'concluida', 'falha')");
            });
            entidade.HasKey(tarefa => tarefa.Id);
            entidade.Property(tarefa => tarefa.Tipo).HasMaxLength(16).IsRequired();
            entidade.Property(tarefa => tarefa.Estado).HasMaxLength(16).IsRequired();
            entidade.Property(tarefa => tarefa.UltimoErroCodigo).HasMaxLength(64);
            entidade.Property(tarefa => tarefa.RowVersion).IsRowVersion();
            entidade.HasOne<FotoVistoria>()
                .WithMany()
                .HasForeignKey(tarefa => tarefa.FotoId)
                .OnDelete(DeleteBehavior.NoAction);
            entidade.HasIndex(tarefa => new { tarefa.Estado, tarefa.LeaseAteUtc });
            entidade.HasIndex(tarefa => new { tarefa.FotoId, tarefa.Tipo });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ImpedirAlteracaoDeHistorico();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ImpedirAlteracaoDeHistorico();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Auditoria, eventos de atendimento, resultados idempotentes, lançamentos
    /// financeiros, cobrança, autorizações, vistoria de saída, entrega e
    /// consumo são append-only durante a operação normal. Apenas
    /// <see cref="PedidoRetirada"/> e <see cref="RazaoAtendimento"/> evoluem sob
    /// versão, e a ocupação encerra uma única vez por atualização condicional.
    /// </summary>
    private void ImpedirAlteracaoDeHistorico()
    {
        var alteracoesProibidas = ChangeTracker.Entries<EventoAuditoria>()
            .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
            .Select(_ => "EventosAuditoria")
            .Concat(ChangeTracker.Entries<EventoAuditoriaNegocio>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "EventosAuditoriaNegocio"))
            .Concat(ChangeTracker.Entries<EventoAtendimento>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "EventosAtendimento"))
            .Concat(ChangeTracker.Entries<Cobranca>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "Cobrancas"))
            .Concat(ChangeTracker.Entries<LancamentoFinanceiro>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "LancamentosFinanceiros"))
            .Concat(ChangeTracker.Entries<ConcessaoAutorizacao>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "ConcessoesAutorizacao"))
            .Concat(ChangeTracker.Entries<RevogacaoAutorizacao>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "RevogacoesAutorizacao"))
            .Concat(ChangeTracker.Entries<VistoriaSaida>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "VistoriasSaida"))
            .Concat(ChangeTracker.Entries<EntregaVeiculo>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "EntregasVeiculo"))
            .Concat(ChangeTracker.Entries<ConsumoAutorizacao>()
                .Where(entrada => entrada.State is EntityState.Modified or EntityState.Deleted)
                .Select(_ => "ConsumosAutorizacao"))
            .ToList();

        if (alteracoesProibidas.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registros append-only não podem ser alterados ou excluídos: {string.Join(", ", alteracoesProibidas.Distinct())}.");
        }

        ImpedirAlteracaoDeConteudoFoto();
    }

    /// <summary>
    /// A foto pode avançar apenas entre estados de reserva, disponibilidade,
    /// limpeza e descarte. Vínculo, chave, hash, autoria e dimensões são
    /// imutáveis após a reserva.
    /// </summary>
    private void ImpedirAlteracaoDeConteudoFoto()
    {
        var camposImutaveis = new[]
        {
            nameof(FotoVistoria.AtendimentoId),
            nameof(FotoVistoria.Tipo),
            nameof(FotoVistoria.VistoriaEntradaId),
            nameof(FotoVistoria.VistoriaSaidaId),
            nameof(FotoVistoria.ChaveObjeto),
            nameof(FotoVistoria.Mime),
            nameof(FotoVistoria.TamanhoBytes),
            nameof(FotoVistoria.Largura),
            nameof(FotoVistoria.Altura),
            nameof(FotoVistoria.HashSha256),
            nameof(FotoVistoria.HashEntradaSha256),
            nameof(FotoVistoria.AtorId),
            nameof(FotoVistoria.CriadoEmUtc),
            nameof(FotoVistoria.VersaoAtendimentoBase),
            nameof(FotoVistoria.ResultadoIdempotenteId)
        };

        foreach (var entrada in ChangeTracker.Entries<FotoVistoria>())
        {
            if (entrada.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Fotos de vistoria não podem ser excluídas; use o descarte auditado.");
            }

            if (entrada.State != EntityState.Modified)
            {
                continue;
            }

            foreach (var campo in camposImutaveis)
            {
                if (entrada.Property(campo).IsModified)
                {
                    throw new InvalidOperationException(
                        $"O campo {campo} da foto de vistoria é imutável após a reserva.");
                }
            }
        }
    }
}
