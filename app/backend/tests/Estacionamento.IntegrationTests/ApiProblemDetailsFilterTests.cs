using Estacionamento.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class ApiProblemDetailsFilterTests
{
    [TestMethod]
    public async Task Business_errors_become_problem_details_with_code_and_correlation_id()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/spaces";
        context.Response.Headers["X-Correlation-ID"] = "correlation-test";
        var actionContext = new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(), new ModelStateDictionary());
        var resultContext = new ResultExecutingContext(actionContext, [], new BadRequestObjectResult(new { code = "invalid_space_query" }), new object());
        var filter = new ApiProblemDetailsFilter();

        await filter.OnResultExecutionAsync(resultContext, () => Task.FromResult(new ResultExecutedContext(actionContext, [], resultContext.Result, new object())));

        var response = (ObjectResult)resultContext.Result;
        var problem = (ProblemDetails)response.Value!;
        Assert.AreEqual(400, problem.Status);
        Assert.AreEqual("application/problem+json", response.ContentTypes.Single());
        Assert.AreEqual("invalid_space_query", problem.Extensions["code"]);
        Assert.AreEqual("correlation-test", problem.Extensions["correlationId"]);
    }

    [TestMethod]
    public async Task Session_response_contract_is_not_rewritten()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/session/login";
        var original = new UnauthorizedObjectResult(new { error = "Credenciais inválidas." });
        var actionContext = new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(), new ModelStateDictionary());
        var resultContext = new ResultExecutingContext(actionContext, [], original, new object());

        await new ApiProblemDetailsFilter().OnResultExecutionAsync(resultContext, () => Task.FromResult(new ResultExecutedContext(actionContext, [], resultContext.Result, new object())));

        Assert.AreSame(original, resultContext.Result);
    }

    [TestMethod]
    public async Task Validation_problem_details_preserve_field_errors_without_echoing_submitted_values()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/daily-rates";
        context.Response.Headers["X-Correlation-ID"] = "validation-correlation";
        var actionContext = new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(), new ModelStateDictionary());
        var validation = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["amount"] = ["Informe valor positivo."]
        });
        var resultContext = new ResultExecutingContext(actionContext, [], new BadRequestObjectResult(validation), new object());

        await new ApiProblemDetailsFilter().OnResultExecutionAsync(resultContext, () => Task.FromResult(new ResultExecutedContext(actionContext, [], resultContext.Result, new object())));

        var problem = (ProblemDetails)((ObjectResult)resultContext.Result).Value!;
        Assert.AreEqual("validation-correlation", problem.Extensions["correlationId"]);
        Assert.IsTrue(problem.Extensions.ContainsKey("errors"));
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(problem).Contains("submitted-secret", StringComparison.Ordinal));
    }
}
