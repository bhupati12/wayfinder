using System.Security.Cryptography;
using System.Text;

namespace GuidesImporter.Application.Helpers;

public static class Hashing
{
    /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes of <paramref name="text"/> (64 characters).</summary>
    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
