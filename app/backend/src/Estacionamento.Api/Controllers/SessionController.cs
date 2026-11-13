using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Estacionamento.Api.Controllers;

[ApiController]
[Route("api/v1/session")]
public sealed class SessionController(
    IAntiforgery antiforgery,
    SignInManager<EmployeeAccount> signInManager,
    UserManager<EmployeeAccount> userManager,
    ILogger<SessionController> logger)
    : ControllerBase
{
    [HttpGet("antiforgery")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AntiforgeryResponse), StatusCodes.Status200OK)]
    public IActionResult GetAntiforgeryToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryResponse(tokens.RequestToken!, "X-CSRF-TOKEN"));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning(
                "Employee sign-in was rejected because the account is absent or inactive. AccountFound={AccountFound}, AccountActive={AccountActive}.",
                user is not null,
                user?.IsActive);
            return Unauthorized(new { error = "Credenciais inválidas." });
        }

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Employee sign-in was rejected. LockedOut={LockedOut}, NotAllowed={NotAllowed}, RequiresTwoFactor={RequiresTwoFactor}.",
                result.IsLockedOut,
                result.IsNotAllowed,
                result.RequiresTwoFactor);
            return Unauthorized(new { error = "Credenciais inválidas." });
        }

        return Ok(await ToSessionResponse(user));
    }

    [HttpGet]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSession()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        return Ok(await ToSessionResponse(user));
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("admin/verify")]
    [Authorize(Roles = EmployeeRoles.Administrator)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult VerifyAdministrator()
        => Ok(new { authorized = true });

    private async Task<SessionResponse> ToSessionResponse(EmployeeAccount user)
        => new(user.Id, user.UserName ?? string.Empty, await userManager.GetRolesAsync(user));
}

public sealed record LoginRequest(string Email, string Password);
public sealed record AntiforgeryResponse(string RequestToken, string HeaderName);
public sealed record SessionResponse(Guid UserId, string UserName, IList<string> Roles);
