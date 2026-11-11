namespace ApexPost.Courier.Offline;

/// <summary>
/// Enumeration of shipment scan types.
/// </summary>
public enum ScanType
{
    Pickup,
    InTransit,
    Delivered,
    Failed
}

/// <summary>
/// Enumeration of synchronization statuses for offline scans.
/// </summary>
public enum SyncStatus
{
    Pending,
    Synced,
    Failed
}

/// <summary>Creates and validates document IDs in the required <c>scan::{Guid}</c> format.</summary>
public static class ScanDocumentId
{
    public const string Prefix = "scan::";

    public static string New() => $"{Prefix}{Guid.NewGuid()}";

    public static bool IsValid(string? id) =>
        id is not null && id.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParse(id.AsSpan(Prefix.Length), out _);
}

/// <summary>
/// Represents a shipment scan recorded by a courier in the field.
/// </summary>
public class ShipmentScan
{
    /// <summary>
    /// Unique identifier for this scan. Format: scan::{Guid}.
    /// </summary>
    public string Id { get; set; } = ScanDocumentId.New();

    /// <summary>
    /// Tracking number of the shipment (e.g., APX-12345678).
    /// </summary>
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>
    /// Type of scan (Pickup, InTransit, Delivered, Failed).
    /// </summary>
    public ScanType ScanType { get; set; }

    /// <summary>
    /// ID of the courier performing the scan.
    /// </summary>
    public string CourierId { get; set; } = string.Empty;

    /// <summary>
    /// Geographic location of the scan.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the scan in UTC.
    /// </summary>
    public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional notes about the scan (e.g., recipient signature name, delivery issues).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Synchronization status with the server (Pending, Synced, or Failed).
    /// </summary>
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
}
