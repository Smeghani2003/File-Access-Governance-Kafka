using System.Security.Cryptography;
using System.Text;

namespace FileAccessGovernance.Shared;

public static class HashUtil
{
    /// <summary>
    /// PathHash as stored in dbo.FsObjects.PathHash (BINARY(32)). Caller must pass
    /// an already-normalized path — see <see cref="PathNormalizer"/>.
    /// </summary>
    public static byte[] Sha256Bytes(string normalizedValue) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(normalizedValue));

    /// <summary>
    /// DescriptorHash as stored in dbo.SecurityDescriptors.DescriptorHash (CHAR(64) hex),
    /// computed from the raw SDDL string of a security descriptor.
    /// </summary>
    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
