-- Called by the Ingestion Consumer on the SAME connection that created and
-- bulk-loaded the three local temp tables below (temp tables are session-scoped,
-- so this only works when called on that connection — see design doc §5.1).
--
-- Expects the caller to have already created and populated:
--   #FsObjectsStaging               (design doc §3)
--   #SecurityDescriptorsStaging     (only rows for descriptors not previously seen by this agent)
--   #SecurityDescriptorAcesStaging  (ACEs for those same new descriptors)
--
-- Retry-on-transient-error (deadlock 1205, PK violation 2627 under concurrent
-- MERGE) is handled by the caller via Polly — see design doc §6 — not in here.

USE FileAccessGovernance;
GO

CREATE OR ALTER PROCEDURE dbo.usp_MergeFsObjectsBatch
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- Step 1: descriptors first — FsObjects.DescriptorId depends on these rows existing.
    -- WITH (HOLDLOCK) matters here specifically: multiple consumers can hit the same
    -- common descriptor hash at the same moment (that's the point of deduplication),
    -- and a MERGE without it has a documented race where two sessions can each decide
    -- a row doesn't exist yet and both try to insert it.
    DECLARE @MergeOutput TABLE (Action NVARCHAR(10), DescriptorId BIGINT, DescriptorHash CHAR(64));

    MERGE dbo.SecurityDescriptors WITH (HOLDLOCK) AS target
    USING #SecurityDescriptorsStaging AS source
        ON target.DescriptorHash = source.DescriptorHash
    WHEN MATCHED THEN
        UPDATE SET target.LastSeenUtc = source.ScannedUtc
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (DescriptorHash, OwnerSid, RawSddl, LastSeenUtc)
        VALUES (source.DescriptorHash, source.OwnerSid, source.RawSddl, source.ScannedUtc)
    OUTPUT $action, inserted.DescriptorId, inserted.DescriptorHash
        INTO @MergeOutput (Action, DescriptorId, DescriptorHash);

    -- Step 2: ACEs for newly-inserted descriptors only. A descriptor's ACE set is
    -- immutable once written (same hash => same DACL), so existing descriptors
    -- never need their ACE rows touched again.
    INSERT INTO dbo.SecurityDescriptorAces (DescriptorId, TrusteeSid, AceType, AccessMask, IsInherited, InheritanceFlags)
    SELECT n.DescriptorId, a.TrusteeSid, a.AceType, a.AccessMask, a.IsInherited, a.InheritanceFlags
    FROM #SecurityDescriptorAcesStaging a
    JOIN @MergeOutput n ON n.DescriptorHash = a.DescriptorHash AND n.Action = 'INSERT';

    -- Step 3: objects, resolving DescriptorHash -> DescriptorId via the now-guaranteed-to-exist
    -- SecurityDescriptors rows.
    MERGE dbo.FsObjects WITH (HOLDLOCK) AS target
    USING (
        SELECT s.PathHash, s.FullPath, s.ParentPathHash, s.IsDirectory,
               sd.DescriptorId, s.IsInheritanceBreak, s.ShareName, s.ScannedUtc
        FROM #FsObjectsStaging s
        JOIN dbo.SecurityDescriptors sd ON sd.DescriptorHash = s.DescriptorHash
    ) AS source
        ON target.PathHash = source.PathHash
    WHEN MATCHED THEN
        UPDATE SET target.DescriptorId = source.DescriptorId,
                   target.IsInheritanceBreak = source.IsInheritanceBreak,
                   target.LastScannedUtc = source.ScannedUtc,
                   target.ParentPathHash = source.ParentPathHash
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (PathHash, FullPath, ParentPathHash, IsDirectory, DescriptorId, IsInheritanceBreak, ShareName, LastScannedUtc)
        VALUES (source.PathHash, source.FullPath, source.ParentPathHash, source.IsDirectory,
                source.DescriptorId, source.IsInheritanceBreak, source.ShareName, source.ScannedUtc);

    -- Step 4: resolve ParentObjectId for any row (from this batch or an earlier one)
    -- whose parent has now shown up. See design doc §5.1.1 — NULL here is a normal,
    -- short-lived state during a scan, not an error, except for the share root.
    UPDATE child
    SET child.ParentObjectId = parent.ObjectId
    FROM dbo.FsObjects child
    JOIN dbo.FsObjects parent ON child.ParentPathHash = parent.PathHash
    WHERE child.ParentObjectId IS NULL;

    COMMIT TRANSACTION;
END
GO
