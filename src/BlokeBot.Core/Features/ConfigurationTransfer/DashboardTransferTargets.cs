using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed record DashboardTransferTargets(
    IReadOnlyList<AutomationFlow> Flows,
    IReadOnlyList<CustomValueDefinition> Values,
    IReadOnlyList<TwitchPollTemplate> Polls,
    IReadOnlyList<TwitchPredictionTemplate> Predictions,
    IReadOnlyList<GuessRoundProfile> Profiles
)
{
    internal static async Task<DashboardTransferTargets> LoadAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        new(
            await db
                .AutomationFlows.AsNoTracking()
                .Include(flow => flow.Nodes)
                .Where(flow => flow.HostId == hostId)
                .ToArrayAsync(ct),
            await db
                .CustomValueDefinitions.AsNoTracking()
                .Where(value => value.HostId == hostId)
                .ToArrayAsync(ct),
            await db
                .TwitchPollTemplates.AsNoTracking()
                .Where(value => value.HostId == hostId)
                .ToArrayAsync(ct),
            await db
                .TwitchPredictionTemplates.AsNoTracking()
                .Where(value => value.HostId == hostId)
                .ToArrayAsync(ct),
            await db.Profiles.AsNoTracking().Where(value => value.HostId == hostId).ToArrayAsync(ct)
        );
}
