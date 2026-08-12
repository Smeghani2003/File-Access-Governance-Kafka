-- File Access Governance Platform — Phase 1 MVP
-- Permanent schema. Staging is session-scoped local temp tables created by the
-- Ingestion Consumer at batch time — see /db/procedures/usp_MergeFsObjectsBatch.sql
-- and language-comparison-and-technical-design.md §3.

IF DB_ID('FileAccessGovernance') IS NULL
BEGIN
    CREATE DATABASE FileAccessGovernance;
END
GO

USE FileAccessGovernance;
GO

CREATE TABLE dbo.SecurityDescriptors (
    DescriptorId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    DescriptorHash  CHAR(64)        NOT NULL,   -- SHA-256 hex of the SDDL string
    OwnerSid        NVARCHAR(184)   NOT NULL,
    RawSddl         NVARCHAR(MAX)   NOT NULL,
    FirstSeenUtc    DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    LastSeenUtc     DATETIME2       NOT NULL,
    CONSTRAINT UQ_SecurityDescriptors_Hash UNIQUE (DescriptorHash)
);
GO

CREATE TABLE dbo.SecurityDescriptorAces (
    AceId            BIGINT IDENTITY(1,1) PRIMARY KEY,
    DescriptorId     BIGINT        NOT NULL REFERENCES dbo.SecurityDescriptors(DescriptorId),
    TrusteeSid       NVARCHAR(184) NOT NULL,   -- the user/group this ACE applies to
    AceType          TINYINT       NOT NULL,   -- 0 = Allow, 1 = Deny
    AccessMask       INT           NOT NULL,   -- raw Windows permission bitfield
    IsInherited      BIT           NOT NULL,
    InheritanceFlags TINYINT       NOT NULL,   -- bitfield: 1=ContainerInherit, 2=ObjectInherit, 4=InheritOnly, 8=NoPropagate
    CONSTRAINT CK_SecurityDescriptorAces_AceType CHECK (AceType IN (0, 1))
);
GO
CREATE INDEX IX_SecurityDescriptorAces_DescriptorId ON dbo.SecurityDescriptorAces(DescriptorId);
GO

CREATE TABLE dbo.FsObjects (
    ObjectId            BIGINT IDENTITY(1,1) PRIMARY KEY,
    PathHash             BINARY(32)     NOT NULL,   -- SHA-256 of the normalized full path; the idempotent MERGE key
    FullPath             NVARCHAR(4000) NOT NULL,
    ParentPathHash        BINARY(32)     NULL,
    ParentObjectId        BIGINT         NULL REFERENCES dbo.FsObjects(ObjectId),
    IsDirectory           BIT            NOT NULL,
    DescriptorId          BIGINT         NOT NULL REFERENCES dbo.SecurityDescriptors(DescriptorId),
    IsInheritanceBreak    BIT            NOT NULL,
    ShareName             NVARCHAR(256)  NOT NULL,
    LastScannedUtc         DATETIME2      NOT NULL,
    CONSTRAINT UQ_FsObjects_PathHash UNIQUE (PathHash)
);
GO
CREATE INDEX IX_FsObjects_ParentObjectId ON dbo.FsObjects(ParentObjectId);
CREATE INDEX IX_FsObjects_ParentPathHash ON dbo.FsObjects(ParentPathHash);
CREATE INDEX IX_FsObjects_DescriptorId ON dbo.FsObjects(DescriptorId);
-- Deliberately NO index on FullPath: NVARCHAR(4000) exceeds SQL Server's 1700-byte
-- nonclustered index key limit and would fail at insert time on real long paths.
-- The Query API looks up by PathHash, not FullPath — see design doc §5.2.
GO

CREATE TABLE dbo.SidNameCache (
    Sid          NVARCHAR(184) PRIMARY KEY,
    DisplayName  NVARCHAR(256) NULL,
    ResolvedUtc  DATETIME2 NOT NULL
);
GO
