using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Estacionamento.Api.OpenApi;

public sealed class ProblemDetailsOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string ProblemJson = "application/problem+json";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        Apply(document);
        return Task.CompletedTask;
    }

    public static void Apply(OpenApiDocument document)
    {
        if (document.Components?.Schemas is { } schemas
            && schemas.TryGetValue("ProblemDetails", out var schema)
            && schema is OpenApiSchema problemDetails)
        {
            problemDetails.Properties ??= new Dictionary<string, IOpenApiSchema>();
            problemDetails.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String };
            problemDetails.Properties["correlationId"] = new OpenApiSchema { Type = JsonSchemaType.String };
            problemDetails.Properties["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                AdditionalProperties = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            };
        }

        foreach (var (path, pathItem) in document.Paths ?? [])
        {
            if (path.StartsWith("/api/v1/session", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pathItem?.Operations is not { } operations)
            {
                continue;
            }

            foreach (var operation in operations.Values)
            {
                if (operation?.Responses is not { } responses)
                {
                    continue;
                }

                foreach (var (statusCode, response) in responses)
                {
                    if (!IsErrorStatus(statusCode) || response?.Content is not { Count: > 0 } content)
                    {
                        continue;
                    }

                    var problemSchema = content.Values.FirstOrDefault()?.Schema;
                    if (problemSchema is null)
                    {
                        continue;
                    }

                    response.Content.Clear();
                    response.Content[ProblemJson] = new OpenApiMediaType { Schema = problemSchema };
                }
            }
        }
    }

    private static bool IsErrorStatus(string statusCode) =>
        int.TryParse(statusCode, out var status) && status >= 400;
}
