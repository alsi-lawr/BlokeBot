using System.Text.Json;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public sealed partial class PredictionService
{
    public async Task<PredictionControlState> ReadControlAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return new(new PredictionAuthorizationReadiness.Disabled(), null, null);
        }
        var readiness = await ReadinessAsync(hostId, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db
            .TwitchPredictionTemplates.AsNoTracking()
            .Include(x => x.Outcomes)
            .SingleOrDefaultAsync(x => x.HostId == hostId && x.Id == templateId, cancellationToken);
        var active = await db
            .TwitchPredictions.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && (
                    x.Status == TwitchPredictionStatus.Active
                    || x.Status == TwitchPredictionStatus.Locked
                )
            )
            .Select(x => new
            {
                x.ProviderPredictionId,
                x.Title,
                x.OutcomesJson,
                x.Status,
                x.IsExternallyStarted,
                x.CreatedAtUtc,
                x.LocksAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);
        return new(
            readiness,
            template is null ? null : View(template),
            active is null
                ? null
                : new(
                    active.ProviderPredictionId,
                    active.Title,
                    JsonSerializer.Deserialize<PredictionControlOutcomeView[]>(active.OutcomesJson)
                        ?? [],
                    active.Status.ToString(),
                    active.IsExternallyStarted,
                    active.CreatedAtUtc,
                    active.LocksAtUtc
                )
        );
    }
}
