using System.Security.Cryptography;
using System.Text;

namespace SilentStream.Core.Remote;

/// <summary>
/// PIN pairing + device-token helpers for the remote-control server (확장계획서 §4.4, D11).
/// Only SHA-256 hashes of device tokens are persisted in config — never the raw token or PIN.
/// </summary>
public static class RemoteAuth
{
    /// <summary>A fresh 6-digit pairing PIN (cryptographically random).</summary>
    public static string NewPin() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>A fresh opaque device token (256-bit, hex).</summary>
    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Lower-case hex SHA-256 of a token, as stored in config.Remote.deviceTokens.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>True when <paramref name="token"/>'s hash is among the known paired hashes.</summary>
    public static bool IsKnownToken(IEnumerable<string> knownHashes, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }
        var hash = HashToken(token);
        return knownHashes.Any(h => FixedTimeEquals(h, hash));
    }

    private static bool FixedTimeEquals(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
