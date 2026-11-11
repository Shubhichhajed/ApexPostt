using ApexPost.Domain;

namespace ApexPost.Application;

public interface IRepository<T> where T : class, IEntity
{
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IAuditStore { Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default); Task<IReadOnlyList<AuditRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default); }
public interface IPasswordService { string Hash(string password); bool Verify(string encodedHash, string password); }
public interface IAppSeeder { Task SeedAsync(CancellationToken cancellationToken = default); }
public interface IRateCache { ValueTask<IReadOnlyList<ShipmentRate>> GetRatesAsync(CancellationToken cancellationToken = default); }
public interface ICourierService { Task<IReadOnlyList<ShipmentRate>> GetRatesAsync(CancellationToken cancellationToken = default); Task<string?> GetTrackingStatusAsync(string shipmentId, CancellationToken cancellationToken = default); }


public interface ICounterService
{
    Task<CounterTransaction> CreateAsync(string branchCode, string counterId, string clerkId, CancellationToken cancellationToken = default);
    Task<CounterTransaction> AddRetailLineAsync(string transactionId, string actorStaffId, string sku, int quantity, CancellationToken cancellationToken = default);
    Task<CounterTransaction> AddShippingLineAsync(string transactionId, string actorStaffId, Shipment shipment, CancellationToken cancellationToken = default);
    Task<CounterTransaction> AddMoneyOrderLineAsync(string transactionId, string actorStaffId, MoneyOrder order, CancellationToken cancellationToken = default);
    Task<CounterTransaction> CompleteAsync(string transactionId, string actorStaffId, PaymentMethod method, CancellationToken cancellationToken = default);
    Task<CounterTransaction> VoidAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default);
    Task<CounterTransaction> RefundAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default);
}
public interface IMoneyOrderService { Task<MoneyOrder> IssueAsync(IssueMoneyOrderCommand command, CancellationToken cancellationToken = default); }
public interface IIdentityVerificationService { Task<IdentityVerification> RecordAsync(RecordIdentityVerificationCommand command, CancellationToken cancellationToken = default); }

public interface IReportService
{
    Task<DashboardSummary> GetDashboardAsync(string? branchCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailySalesRow>> GetDailySalesAsync(DateOnly day, string? branchCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(string? branchCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShippingVolumeRow>> GetShippingVolumeAsync(string? branchCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MoneyOrderAuditRow>> GetMoneyOrderAuditAsync(CancellationToken cancellationToken = default);
}


public interface IPlatformQueryService
{
    Task<IReadOnlyList<ProductStockReadDto>> GetProductStockAsync(string? branchCode, bool includeAllBranches, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransactionListReadDto>> GetTransactionsAsync(string? branchCode, bool includeAllBranches, CancellationToken cancellationToken = default);
    Task<TransactionDetailsReadDto?> GetTransactionDetailsAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IdentityVerificationSummaryDto>> GetSuccessfulVerificationsAsync(string transactionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MoneyOrderReadDto>> GetMoneyOrdersAsync(string? branchCode, bool includeAllBranches, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffReadDto>> GetStaffAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BranchReadDto>> GetBranchesAsync(CancellationToken cancellationToken = default);
}
