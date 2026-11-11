using Couchbase.Lite;
using Couchbase.Lite.Query;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Manages the lifecycle of the local Couchbase Lite database for courier offline operations.
/// </summary>
public class CourierDatabase : IDisposable
{
    /// <summary>Local database name required by the brief.</summary>
    public const string DatabaseName = "apexpost_courier";

    private readonly ILogger<CourierDatabase>? _logger;
    private readonly string? _directory;
    private Database? _database;
    private bool _disposed;

    /// <summary>Raised before the database closes so dependent listeners can release their tokens.</summary>
    public event Action? Disposing;

    /// <summary>
    /// The underlying Couchbase Lite database instance.
    /// </summary>
    public Database Database
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, typeof(CourierDatabase));
            return _database ?? throw new InvalidOperationException("Database not initialized");
        }
    }

    /// <summary>
    /// The default collection used for storing scans.
    /// </summary>
    public Collection? DefaultCollection { get; private set; }

    /// <param name="logger">Optional logger.</param>
    /// <param name="directory">Folder that holds the database files; defaults to the user's application data folder.</param>
    public CourierDatabase(ILogger<CourierDatabase>? logger = null, string? directory = null)
    {
        _logger = logger;
        _directory = directory;
    }

    /// <summary>
    /// Initializes the database asynchronously.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CourierDatabase));

        if (_database is not null)
            return;

        try
        {
            // Create or open the local database
            var config = new DatabaseConfiguration { Directory = GetDatabaseDirectory() };
            _database = new Database(DatabaseName, config);

            // Get the default collection
            DefaultCollection = _database.GetDefaultCollection();

            _logger?.LogInformation("Couchbase Lite database 'apexpost_courier' initialized successfully");

            // Create indexes on commonly queried fields
            await CreateIndexesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Couchbase Lite database");
            _database?.Dispose();
            _database = null;
            DefaultCollection = null;
            throw;
        }
    }

    /// <summary>
    /// Gets the platform-specific database directory.
    /// </summary>
    private string GetDatabaseDirectory()
    {
        var dbPath = _directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexPost", "CourierOffline");
        Directory.CreateDirectory(dbPath);
        return dbPath;
    }

    /// <summary>
    /// Creates value indexes for efficient querying.
    /// </summary>
    private Task CreateIndexesAsync()
    {
        if (DefaultCollection is null)
            throw new InvalidOperationException("Default collection is not available.");

        // CreateIndex is idempotent for an existing index with the same name.
        DefaultCollection.CreateIndex(
            "idx_scans_tracking_number",
            IndexBuilder.ValueIndex(ValueIndexItem.Expression(Expression.Property("trackingNumber"))));
        DefaultCollection.CreateIndex(
            "idx_scans_sync_status",
            IndexBuilder.ValueIndex(ValueIndexItem.Expression(Expression.Property("syncStatus"))));

        _logger?.LogInformation("Created Couchbase Lite value indexes for trackingNumber and syncStatus");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the database and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            try { Disposing?.Invoke(); }
            catch (Exception ex) { _logger?.LogError(ex, "Error notifying database dependants during disposal"); }
            Disposing = null;
            _database?.Dispose();
            DefaultCollection = null;
            _logger?.LogInformation("Couchbase Lite database disposed successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error disposing database");
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
