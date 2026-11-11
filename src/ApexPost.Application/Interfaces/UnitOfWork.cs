namespace ApexPost.Application;

/// <summary>One scoped commit boundary shared by all repositories participating in a use case.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}

public interface IBranchRepository : IRepository<ApexPost.Domain.Branch>
{
    Task<ApexPost.Domain.Branch?> GetByCodeAsync(string branchCode, CancellationToken cancellationToken = default);
}
public interface IProductRepository : IRepository<ApexPost.Domain.Product>
{
    Task<ApexPost.Domain.Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
}
public interface IStockRepository : IRepository<ApexPost.Domain.StockItem>
{
    Task<IReadOnlyList<ApexPost.Domain.StockItem>> GetByBranchAsync(string branchCode, CancellationToken cancellationToken = default);
    Task<ApexPost.Domain.StockItem?> GetByBranchAndSkuAsync(string branchCode, string sku, CancellationToken cancellationToken = default);
}
public interface ICounterTransactionRepository : IRepository<ApexPost.Domain.CounterTransaction> { }
public interface IIdentityVerificationRepository : IRepository<ApexPost.Domain.IdentityVerification> { }
public interface IStaffRepository : IRepository<ApexPost.Domain.Staff>
{
    Task<ApexPost.Domain.Staff?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
}
public interface IShipmentRepository : IRepository<ApexPost.Domain.Shipment> { }
public interface IMoneyOrderRepository : IRepository<ApexPost.Domain.MoneyOrder>
{
    Task<IReadOnlyList<ApexPost.Domain.MoneyOrder>> GetByTransactionAsync(string transactionId, CancellationToken cancellationToken = default);
}
public interface IPaymentRepository : IRepository<ApexPost.Domain.Payment> { }


public interface IStockAdjustmentService
{
    Task AdjustAsync(string branchCode, string sku, int delta, string reason, string changedBy, CancellationToken cancellationToken = default);
}
