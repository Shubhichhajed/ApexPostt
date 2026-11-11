namespace ApexPost.Application;

public sealed record OfflineSyncCommand(string ClientId, string OperationId, string AggregateType, string AggregateId, string PayloadJson, DateTimeOffset ClientOccurredAtUtc);
public sealed record OfflineSyncResult(string OperationId, string Status, bool IsDuplicate, string ConflictPolicy);

public interface IOfflineSyncService
{
    Task<OfflineSyncResult> AcceptAsync(OfflineSyncCommand command, CancellationToken cancellationToken = default);
}

public interface IProductPriceRevisionService
{
    Task<int> ReviseCategoryPricesAsync(ApexPost.Domain.ProductCategory category, decimal multiplier, CancellationToken cancellationToken = default);
}
