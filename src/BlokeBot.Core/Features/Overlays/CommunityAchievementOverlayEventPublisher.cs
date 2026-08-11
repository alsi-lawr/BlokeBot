using System.Text.Json;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed class CommunityAchievementOverlayEventPublisher(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IEnumerable<IOverlayEventPresenter> presenters
) : ICommunityAchievementCompletionObserver
{
    private static readonly HostFeatureFlags _requiredFeatures =
        HostFeatureFlags.CommunityProgression | HostFeatureFlags.Overlays;
    private readonly IOverlayEventPresenter[] _presenters = [.. presenters];

    public async ValueTask AchievementCompletedAsync(
        int hostId,
        Guid completionId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (
            !await db
                .Hosts.AsNoTracking()
                .AnyAsync(
                    value =>
                        value.Id == hostId
                        && (value.EnabledFeatures & _requiredFeatures) == _requiredFeatures,
                    cancellationToken
                )
        )
        {
            return;
        }

        var completion = await (
            from value in db.CommunityCompletions.AsNoTracking()
            join definition in db.CommunityDefinitions.AsNoTracking()
                on value.DefinitionId equals definition.Id
            join season in db.CommunitySeasons.AsNoTracking() on value.SeasonId equals season.Id
            where
                value.HostId == hostId
                && value.PublicId == completionId
                && definition.Kind == CommunityDefinitionKind.Achievement
                && season.Visibility == CommunityVisibility.Public
            select new
            {
                value.PublicId,
                value.ViewerDisplayName,
                value.ViewerLogin,
                value.DefinitionName,
                value.PointsGranted,
                value.RewardSnapshot,
            }
        ).SingleOrDefaultAsync(cancellationToken);
        if (completion is null)
        {
            return;
        }

        var pointLabel =
            await db
                .PointsSettings.AsNoTracking()
                .Where(value => value.HostId == hostId)
                .Select(value => value.PointLabel)
                .SingleOrDefaultAsync(cancellationToken)
            ?? "points";
        var rewards = RewardSummary(
            completion.PointsGranted,
            pointLabel,
            completion.RewardSnapshot
        );
        foreach (var presenter in _presenters)
        {
            await presenter.PresentAsync(
                new OverlayEventPresentation.AchievementCompletion
                {
                    HostId = hostId,
                    SourceKey = completion.PublicId.ToString("N"),
                    Viewer =
                        completion.ViewerDisplayName ?? completion.ViewerLogin ?? "The community",
                    Achievement = completion.DefinitionName,
                    Rewards = rewards,
                },
                cancellationToken
            );
        }
    }

    private static string RewardSummary(
        string pointsGranted,
        string pointLabel,
        string rewardSnapshot
    )
    {
        var rewards = new List<string>();
        var points = PointAmount.ParseAbsolute(pointsGranted);
        if (!points.IsZero)
        {
            rewards.Add($"{points.ToDisplayString()} {pointLabel}");
        }
        rewards.AddRange(
            (JsonSerializer.Deserialize<RewardSnapshot[]>(rewardSnapshot) ?? [])
                .Select(value => value.Name)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
        );
        return rewards.Count == 0 ? "recognition" : string.Join(", ", rewards);
    }

    private sealed record RewardSnapshot(string Name);
}
