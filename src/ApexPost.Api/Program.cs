using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Asp.Versioning;
using ApexPost.Api.Data;
using ApexPost.Api.Middleware;
using ApexPost.Api.Security;
using ApexPost.Api.Services;
using ApexPost.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Composition root and pipeline configuration only. Business rules live in IShipmentService.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<RouteOptions>(options =>
    options.ConstraintMap["trackingNumber"] = typeof(TrackingNumberRouteConstraint));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();

// ---- Versioning: /api/v{version}/..., v1 deprecated with a published sunset date ----
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(2, 0);
    options.AssumeDefaultVersionWhenUnspecified = false;
    options.ReportApiVersions = true; // api-supported-versions / api-deprecated-versions headers
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
    options.Policies.Sunset(1.0)
        .Effective(new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero))
        .Link("https://developer.apexpost.example/api/versioning")
            .Title("ApexPost API versioning and sunset policy")
            .Type("text/html");
}).AddMvc().AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// ---- OpenAPI: the built-in generator is the only generator (Swagger UI only renders its output) ----
foreach (var documentName in new[] { "v1", "v2" })
{
    builder.Services.AddOpenApi(documentName, options =>
    {
        // OpenAPI 3.0 keeps the document compatible with NSwag, which generates the client SDK.
        options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
        options.ShouldInclude = description => description.GroupName == documentName;
        options.AddDocumentTransformer<ApexPostOpenApiTransformer>();
        options.AddOperationTransformer<DeprecatedOperationTransformer>();
    });
}

// ---- JWT bearer authentication: issuer, audience, lifetime and signing key all validated ----
var issuer = builder.Configuration["Auth:Issuer"];
var audience = builder.Configuration["Auth:Audience"];
var signingKey = builder.Configuration["Auth:SigningKey"];
if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) ||
    string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException(
        "Configure Auth:Issuer, Auth:Audience and a signing key of at least 32 bytes (Auth:SigningKey) with dotnet user-secrets.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            // 401 for missing, malformed or expired tokens, as ProblemDetails.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
                context.Response.Headers.WWWAuthenticate = expired
                    ? "Bearer error=\"invalid_token\", error_description=\"The token has expired\""
                    : "Bearer";
                await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized",
                    detail: expired ? "The access token has expired." : "A valid bearer token is required.")
                    .ExecuteAsync(context.HttpContext);
            },
            // 403 when the token is valid but the role is wrong.
            OnForbidden = context => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                    detail: "Your role is not permitted to perform this operation.")
                .ExecuteAsync(context.HttpContext)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiRoles.Courier, policy => policy.RequireAuthenticatedUser().RequireRole(ApiRoles.Courier))
    .AddPolicy(ApiRoles.Admin, policy => policy.RequireAuthenticatedUser().RequireRole(ApiRoles.Admin));

// ---- Week 3 data layer reused unchanged; the API adds its own metadata/scan tables ----
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ConnectionStrings:DefaultConnection with dotnet user-secrets.");
builder.Services.AddDbContext<ApexPostDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDbContext<ApexPostApiDbContext>(options => options.UseSqlServer(connectionString,
    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_ApexPostApi")));

builder.Services.AddScoped<ShipmentMetadataStore>();
builder.Services.AddScoped<IShipmentService, ShipmentService>();
builder.Services.AddSingleton<IPartnerApiKeyValidator, PartnerApiKeyValidator>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", app.Environment.IsDevelopment()))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    await migrationScope.ServiceProvider.GetRequiredService<ApexPostDbContext>().Database.MigrateAsync();
    await migrationScope.ServiceProvider.GetRequiredService<ApexPostApiDbContext>().Database.MigrateAsync();
}

// ---- Pipeline (order matters) ----
// 1. Exception handler first so it catches failures from everything after it.
app.UseExceptionHandler();
// 2. Empty error responses (404 from routing, 405, 415) get a ProblemDetails body.
app.UseStatusCodePages();
// 3. Transport security: HSTS outside Development, HTTP -> HTTPS everywhere.
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
// 4. Routing selects the endpoint so the next middleware can read its metadata.
app.UseRouting();
// 5. Partner X-Api-Key check (only endpoints marked [RequirePartnerApiKey]).
app.UseMiddleware<PartnerApiKeyMiddleware>();
// 6. JWT authentication, then role-based authorization.
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // Documentation is a Development convenience only. Hiding it elsewhere is not a security control;
    // every endpoint is still protected by authentication/authorization or the partner key.
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "v1 (deprecated)");
        options.SwaggerEndpoint("/openapi/v2.json", "v2");
        options.EnablePersistAuthorization();
    });

    // Development-only token issuer used instead of a real identity provider (see SECURITY.md).
    app.MapPost("/dev/token", (DevTokenRequest request, IConfiguration configuration) =>
    {
        if (request.Role is not (ApiRoles.Courier or ApiRoles.Admin))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid role",
                detail: "Role must be Courier or Admin.");
        if (request.Role == ApiRoles.Courier && string.IsNullOrWhiteSpace(request.CourierId))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "courierId required",
                detail: "Courier tokens must carry a courierId claim.");

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(request.LifetimeMinutes ?? 60, 1, 480));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Subject ?? request.CourierId ?? "dev-admin"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, request.Subject ?? request.CourierId ?? "dev-admin"),
            new(ClaimTypes.Role, request.Role)
        };
        if (!string.IsNullOrWhiteSpace(request.CourierId))
            claims.Add(new Claim(ApiClaims.CourierId, request.CourierId));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:SigningKey"]!)), SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(configuration["Auth:Issuer"], configuration["Auth:Audience"], claims,
            notBefore: now, expires: now.Add(lifetime), signingCredentials: credentials);
        return Results.Ok(new DevTokenResponse(new JwtSecurityTokenHandler().WriteToken(jwt), "Bearer", (int)lifetime.TotalSeconds));
    }).AllowAnonymous().ExcludeFromDescription();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound,
    title: "API route not found", detail: "Check the version segment and the tracking number format (APX-12345678)."));

app.Run();

public partial class Program
{
}

/// <summary>Development token request. Role is Courier or Admin; couriers also need a courierId.</summary>
public sealed record DevTokenRequest(string Role, string? CourierId, string? Subject = null, int? LifetimeMinutes = null);

/// <summary>Development token response.</summary>
public sealed record DevTokenResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
