using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Repositories.EfCore;

/// <summary>Maps the rich domain object to a persistence-only row with a bounded masked identifier.</summary>
public sealed class EfIdentityVerificationRepository(ApexPostDbContext db) : IRepository<IdentityVerification>, IIdentityVerificationRepository
{
    public async Task<IReadOnlyList<IdentityVerification>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var results = (await db.IdentityVerifications.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var tracked in db.IdentityVerifications.Local) results[tracked.Id] = tracked;
        return results.Values.Select(ToDomain).ToArray();
    }

    public async Task<IdentityVerification?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = db.IdentityVerifications.Local.FirstOrDefault(x => x.Id == id)
            ?? await db.IdentityVerifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task AddAsync(IdentityVerification entity, CancellationToken cancellationToken = default)
        => await db.IdentityVerifications.AddAsync(ToRecord(entity), cancellationToken);

    public Task UpdateAsync(IdentityVerification entity, CancellationToken cancellationToken = default)
    {
        var record = ToRecord(entity);
        var tracked = db.IdentityVerifications.Local.FirstOrDefault(x => x.Id == entity.Id);
        if (tracked is not null && db.Entry(tracked).State == EntityState.Added) return Task.CompletedTask;
        if (tracked is null) db.IdentityVerifications.Attach(record);
        else db.Entry(tracked).CurrentValues.SetValues(record);
        db.Entry(tracked ?? record).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = db.IdentityVerifications.Local.FirstOrDefault(x => x.Id == id)
            ?? await db.IdentityVerifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is not null) db.IdentityVerifications.Remove(row);
    }

    private static IdentityVerificationRecord ToRecord(IdentityVerification value) => new()
    {
        Id = value.Id, BranchCode = value.BranchCode, CounterTransactionId = value.CounterTransactionId,
        VerifiedByStaffId = value.VerifiedByStaffId, DocumentType = (int)value.DocumentType,
        MaskedDocumentNumber = value.MaskedDocumentNumber, Result = (int)value.Result, VerifiedAt = value.VerifiedAt
    };

    private static IdentityVerification ToDomain(IdentityVerificationRecord value) => new()
    {
        Id = value.Id, BranchCode = value.BranchCode, CounterTransactionId = value.CounterTransactionId,
        VerifiedByStaffId = value.VerifiedByStaffId, DocumentType = (IdentityDocumentType)value.DocumentType,
        MaskedDocumentNumber = value.MaskedDocumentNumber, Result = (VerificationResult)value.Result, VerifiedAt = value.VerifiedAt
    };
}
