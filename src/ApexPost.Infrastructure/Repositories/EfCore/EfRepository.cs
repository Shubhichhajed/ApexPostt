
using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Repositories.EfCore;

/// <summary>
/// Stages aggregate changes only. The application use case commits through IUnitOfWork.
/// </summary>
public class EFRepository<T>(ApexPostDbContext db)
    : IRepository<T>
    where T : class, IEntity
{
    protected ApexPostDbContext Db { get; } = db;

    protected DbSet<T> Set => Db.Set<T>();

    public async Task<IReadOnlyList<T>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = (await Set
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        // Include pending inserts and prefer tracked in-scope instances.
        foreach (var tracked in Set.Local)
        {
            results[tracked.Id] = tracked;
        }

        return results.Values.ToArray();
    }

    public Task<T?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
        => Set.Local.FirstOrDefault(entity => entity.Id == id) is { } local
            ? Task.FromResult<T?>(local)
            : Set.FirstOrDefaultAsync(
                entity => entity.Id == id,
                cancellationToken);

    public async Task AddAsync(
        T entity,
        CancellationToken cancellationToken = default)
        => await Set.AddAsync(entity, cancellationToken);

    public Task UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default)
    {
        var entry = Db.Entry(entity);

        if (entry.State == EntityState.Added)
        {
            return Task.CompletedTask;
        }

        if (entry.State == EntityState.Detached)
        {
            Set.Attach(entity);
        }

        Db.Entry(entity).State = EntityState.Modified;

        return Task.CompletedTask;
    }

    public async Task DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);

        if (entity is not null)
        {
            Set.Remove(entity);
        }
    }
}

public sealed class EfBranchRepository(ApexPostDbContext db)
    : EFRepository<Branch>(db), IBranchRepository
{
    public Task<Branch?> GetByCodeAsync(
        string branchCode,
        CancellationToken cancellationToken = default)
        => Db.Branches.FirstOrDefaultAsync(
            x => x.Code == branchCode,
            cancellationToken);
}

public sealed class EfProductRepository(ApexPostDbContext db)
    : EFRepository<Product>(db), IProductRepository
{
    public Task<Product?> GetBySkuAsync(
        string sku,
        CancellationToken cancellationToken = default)
        => Db.Products.FirstOrDefaultAsync(
            x => x.Sku == sku,
            cancellationToken);
}

public sealed class EfStockRepository(ApexPostDbContext db)
    : EFRepository<StockItem>(db), IStockRepository
{
    public async Task<IReadOnlyList<StockItem>> GetByBranchAsync(
        string branchCode,
        CancellationToken cancellationToken = default)
        => await Db.BranchStock
            .Where(x => x.BranchCode == branchCode)
            .OrderBy(x => x.Sku)
            .ToListAsync(cancellationToken);

    public Task<StockItem?> GetByBranchAndSkuAsync(
        string branchCode,
        string sku,
        CancellationToken cancellationToken = default)
        => Db.BranchStock
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.BranchCode == branchCode && x.Sku == sku,
                cancellationToken);
}

public sealed class EfCounterTransactionRepository(ApexPostDbContext db)
    : EFRepository<CounterTransaction>(db), ICounterTransactionRepository
{
}

public sealed class EfStaffRepository(ApexPostDbContext db)
    : EFRepository<Staff>(db), IStaffRepository
{
    public Task<Staff?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
        => Db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Email == email,
                cancellationToken);
}

public sealed class EfShipmentRepository(ApexPostDbContext db)
    : EFRepository<Shipment>(db), IShipmentRepository
{
}

public sealed class EfMoneyOrderRepository(ApexPostDbContext db)
    : EFRepository<MoneyOrder>(db), IMoneyOrderRepository
{
    public async Task<IReadOnlyList<MoneyOrder>> GetByTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        var results = (await Db.MoneyOrders
                .AsNoTracking()
                .Where(x => x.CounterTransactionId == transactionId)
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        foreach (var tracked in Db.MoneyOrders.Local
                     .Where(x => x.CounterTransactionId == transactionId))
        {
            results[tracked.Id] = tracked;
        }

        return results.Values.ToArray();
    }
}

public sealed class EfPaymentRepository(ApexPostDbContext db)
    : EFRepository<Payment>(db), IPaymentRepository
{
}
