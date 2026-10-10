using System.Security.Cryptography;

namespace ThreeWayMatch.Domain.Extraction;

/// <summary>The SHA-256 of a file's bytes as 64 lower-case hex characters. The same file always gives the same hash.</summary>
public static class ContentHash
{
    public static string Sha256Hex(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
