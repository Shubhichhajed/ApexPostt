using System.Text.Json.Serialization;

namespace ApexPost.Api.Models;

/// <summary>Shipment lifecycle state. v2 serialises it as a string enum so clients get a closed set of values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShipmentStatus>))]
public enum ShipmentStatus
{
    /// <summary>Shipment registered but not yet collected.</summary>
    Created,
    /// <summary>Collected by a courier.</summary>
    PickedUp,
    /// <summary>Moving through the network.</summary>
    InTransit,
    /// <summary>With the delivery courier.</summary>
    OutForDelivery,
    /// <summary>Delivered to the recipient.</summary>
    Delivered,
    /// <summary>Delivery attempt failed.</summary>
    Failed
}

/// <summary>Shipment response contract for deprecated v1 (flat recipient fields).</summary>
public sealed class ShipmentResponseV1
{
    /// <summary>Shipment tracking identifier.</summary>
    public string TrackingNumber { get; set; } = string.Empty;
    /// <summary>Current shipment status.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Shipment weight in kilograms.</summary>
    public decimal Weight { get; set; }
    /// <summary>Recipient full name.</summary>
    public string RecipientName { get; set; } = string.Empty;
    /// <summary>Recipient phone number.</summary>
    public string RecipientPhone { get; set; } = string.Empty;
    /// <summary>Recipient address.</summary>
    public string RecipientAddress { get; set; } = string.Empty;
    /// <summary>Recipient city.</summary>
    public string RecipientCity { get; set; } = string.Empty;
    /// <summary>Optional shipment notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Shipment creation timestamp in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Shipment response contract for v2 (nested recipient and latest scan).</summary>
public sealed class ShipmentResponseV2
{
    /// <summary>Shipment tracking identifier.</summary>
    public string TrackingNumber { get; set; } = string.Empty;
    /// <summary>Current shipment status represented as a string enum.</summary>
    public ShipmentStatus Status { get; set; }
    /// <summary>Shipment weight in kilograms.</summary>
    public decimal Weight { get; set; }
    /// <summary>Recipient details grouped into one object.</summary>
    public RecipientInfo Recipient { get; set; } = new();
    /// <summary>Most recent scan summary, when available.</summary>
    public LatestScanInfo? LatestScan { get; set; }
    /// <summary>Optional shipment notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Shipment creation timestamp in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Nested recipient information used by v2.</summary>
public sealed class RecipientInfo
{
    /// <summary>Recipient full name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Recipient phone number.</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Recipient address.</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>Recipient city.</summary>
    public string City { get; set; } = string.Empty;
}

/// <summary>Summary of the latest shipment scan.</summary>
public sealed class LatestScanInfo
{
    /// <summary>Type of the latest scan.</summary>
    public string ScanType { get; set; } = string.Empty;
    /// <summary>Location of the latest scan.</summary>
    public string Location { get; set; } = string.Empty;
    /// <summary>Timestamp of the latest scan in UTC.</summary>
    public DateTime ScannedAtUtc { get; set; }
}

/// <summary>Generic paginated response envelope.</summary>
public sealed class PaginatedShipmentsResponse<T>
{
    /// <summary>Shipment items for this page.</summary>
    public List<T> Items { get; set; } = new();
    /// <summary>Current page number.</summary>
    public int Page { get; set; }
    /// <summary>Maximum number of items per page.</summary>
    public int PageSize { get; set; }
    /// <summary>Total number of matching shipments.</summary>
    public int Total { get; set; }
    /// <summary>Total number of pages.</summary>
    public int TotalPages { get; set; }
    /// <summary>Whether another page is available.</summary>
    public bool HasNextPage { get; set; }
    /// <summary>Whether a previous page is available.</summary>
    public bool HasPreviousPage { get; set; }
}

/// <summary>Scan history response for one shipment.</summary>
public sealed class ScanHistoryResponse
{
    /// <summary>Shipment tracking number.</summary>
    public string TrackingNumber { get; set; } = string.Empty;
    /// <summary>Scans ordered newest first.</summary>
    public List<ScanDetailResponse> Scans { get; set; } = new();
}

/// <summary>Individual scan history entry.</summary>
public sealed class ScanDetailResponse
{
    /// <summary>Stable scan document identifier.</summary>
    public string ScanId { get; set; } = string.Empty;
    /// <summary>Scan type.</summary>
    public string ScanType { get; set; } = string.Empty;
    /// <summary>Courier who recorded the scan.</summary>
    public string CourierId { get; set; } = string.Empty;
    /// <summary>Location where the scan occurred.</summary>
    public string Location { get; set; } = string.Empty;
    /// <summary>Scan timestamp in UTC.</summary>
    public DateTime ScannedAtUtc { get; set; }
    /// <summary>Optional scan notes.</summary>
    public string? Notes { get; set; }
}
