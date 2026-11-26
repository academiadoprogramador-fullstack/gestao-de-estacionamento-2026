using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Fotos;
using Estacionamento.Application.Fotos;
using Microsoft.Extensions.Options;

namespace Estacionamento.Api.Inicializacao;

/// <summary>
/// Executa um lote de manutenção de fotos após a aplicação iniciar. Não é um
/// worker contínuo: retoma pendências uma vez e encerra, preservando a pausa
/// do serviço gratuito. Testes chamam o serviço diretamente.
/// </summary>
public sealed class ManutencaoFotosHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _escopos;
    private readonly IHostEnvironment _ambiente;
    private readonly IOptions<ArmazenamentoFotosOptions> _opcoes;
    private readonly ILogger<ManutencaoFotosHostedService> _logger;

    public ManutencaoFotosHostedService(
        IServiceScopeFactory escopos,
        IHostEnvironment ambiente,
        IOptions<ArmazenamentoFotosOptions> opcoes,
        ILogger<ManutencaoFotosHostedService> logger)
    {
        _escopos = escopos;
        _ambiente = ambiente;
        _opcoes = opcoes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_ambiente.IsEnvironment("Testing") || !_opcoes.Value.Habilitado)
        {
            return;
        }

        await Task.Yield();

        try
        {
            using var escopo = _escopos.CreateScope();
            var manutencao = escopo.ServiceProvider.GetRequiredService<IServicoManutencaoFotos>();
            await manutencao.ExecutarLoteAsync(stoppingToken);
        }
        catch (Exception excecao)
        {
            _logger.LogWarning(
                "Manutenção inicial de fotos adiada. Tipo: {Tipo}.",
                excecao.GetType().Name);
        }
    }
}
