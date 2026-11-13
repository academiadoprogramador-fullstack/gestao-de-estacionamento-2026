using System.Text.Json;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class OpenApiErrorContractTests
{
    [TestMethod]
    public void Generated_document_describes_operational_problem_details_and_preserves_session_contract()
    {
        var documentPath = FindGeneratedDocument();
        using var document = JsonDocument.Parse(File.ReadAllText(documentPath));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var operationalErrorCount = 0;

        foreach (var path in paths.EnumerateObject().Where(path => !path.Name.StartsWith("/api/v1/session", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var operation in path.Value.EnumerateObject().Where(property => IsHttpMethod(property.Name)))
            {
                foreach (var response in operation.Value.GetProperty("responses").EnumerateObject())
                {
                    if (!IsErrorStatus(response.Name))
                    {
                        continue;
                    }

                    operationalErrorCount++;
                    var content = response.Value.GetProperty("content");
                    Assert.AreEqual(1, content.EnumerateObject().Count(), $"Unexpected media types for {path.Name} {response.Name}.");
                    var problem = content.GetProperty("application/problem+json");
                    Assert.AreEqual("#/components/schemas/ProblemDetails", problem.GetProperty("schema").GetProperty("$ref").GetString());
                }
            }
        }

        Assert.IsTrue(operationalErrorCount > 0, "No operational error responses were found in the generated document.");

        var problemDetails = root.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails");
        var properties = problemDetails.GetProperty("properties");
        Assert.AreEqual("string", properties.GetProperty("code").GetProperty("type").GetString());
        Assert.AreEqual("string", properties.GetProperty("correlationId").GetProperty("type").GetString());
        var errors = properties.GetProperty("errors");
        Assert.AreEqual("object", errors.GetProperty("type").GetString());
        Assert.AreEqual("array", errors.GetProperty("additionalProperties").GetProperty("type").GetString());
        Assert.AreEqual("string", errors.GetProperty("additionalProperties").GetProperty("items").GetProperty("type").GetString());

        if (problemDetails.TryGetProperty("required", out var required))
        {
            Assert.IsFalse(required.EnumerateArray().Any(item => item.GetString() is "code" or "correlationId" or "errors"));
        }

        var sessionLoginUnauthorized = paths.GetProperty("/api/v1/session/login").GetProperty("post")
            .GetProperty("responses").GetProperty("401").GetProperty("content");
        CollectionAssert.AreEquivalent(
            new[] { "text/plain", "application/json", "text/json" },
            sessionLoginUnauthorized.EnumerateObject().Select(mediaType => mediaType.Name).ToArray());
    }

    private static string FindGeneratedDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solution = Path.Combine(directory.FullName, "Estacionamento.slnx");
            var document = Path.Combine(directory.FullName, "src", "Estacionamento.Api", "obj", "Estacionamento.Api.json");
            if (File.Exists(solution) && File.Exists(document))
            {
                return document;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Build the API to generate src/Estacionamento.Api/obj/Estacionamento.Api.json before running this contract test.");
    }

    private static bool IsHttpMethod(string value) => value is "get" or "post" or "put" or "patch" or "delete" or "options" or "head";

    private static bool IsErrorStatus(string value) => int.TryParse(value, out var status) && status >= 400;
}
