using Microsoft.AspNetCore.Identity;

namespace Estacionamento.Infrastructure.Identidade;

/// <summary>
/// Usuário de aplicação. Acrescenta ao Identity o estado ativo, a versão de
/// acesso usada para revogar sessões e o nome de exibição do funcionário.
/// </summary>
public sealed class UsuarioAplicacao : IdentityUser<Guid>
{
    public string Nome { get; set; } = string.Empty;

    /// <summary>
    /// Conta desativada não autentica e não renova sessão. O provisionamento
    /// nunca reativa uma conta existente.
    /// </summary>
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Versão de acesso do usuário. Alterações administrativas de perfis, senha
    /// ou estado incrementam este valor e invalidam sessões emitidas antes.
    /// </summary>
    public long VersaoAcesso { get; set; } = 1;

    /// <summary>
    /// Versão de concorrência da conta, independente do security stamp. É
    /// devolvida como token opaco na administração de acessos.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}
