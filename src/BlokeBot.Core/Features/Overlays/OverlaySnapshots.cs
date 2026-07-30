using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

public interface IOverlayStateProvider
{
    Task<OverlaySnapshotProjection> ProjectAsync(
        ResolvedOverlayInstance instance,
        CancellationToken cancellationToken
    );
}

public abstract record OverlaySnapshotProjection
{
    private OverlaySnapshotProjection() { }

    public sealed record EmptyV1(EmptyV1OverlaySnapshot Snapshot) : OverlaySnapshotProjection;

    public sealed record Unavailable : OverlaySnapshotProjection;
}

public sealed record EmptyV1OverlaySnapshot
{
    public string OverlayType => "empty";

    public int SchemaVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public EmptyV1OverlayPresentationState State { get; } = new();
}

public sealed record EmptyV1OverlayPresentationState;

internal sealed class OverlayServerEpoch
{
    internal Guid Value { get; } = Guid.NewGuid();
}

internal sealed class OverlayStateProvider(
    OverlayServerEpoch serverEpoch,
    TimeProvider timeProvider
) : IOverlayStateProvider
{
    public Task<OverlaySnapshotProjection> ProjectAsync(
        ResolvedOverlayInstance instance,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        OverlaySnapshotProjection projection = instance
            is { Type: OverlayType.Empty, Configuration: OverlayConfiguration.EmptyV1 }
            ? new OverlaySnapshotProjection.EmptyV1(
                new EmptyV1OverlaySnapshot
                {
                    ServerEpoch = serverEpoch.Value,
                    Sequence = instance.Revision.Value,
                    GeneratedAtUtc = timeProvider.GetUtcNow(),
                }
            )
            : new OverlaySnapshotProjection.Unavailable();
        return Task.FromResult(projection);
    }
}
