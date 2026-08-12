using FileAccessGovernance.IngestionConsumer.Sql;
using FileAccessGovernance.Shared.Models;
using Microsoft.Data.SqlClient;
using Xunit;

namespace IngestionConsumer.Tests;

/// <summary>
/// Integration test against a REAL SQL Server instance (not a mock) — exercises the
/// actual StagingWriter + MergeRunner C# code from src/IngestionConsumer/Sql against
/// the corrected schema in /db/migrations, proving the production code path works,
/// not just the hand-written validation script used while fixing the design doc.
///
/// Requires FAG_TEST_CONNECTION_STRING to point at a reachable SQL Server with the
/// schema from /db/migrations already applied — see README.md "Running tests".
/// From inside the SDK container reaching a container on the host, use
/// host.docker.internal rather than localhost.
/// </summary>
public class MergePipelineTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("FAG_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "Set FAG_TEST_CONNECTION_STRING to run MergePipelineTests — see README.md \"Running tests\".");

    private readonly StagingWriter _stagingWriter = new();
    private readonly MergeRunner _mergeRunner = new();

    public async Task InitializeAsync()
    {
        // Isolate this test run from the earlier manual SQL validation and from
        // other test runs — delete anything the test itself will recreate.
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(
            "DELETE FROM dbo.SecurityDescriptorAces; DELETE FROM dbo.FsObjects; DELETE FROM dbo.SecurityDescriptors;",
            connection);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WriteBatchThenMerge_ResolvesParentAndDedupesDescriptors_AgainstRealSqlServer()
    {
        var now = DateTime.UtcNow;
        var rootDescriptor = new SecurityDescriptorRecord(
            DescriptorHash: "ROOTHASH0000000000000000000000000000000000000000000000000001",
            OwnerSid: "S-1-5-21-1-1-1-512",
            RawSddl: "O:S-1-5-21-1-1-1-512D:(A;;FA;;;S-1-5-21-1-1-1-513)",
            Aces: new[] { new AceRecord("S-1-5-21-1-1-1-513", AceType.Allow, 2032127, IsInherited: false, AceInheritanceFlags.ContainerInherit) });

        var childDescriptor = new SecurityDescriptorRecord(
            DescriptorHash: "CHILDHASH000000000000000000000000000000000000000000000001",
            OwnerSid: "S-1-5-21-1-1-1-512",
            RawSddl: "O:S-1-5-21-1-1-1-512D:(D;;FA;;;S-1-5-21-1-1-1-999)(A;;FA;;;S-1-5-21-1-1-1-513)",
            Aces: new[]
            {
                new AceRecord("S-1-5-21-1-1-1-999", AceType.Deny, 2032127, IsInherited: false, AceInheritanceFlags.None),
                new AceRecord("S-1-5-21-1-1-1-513", AceType.Allow, 2032127, IsInherited: true, AceInheritanceFlags.ContainerInherit)
            });

        var batch = new List<ObjectRecord>
        {
            new(@"\\SRV1\SHARE01", null, IsDirectory: true, rootDescriptor.DescriptorHash, IsInheritanceBreak: false, "SHARE01", now, rootDescriptor),
            new(@"\\SRV1\SHARE01\FINANCE", @"\\SRV1\SHARE01", IsDirectory: true, childDescriptor.DescriptorHash, IsInheritanceBreak: true, "SHARE01", now, childDescriptor)
        };

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await _stagingWriter.WriteBatchAsync(connection, batch, CancellationToken.None);
            await _mergeRunner.RunMergeAsync(connection, CancellationToken.None);
        }

        // Assert using a fresh connection/session — proves the data is really
        // committed, not just visible within the temp-table session.
        await using var verify = new SqlConnection(ConnectionString);
        await verify.OpenAsync();

        var (objectCount, unresolvedParents) = await CountObjectsAsync(verify);
        Assert.Equal(2, objectCount);
        Assert.Equal(0, unresolvedParents); // ParentObjectId reconciliation — design doc §5.1.1

        var descriptorCount = await ScalarAsync(verify, "SELECT COUNT(*) FROM dbo.SecurityDescriptors");
        Assert.Equal(2, descriptorCount);

        var aceCount = await ScalarAsync(verify, "SELECT COUNT(*) FROM dbo.SecurityDescriptorAces");
        Assert.Equal(3, aceCount);
    }

    [Fact]
    public async Task ReplayingTheSameBatch_DoesNotCreateDuplicates()
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityDescriptorRecord(
            DescriptorHash: "IDEMPOTENTHASH00000000000000000000000000000000000000000001",
            OwnerSid: "S-1-5-21-1-1-1-512",
            RawSddl: "O:S-1-5-21-1-1-1-512D:(A;;FA;;;S-1-5-21-1-1-1-513)",
            Aces: new[] { new AceRecord("S-1-5-21-1-1-1-513", AceType.Allow, 2032127, IsInherited: false, AceInheritanceFlags.None) });

        var batch = new List<ObjectRecord>
        {
            new(@"\\SRV1\SHARE01", null, IsDirectory: true, descriptor.DescriptorHash, IsInheritanceBreak: false, "SHARE01", now, descriptor)
        };

        // Same batch, processed twice — simulates a consumer crash-and-replay after
        // the write but before the Kafka offset commit (design doc §5.1 step 6).
        for (var i = 0; i < 2; i++)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await _stagingWriter.WriteBatchAsync(connection, batch, CancellationToken.None);
            await _mergeRunner.RunMergeAsync(connection, CancellationToken.None);
        }

        await using var verify = new SqlConnection(ConnectionString);
        await verify.OpenAsync();
        Assert.Equal(1, await ScalarAsync(verify, "SELECT COUNT(*) FROM dbo.FsObjects"));
        Assert.Equal(1, await ScalarAsync(verify, "SELECT COUNT(*) FROM dbo.SecurityDescriptors"));
        Assert.Equal(1, await ScalarAsync(verify, "SELECT COUNT(*) FROM dbo.SecurityDescriptorAces"));
    }

    private static async Task<(int objectCount, int unresolvedParents)> CountObjectsAsync(SqlConnection connection)
    {
        var objectCount = await ScalarAsync(connection, "SELECT COUNT(*) FROM dbo.FsObjects");
        var unresolvedParents = await ScalarAsync(connection,
            "SELECT COUNT(*) FROM dbo.FsObjects WHERE ParentObjectId IS NULL AND ParentPathHash IS NOT NULL");
        return (objectCount, unresolvedParents);
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var cmd = new SqlCommand(sql, connection);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
