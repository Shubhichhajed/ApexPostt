namespace ApexPost.Api.Services;

/// <summary>
/// Shipment use cases shared by every API version. Controllers only bind input, call this
/// service and translate the result into an HTTP response.
/// </summary>
public interface IShipmentService
{
    /// <summary>Gets one page of shipments, optionally filtered by status and city.</summary>
    Task<(List<ShipmentDTO> items, int total)> GetShipmentsAsync(int page, int pageSize, string? status, string? city,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a shipment by tracking number, or null when it does not exist.</summary>
    Task<ShipmentDTO?> GetShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>Creates a shipment. Throws <see cref="ShipmentConflictException"/> for a duplicate tracking number.</summary>
    Task<ShipmentDTO> CreateShipmentAsync(string trackingNumber, decimal weight, string recipientName,
        string recipientPhone, string recipientAddress, string recipientCity, string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces editable details while the shipment is still Created.</summary>
    Task<ShipmentDTO> UpdateShipmentAsync(string trackingNumber, decimal weight, string recipientName,
        string recipientPhone, string recipientAddress, string recipientCity, string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one allowed status transition; other transitions raise a conflict.</summary>
    Task<ShipmentDTO> UpdateShipmentStatusAsync(string trackingNumber, string newStatus,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a shipment only while its status is Created.</summary>
    Task DeleteShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>Idempotent bulk upload: a scan ID that already exists is counted as a duplicate, never inserted twice.</summary>
    Task<ScanUploadResult> UploadScansAsync(string trackingNumber, List<OfflineScanUpload> scans,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the scan history for a shipment, newest first.</summary>
    Task<List<ScanDTO>> GetScansAsync(string trackingNumber, CancellationToken cancellationToken = default);
}

/// <summary>A scan uploaded from the courier's offline store.</summary>
public sealed class OfflineScanUpload
{
    /// <summary>Client-generated idempotency key in scan::{Guid} format.</summary>
    public string ScanId { get; set; } = string.Empty;
    /// <summary>Pickup, InTransit, Delivered or Failed.</summary>
    public string ScanType { get; set; } = string.Empty;
    /// <summary>Courier who recorded the scan.</summary>
    public string CourierId { get; set; } = string.Empty;
    /// <summary>Where the scan happened.</summary>
    public string Location { get; set; } = string.Empty;
    /// <summary>When the scan happened (UTC).</summary>
    public DateTime ScannedAtUtc { get; set; }
    /// <summary>Optional notes, such as the recipient's signature name.</summary>
    public string? Notes { get; set; }
}
