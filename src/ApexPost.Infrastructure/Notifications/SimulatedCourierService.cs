using ApexPost.Application;
using ApexPost.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApexPost.Infrastructure;

public sealed class SimulatedCourierService(IRateCache rateCache, IShipmentRepository shipments, ILogger<SimulatedCourierService> logger) : ICourierService
{
    private readonly SemaphoreSlim _concurrency = new(4, 4);
    public async Task<IReadOnlyList<ShipmentRate>> GetRatesAsync(CancellationToken cancellationToken = default)
    {
        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken); // Simulated network latency; cancellation remains cooperative.
            return await rateCache.GetRatesAsync(cancellationToken);
        }
        finally { _concurrency.Release(); }
    }
    public async Task<string?> GetTrackingStatusAsync(string shipmentId, CancellationToken cancellationToken = default)
    {
        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken);
            var shipment = await shipments.GetByIdAsync(shipmentId, cancellationToken);
            if (shipment is null) return null;
            logger.LogInformation("Simulated courier tracking lookup completed for shipment {ShipmentId}", shipmentId);
            return "Accepted";
        }
        finally { _concurrency.Release(); }
    }
}
