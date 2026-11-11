using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure;

/// <summary>Singleton cache depends on the singleton-safe DbContext factory, never a scoped repository/context.</summary>
public sealed class RateCache(IDbContextFactory<ApexPostDbContext> factory) : IRateCache
{
    private IReadOnlyList<ShipmentRate>? _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IReadOnlyList<ShipmentRate>> GetRatesAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null) return _cache;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is null)
            {
                await using var db = await factory.CreateDbContextAsync(cancellationToken);
                _cache = await db.ShippingRates.AsNoTracking().ToListAsync(cancellationToken);
            }
            return _cache;
        }
        finally { _gate.Release(); }
    }
}
