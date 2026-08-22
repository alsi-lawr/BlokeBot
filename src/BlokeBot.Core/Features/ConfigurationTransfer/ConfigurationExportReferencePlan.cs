using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed record ConfigurationExportReference(
    string Id,
    string Name,
    string? ParentId = null
);

internal sealed record ConfigurationExportReferencePlan(
    IReadOnlyDictionary<int, ConfigurationExportReference> Commands,
    IReadOnlyDictionary<Guid, ConfigurationExportReference> OverlayInstances,
    IReadOnlyDictionary<Guid, ConfigurationExportReference> OverlayCues,
    IReadOnlyDictionary<Guid, ConfigurationExportReference> OverlayMedia,
    IReadOnlyDictionary<string, ConfigurationExportReference> CustomRewards
)
{
    internal static async Task<ConfigurationExportReferencePlan> LoadAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var commands = await db
            .CustomCommands.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.Id)
            .Select(value => new { value.Id, value.Name })
            .ToArrayAsync(cancellationToken);
        var overlays = await db
            .OverlayInstances.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .Select(value => new { value.PublicId, value.Name })
            .ToArrayAsync(cancellationToken);
        var cues = await db
            .OverlayCues.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .Select(value => new { value.PublicId, value.Name })
            .ToArrayAsync(cancellationToken);
        var media = await db
            .OverlayMediaAssets.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .Select(value => new { value.PublicId, value.Name })
            .ToArrayAsync(cancellationToken);
        var rewards = await db
            .TwitchCustomRewards.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Title)
            .ThenBy(value => value.ProviderRewardId)
            .Select(value => new { value.ProviderRewardId, value.Title })
            .ToArrayAsync(cancellationToken);

        return new(
            commands
                .Select(
                    (value, index) =>
                        (
                            value.Id,
                            Reference: new ConfigurationExportReference(
                                Id("command", index),
                                value.Name
                            )
                        )
                )
                .ToDictionary(value => value.Id, value => value.Reference),
            overlays
                .Select(
                    (value, index) =>
                        (
                            value.PublicId,
                            Reference: new ConfigurationExportReference(
                                Id("overlay", index),
                                value.Name
                            )
                        )
                )
                .ToDictionary(value => value.PublicId, value => value.Reference),
            cues.Select(
                    (value, index) =>
                        (
                            value.PublicId,
                            Reference: new ConfigurationExportReference(
                                Id("cue", index),
                                value.Name
                            )
                        )
                )
                .ToDictionary(value => value.PublicId, value => value.Reference),
            media
                .Select(
                    (value, index) =>
                        (
                            value.PublicId,
                            Reference: new ConfigurationExportReference(
                                Id("media", index),
                                value.Name
                            )
                        )
                )
                .ToDictionary(value => value.PublicId, value => value.Reference),
            rewards
                .Select(
                    (value, index) =>
                        (
                            value.ProviderRewardId,
                            Reference: new ConfigurationExportReference(
                                Id("reward", index),
                                value.Title
                            )
                        )
                )
                .ToDictionary(
                    value => value.ProviderRewardId,
                    value => value.Reference,
                    StringComparer.Ordinal
                )
        );
    }

    private static string Id(string prefix, int index) => $"{prefix}-{index + 1:D4}";
}
