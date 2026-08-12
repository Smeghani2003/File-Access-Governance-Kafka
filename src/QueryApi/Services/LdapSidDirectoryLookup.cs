using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Options;

namespace FileAccessGovernance.QueryApi.Services;

public sealed class LdapOptions
{
    public string Host { get; set; } = default!;
    public int Port { get; set; } = 389;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string SearchBaseDn { get; set; } = default!;
}

/// <summary>Real Active Directory lookup via LDAP — resolves a SID to displayName (falling
/// back to sAMAccountName) using System.DirectoryServices.Protocols, which is cross-platform
/// and doesn't require Windows ADSI. Requires a reachable domain controller; use
/// <see cref="NullSidDirectoryLookup"/> for local dev without one — see design doc §7.</summary>
public sealed class LdapSidDirectoryLookup : ISidDirectoryLookup
{
    private readonly LdapOptions _options;

    public LdapSidDirectoryLookup(IOptions<LdapOptions> options) => _options = options.Value;

    public Task<string?> LookupDisplayNameAsync(string sid, CancellationToken ct)
    {
        // SearchRequest.SendRequest doesn't accept a CancellationToken; keeping this
        // synchronous-under-a-Task wrapper explicit rather than pretending it's cancellable.
        return Task.Run(() =>
        {
            var filter = $"(objectSid={FormatBinaryForLdapFilter(ParseSidToBytes(sid))})";

            using var connection = new LdapConnection(new LdapDirectoryIdentifier(_options.Host, _options.Port));
            if (_options.Username is not null)
            {
                connection.Credential = new NetworkCredential(_options.Username, _options.Password);
            }
            connection.AuthType = AuthType.Negotiate;

            var request = new SearchRequest(
                _options.SearchBaseDn, filter, System.DirectoryServices.Protocols.SearchScope.Subtree,
                "displayName", "sAMAccountName");

            var response = (SearchResponse)connection.SendRequest(request);
            if (response.Entries.Count == 0) return null;

            var entry = response.Entries[0];
            var displayName = entry.Attributes["displayName"]?[0]?.ToString();
            var samAccountName = entry.Attributes["sAMAccountName"]?[0]?.ToString();
            return displayName ?? samAccountName;
        }, ct);
    }

    private static string FormatBinaryForLdapFilter(byte[] bytes) =>
        string.Concat(bytes.Select(b => $"\\{b:x2}"));

    /// <summary>
    /// Manual SID-string ("S-1-5-21-...") to binary conversion per the documented
    /// SID structure (MS-DTYP §2.4.2) — deliberately not System.Security.Principal.SecurityIdentifier,
    /// which the platform-compatibility analyzer (CA1416) flags as Windows-only. This
    /// service runs on Linux (see design doc §1), so it shouldn't depend on a type
    /// annotated as unsupported there, even if it happens to work in practice.
    /// </summary>
    private static byte[] ParseSidToBytes(string sid)
    {
        var parts = sid.Split('-');
        if (parts.Length < 3 || parts[0] != "S")
        {
            throw new FormatException($"'{sid}' is not a valid SID string.");
        }

        var revision = byte.Parse(parts[1]);
        var authority = ulong.Parse(parts[2]);
        var subAuthorities = parts.Skip(3).Select(uint.Parse).ToArray();

        var bytes = new byte[8 + subAuthorities.Length * 4];
        bytes[0] = revision;
        bytes[1] = (byte)subAuthorities.Length;
        for (var i = 0; i < 6; i++)
        {
            bytes[2 + i] = (byte)(authority >> (8 * (5 - i)));
        }
        for (var i = 0; i < subAuthorities.Length; i++)
        {
            var sub = subAuthorities[i];
            var offset = 8 + i * 4;
            bytes[offset] = (byte)sub;
            bytes[offset + 1] = (byte)(sub >> 8);
            bytes[offset + 2] = (byte)(sub >> 16);
            bytes[offset + 3] = (byte)(sub >> 24);
        }
        return bytes;
    }
}
