using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.Services;

/// <summary>
/// Document transformer: sets the title, description, version and contact, and registers the
/// Bearer scheme (and, in v2, the partner API key scheme) so Swagger UI shows the Authorize button.
/// </summary>
public sealed class ApexPostOpenApiTransformer : IOpenApiDocumentTransformer
{
    public const string BearerScheme = "Bearer";
    public const string PartnerScheme = "PartnerApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var isV1 = context.DocumentName.Equals("v1", StringComparison.OrdinalIgnoreCase);
        document.Info = new OpenApiInfo
        {
            Title = "ApexPost Logistics API",
            Description = isV1
                ? "Deprecated v1 contract with flat recipient fields. New integrations should use v2."
                : "Shipments, offline courier scan uploads and partner tracking. v2 groups recipient fields, adds latestScan and returns status as a string enum.",
            Version = context.DocumentName,
            Contact = new OpenApiContact { Name = "ApexPost Platform Team", Email = "platform-team@apexpost.example" }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste an access token (POST /dev/token in Development)."
        };
        if (!isV1)
        {
            document.Components.SecuritySchemes[PartnerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "Read-only partner API key."
            };
        }

        if (document.Paths is null)
            return Task.CompletedTask;

        foreach (var path in document.Paths)
        {
            var operations = path.Value.Operations;
            if (operations is null)
                continue;

            var schemeName = path.Key.Contains("/partner/", StringComparison.OrdinalIgnoreCase) ? PartnerScheme : BearerScheme;
            foreach (var operation in operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(schemeName, document)] = []
                });
            }
        }

        return Task.CompletedTask;
    }
}
