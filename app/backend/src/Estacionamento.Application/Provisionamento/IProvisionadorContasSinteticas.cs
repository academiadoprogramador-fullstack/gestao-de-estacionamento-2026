using Estacionamento.Application.Configuracao;

namespace Estacionamento.Application.Provisionamento;

/// <summary>
/// Resultado do provisionamento idempotente, restrito a contagens e nomes de
/// perfis — nunca e-mails, senhas ou identificadores de conta.
/// </summary>
public sealed record ResultadoProvisionamento(int ContasCriadas, int ContasPreservadas);

/// <summary>
/// Porta do provisionamento de perfis fixos e contas sintéticas ausentes,
/// executado imediatamente após as migrations e somente em Development.
/// </summary>
public interface IProvisionadorContasSinteticas
{
    Task<ResultadoProvisionamento> ProvisionarAsync(
        ContasSinteticasOptions contas,
        CancellationToken cancellationToken = default);
}
