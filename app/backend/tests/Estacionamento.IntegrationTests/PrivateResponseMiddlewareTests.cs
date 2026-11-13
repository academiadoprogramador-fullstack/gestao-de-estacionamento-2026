using Estacionamento.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class PrivateResponseMiddlewareTests
{
    [TestMethod]
    public async Task Api_responses_are_not_cacheable_and_unexpected_failure_is_generic()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/synthetic-sensitive-read";
        context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<PrivateResponseMiddleware>();
        var sensitiveValues = new[] { "synthetic-document-secret", "synthetic-inspection-secret", "synthetic-password-secret", "synthetic-cookie-secret" };
        var middleware = new PrivateResponseMiddleware(
            _ => throw new InvalidOperationException(string.Join(" ", sensitiveValues)),
            logger);

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.AreEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.AreEqual("private, no-store, no-cache, max-age=0", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(context.Response.Headers["X-Correlation-ID"]));
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        StringAssert.Contains(responseBody, "unexpected_error");
        StringAssert.Contains(responseBody, "correlationId");
        Assert.IsFalse(responseBody.Contains("InvalidOperationException", StringComparison.Ordinal));
        foreach (var sensitiveValue in sensitiveValues)
        {
            Assert.IsFalse(responseBody.Contains(sensitiveValue, StringComparison.Ordinal));
            Assert.IsFalse(string.Join("\n", logger.Messages).Contains(sensitiveValue, StringComparison.Ordinal));
        }

        var denied = new DefaultHttpContext();
        denied.Request.Path = "/api/v1/synthetic-private-resource";
        denied.Response.Body = new MemoryStream();
        await new PrivateResponseMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }, logger).InvokeAsync(denied);
        await denied.Response.StartAsync();
        Assert.AreEqual(StatusCodes.Status403Forbidden, denied.Response.StatusCode);
        Assert.AreEqual("private, no-store, no-cache, max-age=0", denied.Response.Headers.CacheControl.ToString());
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
