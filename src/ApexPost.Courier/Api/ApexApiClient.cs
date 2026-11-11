using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ApexPost.Courier.Resilience;

namespace ApexPost.Courier.Api;

/// <summary>
/// Typed client for the v2 API, registered with AddHttpClient&lt;ApexApiClient&gt;(). The HttpClient is
/// supplied by IHttpClientFactory (never "new HttpClient()" per call); the bearer token is added by
/// <see cref="Auth.AuthHeaderHandler"/> and retries/circuit breaking/timeouts by the resilience handler.
/// </summary>
public sealed class ApexApiClient(HttpClient httpClient)
{
    public async Task<ScanUploadResponseDto> UploadScansAsync(string trackingNumber, ScanUploadRequestDto body,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"api/v2/shipments/{Uri.EscapeDataString(trackingNumber)}/scans")
        {
            Content = JsonContent.Create(body, CourierJsonContext.Default.ScanUploadRequestDto)
        };
        // Safe to retry: every scan carries its scan::{Guid} ID and the server skips IDs it already has.
        request.Options.Set(ApexApiResilience.IdempotentRequest, true);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredAsync(response, CourierJsonContext.Default.ScanUploadResponseDto, cancellationToken);
    }

    public async Task<ShipmentDto?> GetShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v2/shipments/{Uri.EscapeDataString(trackingNumber)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return await ReadRequiredAsync(response, CourierJsonContext.Default.ShipmentDto, cancellationToken);
    }

    /// <summary>Development only: asks the API's /dev/token endpoint for a Courier token.</summary>
    public async Task<DevTokenResponseDto> RequestDevelopmentTokenAsync(string courierId, CancellationToken cancellationToken = default)
    {
        var body = new DevTokenRequestDto { Role = "Courier", CourierId = courierId, Subject = courierId };
        using var request = new HttpRequestMessage(HttpMethod.Post, "dev/token")
        {
            Content = JsonContent.Create(body, CourierJsonContext.Default.DevTokenRequestDto)
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredAsync(response, CourierJsonContext.Default.DevTokenResponseDto, cancellationToken);
    }

    internal static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SessionExpiredException();

        if (!response.IsSuccessStatusCode)
            throw new ApiProblemException(response.StatusCode, await TryReadProblemAsync(response, cancellationToken));

        try
        {
            var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
            return value ?? throw new MalformedResponseException("The server returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new MalformedResponseException("The server returned a response this app could not read.", ex);
        }
    }

    internal static async Task<ProblemDetailsDto?> TryReadProblemAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(text)
                ? null
                : JsonSerializer.Deserialize(text, CourierJsonContext.Default.ProblemDetailsDto);
        }
        catch (JsonException)
        {
            // Not JSON (for example an HTML error page from a proxy): fall back to the status code message.
            return null;
        }
    }
}
