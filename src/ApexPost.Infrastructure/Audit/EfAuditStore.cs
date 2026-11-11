using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Audit;

/// <summary>Audit durability is isolated from the business DbContext/transaction.</summary>
public sealed class EfAuditStore(IDbContextFactory<ApexPostDbContext> factory) : IAuditStore
{
    public async Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.AuditEntries.Add(record);
        db.Entry(record).Property("CreatedBy").CurrentValue = record.StaffId ?? "anonymous";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AuditEntries.AsNoTracking().OrderByDescending(x => x.Timestamp).Take(Math.Clamp(count, 1, 1000)).ToListAsync(cancellationToken);
    }
}
