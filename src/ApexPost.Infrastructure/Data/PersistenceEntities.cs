namespace ApexPost.Infrastructure.Data;

/// <summary>Persistence-only representation. The domain never depends on SQL column constraints.</summary>
public sealed class IdentityVerificationRecord
{
    public string Id { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string CounterTransactionId { get; set; } = string.Empty;
    public string VerifiedByStaffId { get; set; } = string.Empty;
    public int DocumentType { get; set; }
    public string MaskedDocumentNumber { get; set; } = "****";
    public int Result { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
}

public sealed class RoleRecord
{
    public ApexPost.Domain.StaffRole Name { get; set; }
}


public sealed class ProductCategoryRecord
{
    public ApexPost.Domain.ProductCategory Name { get; set; }
}

public sealed class ShippingZoneRecord
{
    public string Code { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}
