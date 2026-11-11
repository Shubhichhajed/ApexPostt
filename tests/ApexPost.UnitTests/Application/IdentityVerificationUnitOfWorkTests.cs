using Xunit;
using ApexPost.Application;
using ApexPost.Domain;

namespace ApexPost.UnitTests.Application;

public sealed class IdentityVerificationUnitOfWorkTests
{
    [Fact]
    public async Task Successful_verification_commits_once()
    {
        var tx = new CounterTransaction { Id = "tx-1", BranchCode = "IND01", Status = TransactionStatus.Open };
        var employee = new Staff { Id = "staff-1", Name = "Clerk", Email = "clerk@test.local", Role = StaffRole.CounterClerk, HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Id = "branch-1", Code = "IND01", Name = "Indore", Region = "Central", Status = BranchStatus.Active };
        var verifications = new MemoryRepository<IdentityVerification>();
        var unitOfWork = new SpyUnitOfWork();
        var service = new IdentityVerificationService(verifications, new MemoryRepository<CounterTransaction>(tx), new MemoryRepository<Staff>(employee), new MemoryRepository<Branch>(branch), unitOfWork);

        var result = await service.RecordAsync(new RecordIdentityVerificationCommand("tx-1", "staff-1", IdentityDocumentType.NationalId, "ABCD1234", VerificationResult.Successful));

        Assert.Equal("****1234", result.MaskedDocumentNumber);
        Assert.Single(verifications.Items);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Invalid_verification_does_not_commit()
    {
        var tx = new CounterTransaction { Id = "tx-1", BranchCode = "IND01", Status = TransactionStatus.Open };
        var employee = new Staff { Id = "staff-1", Name = "Clerk", Email = "clerk@test.local", Role = StaffRole.CounterClerk, HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Id = "branch-1", Code = "IND01", Name = "Indore", Region = "Central", Status = BranchStatus.Active };
        var unitOfWork = new SpyUnitOfWork();
        var service = new IdentityVerificationService(new MemoryRepository<IdentityVerification>(), new MemoryRepository<CounterTransaction>(tx), new MemoryRepository<Staff>(employee), new MemoryRepository<Branch>(branch), unitOfWork);

        await Assert.ThrowsAsync<DomainException>(() => service.RecordAsync(new RecordIdentityVerificationCommand("tx-1", "staff-1", IdentityDocumentType.NationalId, "ABCD1234", VerificationResult.Pending)));
        Assert.Equal(0, unitOfWork.CommitCount);
    }

    private sealed class SpyUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { CommitCount++; return Task.FromResult(1); }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class MemoryRepository<T>(params T[] seed) : IRepository<T> where T : class, IEntity
    {
        public List<T> Items { get; } = seed.ToList();
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items);
        public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(T entity, CancellationToken cancellationToken = default) { Items.Add(entity); return Task.CompletedTask; }
        public Task UpdateAsync(T entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) { Items.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    }
}
