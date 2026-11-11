using ApexPost.Api.Security;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexPost.UnitTests;

/// <summary>The X-Api-Key check runs only on endpoints marked [RequirePartnerApiKey] and fails closed.</summary>
public sealed class PartnerApiKeyMiddlewareTests
{
    [Fact]
    public async Task Unmarked_endpoints_are_not_checked()
    {
        var (context, nextCalled) = await RunAsync(configuredKey: "partner-secret", suppliedKey: null, marked: false);
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public async Task Missing_or_invalid_key_returns_401(string? suppliedKey)
    {
        var (context, nextCalled) = await RunAsync("partner-secret", suppliedKey, marked: true);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Valid_key_reaches_the_endpoint()
    {
        var (context, nextCalled) = await RunAsync("partner-secret", "partner-secret", marked: true);
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Unconfigured_key_returns_500_instead_of_letting_requests_through()
    {
        var (context, nextCalled) = await RunAsync(configuredKey: null, suppliedKey: "anything", marked: true);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    private static async Task<(HttpContext Context, bool NextCalled)> RunAsync(string? configuredKey, string? suppliedKey, bool marked)
    {
        var configuration = new ConfigurationManager();
        if (configuredKey is not null)
            configuration[PartnerApiKeyValidator.ConfigurationKey] = configuredKey;

        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        if (suppliedKey is not null)
            context.Request.Headers[PartnerApiKeyMiddleware.HeaderName] = suppliedKey;
        var metadata = marked ? new EndpointMetadataCollection(new RequirePartnerApiKeyAttribute()) : new EndpointMetadataCollection();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

        var nextCalled = false;
        var middleware = new PartnerApiKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<PartnerApiKeyMiddleware>.Instance);
        await middleware.InvokeAsync(context, new PartnerApiKeyValidator(configuration));
        return (context, nextCalled);
    }
}
