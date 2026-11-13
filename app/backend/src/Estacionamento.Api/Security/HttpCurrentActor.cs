using System.Security.Claims;
using Estacionamento.Application.Abstractions;

namespace Estacionamento.Api.Security;

public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public IReadOnlyCollection<string> Roles => Principal?.Claims
        .Where(claim => claim.Type == ClaimTypes.Role)
        .Select(claim => claim.Value)
        .Distinct(StringComparer.Ordinal)
        .ToArray() ?? [];
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
