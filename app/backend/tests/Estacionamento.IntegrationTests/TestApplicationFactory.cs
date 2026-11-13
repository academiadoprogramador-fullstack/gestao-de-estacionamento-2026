using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Estacionamento.Application.Abstractions;
using Estacionamento.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.SqlClient;

namespace Estacionamento.IntegrationTests;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    public string KeyRingPath { get; } = Path.Combine(Path.GetTempPath(), $"estacionamento-api-test-{Guid.NewGuid():N}");
    public TestLogCapture LogCapture { get; } = new();
    private readonly Dictionary<string, string?> originalEnvironment = [];

    private readonly string environmentName;

    private readonly bool failSensitiveReadAudit;

    public TestApplicationFactory(
        string environmentName = "Testing",
        string? connectionString = null,
        bool omitValetPassword = false,
        bool failSensitiveReadAudit = false)
    {
        var configuredConnection = connectionString ?? Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to a dedicated Estacionamento* SQL Server test database.");
        DedicatedSqlTestDatabase.ParseAndValidate(configuredConnection, "TEST_SQLSERVER_CONNECTION");
        this.environmentName = environmentName;
        this.failSensitiveReadAudit = failSensitiveReadAudit;
        SetEnvironmentVariable("ConnectionStrings__Identity", configuredConnection);
        SetEnvironmentVariable("Security__SpaOrigin", "https://localhost");
        SetEnvironmentVariable("DataProtection__KeyRingPath", KeyRingPath);
        SetEnvironmentVariable("Idempotency__Hmac__ActiveKeyVersion", "1");
        SetEnvironmentVariable("Idempotency__Hmac__Keys__1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        SetEnvironmentVariable("Authentication__IdleTimeoutSeconds", "2");
        SetEnvironmentVariable("SyntheticAccounts__ValetPassword", omitValetPassword ? "" : Environment.GetEnvironmentVariable("TEST_VALET_PASSWORD"));
        SetEnvironmentVariable("SyntheticAccounts__CashierPassword", Environment.GetEnvironmentVariable("TEST_CASHIER_PASSWORD"));
        SetEnvironmentVariable("SyntheticAccounts__AdministratorPassword", Environment.GetEnvironmentVariable("TEST_ADMIN_PASSWORD"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));
        if (failSensitiveReadAudit)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditWriter>();
                services.AddScoped<IAuditWriter>(provider =>
                    new FailingSensitiveReadAuditWriter(provider.GetRequiredService<EfAuditStore>()));
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(KeyRingPath))
        {
            Directory.Delete(KeyRingPath, recursive: true);
        }

        if (disposing)
        {
            foreach (var (key, value) in originalEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private void SetEnvironmentVariable(string key, string? value)
    {
        originalEnvironment.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }

    private sealed class FailingSensitiveReadAuditWriter(IAuditWriter inner) : IAuditWriter
    {
        public Task AppendAsync(
            Guid actorId,
            string action,
            Guid? targetId,
            DateTimeOffset occurredAtUtc,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            if (action is "attendance.document.read" or "attendance.entry-inspection.read")
            {
                throw new InvalidOperationException("Injected sensitive-read audit failure.");
            }

            return inner.AppendAsync(actorId, action, targetId, occurredAtUtc, metadata, cancellationToken);
        }
    }
}

public sealed class TestLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entries = new();
    public IReadOnlyCollection<string> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, entries);
    public void Dispose() { }

    private sealed class CaptureLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue($"{category}: {formatter(state, exception)} {exception?.Message}");
            }
        }
    }
}
