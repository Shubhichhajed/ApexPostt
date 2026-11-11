namespace ApexPost.Api.Services;

/// <summary>Internal service-layer shipment projection used to map versioned response DTOs.</summary>
public sealed class ShipmentDTO
{
    /// <summary>Internal shipment identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Shipment tracking number.</summary>
    public string TrackingNumber { get; set; } = string.Empty;
    /// <summary>Current shipment status.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Shipment weight in kilograms.</summary>
    public decimal Weight { get; set; }
    /// <summary>Recipient full name.</summary>
    public string RecipientName { get; set; } = string.Empty;
    /// <summary>Recipient phone number.</summary>
    public string RecipientPhone { get; set; } = string.Empty;
    /// <summary>Recipient delivery address.</summary>
    public string RecipientAddress { get; set; } = string.Empty;
    /// <summary>Recipient city.</summary>
    public string RecipientCity { get; set; } = string.Empty;
    /// <summary>Optional shipment notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Creation timestamp in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Most recent scan, when available.</summary>
    public ScanDTO? LatestScan { get; set; }
}

/// <summary>Internal service-layer scan projection.</summary>
public sealed class ScanDTO
{
    /// <summary>Stable scan document identifier.</summary>
    public string Id { get; set; } = string.Empty;
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

/// <summary>Aggregate counts returned by the server-side idempotent scan upload.</summary>
public sealed class ScanUploadResult
{
    /// <summary>Number of newly accepted scans.</summary>
    public int Accepted { get; set; }
    /// <summary>Number of duplicate scan IDs skipped.</summary>
    public int DuplicatesSkipped { get; set; }
    /// <summary>Number of rejected scans.</summary>
    public int Rejected { get; set; }
    /// <summary>Rejection reasons, including the affected scan ID.</summary>
    public List<string> RejectionReasons { get; set; } = new();
}
