using FileAccessGovernance.QueryApi.Data;
using FileAccessGovernance.QueryApi.Services;
using FileAccessGovernance.Shared;
using Moq;
using Xunit;

namespace QueryApi.Tests;

public class FolderAccessServiceTests
{
    private readonly Mock<IFsObjectRepository> _repository = new();
    private readonly Mock<ISidNameResolver> _sidResolver = new();
    private readonly FolderAccessService _service;

    public FolderAccessServiceTests()
    {
        _service = new FolderAccessService(_repository.Object, _sidResolver.Object);
    }

    [Fact]
    public async Task GetAccessAsync_ReturnsNull_WhenPathNotScanned()
    {
        _repository
            .Setup(r => r.FindByPathHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FsObject?)null);

        var result = await _service.GetAccessAsync(@"\\srv1\share01\NotScanned", CancellationToken.None);

        Assert.Null(result); // controller maps this to 404 — see AccessController
    }

    [Fact]
    public async Task GetAccessAsync_LooksUpByHashOfTheNormalizedPath()
    {
        // Regression coverage for the normalization fix: two differently-cased
        // inputs for the same object must produce the same lookup hash.
        byte[]? capturedHash = null;
        _repository
            .Setup(r => r.FindByPathHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<byte[], CancellationToken>((hash, _) => capturedHash = hash)
            .ReturnsAsync((FsObject?)null);

        await _service.GetAccessAsync(@"\\SRV1\SHARE01\Finance", CancellationToken.None);
        var hashForUppercaseInput = capturedHash;

        await _service.GetAccessAsync(@"\\srv1\share01\FINANCE", CancellationToken.None);
        var hashForMixedCaseInput = capturedHash;

        Assert.Equal(
            HashUtil.Sha256Bytes(PathNormalizer.Normalize(@"\\srv1\share01\Finance")),
            hashForUppercaseInput);
        Assert.Equal(hashForUppercaseInput, hashForMixedCaseInput);
    }

    [Fact]
    public async Task GetAccessAsync_MapsAcesAndResolvesTrusteeNames_IncludingOrphanedSid()
    {
        var obj = new FsObject
        {
            ObjectId = 1,
            FullPath = @"\\srv1\share01\Finance",
            IsDirectory = true,
            IsInheritanceBreak = true,
            DescriptorId = 42,
            LastScannedUtc = new DateTime(2026, 8, 5, 2, 14, 0, DateTimeKind.Utc)
        };

        var aces = new List<SecurityDescriptorAce>
        {
            new() { AceId = 1, DescriptorId = 42, TrusteeSid = "S-1-5-21-1-1-1-513", AceType = 0, AccessMask = 2032127, IsInherited = true },
            new() { AceId = 2, DescriptorId = 42, TrusteeSid = "S-1-5-21-1-1-1-999", AceType = 1, AccessMask = 2032127, IsInherited = false }
        };

        _repository.Setup(r => r.FindByPathHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(obj);
        _repository.Setup(r => r.GetAcesForDescriptorAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(aces);

        _sidResolver
            .Setup(s => s.ResolveNamesAsync(aces, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string?>
            {
                ["S-1-5-21-1-1-1-513"] = "CONTOSO\\jane.doe",
                ["S-1-5-21-1-1-1-999"] = null // orphaned SID — no matching AD object
            });

        var result = await _service.GetAccessAsync(@"\\srv1\share01\Finance", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsInheritanceBreak);
        Assert.Equal(2, result.Entries.Count);

        var allow = result.Entries.Single(e => e.TrusteeSid == "S-1-5-21-1-1-1-513");
        Assert.Equal("Allow", allow.AceType);
        Assert.Equal("CONTOSO\\jane.doe", allow.TrusteeName);

        var deny = result.Entries.Single(e => e.TrusteeSid == "S-1-5-21-1-1-1-999");
        Assert.Equal("Deny", deny.AceType);
        Assert.Null(deny.TrusteeName); // orphaned SID returned as-is, not treated as an error — see design doc §4
    }
}
