using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Autenticacao;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Funcionarios;
using Estacionamento.Application.Provisionamento;
using Estacionamento.Application.Seguranca;
using Estacionamento.Application.Tarifas;
using Estacionamento.Application.Tempo;
using Estacionamento.Application.Vagas;
using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia;
using Estacionamento.Infrastructure.Seguranca;
using Estacionamento.Infrastructure.Servicos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Estacionamento.Infrastructure;

/// <summary>
/// Composição de persistência, Identity, proteção de campos e casos de uso da
/// Fase 2. Não lê User Secrets: a configuração recebida já reflete a precedência
/// do ambiente.
/// </summary>
public static class RegistroInfraestrutura
{
    public static IServiceCollection AdicionarInfraestrutura(
        this IServiceCollection services,
        IConfiguration configuracao,
        string nomeAmbiente)
    {
        services.AddDbContext<EstacionamentoDbContext>((provedor, options) =>
        {
            // A conexão é lida do provedor de configuração final para respeitar
            // a precedência do ambiente e permitir substituição em testes.
            var conexaoAtual = provedor
                .GetRequiredService<IConfiguration>()
                .GetConnectionString(EstacionamentoDbContext.NomeConexao);

            if (string.IsNullOrWhiteSpace(conexaoAtual))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{EstacionamentoDbContext.NomeConexao} é obrigatória.");
            }

            // A retentativa automática do provider é incompatível com as
            // transações iniciadas pelos casos de uso desta fase. Repetição
            // segura de unidade transacional pertence ao desenho de resiliência
            // da publicação; aqui a falha é propagada de forma fechada.
            options.UseSqlServer(conexaoAtual, sql =>
                sql.MigrationsAssembly(typeof(EstacionamentoDbContext).Assembly.FullName));
        });

        services
            .AddIdentityCore<UsuarioAplicacao>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<EstacionamentoDbContext>();

        services.AddSingleton<IRelogio, RelogioSistema>();
        services.AddSingleton<IContextoAplicacao>(new ContextoAplicacao(nomeAmbiente));
        services.AddScoped<IGeradorRefresh, GeradorRefresh>();
        services.AddScoped<IGeradorTokenJwt, GeradorTokenJwt>();
        services.AddScoped<IServicoAuditoria, ServicoAuditoria>();
        services.AddScoped<IServicoSessao, ServicoSessao>();
        services.AddScoped<IProvisionadorContasSinteticas, ProvisionadorContasSinteticas>();
        services.AddScoped<ICriptografiaCampos, CriptografiaCamposAesGcm>();
        services.AddScoped<IAuditoriaNegocio, ServicoAuditoriaNegocio>();
        services.AddScoped<IServicoFuncionarios, ServicoFuncionarios>();
        services.AddScoped<IServicoVagas, ServicoVagas>();
        services.AddScoped<IServicoTarifas, ServicoTarifas>();
        services.AddScoped<IServicoAtendimentos, ServicoAtendimentos>();
        services.AddScoped<IServicoConsultaAtendimentos, ServicoConsultaAtendimentos>();

        services.AddSingleton<IValidateOptions<AutenticacaoJwtOptions>, AutenticacaoJwtOptionsValidator>();
        services.AddSingleton<IValidateOptions<ProtecaoDadosOptions>, ProtecaoDadosOptionsValidator>();
        services.AddSingleton<IValidateOptions<CorsOptions>, CorsOptionsValidator>();
        services.AddSingleton<IValidateOptions<ConfiguracaoRegionalOptions>, ConfiguracaoRegionalOptionsValidator>();
        services.AddSingleton<IValidateOptions<OperacaoOptions>, OperacaoOptionsValidator>();

        services.AddOptions<AutenticacaoJwtOptions>()
            .Bind(configuracao.GetSection(AutenticacaoJwtOptions.Secao))
            .ValidateOnStart();
        services.AddOptions<ProtecaoDadosOptions>()
            .Bind(configuracao.GetSection(ProtecaoDadosOptions.Secao))
            .ValidateOnStart();
        services.AddOptions<CorsOptions>()
            .Bind(configuracao.GetSection(CorsOptions.Secao))
            .ValidateOnStart();
        services.AddOptions<ConfiguracaoRegionalOptions>()
            .Bind(configuracao.GetSection(ConfiguracaoRegionalOptions.Secao))
            .ValidateOnStart();
        services.AddOptions<OperacaoOptions>()
            .Bind(configuracao.GetSection(OperacaoOptions.Secao))
            .ValidateOnStart();
        services.AddOptions<ContasSinteticasOptions>()
            .Bind(configuracao.GetSection(ContasSinteticasOptions.Secao));

        services.AddSingleton<ValidadorContasSinteticas>();

        return services;
    }
}
