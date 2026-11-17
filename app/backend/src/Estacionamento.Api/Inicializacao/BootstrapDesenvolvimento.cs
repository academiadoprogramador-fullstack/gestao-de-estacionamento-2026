using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Provisionamento;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Estacionamento.Api.Inicializacao;

/// <summary>
/// Bootstrap exclusivo de Development. Executa, nesta ordem e sem etapa
/// intermediária: validação da configuração e das três contas, verificação da
/// conexão/destino, aplicação das migrations pendentes e provisionamento
/// imediato das contas ausentes. O pipeline HTTP só inicia após o sucesso.
/// </summary>
public sealed class BootstrapDesenvolvimento
{
    private readonly IHostEnvironment _ambiente;
    private readonly IConfiguration _configuracao;
    private readonly IOptions<AutenticacaoJwtOptions> _jwt;
    private readonly IOptions<ProtecaoDadosOptions> _protecaoDados;
    private readonly IOptions<CorsOptions> _cors;
    private readonly IOptions<ConfiguracaoRegionalOptions> _regional;
    private readonly IOptions<OperacaoOptions> _operacao;
    private readonly IOptions<ContasSinteticasOptions> _contas;
    private readonly ValidadorContasSinteticas _validadorContas;
    private readonly IServiceScopeFactory _fabricasEscopo;
    private readonly IRegistroBootstrap _registro;
    private readonly ILogger<BootstrapDesenvolvimento> _logger;

    public BootstrapDesenvolvimento(
        IHostEnvironment ambiente,
        IConfiguration configuracao,
        IOptions<AutenticacaoJwtOptions> jwt,
        IOptions<ProtecaoDadosOptions> protecaoDados,
        IOptions<CorsOptions> cors,
        IOptions<ConfiguracaoRegionalOptions> regional,
        IOptions<OperacaoOptions> operacao,
        IOptions<ContasSinteticasOptions> contas,
        ValidadorContasSinteticas validadorContas,
        IServiceScopeFactory fabricasEscopo,
        IRegistroBootstrap registro,
        ILogger<BootstrapDesenvolvimento> logger)
    {
        _ambiente = ambiente;
        _configuracao = configuracao;
        _jwt = jwt;
        _protecaoDados = protecaoDados;
        _cors = cors;
        _regional = regional;
        _operacao = operacao;
        _contas = contas;
        _validadorContas = validadorContas;
        _fabricasEscopo = fabricasEscopo;
        _registro = registro;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        if (!_ambiente.IsDevelopment())
        {
            _logger.LogInformation(
                "Bootstrap automático desabilitado no ambiente {Ambiente}.",
                _ambiente.EnvironmentName);
            return;
        }

        _registro.Registrar("validacao");
        ValidarConfiguracao();

        _registro.Registrar("conexao");
        using var escopo = _fabricasEscopo.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();

        await VerificarServidorEDestinoAsync(db, cancellationToken);

        _registro.Registrar("migracoes");
        // O EF cria o catálogo quando ele ainda não existe e, em seguida,
        // aplica as migrations pendentes. O login de Development precisa ter
        // permissão para criar o banco (dbcreator); a API nunca usa `sa`.
        await db.Database.MigrateAsync(cancellationToken);

        _registro.Registrar("provisionamento");
        var provisionador = escopo.ServiceProvider.GetRequiredService<IProvisionadorContasSinteticas>();
        var resultado = await provisionador.ProvisionarAsync(_contas.Value, cancellationToken);

        _logger.LogInformation(
            "Bootstrap de Development concluído. Contas criadas: {Criadas}; preservadas: {Preservadas}.",
            resultado.ContasCriadas,
            resultado.ContasPreservadas);
    }

    private void ValidarConfiguracao()
    {
        // Forçar a leitura dispara os validadores tipados antes de qualquer
        // acesso ao banco. Os valores sensíveis nunca aparecem nas mensagens.
        _ = _jwt.Value;
        _ = _protecaoDados.Value;
        _ = _cors.Value;
        _ = _regional.Value;
        _ = _operacao.Value;

        var conexao = _configuracao.GetConnectionString(EstacionamentoDbContext.NomeConexao);
        if (string.IsNullOrWhiteSpace(conexao))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{EstacionamentoDbContext.NomeConexao} é obrigatória.");
        }

        var validacaoContas = _validadorContas.Validar(_contas.Value);
        if (!validacaoContas.Valido)
        {
            throw new InvalidOperationException(
                "Contas sintéticas inválidas: " + string.Join(" ", validacaoContas.Erros));
        }
    }

    private async Task VerificarServidorEDestinoAsync(
        EstacionamentoDbContext db,
        CancellationToken cancellationToken)
    {
        var construtor = new SqlConnectionStringBuilder(
            db.Database.GetDbConnection().ConnectionString);

        var destino = construtor.InitialCatalog;
        if (string.IsNullOrWhiteSpace(destino) ||
            destino.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            destino.Equals("model", StringComparison.OrdinalIgnoreCase) ||
            destino.Equals("msdb", StringComparison.OrdinalIgnoreCase) ||
            destino.Equals("tempdb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O catálogo configurado não é um catálogo de aplicação válido.");
        }

        // Verifica o servidor por `master` sem exigir que o catálogo da
        // aplicação já exista: a migration é responsável por criá-lo.
        construtor.InitialCatalog = "master";
        await using var conexao = new SqlConnection(construtor.ConnectionString);
        await conexao.OpenAsync(cancellationToken);

        await using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT DB_ID(@nome)";
        comando.Parameters.AddWithValue("@nome", destino);
        var idBanco = await comando.ExecuteScalarAsync(cancellationToken);

        if (idBanco is null or DBNull)
        {
            _logger.LogInformation(
                "O catálogo {Catalogo} ainda não existe e será criado pela migration.",
                destino);
        }
    }
}
