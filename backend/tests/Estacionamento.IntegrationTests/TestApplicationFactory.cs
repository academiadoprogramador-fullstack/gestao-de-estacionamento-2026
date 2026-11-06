using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Estacionamento.IntegrationTests;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    public string KeyRingPath { get; } = Path.Combine(Path.GetTempPath(), $"estacionamento-api-test-{Guid.NewGuid():N}");
    public TestLogCapture LogCapture { get; } = new();
    private readonly Dictionary<string, string?> originalEnvironment = [];

    private readonly string environmentName;

    public TestApplicationFactory(string environmentName = "Testing", string? connectionString = null, bool omitValetPassword = false)
    {
        this.environmentName = environmentName;
        SetEnvironmentVariable("ConnectionStrings__Identity", connectionString ?? Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION"));
        SetEnvironmentVariable("Security__SpaOrigin", "https://localhost");
        SetEnvironmentVariable("DataProtection__KeyRingPath", KeyRingPath);
        SetEnvironmentVariable("Authentication__IdleTimeoutSeconds", "2");
        SetEnvironmentVariable("SyntheticAccounts__ValetPassword", omitValetPassword ? "" : Environment.GetEnvironmentVariable("TEST_VALET_PASSWORD"));
        SetEnvironmentVariable("SyntheticAccounts__CashierPassword", Environment.GetEnvironmentVariable("TEST_CASHIER_PASSWORD"));
        SetEnvironmentVariable("SyntheticAccounts__AdministratorPassword", Environment.GetEnvironmentVariable("TEST_ADMIN_PASSWORD"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));
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
