using Estacionamento.Application.Abstractions;
using Estacionamento.Application.Security;
using Estacionamento.Domain.Billing;

namespace Estacionamento.Application.Billing;

public sealed record DailyRateView(Guid Id, decimal Amount, DateTimeOffset PublishedAtUtc, Guid PublishedById);
public sealed record CurrentRateSnapshot(Guid Id, decimal Amount, DateTimeOffset PublishedAtUtc);
public sealed record DailyRateConfigurationView(DailyRateView? CurrentRate, string Version);
public sealed record DailyRatePage(IReadOnlyList<DailyRateView> Items, int Page, int PageSize, int Total);
public sealed record PublishDailyRateCommand(decimal Amount, string BaseVersion);
public enum DailyRatePublishStatus { Published, Invalid, Conflict, Forbidden }

public interface IDailyRateDirectory
{
    Task<DailyRateConfigurationView> GetConfigurationAsync(CancellationToken cancellationToken);
    Task<DailyRatePage> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<CurrentRateSnapshot?> ReadCurrentForAttendanceAsync(CancellationToken cancellationToken);
    Task<DailyRateView> PublishAsync(decimal amount, string baseVersion, Guid publishedById, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken);
}

public sealed class DailyRateAdministration(
    IDailyRateDirectory directory,
    ICurrentActor actor,
    IUseCaseAuthorizer authorizer,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IClock clock)
{
    public async Task<DailyRateConfigurationView?> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        return Allowed() ? await directory.GetConfigurationAsync(cancellationToken) : null;
    }

    public async Task<DailyRatePage?> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!Allowed() || page < 1 || pageSize is < 1 or > 100)
        {
            return null;
        }

        return await directory.ListAsync(page, pageSize, cancellationToken);
    }

    public async Task<DailyRatePublishStatus> PublishAsync(PublishDailyRateCommand command, CancellationToken cancellationToken)
    {
        if (!Allowed())
        {
            return DailyRatePublishStatus.Forbidden;
        }

        try { _ = DailyRateAmount.Create(command.Amount); }
        catch (ArgumentOutOfRangeException) { return DailyRatePublishStatus.Invalid; }

        byte[] expectedVersion;
        try { expectedVersion = Convert.FromBase64String(command.BaseVersion); }
        catch (FormatException) { return DailyRatePublishStatus.Invalid; }
        if (expectedVersion.Length != 8)
        {
            return DailyRatePublishStatus.Invalid;
        }

        try
        {
            return await unitOfWork.ExecuteAsync(async token =>
            {
                var rate = await directory.PublishAsync(command.Amount, command.BaseVersion, actor.UserId!.Value, clock.UtcNow, token);
                await auditWriter.AppendAsync(actor.UserId.Value, "daily_rate.published", rate.Id, clock.UtcNow,
                    new Dictionary<string, string> { ["dailyRateId"] = rate.Id.ToString("D") }, token);
                return DailyRatePublishStatus.Published;
            }, cancellationToken);
        }
        catch (DailyRateConcurrencyException)
        {
            return DailyRatePublishStatus.Conflict;
        }
    }

    private bool Allowed() => actor.UserId is not null && authorizer.IsAllowed(actor.Roles, EmployeeAction.ManageDailyRates);
}

public sealed class DailyRateConcurrencyException : Exception { }
