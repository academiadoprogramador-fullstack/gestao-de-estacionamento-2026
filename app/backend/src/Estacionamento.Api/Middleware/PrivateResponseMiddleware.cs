using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Middleware;

public sealed class PrivateResponseMiddleware(RequestDelegate next, ILogger<PrivateResponseMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        await ApplyHeaders(context, correlationId);
        context.Response.OnStarting(() => ApplyHeaders(context, correlationId));

        try
        {
            await next(context);
        }
        catch (Exception) when (!context.Response.HasStarted)
        {
            logger.LogError("Unhandled request failure. CorrelationId={CorrelationId} TraceId={TraceId}", correlationId, Activity.Current?.Id);
            context.Response.Clear();
            await ApplyHeaders(context, correlationId);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "about:blank",
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Extensions = { ["code"] = "unexpected_error", ["correlationId"] = correlationId }
            });
        }
    }

    private static Task ApplyHeaders(HttpContext context, string correlationId)
    {
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        }

        return Task.CompletedTask;
    }
}
