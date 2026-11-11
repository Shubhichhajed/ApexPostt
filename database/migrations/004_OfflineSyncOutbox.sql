-- REFERENCE ONLY: equivalent tables are applied by EF migration 202611010004_OfflineSyncOutbox. Do not run alongside EF migrations.
USE ApexPostDb;
GO
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
GO
