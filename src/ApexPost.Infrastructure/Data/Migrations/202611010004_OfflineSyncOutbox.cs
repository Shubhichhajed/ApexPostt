using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApexPost.Infrastructure.Data.Migrations;

[DbContext(typeof(ApexPostDbContext))]
[Migration("202611010004_OfflineSyncOutbox")]
public sealed class OfflineSyncOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        CREATE TABLE dbo.OutboxMessages
        (
         Id varchar(32) NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY,
         AggregateType varchar(80) NOT NULL, AggregateId varchar(32) NOT NULL, EventType varchar(120) NOT NULL,
         PayloadJson nvarchar(4000) NOT NULL, OccurredAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_OutboxMessages_OccurredAt DEFAULT SYSUTCDATETIME(),
         ProcessedAtUtc datetimeoffset(7) NULL, AttemptCount int NOT NULL CONSTRAINT DF_OutboxMessages_AttemptCount DEFAULT 0,
         LastError nvarchar(200) NULL, RowVersion rowversion NOT NULL,
         CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_OutboxMessages_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_OutboxMessages_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL
        );
        CREATE INDEX IX_OutboxMessages_Pending ON dbo.OutboxMessages(ProcessedAtUtc, OccurredAtUtc);
        CREATE TABLE dbo.SyncInbox
        (
         Id varchar(32) NOT NULL CONSTRAINT PK_SyncInbox PRIMARY KEY,
         ClientId varchar(80) NOT NULL, OperationId varchar(80) NOT NULL, AggregateType varchar(80) NOT NULL,
         AggregateId varchar(32) NOT NULL, PayloadJson nvarchar(max) NOT NULL, ClientOccurredAtUtc datetimeoffset(7) NOT NULL,
         ReceivedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_SyncInbox_ReceivedAt DEFAULT SYSUTCDATETIME(),
         Status varchar(16) NOT NULL CONSTRAINT DF_SyncInbox_Status DEFAULT 'Pending', ConflictPolicy varchar(80) NOT NULL,
         RowVersion rowversion NOT NULL,
         CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_SyncInbox_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_SyncInbox_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT UQ_SyncInbox_Client_Operation UNIQUE(ClientId,OperationId),
         CONSTRAINT CK_SyncInbox_Status CHECK(Status IN ('Pending','Applied','Conflict','Rejected')),
         CONSTRAINT CK_SyncInbox_PayloadLength CHECK(LEN(PayloadJson) <= 32768)
        );
        """);
            }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.SyncInbox; DROP TABLE IF EXISTS dbo.OutboxMessages;");
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        ApexPostDbContext.ConfigureModel(modelBuilder);
    }
}
