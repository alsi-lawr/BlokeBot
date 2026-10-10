using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationTransferCoordinator
{
    private static async Task CaptureSkippedProfilesAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessingSectionV1 section,
        SectionImportSelection selection,
        IDictionary<string, GuessRoundProfile> profiles,
        CancellationToken ct
    )
    {
        var existing = await db
            .Profiles.AsNoTracking()
            .Where(row => row.HostId == hostId)
            .Select(row => new { Profile = row, HasRounds = row.Rounds.Any() })
            .ToArrayAsync(ct);
        var plan = GuessingConfigurationImportMapping.Resolve(
            section,
            selection,
            existing
                .Select(row => new GuessingConfigurationImportTarget(
                    row.Profile.Id,
                    row.Profile.Name,
                    row.Profile.Slug,
                    row.HasRounds
                ))
                .ToArray()
        );
        foreach (var (importedId, targetId) in plan.TargetIds)
        {
            profiles.Add(importedId, existing.Single(row => row.Profile.Id == targetId).Profile);
        }
    }
}
