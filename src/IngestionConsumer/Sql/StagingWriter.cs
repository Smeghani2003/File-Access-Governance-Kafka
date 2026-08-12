using System.Data;
using FileAccessGovernance.Shared;
using FileAccessGovernance.Shared.Models;
using Microsoft.Data.SqlClient;

namespace FileAccessGovernance.IngestionConsumer.Sql;

/// <summary>
/// Creates the three LOCAL TEMP tables (design doc §3, "Staging is session-scoped,
/// not a shared table") on the caller's connection and bulk-loads a batch into them.
/// Local temp tables are only visible on the connection that created them, which is
/// what makes this safe under N concurrent consumer instances without a BatchId
/// column or any extra coordination.
/// </summary>
public sealed class StagingWriter
{
    private const string CreateTempTablesSql = """
        CREATE TABLE #FsObjectsStaging (
            PathHash           BINARY(32)     NOT NULL,
            FullPath           NVARCHAR(4000) NOT NULL,
            ParentPathHash     BINARY(32)     NULL,
            IsDirectory        BIT            NOT NULL,
            DescriptorHash     CHAR(64)       NOT NULL,
            IsInheritanceBreak BIT            NOT NULL,
            ShareName          NVARCHAR(256)  NOT NULL,
            ScannedUtc         DATETIME2      NOT NULL
        );

        CREATE TABLE #SecurityDescriptorsStaging (
            DescriptorHash CHAR(64)      NOT NULL,
            OwnerSid       NVARCHAR(184) NOT NULL,
            RawSddl        NVARCHAR(MAX) NOT NULL,
            ScannedUtc     DATETIME2     NOT NULL
        );

        CREATE TABLE #SecurityDescriptorAcesStaging (
            DescriptorHash   CHAR(64)      NOT NULL,
            TrusteeSid       NVARCHAR(184) NOT NULL,
            AceType          TINYINT       NOT NULL,
            AccessMask       INT           NOT NULL,
            IsInherited      BIT           NOT NULL,
            InheritanceFlags TINYINT       NOT NULL
        );
        """;

    public async Task WriteBatchAsync(SqlConnection connection, IReadOnlyList<ObjectRecord> batch, CancellationToken ct)
    {
        await using (var createTables = new SqlCommand(CreateTempTablesSql, connection))
        {
            await createTables.ExecuteNonQueryAsync(ct);
        }

        await BulkCopyAsync(connection, "#FsObjectsStaging", BuildFsObjectsTable(batch), ct);

        // A batch can contain the same new descriptor more than once (many objects
        // sharing one descriptor, discovered by the agent in the same window) —
        // dedupe within the batch itself; the MERGE also dedupes against what's
        // already in SQL Server, but there's no reason to ship duplicate rows.
        var newDescriptors = batch
            .Select(r => r.NewDescriptor)
            .Where(d => d is not null)
            .Cast<SecurityDescriptorRecord>()
            .GroupBy(d => d.DescriptorHash)
            .Select(g => g.First())
            .ToList();

        await BulkCopyAsync(connection, "#SecurityDescriptorsStaging", BuildDescriptorsTable(newDescriptors, batch), ct);
        await BulkCopyAsync(connection, "#SecurityDescriptorAcesStaging", BuildAcesTable(newDescriptors), ct);
    }

    private static DataTable BuildFsObjectsTable(IReadOnlyList<ObjectRecord> batch)
    {
        var table = new DataTable();
        table.Columns.Add("PathHash", typeof(byte[]));
        table.Columns.Add("FullPath", typeof(string));
        table.Columns.Add("ParentPathHash", typeof(byte[]));
        table.Columns.Add("IsDirectory", typeof(bool));
        table.Columns.Add("DescriptorHash", typeof(string));
        table.Columns.Add("IsInheritanceBreak", typeof(bool));
        table.Columns.Add("ShareName", typeof(string));
        table.Columns.Add("ScannedUtc", typeof(DateTime));

        foreach (var record in batch)
        {
            var normalizedPath = PathNormalizer.Normalize(record.FullPath);
            var parentHash = record.ParentPath is null
                ? (object)DBNull.Value
                : HashUtil.Sha256Bytes(PathNormalizer.Normalize(record.ParentPath));

            table.Rows.Add(
                HashUtil.Sha256Bytes(normalizedPath),
                record.FullPath,
                parentHash,
                record.IsDirectory,
                record.DescriptorHash,
                record.IsInheritanceBreak,
                record.ShareName,
                record.ScannedUtc);
        }

        return table;
    }

    private static DataTable BuildDescriptorsTable(IReadOnlyList<SecurityDescriptorRecord> newDescriptors, IReadOnlyList<ObjectRecord> batch)
    {
        var table = new DataTable();
        table.Columns.Add("DescriptorHash", typeof(string));
        table.Columns.Add("OwnerSid", typeof(string));
        table.Columns.Add("RawSddl", typeof(string));
        table.Columns.Add("ScannedUtc", typeof(DateTime));

        // ScannedUtc per descriptor: use the timestamp of whichever object record introduced it.
        var scannedAtByHash = batch
            .Where(r => r.NewDescriptor is not null)
            .GroupBy(r => r.NewDescriptor!.DescriptorHash)
            .ToDictionary(g => g.Key, g => g.First().ScannedUtc);

        foreach (var descriptor in newDescriptors)
        {
            table.Rows.Add(descriptor.DescriptorHash, descriptor.OwnerSid, descriptor.RawSddl, scannedAtByHash[descriptor.DescriptorHash]);
        }

        return table;
    }

    private static DataTable BuildAcesTable(IReadOnlyList<SecurityDescriptorRecord> newDescriptors)
    {
        var table = new DataTable();
        table.Columns.Add("DescriptorHash", typeof(string));
        table.Columns.Add("TrusteeSid", typeof(string));
        table.Columns.Add("AceType", typeof(byte));
        table.Columns.Add("AccessMask", typeof(int));
        table.Columns.Add("IsInherited", typeof(bool));
        table.Columns.Add("InheritanceFlags", typeof(byte));

        foreach (var descriptor in newDescriptors)
        {
            foreach (var ace in descriptor.Aces)
            {
                table.Rows.Add(descriptor.DescriptorHash, ace.TrusteeSid, (byte)ace.AceType, ace.AccessMask, ace.IsInherited, (byte)ace.InheritanceFlags);
            }
        }

        return table;
    }

    private static async Task BulkCopyAsync(SqlConnection connection, string destinationTable, DataTable data, CancellationToken ct)
    {
        if (data.Rows.Count == 0) return;

        using var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = destinationTable };
        foreach (DataColumn column in data.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }
        await bulkCopy.WriteToServerAsync(data, ct);
    }
}
