using Estacionamento.Application.Configuracao;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.Extensions.Options;

namespace Estacionamento.Api.Inicializacao;

/// <summary>
/// Validação comum a todos os ambientes, executada antes de qualquer bootstrap
/// ou tráfego. Falha fechada quando um segredo ou parâmetro obrigatório falta,
/// sem imprimir o valor ausente.
/// </summary>
public sealed class ValidadorConfiguracaoInicial
{
    private readonly IConfiguration _configuracao;
    private readonly IOptions<AutenticacaoJwtOptions> _jwt;
    private readonly IOptions<ProtecaoDadosOptions> _protecaoDados;
    private readonly IOptions<CorsOptions> _cors;
    private readonly IOptions<ConfiguracaoRegionalOptions> _regional;
    private readonly IOptions<OperacaoOptions> _operacao;

    public ValidadorConfiguracaoInicial(
        IConfiguration configuracao,
        IOptions<AutenticacaoJwtOptions> jwt,
        IOptions<ProtecaoDadosOptions> protecaoDados,
        IOptions<CorsOptions> cors,
        IOptions<ConfiguracaoRegionalOptions> regional,
        IOptions<OperacaoOptions> operacao)
    {
        _configuracao = configuracao;
        _jwt = jwt;
        _protecaoDados = protecaoDados;
        _cors = cors;
        _regional = regional;
        _operacao = operacao;
    }

    public void Validar()
    {
        _ = _jwt.Value;
        _ = _protecaoDados.Value;
        _ = _cors.Value;
        _ = _regional.Value;
        _ = _operacao.Value;

        if (string.IsNullOrWhiteSpace(_configuracao.GetConnectionString(EstacionamentoDbContext.NomeConexao)))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{EstacionamentoDbContext.NomeConexao} é obrigatória.");
        }
    }
}
