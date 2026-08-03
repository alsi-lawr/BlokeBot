using System.Security.Cryptography;
using System.Text;

namespace BlokeBot.Core.Features.Overlays;

public interface IOverlayAccessKeyGenerator
{
    string Generate();
}

public sealed class CryptographicOverlayAccessKeyGenerator : IOverlayAccessKeyGenerator
{
    private const int _entropyBytes = 32;

    public string Generate() =>
        Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(_entropyBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

internal static class OverlayAccessKeyDigest
{
    internal const int Size = 32;

    internal static byte[] Compute(string accessKey) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(accessKey));

    internal static bool HasCanonicalShape(string accessKey) =>
        accessKey.Length == 43
        && accessKey.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
        );
}
