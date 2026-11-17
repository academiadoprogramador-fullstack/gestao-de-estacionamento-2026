using Estacionamento.Api.Autenticacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Endpoint administrativo mínimo da fundação. Comprova que a autorização por
/// perfil é aplicada no servidor independentemente da interface.
/// </summary>
[ApiController]
[Route("api/v1/administracao")]
[Authorize(Policy = PoliticasAutorizacao.Administrador)]
public sealed class AdministracaoController : ControllerBase
{
    [HttpGet("verificacao")]
    public IActionResult Verificacao() =>
        Ok(new
        {
            escopo = "administracao",
            descricao = "Acesso administrativo confirmado pelo servidor."
        });
}
