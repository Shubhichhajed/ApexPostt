namespace ApexPost.Courier.Offline;

/// <summary>
/// Repository interface for managing shipment scans in the local database.
/// </summary>
public interface IScanRepository
{
    /// <summary>  
    /// Creates and stores a new scan.
    /// </summary>
    Task<ShipmentScan> CreateAsync(ShipmentScan scan);

    /// <summary>
    /// Retrieves a scan by its ID.
    /// </summary>
    Task<ShipmentScan?> GetByIdAsync(string id);

    /// <summary>
    /// Updates an existing scan.
    /// </summary>
    Task<ShipmentScan> UpdateAsync(ShipmentScan scan);

    /// <summary>
    /// Deletes a scan by its ID.
    /// </summary>
    Task DeleteAsync(string id);

    /// <summary>
    /// Lists all scans.
    /// </summary>
    Task<IEnumerable<ShipmentScan>> ListAllAsync();

    /// <summary>
    /// Finds all scans for a given tracking number, ordered by scanned time.
    /// </summary>
    Task<IEnumerable<ShipmentScan>> GetScansByTrackingNumberAsync(string trackingNumber);

    /// <summary>
    /// Finds all scans with pending sync status.
    /// </summary>
    Task<IEnumerable<ShipmentScan>> GetPendingScansAsync();

    /// <summary>
    /// Gets a count of scans grouped by scan type for a specific courier.
    /// </summary>
    Task<Dictionary<ScanType, int>> GetScanCountByCourierAsync(string courierId);

    /// <summary>
    /// Gets all scans for a specific courier.
    /// </summary>
    Task<IEnumerable<ShipmentScan>> GetScansByCourierAsync(string courierId);
}
