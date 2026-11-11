using ApexPost.Domain;

namespace ApexPost.Application;

public sealed record ProductStockReadDto(string Sku, string Name, ProductCategory Category, decimal UnitPrice, TaxCategory TaxCategory, int Quantity);
public sealed record TransactionListReadDto(string Id, string BranchCode, DateTimeOffset CreatedAt, int LineCount, decimal Total, TransactionStatus Status);
public sealed record TransactionLineReadDto(string Id, LineKind Kind, string Description, string? Sku, int Quantity, decimal UnitPrice, decimal TaxRate, decimal LineTotal);
public sealed record TransactionDetailsReadDto(
    string Id, string BranchCode, string CounterId, string ClerkId,
    IReadOnlyList<TransactionLineReadDto> Lines,
    decimal Subtotal, decimal Tax, decimal Total, PaymentMethod? PaymentMethod,
    TransactionStatus Status, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record MoneyOrderReadDto(string Id, decimal Amount, string Sender, string Recipient, decimal Fee, string BranchCode, string CounterTransactionId, MoneyOrderStatus Status, DateTimeOffset CreatedAt);
public sealed record StaffReadDto(string Id, string Name, string Email, StaffRole Role, string HomeBranchCode, StaffStatus Status);
public sealed record BranchReadDto(string Id, string Code, string Name, string Region, string OpeningHours, BranchStatus Status, int CounterCount);
public sealed record IdentityVerificationSummaryDto(string Id, string MaskedDocumentNumber, VerificationResult Result, DateTimeOffset VerifiedAt);
