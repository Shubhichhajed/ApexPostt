using System.Text.Json;
using ApexPost.Api.Middleware;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ApexPost.UnitTests;

public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData(404, "Shipment not found")]
    [InlineData(409, "Shipment conflict")]
    [InlineData(400, "Invalid request")]
    public async Task Known_exceptions_are_mapped_to_problem_details(int expectedStatus, string expectedTitle)
    {
        Exception exception = expectedStatus switch
        {
            404 => new ShipmentNotFoundException("No matching shipment."),
            409 => new ShipmentConflictException("The transition is not allowed."),
            _ => new ArgumentException("Invalid weight.")
        };

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance,
            new TestHostEnvironment { EnvironmentName = Environments.Production });

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(expectedStatus, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Production_500_response_does_not_disclose_exception_message()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance,
            new TestHostEnvironment { EnvironmentName = Environments.Production });

        await handler.TryHandleAsync(context, new InvalidOperationException("secret stack detail"), CancellationToken.None);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var payload = document.RootElement.GetRawText();
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(payload.Contains("secret stack detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_500_response_includes_detail_for_local_debugging()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance,
            new TestHostEnvironment { EnvironmentName = Environments.Development });

        await handler.TryHandleAsync(context, new InvalidOperationException("development diagnostic"), CancellationToken.None);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("development diagnostic", document.RootElement.GetProperty("detail").GetString());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "ApexPost.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
