using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.ChannelPoints;

public sealed class ChannelPointsService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBroadcasterTokenStatusProvider broadcasters,
    HelixClient helix,
    BotSettings settings,
    EventBus<AppEventKind> events,
    DurableAlertService alerts,
    TimeProvider timeProvider
) : IChannelPointsEventObserver
{
    private const int _terminalToKeep = 100;

    public async Task<ChannelPointsDashboardState> LoadAsync(int hostId, CancellationToken ct)
    {
        await ReconcileAsync(hostId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var readiness = await ReadinessAsync(hostId, ct);
        var rewards = await db
            .TwitchCustomRewards.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Title)
            .ToArrayAsync(ct);
        var active = await db
            .TwitchRewardRedemptions.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Status == TwitchRewardRedemptionStatus.Unfulfilled)
            .OrderByDescending(x => x.RedeemedAtUtc)
            .ToArrayAsync(ct);
        var history = await db
            .TwitchRewardRedemptions.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Status != TwitchRewardRedemptionStatus.Unfulfilled)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(_terminalToKeep)
            .ToArrayAsync(ct);
        return new(
            readiness,
            rewards.Select(View).ToArray(),
            active.Select(x => View(x, rewards)).ToArray(),
            history.Select(x => View(x, rewards)).ToArray()
        );
    }

    public async Task<ChannelPointsOperationOutcome> CreateRewardAsync(
        int hostId,
        ChannelPointsRewardDraft draft,
        CancellationToken ct
    )
    {
        var validation = draft.Validate();
        if (validation is not null)
        {
            return new ChannelPointsOperationOutcome.InvalidRequest(validation);
        }
        var context = await ProviderContextAsync(hostId, ct);
        if (context is null)
        {
            return NotReady();
        }
        var result = await helix.CreateCustomRewardAsync(
            context.Value.Context,
            context.Value.BroadcasterId,
            ToProvider(draft),
            ct
        );
        if (result.Outcome is not HelixChannelPointsOutcome.Success || result.Reward is null)
        {
            return Map(result.Outcome);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reward = UpsertReward(
            db,
            hostId,
            result.Reward,
            true,
            timeProvider.GetUtcNow().UtcDateTime
        ).Reward;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new ChannelPointsOperationOutcome.RewardCreated(View(reward));
    }

    public async Task<ChannelPointsOperationOutcome> UpdateRewardAsync(
        int hostId,
        string rewardId,
        ChannelPointsRewardDraft draft,
        bool isEnabled,
        bool paused,
        CancellationToken ct
    )
    {
        var validation = draft.Validate();
        if (validation is not null)
        {
            return new ChannelPointsOperationOutcome.InvalidRequest(validation);
        }
        var context = await ProviderContextAsync(hostId, ct);
        if (context is null)
        {
            return NotReady();
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reward = await db.TwitchCustomRewards.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.ProviderRewardId == rewardId,
            ct
        );
        if (reward is null || !reward.IsManageable)
        {
            return new ChannelPointsOperationOutcome.ExternalReadOnly();
        }
        var result = await helix.UpdateCustomRewardAsync(
            context.Value.Context,
            context.Value.BroadcasterId,
            rewardId,
            ToProvider(draft),
            isEnabled,
            paused,
            ct
        );
        if (result is not HelixChannelPointsOutcome.Success)
        {
            return Map(result);
        }
        Apply(
            reward,
            new HelixCustomReward(
                rewardId,
                draft.Title.Trim(),
                draft.Prompt?.Trim(),
                draft.Cost,
                isEnabled,
                paused,
                draft.IsUserInputRequired,
                draft.IsMaxPerStreamEnabled,
                draft.MaxPerStream,
                draft.IsMaxPerUserPerStreamEnabled,
                draft.MaxPerUserPerStream,
                draft.IsGlobalCooldownEnabled,
                draft.GlobalCooldownSeconds,
                draft.ShouldRedemptionsSkipRequestQueue,
                draft.BackgroundColor
            ),
            true
        );
        reward.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new ChannelPointsOperationOutcome.RewardUpdated();
    }

    public async Task<ChannelPointsOperationOutcome> DeleteRewardAsync(
        int hostId,
        string rewardId,
        bool confirmed,
        CancellationToken ct
    )
    {
        if (!confirmed)
        {
            return new ChannelPointsOperationOutcome.ConfirmationRequired(
                "Deleting this reward makes Twitch fulfil all outstanding unfulfilled redemptions. Cancel redemptions first if viewers should receive a refund."
            );
        }
        var context = await ProviderContextAsync(hostId, ct);
        if (context is null)
        {
            return NotReady();
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reward = await db.TwitchCustomRewards.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.ProviderRewardId == rewardId,
            ct
        );
        if (reward is null || !reward.IsManageable)
        {
            return new ChannelPointsOperationOutcome.ExternalReadOnly();
        }
        var result = await helix.DeleteCustomRewardAsync(
            context.Value.Context,
            context.Value.BroadcasterId,
            rewardId,
            ct
        );
        if (result is not HelixChannelPointsOutcome.Success)
        {
            return Map(result);
        }
        db.TwitchCustomRewards.Remove(reward);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new ChannelPointsOperationOutcome.RewardDeleted();
    }

    public async Task<ChannelPointsOperationOutcome> UpdateRedemptionAsync(
        int hostId,
        string redemptionId,
        bool fulfill,
        CancellationToken ct
    )
    {
        var context = await ProviderContextAsync(hostId, ct);
        if (context is null)
        {
            return NotReady();
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var redemption = await db.TwitchRewardRedemptions.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.ProviderRedemptionId == redemptionId,
            ct
        );
        if (redemption is null || redemption.Status != TwitchRewardRedemptionStatus.Unfulfilled)
        {
            return new ChannelPointsOperationOutcome.RedemptionNotActionable();
        }
        var manageable = await db.TwitchCustomRewards.AnyAsync(
            x =>
                x.HostId == hostId
                && x.ProviderRewardId == redemption.ProviderRewardId
                && x.IsManageable,
            ct
        );
        if (!manageable)
        {
            return new ChannelPointsOperationOutcome.ExternalReadOnly();
        }
        var status = fulfill
            ? HelixRewardRedemptionStatus.Fulfilled
            : HelixRewardRedemptionStatus.Canceled;
        var result = await helix.UpdateRedemptionStatusAsync(
            context.Value.Context,
            context.Value.BroadcasterId,
            redemption.ProviderRewardId,
            redemption.ProviderRedemptionId,
            status,
            ct
        );
        if (result is not HelixChannelPointsOutcome.Success)
        {
            return Map(result);
        }
        redemption.Status = fulfill
            ? TwitchRewardRedemptionStatus.Fulfilled
            : TwitchRewardRedemptionStatus.Canceled;
        redemption.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await TrimTerminalAsync(db, hostId, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new ChannelPointsOperationOutcome.RedemptionUpdated();
    }

    public async Task ReconcileChannelAsync(string channel, CancellationToken ct)
    {
        var login = Login.Normalize(channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await db
            .Hosts.Where(x => x.Login == login)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is { } id)
        {
            await ReconcileAsync(id, ct);
        }
    }

    public async Task ReconcileAsync(int hostId, CancellationToken ct)
    {
        var context = await ProviderContextAsync(hostId, ct);
        if (context is null)
        {
            return;
        }
        var allRewards = await helix.GetCustomRewardsAsync(
            context.Value.Context,
            context.Value.BroadcasterId,
            false,
            ct
        );
        if (allRewards is not HelixCustomRewardsLookupOutcome.Found all)
        {
            return;
        }
        var manageableRewards = await helix.GetCustomRewardsAsync(
            context.Value.Context,
            context.Value.BroadcasterId,
            true,
            ct
        );
        if (manageableRewards is not HelixCustomRewardsLookupOutcome.Found manageable)
        {
            return;
        }
        var redemptions = new Dictionary<string, IReadOnlyList<HelixRewardRedemption>>();
        foreach (var reward in manageable.Rewards)
        {
            var recovered = await ReconcileRewardRedemptionsAsync(
                context.Value.Context,
                context.Value.BroadcasterId,
                reward.Id,
                ct
            );
            if (recovered is null)
            {
                return;
            }
            redemptions[reward.Id] = recovered;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var manageableRewardIds = manageable.Rewards.Select(x => x.Id).ToHashSet();
        var changed = false;
        foreach (var reward in all.Rewards)
        {
            changed |= UpsertReward(
                db,
                hostId,
                reward,
                manageableRewardIds.Contains(reward.Id),
                now
            ).Changed;
        }
        var absentRewards = await db
            .TwitchCustomRewards.Where(x =>
                x.HostId == hostId && !all.Rewards.Select(y => y.Id).Contains(x.ProviderRewardId)
            )
            .ToArrayAsync(ct);
        if (absentRewards.Length > 0)
        {
            db.TwitchCustomRewards.RemoveRange(absentRewards);
            changed = true;
        }
        foreach (var pair in redemptions)
        {
            foreach (var redemption in pair.Value)
            {
                changed |= UpsertRedemption(db, hostId, redemption, now);
            }
        }
        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
        if (await TrimTerminalAsync(db, hostId, ct))
        {
            await db.SaveChangesAsync(ct);
            changed = true;
        }
        if (changed)
        {
            await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        }
    }

    public async Task RedemptionReceivedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(
            x =>
                x.TwitchUserId == redemption.BroadcasterUserId
                || x.Login == Login.Normalize(redemption.BroadcasterUserLogin),
            ct
        );
        if (host is null)
        {
            return;
        }
        var changed = UpsertRedemption(
            db,
            host.Id,
            redemption.ToHelix(),
            timeProvider.GetUtcNow().UtcDateTime
        );
        if (!changed)
        {
            return;
        }
        await db.SaveChangesAsync(ct);
        await TrimTerminalAsync(db, host.Id, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    private async Task<(HelixRequestContext Context, string BroadcasterId)?> ProviderContextAsync(
        int hostId,
        CancellationToken ct
    )
    {
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return null;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await db
            .Hosts.Where(x => x.Id == hostId)
            .Select(x => x.TwitchUserId)
            .SingleOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(id)
            ? null
            : new(new(settings.Identity.ClientId, token), id);
    }

    private async Task<ChannelPointsAuthorizationReadiness> ReadinessAsync(
        int hostId,
        CancellationToken ct
    )
    {
        return await ReadyTokenAsync(hostId, ct) is null
            ? new ChannelPointsAuthorizationReadiness.NeedsBroadcasterAuthorization(
                "Reconnect the selected broadcaster with Twitch Channel Points permissions."
            )
            : new ChannelPointsAuthorizationReadiness.Ready();
    }

    private async Task<string?> ReadyTokenAsync(int hostId, CancellationToken ct)
    {
        var status = await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            ct
        );
        if (status is TokenStatus.Ready ready)
        {
            return ready.AccessToken;
        }
        await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "twitch-broadcaster-authorization",
                "reauthorize-v1",
                "Reconnect broadcaster for Twitch operations",
                "Twitch operations needs the selected broadcaster to reconnect and approve all requested permissions.",
                "/twitch-operations"
            )
            .ExecuteAsync(ct);
        return null;
    }

    private static ChannelPointsOperationOutcome NotReady()
    {
        return new ChannelPointsOperationOutcome.NotReady(
            "Reconnect the selected broadcaster with Twitch Channel Points permissions."
        );
    }

    private static ChannelPointsOperationOutcome Map(HelixChannelPointsOutcome value)
    {
        return value switch
        {
            HelixChannelPointsOutcome.Unauthorized => NotReady(),
            HelixChannelPointsOutcome.Ineligible => new ChannelPointsOperationOutcome.Ineligible(
                "Twitch Channel Points reward management is available only to eligible Affiliate or Partner channels."
            ),
            HelixChannelPointsOutcome.ExternalReward =>
                new ChannelPointsOperationOutcome.ExternalReadOnly(),
            _ => new ChannelPointsOperationOutcome.ProviderRejected(
                "Twitch did not permit this Channel Points operation."
            ),
        };
    }

    private static HelixCustomRewardDraft ToProvider(ChannelPointsRewardDraft x)
    {
        return new(
            x.Title.Trim(),
            x.Prompt?.Trim(),
            x.Cost,
            x.IsUserInputRequired,
            x.IsMaxPerStreamEnabled,
            x.MaxPerStream,
            x.IsMaxPerUserPerStreamEnabled,
            x.MaxPerUserPerStream,
            x.IsGlobalCooldownEnabled,
            x.GlobalCooldownSeconds,
            x.ShouldRedemptionsSkipRequestQueue,
            x.BackgroundColor
        );
    }

    private async Task<IReadOnlyList<HelixRewardRedemption>?> ReconcileRewardRedemptionsAsync(
        HelixRequestContext context,
        string broadcasterId,
        string rewardId,
        CancellationToken ct
    )
    {
        var redemptions = new List<HelixRewardRedemption>();
        foreach (
            var status in new[]
            {
                HelixRewardRedemptionStatus.Unfulfilled,
                HelixRewardRedemptionStatus.Fulfilled,
                HelixRewardRedemptionStatus.Canceled,
            }
        )
        {
            string? cursor = null;
            do
            {
                var result = await helix.GetRewardRedemptionsAsync(
                    context,
                    broadcasterId,
                    rewardId,
                    status,
                    cursor,
                    ct
                );
                if (result is not HelixRewardRedemptionsLookupOutcome.Found page)
                {
                    return null;
                }
                redemptions.AddRange(page.Page.Redemptions);
                cursor = page.Page.Cursor;
            } while (!string.IsNullOrEmpty(cursor));
        }
        return redemptions.OrderByDescending(x => x.RedeemedAt).ToArray();
    }

    private static (TwitchCustomReward Reward, bool Changed) UpsertReward(
        BlokeBotDbContext db,
        int hostId,
        HelixCustomReward value,
        bool isManageable,
        DateTime now
    )
    {
        var entity =
            db.TwitchCustomRewards.Local.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderRewardId == value.Id
            )
            ?? db.TwitchCustomRewards.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderRewardId == value.Id
            );
        if (entity is null)
        {
            entity = new() { HostId = hostId, ProviderRewardId = value.Id };
            db.TwitchCustomRewards.Add(entity);
            Apply(entity, value, isManageable);
            entity.UpdatedAtUtc = now;
            return (entity, true);
        }
        if (!Apply(entity, value, isManageable))
        {
            return (entity, false);
        }
        entity.UpdatedAtUtc = now;
        return (entity, true);
    }

    private static bool Apply(TwitchCustomReward x, HelixCustomReward y, bool isManageable)
    {
        var changed =
            x.Title != y.Title
            || x.Prompt != y.Prompt
            || x.Cost != y.Cost
            || x.IsManageable != isManageable
            || x.IsEnabled != y.IsEnabled
            || x.IsPaused != y.IsPaused
            || x.IsUserInputRequired != y.IsUserInputRequired
            || x.IsMaxPerStreamEnabled != y.IsMaxPerStreamEnabled
            || x.MaxPerStream != y.MaxPerStream
            || x.IsMaxPerUserPerStreamEnabled != y.IsMaxPerUserPerStreamEnabled
            || x.MaxPerUserPerStream != y.MaxPerUserPerStream
            || x.IsGlobalCooldownEnabled != y.IsGlobalCooldownEnabled
            || x.GlobalCooldownSeconds != y.GlobalCooldownSeconds
            || x.ShouldRedemptionsSkipRequestQueue != y.ShouldRedemptionsSkipRequestQueue
            || x.BackgroundColor != y.BackgroundColor;
        if (!changed)
        {
            return false;
        }
        x.Title = y.Title;
        x.Prompt = y.Prompt;
        x.Cost = y.Cost;
        x.IsManageable = isManageable;
        x.IsEnabled = y.IsEnabled;
        x.IsPaused = y.IsPaused;
        x.IsUserInputRequired = y.IsUserInputRequired;
        x.IsMaxPerStreamEnabled = y.IsMaxPerStreamEnabled;
        x.MaxPerStream = y.MaxPerStream;
        x.IsMaxPerUserPerStreamEnabled = y.IsMaxPerUserPerStreamEnabled;
        x.MaxPerUserPerStream = y.MaxPerUserPerStream;
        x.IsGlobalCooldownEnabled = y.IsGlobalCooldownEnabled;
        x.GlobalCooldownSeconds = y.GlobalCooldownSeconds;
        x.ShouldRedemptionsSkipRequestQueue = y.ShouldRedemptionsSkipRequestQueue;
        x.BackgroundColor = y.BackgroundColor;
        return true;
    }

    private static bool UpsertRedemption(
        BlokeBotDbContext db,
        int hostId,
        HelixRewardRedemption x,
        DateTime now
    )
    {
        if (x.Status == HelixRewardRedemptionStatus.Unknown)
        {
            return false;
        }
        var item =
            db.TwitchRewardRedemptions.Local.SingleOrDefault(y =>
                y.HostId == hostId && y.ProviderRedemptionId == x.Id
            )
            ?? db.TwitchRewardRedemptions.SingleOrDefault(y =>
                y.HostId == hostId && y.ProviderRedemptionId == x.Id
            );
        var status = x.Status switch
        {
            HelixRewardRedemptionStatus.Unfulfilled => TwitchRewardRedemptionStatus.Unfulfilled,
            HelixRewardRedemptionStatus.Fulfilled => TwitchRewardRedemptionStatus.Fulfilled,
            _ => TwitchRewardRedemptionStatus.Canceled,
        };
        if (
            item is not null
            && item.ProviderRewardId == x.RewardId
            && item.RewardTitle == x.RewardTitle
            && item.UserId == x.UserId
            && item.UserLogin == x.UserLogin
            && item.UserInput == x.UserInput
            && item.Status == status
            && item.RedeemedAtUtc == x.RedeemedAt.UtcDateTime
        )
        {
            return false;
        }
        if (item is null)
        {
            item = new() { HostId = hostId, ProviderRedemptionId = x.Id };
            db.TwitchRewardRedemptions.Add(item);
        }
        item.ProviderRewardId = x.RewardId;
        item.RewardTitle = x.RewardTitle;
        item.UserId = x.UserId;
        item.UserLogin = x.UserLogin;
        item.UserInput = x.UserInput;
        item.Status = status;
        item.RedeemedAtUtc = x.RedeemedAt.UtcDateTime;
        item.UpdatedAtUtc = now;
        return true;
    }

    private static async Task<bool> TrimTerminalAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var remove = await db
            .TwitchRewardRedemptions.Where(x =>
                x.HostId == hostId && x.Status != TwitchRewardRedemptionStatus.Unfulfilled
            )
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(_terminalToKeep)
            .ToArrayAsync(ct);
        db.TwitchRewardRedemptions.RemoveRange(remove);
        return remove.Length > 0;
    }

    private static ChannelPointsRewardView View(TwitchCustomReward x)
    {
        return new(
            x.ProviderRewardId,
            x.Title,
            x.Prompt,
            x.Cost,
            x.IsManageable,
            x.IsEnabled,
            x.IsPaused,
            x.IsUserInputRequired,
            x.IsMaxPerStreamEnabled,
            x.MaxPerStream,
            x.IsMaxPerUserPerStreamEnabled,
            x.MaxPerUserPerStream,
            x.IsGlobalCooldownEnabled,
            x.GlobalCooldownSeconds,
            x.ShouldRedemptionsSkipRequestQueue,
            x.BackgroundColor
        );
    }

    private static ChannelPointsRedemptionView View(
        TwitchRewardRedemption x,
        IReadOnlyList<TwitchCustomReward> rewards
    )
    {
        return new(
            x.ProviderRedemptionId,
            x.ProviderRewardId,
            x.RewardTitle,
            x.UserLogin,
            x.UserInput,
            PersistedEnumTokens<TwitchRewardRedemptionStatus>.Format(x.Status),
            x.RedeemedAtUtc,
            x.UpdatedAtUtc,
            rewards.Any(y => y.ProviderRewardId == x.ProviderRewardId && y.IsManageable)
        );
    }
}
