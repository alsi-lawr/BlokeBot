using BlokeBot.Core.Features.Overlays.Full;

namespace BlokeBot.Core.Features.Overlays;

internal abstract record OverlayCueTarget
{
    private OverlayCueTarget() { }

    internal abstract int HostId { get; }
    internal abstract Guid OverlayId { get; }

    internal abstract TResult Match<TResult>(
        Func<Simple, TResult> simple,
        Func<Full, TResult> full
    );

    internal sealed record Simple(ResolvedOverlayInstance Instance) : OverlayCueTarget
    {
        internal override int HostId => Instance.HostId;
        internal override Guid OverlayId => Instance.OverlayId;

        internal override TResult Match<TResult>(
            Func<Simple, TResult> simple,
            Func<Full, TResult> full
        ) => simple(this);
    }

    internal sealed record Full(int OwnerHostId, Guid PublicId, FullOverlayGeneration Generation)
        : OverlayCueTarget
    {
        internal override int HostId => OwnerHostId;
        internal override Guid OverlayId => PublicId;

        internal override TResult Match<TResult>(
            Func<Simple, TResult> simple,
            Func<Full, TResult> full
        ) => full(this);
    }
}
