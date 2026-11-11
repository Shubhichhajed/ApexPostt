using System.Net;
using System.Text;
using System.Text.Json;
using ApexPost.Courier.Api;
using ApexPost.Courier.Auth;
using ApexPost.Courier.Offline;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexPost.Courier.Tests;

public sealed class ApiClientAndUploaderTests
{
    // ---------- JSON (source-generated context) ----------

    [Fact]
    public void Serialises_requests_as_camelCase_without_nulls()
    {
        var json = JsonSerializer.Serialize(ApiShipmentUploader.ToRequest([Scan("APX-12345678")]),
            CourierJsonContext.Default.ScanUploadRequestDto);

        Assert.Contains("\"scans\":[", json);
        Assert.Contains("\"scanType\":\"Pickup\"", json);
        Assert.DoesNotContain("\"notes\"", json);
    }

    [Fact]
    public async Task Unknown_and_null_fields_do_not_break_deserialisation()
    {
        using var response = StubHandler.Json(HttpStatusCode.OK,
            """{"trackingNumber":"APX-12345678","status":"InTransit","recipient":null,"latestScan":null,"brandNewField":{"x":1}}""");

        var shipment = await ApexApiClient.ReadRequiredAsync(response, CourierJsonContext.Default.ShipmentDto, CancellationToken.None);

        Assert.Equal("APX-12345678", shipment.TrackingNumber);
        Assert.Equal(ShipmentStatus.InTransit, shipment.Status);
        Assert.Null(shipment.Recipient);
    }

    [Fact]
    public async Task Malformed_success_body_becomes_a_friendly_error_not_a_crash()
    {
        using var response = StubHandler.Json(HttpStatusCode.OK, """{"trackingNumber": """);

        await Assert.ThrowsAsync<MalformedResponseException>(() =>
            ApexApiClient.ReadRequiredAsync(response, CourierJsonContext.Default.ShipmentDto, CancellationToken.None));
    }

    [Fact]
    public async Task Validation_problem_details_are_turned_into_a_readable_message()
    {
        using var response = StubHandler.Json(HttpStatusCode.BadRequest,
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"Scans[0].Notes":["A Delivered scan requires the recipient signature/name in Notes."]},"traceId":"00-abc"}""",
            "application/problem+json");

        var error = await Assert.ThrowsAsync<ApiProblemException>(() =>
            ApexApiClient.ReadRequiredAsync(response, CourierJsonContext.Default.ShipmentDto, CancellationToken.None));

        Assert.Equal("The server rejected the data: A Delivered scan requires the recipient signature/name in Notes.", error.Message);
        Assert.True(error.IsPermanent);
        Assert.Contains("traceId", error.Problem!.Extensions!.Keys);
    }

    [Fact]
    public async Task Non_json_error_body_still_gives_a_message()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>proxy error</html>", Encoding.UTF8, "text/html")
        };

        var error = await Assert.ThrowsAsync<ApiProblemException>(() =>
            ApexApiClient.ReadRequiredAsync(response, CourierJsonContext.Default.ShipmentDto, CancellationToken.None));

        Assert.Null(error.Problem);
        Assert.Equal("The server had a problem.", error.Message);
        Assert.False(error.IsPermanent);
    }

    [Fact]
    public async Task Unauthorized_response_means_session_expired()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            ApexApiClient.ReadRequiredAsync(response, CourierJsonContext.Default.ShipmentDto, CancellationToken.None));
    }

    // ---------- Uploader: server response -> per-scan outcome ----------

    [Fact]
    public void Rejection_reasons_are_matched_to_scan_ids()
    {
        var parsed = ApiShipmentUploader.ParseRejectionReasons(
        [
            "scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301: Scan timestamp cannot be in the future.",
            "malformed line"
        ]);

        Assert.Equal("Scan timestamp cannot be in the future.", Assert.Single(parsed).Value);
    }

    [Fact]
    public async Task Successful_upload_marks_listed_scans_rejected_and_the_rest_synced()
    {
        var good = Scan("APX-12345678");
        var bad = Scan("APX-12345678");
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK,
            $$"""{"accepted":1,"duplicatesSkipped":0,"rejected":1,"rejectionReasons":["{{bad.Id}}: Unsupported scan type."]}"""));

        var result = await Uploader(stub).UploadScansAsync([good, bad]);

        Assert.Equal(ScanSyncOutcome.Accepted, result.Outcomes[good.Id]);
        Assert.Equal(ScanSyncOutcome.Rejected, result.Outcomes[bad.Id]);
        Assert.Equal("Unsupported scan type.", result.RejectionReasonsByScanId[bad.Id]);
        Assert.Null(result.IncompleteReason);
    }

    [Fact]
    public async Task Scans_are_posted_once_per_tracking_number()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK,
            """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejectionReasons":[]}"""));

        await Uploader(stub).UploadScansAsync([Scan("APX-11111111"), Scan("APX-22222222"), Scan("APX-11111111")]);

        Assert.Equal(2, stub.Calls);
        Assert.Contains("/api/v2/shipments/APX-11111111/scans", stub.Paths);
        Assert.Contains("/api/v2/shipments/APX-22222222/scans", stub.Paths);
    }

    [Fact]
    public async Task Permanent_rejection_marks_the_whole_request_failed()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Forbidden,
            """{"title":"Forbidden","status":403,"detail":"A courier may upload scans only under their own courierId claim."}""",
            "application/problem+json"));
        var scan = Scan("APX-12345678");

        var result = await Uploader(stub).UploadScansAsync([scan]);

        Assert.Equal(ScanSyncOutcome.Rejected, result.Outcomes[scan.Id]);
        Assert.StartsWith("You are not allowed to do that", result.RejectionReasonsByScanId[scan.Id]);
    }

    [Fact]
    public async Task Server_error_leaves_scans_without_an_outcome_so_they_stay_pending()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await Uploader(stub).UploadScansAsync([Scan("APX-11111111"), Scan("APX-22222222")]);

        Assert.Empty(result.Outcomes);
        Assert.NotNull(result.IncompleteReason);
        Assert.Equal(1, stub.Calls); // stops at the first failure instead of hammering the server
    }

    // ---------- Token / configuration helpers ----------

    [Fact]
    public void Courier_id_and_expiry_are_read_from_the_token_payload()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var token = $"{Base64Url("""{"alg":"HS256"}""")}.{Base64Url($$"""{"courierId":"courier-demo-01","exp":{{expires}}}""")}.signature";

        Assert.True(JwtPayloadReader.TryRead(token, out var courierId, out var expiresAt));
        Assert.Equal("courier-demo-01", courierId);
        Assert.Equal(expires, expiresAt!.Value.ToUnixTimeSeconds());
        Assert.False(JwtPayloadReader.TryRead("not-a-jwt", out _, out _));
    }

    [Fact]
    public void Base_url_is_chosen_per_platform_and_must_be_https()
    {
        var options = new ApexApiOptions
        {
            Platform = "AndroidEmulator",
            BaseUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Windows"] = "https://localhost:7001/",
                ["AndroidEmulator"] = "https://10.0.2.2:7001"
            }
        };
        Assert.Equal("https://10.0.2.2:7001/", options.ResolveBaseUrl().AbsoluteUri);

        options.BaseUrls["AndroidEmulator"] = "http://10.0.2.2:5001/";
        Assert.Throws<InvalidOperationException>(() => options.ResolveBaseUrl());
    }

    private static ApiShipmentUploader Uploader(StubHandler stub) => new(
        new ApexApiClient(new HttpClient(stub) { BaseAddress = new Uri("https://api.test/") }),
        NullLogger<ApiShipmentUploader>.Instance);

    private static ShipmentScan Scan(string trackingNumber) => new()
    {
        TrackingNumber = trackingNumber,
        ScanType = ScanType.Pickup,
        CourierId = "courier-demo-01",
        Location = "Indore Hub",
        ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5)
    };

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
