using Xunit;
using ApexPost.Application;
using ApexPost.Domain;

namespace ApexPost.UnitTests.Application;

public sealed class MoneyOrderServiceUnitOfWorkTests
{
    [Fact]
    public async Task Adding_money_order_to_existing_basket_commits_once()
    {
        var transaction = new CounterTransaction { Id = "tx-1", BranchCode = "IND01", CounterId = "IND01-C1", ClerkId = "clerk-1" };
        var clerk = new Staff { Id = "clerk-1", Name = "Demo Clerk", Email = "clerk@test.local", Role = StaffRole.CounterClerk, HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Id = "branch-1", Code = "IND01", Name = "Indore", Region = "Central", Status = BranchStatus.Active, Counters = [new Counter("IND01-C1", "Counter 1")] };
        var orders = new MemoryRepository<MoneyOrder>();
        var unitOfWork = new SpyUnitOfWork();
        var service = new MoneyOrderService(
            orders,
            new MemoryRepository<IdentityVerification>(),
            new MemoryRepository<Branch>(branch),
            new MemoryRepository<Staff>(clerk),
            new MemoryRepository<CounterTransaction>(transaction),
            new StagingCounterService(transaction),
            new MoneyOrderPolicy(1000m),
            unitOfWork);

        var result = await service.IssueAsync(new IssueMoneyOrderCommand(
            500m, "Demo Sender", "Demo Recipient", "IND01", "clerk-1", "tx-1", null, PaymentMethod.Cash));

        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Single(orders.Items);
        Assert.Equal(transaction.Id, result.CounterTransactionId);
        Assert.Equal(TransactionStatus.Open, transaction.Status);
        Assert.Single(transaction.Lines);
    }

    [Fact]
    public async Task High_value_order_without_successful_verification_does_not_commit()
    {
        var transaction = new CounterTransaction { Id = "tx-1", BranchCode = "IND01", CounterId = "IND01-C1", ClerkId = "clerk-1" };
        var clerk = new Staff { Id = "clerk-1", Name = "Demo Clerk", Email = "clerk@test.local", Role = StaffRole.CounterClerk, HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Id = "branch-1", Code = "IND01", Name = "Indore", Region = "Central", Status = BranchStatus.Active, Counters = [new Counter("IND01-C1", "Counter 1")] };
        var unitOfWork = new SpyUnitOfWork();
        var service = new MoneyOrderService(
            new MemoryRepository<MoneyOrder>(),
            new MemoryRepository<IdentityVerification>(),
            new MemoryRepository<Branch>(branch),
            new MemoryRepository<Staff>(clerk),
            new MemoryRepository<CounterTransaction>(transaction),
            new StagingCounterService(transaction),
            new MoneyOrderPolicy(1000m),
            unitOfWork);

        await Assert.ThrowsAsync<DomainException>(() => service.IssueAsync(new IssueMoneyOrderCommand(
            1500m, "Demo Sender", "Demo Recipient", "IND01", "clerk-1", "tx-1", null, PaymentMethod.Cash)));
        Assert.Equal(0, unitOfWork.CommitCount);
    }

    private sealed class SpyUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            return Task.FromResult(1);
        }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private sealed class MemoryRepository<T>(params T[] seed) : IRepository<T> where T : class, IEntity
    {
        public List<T> Items { get; } = seed.ToList();
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items);
        public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(T entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class StagingCounterService(CounterTransaction transaction) : ICounterService
    {
        public Task<CounterTransaction> CreateAsync(string branchCode, string counterId, string clerkId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> AddRetailLineAsync(string transactionId, string actorStaffId, string sku, int quantity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> AddShippingLineAsync(string transactionId, string actorStaffId, Shipment shipment, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> AddMoneyOrderLineAsync(string transactionId, string actorStaffId, MoneyOrder order, CancellationToken cancellationToken = default)
        {
            var lineTotal = order.Amount + order.Fee;
            transaction.AddLine(new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.MoneyOrder, "Money order", null, 1, lineTotal, 0m, lineTotal));
            return Task.FromResult(transaction);
        }
        public Task<CounterTransaction> CompleteAsync(string transactionId, string actorStaffId, PaymentMethod method, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> VoidAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> RefundAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
