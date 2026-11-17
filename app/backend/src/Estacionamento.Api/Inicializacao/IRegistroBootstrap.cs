namespace Estacionamento.Api.Inicializacao;

/// <summary>
/// Registro em memória da ordem das etapas do bootstrap de Development. É usado
/// por testes e logs para comprovar validação → conexão → migrations →
/// provisionamento → HTTP.
/// </summary>
public interface IRegistroBootstrap
{
    IReadOnlyList<string> Etapas { get; }

    void Registrar(string etapa);
}

public sealed class RegistroBootstrap : IRegistroBootstrap
{
    private readonly List<string> _etapas = [];
    private readonly object _trava = new();

    public IReadOnlyList<string> Etapas
    {
        get
        {
            lock (_trava)
            {
                return _etapas.ToArray();
            }
        }
    }

    public void Registrar(string etapa)
    {
        lock (_trava)
        {
            _etapas.Add(etapa);
        }
    }
}
