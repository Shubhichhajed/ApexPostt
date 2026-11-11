using ApexPost.Api.Services;

namespace ApexPost.Api.Models;

/// <summary>
/// Maps the service-layer projection to each versioned contract. Both versions call the same
/// service; only this mapping and the DTO shapes differ between v1 and v2.
/// </summary>
public static class ShipmentResponseMapper
{
    /// <summary>Flat v1 shape (kept unchanged for existing clients).</summary>
    public static ShipmentResponseV1 ToV1(ShipmentDTO dto) => new()
    {
        TrackingNumber = dto.TrackingNumber,
        Status = dto.Status,
        Weight = dto.Weight,
        RecipientName = dto.RecipientName,
        RecipientPhone = dto.RecipientPhone,
        RecipientAddress = dto.RecipientAddress,
        RecipientCity = dto.RecipientCity,
        Notes = dto.Notes,
        CreatedAtUtc = dto.CreatedAtUtc
    };

    /// <summary>v2 shape: nested recipient, latest scan summary and an enum status.</summary>
    public static ShipmentResponseV2 ToV2(ShipmentDTO dto) => new()
    {
        TrackingNumber = dto.TrackingNumber,
        Status = ParseStatus(dto.Status),
        Weight = dto.Weight,
        Recipient = new RecipientInfo
        {
            Name = dto.RecipientName,
            Phone = dto.RecipientPhone,
            Address = dto.RecipientAddress,
            City = dto.RecipientCity
        },
        LatestScan = dto.LatestScan is null ? null : new LatestScanInfo
        {
            ScanType = dto.LatestScan.ScanType,
            Location = dto.LatestScan.Location,
            ScannedAtUtc = dto.LatestScan.ScannedAtUtc
        },
        Notes = dto.Notes,
        CreatedAtUtc = dto.CreatedAtUtc
    };

    public static ScanHistoryResponse ToScanHistory(string trackingNumber, IEnumerable<ScanDTO> scans) => new()
    {
        TrackingNumber = trackingNumber,
        Scans = scans.Select(x => new ScanDetailResponse
        {
            ScanId = x.Id,
            ScanType = x.ScanType,
            CourierId = x.CourierId,
            Location = x.Location,
            ScannedAtUtc = x.ScannedAtUtc,
            Notes = x.Notes
        }).ToList()
    };

    public static ScanUploadResponse ToUploadResponse(ScanUploadResult result) => new()
    {
        Accepted = result.Accepted,
        DuplicatesSkipped = result.DuplicatesSkipped,
        Rejected = result.Rejected,
        RejectionReasons = result.RejectionReasons
    };

    public static List<OfflineScanUpload> ToUploads(ScanUploadRequest request) => request.Scans
        .Select(x => new OfflineScanUpload
        {
            ScanId = x.ScanId,
            ScanType = x.ScanType,
            CourierId = x.CourierId,
            Location = x.Location,
            ScannedAtUtc = x.ScannedAtUtc,
            Notes = x.Notes
        })
        .ToList();

    public static PaginatedShipmentsResponse<T> ToPage<T>(IEnumerable<ShipmentDTO> items, Func<ShipmentDTO, T> map,
        int page, int pageSize, int total) => new()
    {
        Items = items.Select(map).ToList(),
        Page = page,
        PageSize = pageSize,
        Total = total,
        TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        HasNextPage = page * pageSize < total,
        HasPreviousPage = page > 1
    };

    // Statuses are stored as text; anything unexpected is surfaced as a server fault rather than guessed.
    private static ShipmentStatus ParseStatus(string status) =>
        Enum.TryParse<ShipmentStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored shipment status '{status}' is not a known value.");
}
