using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BlokeBot.Core.Features.Bounties;

internal static class BountyPauseAdjustmentOperationId
{
    private static readonly Guid _namespaceId = new("0a0b02e1-c487-46d2-9ff7-7c90f252c9e8");

    internal static Guid Create(Guid bountyPublicId, DateTime pausedAtUtc, DateTime recoveredAtUtc)
    {
        Span<byte> identity = stackalloc byte[48];
        _ = _namespaceId.TryWriteBytes(identity[..16]);
        _ = bountyPublicId.TryWriteBytes(identity[16..32]);
        BinaryPrimitives.WriteInt64BigEndian(identity[32..40], pausedAtUtc.Ticks);
        BinaryPrimitives.WriteInt64BigEndian(identity[40..], recoveredAtUtc.Ticks);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        _ = SHA256.HashData(identity, hash);
        return new Guid(hash[..16]);
    }
}
