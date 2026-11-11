using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexPost.Courier.Api;

// DTOs that mirror the v2 API contract. They are separate from the offline domain model
// (ShipmentScan) so neither side leaks into the other.

public sealed class ScanUploadRequestDto
{
    public List<ScanItemDto> Scans { get; set; } = [];
}

public sealed class ScanItemDto
{
    public string ScanId { get; set; } = string.Empty;
    public string ScanType { get; set; } = string.Empty;
    public string CourierId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime ScannedAtUtc { get; set; }
    public string? Notes { get; set; }
}

public sealed class ScanUploadResponseDto
{
    public int Accepted { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int Rejected { get; set; }
    public List<string>? RejectionReasons { get; set; }
}

public enum ShipmentStatus
{
    Created,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed
}

public sealed class ShipmentDto
{
    public string? TrackingNumber { get; set; }
    public ShipmentStatus? Status { get; set; }
    public decimal? Weight { get; set; }
    public RecipientDto? Recipient { get; set; }
    public LatestScanDto? LatestScan { get; set; }
    public string? Notes { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
}

public sealed class RecipientDto
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
}

public sealed class LatestScanDto
{
    public string? ScanType { get; set; }
    public string? Location { get; set; }
    public DateTime? ScannedAtUtc { get; set; }
}

/// <summary>RFC 7807 body. Also covers ValidationProblemDetails through <see cref="Errors"/>.</summary>
public sealed class ProblemDetailsDto
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public string? Instance { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    /// <summary>Anything else the server sends (traceId and so on) is kept rather than causing an error.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public sealed class DevTokenRequestDto
{
    public string Role { get; set; } = "Courier";
    public string? CourierId { get; set; }
    public string? Subject { get; set; }
}

public sealed class DevTokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }
}

/// <summary>
/// Source-generated serializer metadata shared by every API call: camelCase names, case-insensitive
/// reads and enums as strings, matching the v2 API. No reflection is needed at runtime.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScanUploadRequestDto))]
[JsonSerializable(typeof(ScanUploadResponseDto))]
[JsonSerializable(typeof(ShipmentDto))]
[JsonSerializable(typeof(ProblemDetailsDto))]
[JsonSerializable(typeof(DevTokenRequestDto))]
[JsonSerializable(typeof(DevTokenResponseDto))]
public partial class CourierJsonContext : JsonSerializerContext
{
}
