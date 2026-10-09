using System.Collections.Immutable;
using BlokeBot.Core.Features.Overlays.Full;

namespace BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;

internal sealed record FullOverlayPackage(
    int FormatVersion,
    string Name,
    FullOverlayDocument Document,
    ImmutableArray<FullOverlayPackageAsset> Assets,
    ImmutableArray<FullOverlayPackageContent> Contents
);

internal sealed record FullOverlayPackageAsset(Guid Id, string Name, Guid? ContentId);

internal sealed record FullOverlayPackageContent(Guid Id, string ContentType, long ByteLength)
{
    internal string EntryName => $"media/{Id:N}";
}

internal sealed record FullOverlayImportApplied(
    FullOverlayCreation Created,
    bool NotificationPending
);
