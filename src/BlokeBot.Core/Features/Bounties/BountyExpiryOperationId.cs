using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BlokeBot.Core.Features.Bounties;

internal static class BountyExpiryOperationId
{
    private static readonly Guid _namespaceId = new("fe75491b-48a8-45e3-a321-07dfacde45db");

    internal static Guid Create(Guid bountyPublicId, DateTime expiresAtUtc)
    {
        Span<byte> identity = stackalloc byte[40];
        _ = _namespaceId.TryWriteBytes(identity[..16]);
        _ = bountyPublicId.TryWriteBytes(identity[16..32]);
        BinaryPrimitives.WriteInt64BigEndian(identity[32..], expiresAtUtc.Ticks);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        _ = SHA256.HashData(identity, hash);
        return new Guid(hash[..16]);
    }
}
