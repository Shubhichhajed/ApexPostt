using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ApexPost.Client;
using ApexPost.Client.Generated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Console consumer of the NSwag-generated v2 SDK:
// create a shipment, fetch it, upload two scans and handle a 404 through the generated exception type.
var builder = Host.CreateApplicationBuilder(args);
var baseUrl = new Uri(builder.Configuration["ApexApi:BaseUrl"] ?? "https://localhost:7001/");
var courierId = builder.Configuration["ApexApi:CourierId"] ?? "courier-demo-01";

builder.Services.AddSingleton<BearerToken>();
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddApexPostClient(baseUrl).AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddHttpClient("dev-token", client => client.BaseAddress = baseUrl);

using var host = builder.Build();
var api = host.Services.GetRequiredService<IApexPostClient>();
var bearer = host.Services.GetRequiredService<BearerToken>();
var tokens = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("dev-token");

try
{
    // Tokens come from the environment, or from the Development-only /dev/token endpoint.
    var adminToken = Environment.GetEnvironmentVariable("APEXPOST_ADMIN_TOKEN")
        ?? await RequestDevTokenAsync(tokens, new DevTokenRequest("Admin", null, "sdk-consumer-admin"));
    var courierToken = Environment.GetEnvironmentVariable("APEXPOST_COURIER_TOKEN")
        ?? await RequestDevTokenAsync(tokens, new DevTokenRequest("Courier", courierId, "sdk-consumer-courier"));

    var trackingNumber = $"APX-{Random.Shared.Next(10_000_000, 100_000_000):D8}";

    // 1. Create (Admin)
    bearer.Value = adminToken;
    var created = await api.CreateShipmentV2Async(new CreateShipmentRequest
    {
        TrackingNumber = trackingNumber,
        Weight = 2.5,
        RecipientName = "Alex Morgan",
        RecipientPhone = "+44 20 7946 0958",
        RecipientAddress = "100 Example Avenue",
        RecipientCity = "Springfield",
        Notes = "Fictional SDK consumer test"
    });
    Console.WriteLine($"Created : {created.Summary}");

    // 2. Fetch
    var fetched = await api.GetShipmentV2Async(trackingNumber);
    Console.WriteLine($"Fetched : {fetched.Summary}");

    // 3. Upload two scans (Courier; courierId must match the token claim)
    bearer.Value = courierToken;
    var upload = await api.UploadScansV2Async(trackingNumber, new ScanUploadRequest
    {
        Scans = new List<ScanItemRequest>
        {
            new() { ScanId = $"scan::{Guid.NewGuid()}", ScanType = "Pickup", CourierId = courierId,
                    Location = "Springfield Depot", ScannedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10) },
            new() { ScanId = $"scan::{Guid.NewGuid()}", ScanType = "InTransit", CourierId = courierId,
                    Location = "Springfield Route 4", ScannedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }
        }
    });
    Console.WriteLine($"Scans   : accepted={upload.Accepted}, duplicatesSkipped={upload.DuplicatesSkipped}, rejected={upload.Rejected}");

    // 4. A 404 handled through the generated exception type
    try
    {
        await api.GetShipmentV2Async("APX-00000000");
    }
    catch (ApexPostApiException<ProblemDetails> ex) when (ex.StatusCode == 404)
    {
        Console.WriteLine($"404     : {ex.Result.Title} - {ex.Result.Detail}");
    }

    // The hand-written wrapper turns the same 404 into null.
    var missing = await api.FindShipmentAsync("APX-00000001");
    Console.WriteLine($"Wrapper : FindShipmentAsync returned {(missing is null ? "null" : missing.Summary)}");
}
catch (ApexPostApiException ex)
{
    Console.Error.WriteLine($"API error {ex.StatusCode}: {ex.Message}");
    Environment.ExitCode = 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not reach the API at {baseUrl}: {ex.Message}");
    Environment.ExitCode = 1;
}

static async Task<string> RequestDevTokenAsync(HttpClient client, DevTokenRequest request)
{
    using var response = await client.PostAsJsonAsync("dev/token", request);
    response.EnsureSuccessStatusCode();
    var token = await response.Content.ReadFromJsonAsync<DevTokenResponse>();
    return token?.AccessToken ?? throw new InvalidOperationException("The token endpoint returned no access_token.");
}

/// <summary>Holds the bearer token for the current step (Admin or Courier).</summary>
internal sealed class BearerToken
{
    public string? Value { get; set; }
}

/// <summary>Adds the current bearer token to every SDK request.</summary>
internal sealed class BearerTokenHandler(BearerToken token) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(token.Value))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed record DevTokenRequest(string Role, string? CourierId, string? Subject);

internal sealed record DevTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
