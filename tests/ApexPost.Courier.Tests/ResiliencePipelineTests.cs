using System.Net;
using ApexPost.Courier.Api;
using ApexPost.Courier.Auth;
using ApexPost.Courier.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace ApexPost.Courier.Tests;

public sealed class ResiliencePipelineTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Conflict, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public void Only_408_and_5xx_responses_are_transient(HttpStatusCode status, bool expected)
    {
        using var response = new HttpResponseMessage(status);
        Assert.Equal(expected, ApexApiResilience.IsTransient(Outcome.FromResult(response)));
    }

    [Fact]
    public void Network_errors_and_attempt_timeouts_are_transient_other_exceptions_are_not()
    {
        Assert.True(ApexApiResilience.IsTransient(Outcome.FromException<HttpResponseMessage>(new HttpRequestException("refused"))));
        Assert.True(ApexApiResilience.IsTransient(Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException())));
        Assert.False(ApexApiResilience.IsTransient(Outcome.FromException<HttpResponseMessage>(new InvalidOperationException())));
    }

    [Fact]
    public void Only_idempotent_requests_may_be_retried()
    {
        Assert.True(ApexApiResilience.IsRetryAllowed(new HttpRequestMessage(HttpMethod.Get, "https://api.test/x")));
        Assert.True(ApexApiResilience.IsRetryAllowed(new HttpRequestMessage(HttpMethod.Put, "https://api.test/x")));
        Assert.False(ApexApiResilience.IsRetryAllowed(new HttpRequestMessage(HttpMethod.Post, "https://api.test/x")));
        Assert.False(ApexApiResilience.IsRetryAllowed(new HttpRequestMessage(HttpMethod.Patch, "https://api.test/x")));
        Assert.False(ApexApiResilience.IsRetryAllowed(null));

        var upload = new HttpRequestMessage(HttpMethod.Post, "https://api.test/api/v2/shipments/APX-12345678/scans");
        upload.Options.Set(ApexApiResilience.IdempotentRequest, true);
        Assert.True(ApexApiResilience.IsRetryAllowed(upload));
    }

    [Fact]
    public async Task Scan_upload_is_retried_on_503_and_then_succeeds()
    {
        var stub = new StubHandler((_, call) => call < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : StubHandler.Json(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejectionReasons":[]}"""));
        await using var provider = BuildProvider(stub);
        var client = provider.GetRequiredService<ApexApiClient>();

        var result = await client.UploadScansAsync("APX-12345678", OneScan());

        Assert.Equal(1, result.Accepted);
        Assert.Equal(3, stub.Calls);
        Assert.Equal(2, provider.GetRequiredService<ConnectionState>().RetryCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Client_errors_are_never_retried(HttpStatusCode status)
    {
        var stub = new StubHandler((_, _) => StubHandler.Json(status, """{"title":"Rejected","status":400}""", "application/problem+json"));
        await using var provider = BuildProvider(stub);

        var error = await Assert.ThrowsAsync<ApiProblemException>(() =>
            provider.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-12345678", OneScan()));

        Assert.Equal(status, error.StatusCode);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Non_idempotent_post_is_not_retried()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(stub);

        await Assert.ThrowsAsync<ApiProblemException>(() =>
            provider.GetRequiredService<ApexApiClient>().RequestDevelopmentTokenAsync("courier-demo-01"));

        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Circuit_opens_after_repeated_failures_and_then_fails_fast()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await using var provider = BuildProvider(stub);
        var client = provider.GetRequiredService<ApexApiClient>();
        var state = provider.GetRequiredService<ConnectionState>();

        // One logical call = 1 attempt + 3 retries = 4 failures, which meets MinimumThroughput with a 100% failure ratio.
        await Assert.ThrowsAsync<ApiProblemException>(() => client.UploadScansAsync("APX-12345678", OneScan()));
        Assert.Equal(CircuitStatus.Open, state.Circuit);
        Assert.True(state.ShowOfflineBanner);

        var callsWhenOpened = stub.Calls;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.UploadScansAsync("APX-12345678", OneScan()));
        Assert.Equal(callsWhenOpened, stub.Calls); // the server was not called while the circuit was open
    }

    private static ScanUploadRequestDto OneScan() => new()
    {
        Scans =
        [
            new ScanItemDto
            {
                ScanId = $"scan::{Guid.NewGuid()}", ScanType = "Pickup", CourierId = "courier-demo-01",
                Location = "Indore Hub", ScannedAtUtc = DateTime.UtcNow
            }
        ]
    };

    private static ServiceProvider BuildProvider(StubHandler stub)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ResilienceSettings>(settings =>
        {
            settings.BaseDelaySeconds = 0.01;
            settings.AttemptTimeoutSeconds = 5;
            settings.TotalTimeoutSeconds = 30;
            settings.BreakDurationSeconds = 30;
        });
        services.AddSingleton<ConnectionState>();
        services.AddSingleton<ITokenStore>(new InMemoryTokenStore("aaa.bbb.ccc"));
        services.AddSingleton<SessionEvents>();
        services.AddTransient<AuthHeaderHandler>();
        services.AddHttpClient<ApexApiClient>(client => client.BaseAddress = new Uri("https://api.test/"))
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddResilienceHandler(ApexApiResilience.PipelineName, ApexApiResilience.Configure);
        return services.BuildServiceProvider();
    }
}
