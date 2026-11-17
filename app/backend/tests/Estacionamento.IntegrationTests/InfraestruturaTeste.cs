using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Estacionamento.Api.Inicializacao;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Provisionamento;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Estacionamento.IntegrationTests;

/// <summary>
/// Utilitários de SQL Server real para testes. O destino é validado antes de
/// criar, migrar ou limpar qualquer catálogo.
/// </summary>
internal static class SqlTeste
{
    public const string VariavelConexao = "ESTACIONAMENTO_TESTE_CONEXAO";

    public static string? ConexaoMaster => Environment.GetEnvironmentVariable(VariavelConexao);

    public static void GarantirDisponivel()
    {
        if (string.IsNullOrWhiteSpace(ConexaoMaster))
        {
            Assert.Inconclusive(
                $"Defina {VariavelConexao} com uma conexão de teste (catálogo master) para executar os testes de integração.");
        }

        var construtor = new SqlConnectionStringBuilder(ConexaoMaster);
        if (!EhHostLocal(construtor.DataSource))
        {
            Assert.Fail("A conexão de teste precisa apontar para um host local (localhost/127.0.0.1).");
        }
    }

    public static string CriarBanco()
    {
        GarantirDisponivel();
        var nome = $"EstacionamentoTeste_{Guid.NewGuid():N}";
        ExecutarEmMaster($"CREATE DATABASE [{nome}];");
        return nome;
    }

    public static string Conexao(string nomeBanco)
    {
        var construtor = new SqlConnectionStringBuilder(ConexaoMaster!)
        {
            InitialCatalog = nomeBanco
        };
        return construtor.ConnectionString;
    }

    public static void RemoverBanco(string nomeBanco)
    {
        if (string.IsNullOrWhiteSpace(ConexaoMaster) || !nomeBanco.StartsWith("EstacionamentoTeste_", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            ExecutarEmMaster(
                $"IF DB_ID(N'{nomeBanco}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{nomeBanco}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{nomeBanco}]; END");
        }
        catch (SqlException)
        {
            // A limpeza é best-effort; o nome é exclusivo por execução.
        }
    }

    public static void Executar(string nomeBanco, string comando)
    {
        using var conexao = new SqlConnection(Conexao(nomeBanco));
        conexao.Open();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = comando;
        cmd.ExecuteNonQuery();
    }

    public static int ContarTabelas(string nomeBanco)
    {
        using var conexao = new SqlConnection(Conexao(nomeBanco));
        conexao.Open();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static bool TabelaExiste(string nomeBanco, string tabela)
    {
        using var conexao = new SqlConnection(Conexao(nomeBanco));
        conexao.Open();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @nome";
        cmd.Parameters.AddWithValue("@nome", tabela);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static async Task AplicarMigrationsAsync(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
            .UseSqlServer(Conexao(nomeBanco), sql =>
                sql.MigrationsAssembly(typeof(EstacionamentoDbContext).Assembly.FullName))
            .Options;

        await using var db = new EstacionamentoDbContext(options);
        await db.Database.MigrateAsync();
    }

    public static async Task<IReadOnlyList<string>> MigrationsPendentesAsync(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
            .UseSqlServer(Conexao(nomeBanco), sql =>
                sql.MigrationsAssembly(typeof(EstacionamentoDbContext).Assembly.FullName))
            .Options;

        await using var db = new EstacionamentoDbContext(options);
        return (await db.Database.GetPendingMigrationsAsync()).ToArray();
    }

    public static void ExecutarEmMaster(string comando)
    {
        var construtor = new SqlConnectionStringBuilder(ConexaoMaster!)
        {
            InitialCatalog = "master"
        };

        using var conexao = new SqlConnection(construtor.ConnectionString);
        conexao.Open();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = comando;
        cmd.ExecuteNonQuery();
    }

    private static bool EhHostLocal(string dataSource) =>
        dataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
        dataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        dataSource.StartsWith("(local", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Contas sintéticas explícitas dos testes. Não há leitura de User Secrets
/// pessoais: o fixture desabilita o provider e fornece os valores por memória.
/// </summary>
internal static class ContasTeste
{
    public const string Senha = "SenhaTeste1";

    public static ContasSinteticasOptions Contas() => new()
    {
        Administrador = new ContaSinteticaOptions
        {
            Email = "admin.teste@example.test",
            Senha = Senha
        },
        Caixa = new ContaSinteticaOptions
        {
            Email = "caixa.teste@example.test",
            Senha = Senha
        },
        Manobrista = new ContaSinteticaOptions
        {
            Email = "manobrista.teste@example.test",
            Senha = Senha
        }
    };
}

/// <summary>
/// Fábrica de API com banco isolado por execução, migrations explícitas em
/// Testing e sem carregar User Secrets pessoais.
/// </summary>
internal sealed class FabricaApiTeste : WebApplicationFactory<Program>
{
    private readonly string _ambiente;
    private readonly string _nomeBanco;
    private readonly bool _aplicarMigrationsAntesDoStartup;
    private readonly Dictionary<string, string?> _configuracaoExtra;
    private readonly string? _variavelUserSecretsAnterior;
    private readonly bool _removerBancoAoDispor;

    public FabricaApiTeste(
        string ambiente = "Testing",
        bool aplicarMigrationsAntesDoStartup = true,
        Dictionary<string, string?>? configuracaoExtra = null,
        string? nomeBancoExistente = null,
        bool removerBancoAoDispor = true)
    {
        _removerBancoAoDispor = removerBancoAoDispor;
        SqlTeste.GarantirDisponivel();
        _ambiente = ambiente;
        _aplicarMigrationsAntesDoStartup = aplicarMigrationsAntesDoStartup;
        _configuracaoExtra = configuracaoExtra ?? [];
        _nomeBanco = nomeBancoExistente ?? SqlTeste.CriarBanco();

        _variavelUserSecretsAnterior = Environment.GetEnvironmentVariable(
            Estacionamento.Api.Configuracao.PrecedenciaConfiguracao.VariavelDesabilitarUserSecrets);
        Environment.SetEnvironmentVariable(
            Estacionamento.Api.Configuracao.PrecedenciaConfiguracao.VariavelDesabilitarUserSecrets,
            "1");

        if (_aplicarMigrationsAntesDoStartup)
        {
            SqlTeste.AplicarMigrationsAsync(_nomeBanco).GetAwaiter().GetResult();
        }
    }

    public string NomeBanco => _nomeBanco;

    public string StringConexao => SqlTeste.Conexao(_nomeBanco);

    public string ChaveJwt { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string Emissor => "emissor-integracao";

    public string Audiencia => "audiencia-integracao";

    public AutenticacaoJwtOptions OpcoesJwt => new()
    {
        Emissor = Emissor,
        Audiencia = Audiencia,
        VersaoAtiva = "v1",
        DuracaoAcessoMinutos = 15,
        DuracaoSessaoHoras = 8,
        ToleranciaRelogioSegundos = 30,
        RotacaoChaveDias = 90,
        Chaves = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v1"] = ChaveJwt
        }
    };

    public Dictionary<string, string?> ConfiguracaoPadrao(bool incluirChaveJwt = true)
    {
        var contas = ContasTeste.Contas();
        var configuracao = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Estacionamento"] = StringConexao,
            ["Autenticacao:Jwt:Emissor"] = Emissor,
            ["Autenticacao:Jwt:Audiencia"] = Audiencia,
            ["Autenticacao:Jwt:VersaoAtiva"] = "v1",
            ["Autenticacao:Jwt:DuracaoAcessoMinutos"] = "15",
            ["Autenticacao:Jwt:DuracaoSessaoHoras"] = "8",
            ["Autenticacao:Jwt:ToleranciaRelogioSegundos"] = "30",
            ["Autenticacao:Jwt:RotacaoChaveDias"] = "90",
            ["ProtecaoDados:VersaoAtiva"] = "v1",
            ["ProtecaoDados:Chaves:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Cors:OrigensPermitidas:0"] = "https://localhost:4200",
            ["Cors:OrigensPermitidas:1"] = "https://localhost:4300",
            ["ContasSinteticas:Administrador:Email"] = contas.Administrador!.Email,
            ["ContasSinteticas:Administrador:Senha"] = contas.Administrador.Senha,
            ["ContasSinteticas:Caixa:Email"] = contas.Caixa!.Email,
            ["ContasSinteticas:Caixa:Senha"] = contas.Caixa.Senha,
            ["ContasSinteticas:Manobrista:Email"] = contas.Manobrista!.Email,
            ["ContasSinteticas:Manobrista:Senha"] = contas.Manobrista.Senha
        };

        if (incluirChaveJwt)
        {
            configuracao["Autenticacao:Jwt:Chaves:v1"] = ChaveJwt;
        }

        foreach (var (chave, valor) in _configuracaoExtra)
        {
            configuracao[chave] = valor;
        }

        return configuracao;
    }

    public async Task ProvisionarContasAsync()
    {
        using var escopo = Services.CreateScope();
        var provisionador = escopo.ServiceProvider.GetRequiredService<IProvisionadorContasSinteticas>();
        await provisionador.ProvisionarAsync(ContasTeste.Contas());
    }

    public IReadOnlyList<string> EtapasBootstrap =>
        Services.GetRequiredService<IRegistroBootstrap>().Etapas;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_ambiente);
        builder.ConfigureAppConfiguration((_, configuracao) =>
        {
            configuracao.AddInMemoryCollection(ConfiguracaoPadrao());
        });
    }

    public HttpClient CriarCliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("http://localhost")
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Environment.SetEnvironmentVariable(
                Estacionamento.Api.Configuracao.PrecedenciaConfiguracao.VariavelDesabilitarUserSecrets,
                _variavelUserSecretsAnterior);
            if (_removerBancoAoDispor)
            {
                SqlTeste.RemoverBanco(_nomeBanco);
            }
        }
    }
}

/// <summary>
/// Cliente auxiliar com login e respostas Problem Details tipadas.
/// </summary>
internal static class ClienteSessao
{
    public static async Task<RespostaSessao> LoginAsync(HttpClient cliente, string email, string senha)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/v1/session/login", new { email, senha });
        return await RespostaSessao.LerAsync(resposta);
    }

    public static async Task<RespostaSessao> LoginAdministradorAsync(HttpClient cliente) =>
        await LoginAsync(
            cliente,
            ContasTeste.Contas().Administrador!.Email,
            ContasTeste.Contas().Administrador!.Senha);

    public static async Task<RespostaSessao> LoginCaixaAsync(HttpClient cliente) =>
        await LoginAsync(
            cliente,
            ContasTeste.Contas().Caixa!.Email,
            ContasTeste.Contas().Caixa!.Senha);

    public static void UsarBearer(HttpClient cliente, string? token)
    {
        cliente.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }
}

internal sealed record RespostaSessao(
    int Status,
    string? TokenAcesso,
    string? Refresh,
    string? Nome,
    string? Codigo,
    IReadOnlyList<string> Perfis,
    string? Corpo)
{
    public static async Task<RespostaSessao> LerAsync(HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();

        if (!resposta.IsSuccessStatusCode)
        {
            var codigo = string.Empty;
            try
            {
                using var documento = JsonDocument.Parse(corpo);
                codigo = documento.RootElement.TryGetProperty("code", out var valor)
                    ? valor.GetString() ?? string.Empty
                    : string.Empty;
            }
            catch (JsonException)
            {
                // Corpo não JSON permanece apenas como evidência textual.
            }

            return new RespostaSessao(
                (int)resposta.StatusCode, null, null, null, codigo, [], corpo);
        }

        using var json = JsonDocument.Parse(corpo);
        var raiz = json.RootElement;
        var funcionario = raiz.GetProperty("funcionario");

        return new RespostaSessao(
            (int)resposta.StatusCode,
            raiz.GetProperty("tokenAcesso").GetString(),
            raiz.GetProperty("refresh").GetString(),
            funcionario.GetProperty("nome").GetString(),
            null,
            funcionario.GetProperty("perfis").EnumerateArray()
                .Select(perfil => perfil.GetString() ?? string.Empty)
                .ToArray(),
            corpo);
    }
}
