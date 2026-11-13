using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;

namespace Estacionamento.Api.Filters;

public sealed class ApiProblemDetailsFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Path.StartsWithSegments("/api/v1/session"))
        {
            var source = context.Result switch
            {
                ObjectResult { StatusCode: >= 400 } result => (result, result.StatusCode!.Value),
                StatusCodeResult { StatusCode: >= 400 } result => (new ObjectResult(null), result.StatusCode),
                ForbidResult => (new ObjectResult(null), StatusCodes.Status403Forbidden),
                UnauthorizedResult => (new ObjectResult(null), StatusCodes.Status401Unauthorized),
                _ => ((ObjectResult?)null, 0)
            };
            if (source.Item1 is not null)
            {
                context.Result = ToProblemDetails(context.HttpContext, source.Item1, source.Item2);
            }
        }

        await next();
    }

    private static ObjectResult ToProblemDetails(HttpContext context, ObjectResult source, int status)
    {
        var code = source.Value?.GetType().GetProperty("code")?.GetValue(source.Value)?.ToString()
            ?? status switch
            {
                401 => "unauthenticated",
                403 => "forbidden",
                >= 500 => "internal_error",
                _ => "request_rejected"
            };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Type = $"https://httpstatuses.com/{status}",
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = context.Response.Headers["X-Correlation-ID"].ToString();

        if (source.Value is ValidationProblemDetails validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
