using ApexPost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Infrastructure.Data.Configurations;

public sealed class RoleRecordConfiguration : IEntityTypeConfiguration<RoleRecord>
{
    public void Configure(EntityTypeBuilder<RoleRecord> builder)
    {
        builder.ToTable("Roles", table => table.HasCheckConstraint("CK_Roles_Name", "[Name] IN ('CounterClerk','BranchManager','Auditor','Administrator')"));
        MappingConventions.ConfigureAudit(builder);
        builder.HasKey(x => x.Name);
        builder.Property(x => x.Name).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired();
    }
}

public sealed class ProductCategoryRecordConfiguration : IEntityTypeConfiguration<ProductCategoryRecord>
{
    public void Configure(EntityTypeBuilder<ProductCategoryRecord> builder)
    {
        builder.ToTable("ProductCategories", table => table.HasCheckConstraint("CK_ProductCategories_Name", "[Name] IN ('Packaging','Stationery','OfficeSupplies')"));
        MappingConventions.ConfigureAudit(builder);
        builder.HasKey(x => x.Name);
        builder.Property(x => x.Name).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
    }
}

public sealed class ShippingZoneRecordConfiguration : IEntityTypeConfiguration<ShippingZoneRecord>
{
    public void Configure(EntityTypeBuilder<ShippingZoneRecord> builder)
    {
        builder.ToTable("ShippingZones");
        MappingConventions.ConfigureAudit(builder);
        builder.HasKey(x => x.Code);
        builder.Property(x => x.Code).HasMaxLength(16).IsUnicode(false).IsRequired();
        builder.Property(x => x.Region).HasMaxLength(60).IsRequired();
    }
}

public sealed class IdentityVerificationRecordConfiguration : IEntityTypeConfiguration<IdentityVerificationRecord>
{
    public void Configure(EntityTypeBuilder<IdentityVerificationRecord> builder)
    {
        builder.ToTable("IdentityVerifications", table =>
        {
            table.HasCheckConstraint("CK_IdentityVerifications_MaskedDocument", "LEN([MaskedDocumentNumber]) BETWEEN 4 AND 8 AND LEFT([MaskedDocumentNumber], 4) = '****'");
            table.HasCheckConstraint("CK_IdentityVerifications_DocumentType", "[DocumentType] BETWEEN 0 AND 3");
            table.HasCheckConstraint("CK_IdentityVerifications_Result", "[Result] BETWEEN 0 AND 2");
        });
        MappingConventions.ConfigureAudit(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.BranchCode).HasMaxLength(12).IsUnicode(false).IsRequired();
        builder.Property(x => x.CounterTransactionId).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.VerifiedByStaffId).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.DocumentType).IsRequired();
        builder.Property(x => x.MaskedDocumentNumber).HasMaxLength(8).IsUnicode(false).IsRequired();
        builder.Property(x => x.Result).IsRequired();
        builder.Property(x => x.VerifiedAt).HasColumnType("datetimeoffset").IsRequired();
        builder.HasIndex(x => new { x.BranchCode, x.VerifiedAt });
        builder.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CounterTransaction>().WithMany().HasForeignKey(x => x.CounterTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Staff>().WithMany().HasForeignKey(x => x.VerifiedByStaffId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        MappingConventions.ConfigureAudit(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(80).IsUnicode(false).IsRequired();
        builder.Property(x => x.AggregateId).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(120).IsUnicode(false).IsRequired();
        builder.Property(x => x.PayloadJson).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ProcessedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.LastError).HasMaxLength(200);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
    }
}

public sealed class SyncInboxRecordConfiguration : IEntityTypeConfiguration<SyncInboxRecord>
{
    public void Configure(EntityTypeBuilder<SyncInboxRecord> builder)
    {
        builder.ToTable("SyncInbox", table =>
        {
            table.HasCheckConstraint("CK_SyncInbox_Status", "[Status] IN ('Pending','Applied','Conflict','Rejected')");
            table.HasCheckConstraint("CK_SyncInbox_PayloadLength", "LEN([PayloadJson]) <= 32768");
        });
        MappingConventions.ConfigureAudit(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.ClientId).HasMaxLength(80).IsUnicode(false).IsRequired();
        builder.Property(x => x.OperationId).HasMaxLength(80).IsUnicode(false).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(80).IsUnicode(false).IsRequired();
        builder.Property(x => x.AggregateId).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ClientOccurredAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ReceivedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        builder.Property(x => x.ConflictPolicy).HasMaxLength(80).IsUnicode(false).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ClientId, x.OperationId }).IsUnique();
    }
}
