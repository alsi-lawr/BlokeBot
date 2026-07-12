using System.Collections.Immutable;

namespace BlokeBot.Twitch.Runtime;

internal sealed record PublicChatOutboxBatch
{
    public required string Channel { get; init; }

    public required ImmutableArray<PublicChatOutboxItem> Items { get; init; }

    public required DateTimeOffset EnqueuedAt { get; init; }
}

internal sealed record PublicChatOutboxItem
{
    public required string Message { get; init; }

    public required PublicChatDeduplicationKey DeduplicationKey { get; init; }
}

internal readonly record struct PublicChatOutboxReceipt(ImmutableArray<long> MessageIds)
{
    public static PublicChatOutboxReceipt Empty { get; } = new([]);
}

internal readonly record struct PublicChatClaimToken(Guid Value);

internal readonly record struct PublicChatDeduplicationKey(string Value);

internal sealed record PublicChatClaimedMessage
{
    public required long Id { get; init; }

    public required string Channel { get; init; }

    public required string Message { get; init; }

    public required DateTimeOffset EnqueuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required int Attempt { get; init; }

    public required PublicChatClaimToken ClaimToken { get; init; }

    public required DateTimeOffset ClaimExpiresAt { get; init; }

    public required PublicChatDeduplicationKey DeduplicationKey { get; init; }
}

internal readonly record struct PublicChatPendingMessage(
    string Channel,
    DateTimeOffset EnqueuedAt
);

internal abstract record PublicChatClaimOutcome
{
    private PublicChatClaimOutcome() { }

    public sealed record Claimed(PublicChatClaimedMessage Message)
        : PublicChatClaimOutcome;

    public sealed record AwaitingAvailability(DateTimeOffset AvailableAt)
        : PublicChatClaimOutcome;

    public sealed record Empty : PublicChatClaimOutcome;

    public sealed record Contended : PublicChatClaimOutcome;
}

internal abstract record PublicChatClaimUpdate
{
    private PublicChatClaimUpdate() { }

    public sealed record Applied : PublicChatClaimUpdate;

    public sealed record OwnershipLost : PublicChatClaimUpdate;

    public sealed record Contended : PublicChatClaimUpdate;

    public sealed record Expired : PublicChatClaimUpdate;
}

internal interface IPublicChatOutbox
{
    ValueTask<PublicChatOutboxReceipt> EnqueueAsync(
        PublicChatOutboxBatch batch,
        CancellationToken cancellationToken
    );

    ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
        DateTimeOffset now,
        DateTimeOffset claimExpiresAt,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown,
        CancellationToken cancellationToken
    );

    ValueTask<PublicChatClaimUpdate> BeginSendAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset claimExpiresAt,
        CancellationToken cancellationToken
    );

    ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    );

    ValueTask<PublicChatClaimUpdate> RecordPostBoundaryInterruptionAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Send diagnostic,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken
    );

    ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken
    );

    ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
        CancellationToken cancellationToken
    );
}
