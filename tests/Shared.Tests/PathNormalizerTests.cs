using FileAccessGovernance.Shared;
using Xunit;

namespace Shared.Tests;

public class PathNormalizerTests
{
    [Fact]
    public void Normalize_UppercasesTheEntirePath_NotJustTheShareSegment()
    {
        // Regression test for the bug fixed in design doc §5.2: an earlier draft
        // only uppercased the host/share portion, which would have hashed these
        // two paths (the same folder on disk, since NTFS/SMB is case-insensitive
        // end-to-end) to two different PathHash values.
        var a = PathNormalizer.Normalize(@"\\srv1\share01\Finance\Reports");
        var b = PathNormalizer.Normalize(@"\\SRV1\SHARE01\FINANCE\REPORTS");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Normalize_TrimsTrailingSeparator()
    {
        Assert.Equal(PathNormalizer.Normalize(@"\\srv1\share01\Finance"), PathNormalizer.Normalize(@"\\srv1\share01\Finance\"));
    }

    [Fact]
    public void Normalize_TrimsSurroundingWhitespace()
    {
        Assert.Equal(PathNormalizer.Normalize(@"\\srv1\share01"), PathNormalizer.Normalize("  \\\\srv1\\share01  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_RejectsNullOrWhitespace(string? path)
    {
        // ArgumentNullException specifically for null (it derives from ArgumentException),
        // plain ArgumentException for empty/whitespace — ThrowsAny accepts either.
        Assert.ThrowsAny<ArgumentException>(() => PathNormalizer.Normalize(path!));
    }
}
