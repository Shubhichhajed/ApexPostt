using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.Services;

/// <summary>Copies the Asp.Versioning "deprecated" flag onto each OpenAPI operation so v1 shows as deprecated in Swagger UI.</summary>
public sealed class DeprecatedOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.IsDeprecated())
            operation.Deprecated = true;
        return Task.CompletedTask;
    }
}
