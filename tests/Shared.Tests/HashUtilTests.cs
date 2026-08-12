using FileAccessGovernance.Shared;
using Xunit;

namespace Shared.Tests;

public class HashUtilTests
{
    [Fact]
    public void Sha256Bytes_IsDeterministic()
    {
        var a = HashUtil.Sha256Bytes("\\\\SRV1\\SHARE01\\FINANCE");
        var b = HashUtil.Sha256Bytes("\\\\SRV1\\SHARE01\\FINANCE");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Sha256Bytes_DifferentInputsProduceDifferentHashes()
    {
        var a = HashUtil.Sha256Bytes("\\\\SRV1\\SHARE01\\FINANCE");
        var b = HashUtil.Sha256Bytes("\\\\SRV1\\SHARE01\\HR");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Sha256Bytes_Is32BytesLong_MatchingBinary32ColumnType()
    {
        Assert.Equal(32, HashUtil.Sha256Bytes("anything").Length);
    }

    [Fact]
    public void Sha256Hex_Is64CharactersLong_MatchingChar64ColumnType()
    {
        Assert.Equal(64, HashUtil.Sha256Hex("O:S-1-5-21-1-1-1-512D:(A;;FA;;;S-1-5-21-1-1-1-513)").Length);
    }

    [Fact]
    public void PathNormalizerPlusHashUtil_ProducesSamePathHash_ForDifferentlyCasedPaths()
    {
        // End-to-end version of the PathNormalizerTests regression: proves the
        // combination that's actually used (Normalize then hash) collapses
        // differently-cased paths to the same PathHash, matching how the real
        // pipeline computes it in StagingWriter and FolderAccessService.
        var hashA = HashUtil.Sha256Bytes(PathNormalizer.Normalize(@"\\srv1\share01\Finance"));
        var hashB = HashUtil.Sha256Bytes(PathNormalizer.Normalize(@"\\SRV1\SHARE01\FINANCE"));

        Assert.Equal(hashA, hashB);
    }
}
