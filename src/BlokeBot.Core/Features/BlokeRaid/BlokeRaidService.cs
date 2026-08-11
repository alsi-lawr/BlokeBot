using System.Globalization;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.BlokeRaid;

public sealed class BlokeRaidService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    IBlokeRaidRandom random,
    TimeProvider timeProvider
)
{
    private const int _persistenceRetryCount = 20;

    public async Task<BlokeRaidModeratorView?> LoadModeratorAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await FeatureIsEnabledAsync(db, hostId, cancellationToken))
        {
            return null;
        }

        var configuration =
            await db
                .BlokeRaidConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(value => value.HostId == hostId, cancellationToken)
            ?? NewConfiguration(hostId, timeProvider.GetUtcNow().UtcDateTime);
        var campaigns = await CampaignQuery(db, hostId)
            .OrderByDescending(value => value.StartedAtUtc)
            .Take(BlokeRaidLimits.MaximumHistoryCount + 1)
            .ToArrayAsync(cancellationToken);
        var active = campaigns.SingleOrDefault(value =>
            value.Status == BlokeRaidCampaignStatus.Active
        );
        return new(
            ToConfigurationView(configuration),
            active is null ? null : ToCampaignView(active),
            [
                .. campaigns
                    .Where(value => value.Status != BlokeRaidCampaignStatus.Active)
                    .Take(BlokeRaidLimits.MaximumHistoryCount)
                    .Select(ToCampaignView),
            ]
        );
    }

    public async Task<BlokeRaidPublicView?> LoadPublicAsync(
        string hostLogin,
        CancellationToken cancellationToken
    )
    {
        var login = CommunityInput.NormalizeLogin(hostLogin);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == login
                && (value.EnabledFeatures & HostFeatureFlags.CooperativeGame)
                    == HostFeatureFlags.CooperativeGame
            )
            .Select(value => new
            {
                value.Id,
                value.Login,
                value.DisplayName,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (host is null)
        {
            return null;
        }

        var campaigns = await CampaignQuery(db, host.Id)
            .OrderByDescending(value => value.StartedAtUtc)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return new(
            host.Login,
            host.DisplayName,
            campaigns.FirstOrDefault(value => value.Status == BlokeRaidCampaignStatus.Active)
                is { } active
                ? ToCampaignView(active)
                : null,
            campaigns.FirstOrDefault(value => value.Status != BlokeRaidCampaignStatus.Active)
                is { } recap
                ? ToCampaignView(recap)
                : null
        );
    }

    public async Task<IReadOnlyList<BlokeRaidEventView>> LoadEventsAsync(
        int hostId,
        long afterEventId,
        int count,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await FeatureIsEnabledAsync(db, hostId, cancellationToken))
        {
            return [];
        }

        var bounded = Math.Clamp(count, 1, BlokeRaidLimits.MaximumEventReadCount);
        return await db
            .BlokeRaidEvents.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Id > afterEventId)
            .OrderBy(value => value.Id)
            .Take(bounded)
            .Select(value => new BlokeRaidEventView(
                value.Id,
                value.Campaign!.PublicId,
                value.Kind,
                value.PublicPayload,
                value.OccurredAtUtc
            ))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<BlokeRaidConfigurationOutcome> SaveConfigurationAsync(
        int hostId,
        BlokeRaidConfigurationDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (Validate(draft) is { } invalid)
        {
            return invalid;
        }

        var result = await RetryAsync(
            () => SaveConfigurationAttemptAsync(hostId, draft, cancellationToken),
            cancellationToken
        );
        if (result is BlokeRaidConfigurationOutcome.Saved)
        {
            await PublishChangedAsync(pointsChanged: false, cancellationToken);
        }
        return result;
    }

    public async Task<BlokeRaidCampaignOutcome> StartAsync(
        int hostId,
        BlokeRaidCampaignCommand command,
        CancellationToken cancellationToken
    )
    {
        if (Validate(command) is { } invalid)
        {
            return invalid;
        }

        var result = await RetryAsync(
            () => StartAttemptAsync(hostId, command, cancellationToken),
            cancellationToken
        );
        if (result is BlokeRaidCampaignOutcome.Succeeded { WasIdempotent: false })
        {
            await PublishChangedAsync(pointsChanged: false, cancellationToken);
        }
        return result;
    }

    public async Task<BlokeRaidCampaignOutcome> EndAsync(
        int hostId,
        BlokeRaidCampaignCommand command,
        CancellationToken cancellationToken
    )
    {
        if (Validate(command) is { } invalid)
        {
            return invalid;
        }

        var result = await RetryAsync(
            () => EndAttemptAsync(hostId, command, cancellationToken),
            cancellationToken
        );
        if (result is BlokeRaidCampaignOutcome.Succeeded { WasIdempotent: false })
        {
            await PublishChangedAsync(pointsChanged: false, cancellationToken);
        }
        return result;
    }

    public async Task<BlokeRaidCampaignOutcome> ResetAsync(
        int hostId,
        BlokeRaidCampaignCommand command,
        CancellationToken cancellationToken
    )
    {
        if (Validate(command) is { } invalid)
        {
            return invalid;
        }

        var result = await RetryAsync(
            () => ResetAttemptAsync(hostId, command, cancellationToken),
            cancellationToken
        );
        if (result is BlokeRaidCampaignOutcome.Succeeded { WasIdempotent: false })
        {
            await PublishChangedAsync(pointsChanged: false, cancellationToken);
        }
        return result;
    }

    public async Task<BlokeRaidActionOutcome> ActAsync(
        int hostId,
        BlokeRaidActionCommand command,
        CancellationToken cancellationToken
    )
    {
        if (Validate(command) is { } invalid)
        {
            return invalid;
        }

        command = command with { Viewer = Normalize(command.Viewer) };
        int? resolvedOutcome = null;
        var result = await RetryAsync(
            () => ActAttemptAsync(hostId, command, ResolveOutcome, cancellationToken),
            cancellationToken
        );
        if (result is BlokeRaidActionOutcome.Succeeded { WasIdempotent: false } succeeded)
        {
            await PublishChangedAsync(
                command.Kind == BlokeRaidActionKind.Special
                    || succeeded.Campaign.Status == BlokeRaidCampaignStatus.Victory,
                cancellationToken
            );
        }
        return result;

        int ResolveOutcome(int minimum, int maximum) =>
            resolvedOutcome ??= random.NextInclusive(minimum, maximum);
    }

    public async Task<BlokeRaidActionOutcome> ApplyGuessingResultAsync(
        int hostId,
        BlokeRaidGuessingResult result,
        CancellationToken cancellationToken
    )
    {
        if (result.RoundId < 1)
        {
            return new BlokeRaidActionOutcome.Invalid("A guessing round identity is required.");
        }

        var distinct = result
            .CorrectGuessers.Select(Normalize)
            .Where(IsValid)
            .DistinctBy(value => value.TwitchUserId, StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length == 0)
        {
            return new BlokeRaidActionOutcome.Invalid(
                "The guessing result has no correct viewers."
            );
        }

        var outcome = await RetryAsync(
            () => ApplyGuessingAttemptAsync(hostId, result, distinct, cancellationToken),
            cancellationToken
        );
        if (outcome is BlokeRaidActionOutcome.Succeeded { WasIdempotent: false } succeeded)
        {
            await PublishChangedAsync(
                succeeded.Campaign.Status == BlokeRaidCampaignStatus.Victory,
                cancellationToken
            );
        }
        return outcome;
    }

    internal async Task ProcessDueWorkAsync(int hostId, CancellationToken cancellationToken)
    {
        var outcome = await RetryAsync(
            () => ProcessDueWorkAttemptAsync(hostId, cancellationToken),
            cancellationToken
        );
        if (outcome.Changed)
        {
            await PublishChangedAsync(outcome.PointsChanged, cancellationToken);
        }
    }

    private async Task<BlokeRaidConfigurationOutcome> SaveConfigurationAttemptAsync(
        int hostId,
        BlokeRaidConfigurationDraft draft,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ImmediateTransaction.StartAsync(db, cancellationToken);
        if (!await FeatureIsEnabledAsync(db, hostId, cancellationToken))
        {
            return new BlokeRaidConfigurationOutcome.FeatureDisabled();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var configuration = await db.BlokeRaidConfigurations.SingleOrDefaultAsync(
            value => value.HostId == hostId,
            cancellationToken
        );
        if (configuration is null)
        {
            if (draft.Revision != 0)
            {
                return new BlokeRaidConfigurationOutcome.Conflict(
                    "The saved configuration changed. Reload and try again."
                );
            }
            configuration = NewConfiguration(hostId, now);
            _ = db.BlokeRaidConfigurations.Add(configuration);
        }
        else if (configuration.Revision != draft.Revision)
        {
            return new BlokeRaidConfigurationOutcome.Conflict(
                "The saved configuration changed. Reload and try again."
            );
        }

        var scheduleChanged =
            configuration.ResetPolicy != draft.ResetPolicy
            || configuration.WeeklyResetDay != (int)draft.WeeklyResetDay
            || configuration.WeeklyResetHourUtc != draft.WeeklyResetHourUtc;
        Apply(configuration, draft);
        configuration.Revision++;
        configuration.UpdatedAtUtc = now;
        if (draft.ResetPolicy == BlokeRaidResetPolicy.Weekly)
        {
            if (scheduleChanged || configuration.NextWeeklyResetAtUtc is null)
            {
                configuration.NextWeeklyResetAtUtc = NextWeeklyReset(
                    now,
                    draft.WeeklyResetDay,
                    draft.WeeklyResetHourUtc
                );
            }
        }
        else
        {
            configuration.NextWeeklyResetAtUtc = null;
        }

        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BlokeRaidConfigurationOutcome.Saved(ToConfigurationView(configuration));
    }

    private async Task<BlokeRaidCampaignOutcome> StartAttemptAsync(
        int hostId,
        BlokeRaidCampaignCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ImmediateTransaction.StartAsync(db, cancellationToken);
        if (!await FeatureIsEnabledAsync(db, hostId, cancellationToken))
        {
            return new BlokeRaidCampaignOutcome.FeatureDisabled();
        }

        var prior = await CampaignQuery(db, hostId)
            .SingleOrDefaultAsync(
                value => value.StartOperationKey == command.OperationKey,
                cancellationToken
            );
        if (prior is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new BlokeRaidCampaignOutcome.Succeeded(ToCampaignView(prior), true);
        }
        if (
            await db.BlokeRaidCampaigns.AnyAsync(
                value => value.HostId == hostId && value.Status == BlokeRaidCampaignStatus.Active,
                cancellationToken
            )
        )
        {
            return new BlokeRaidCampaignOutcome.Conflict(
                "This channel already has an active BlokeRaid campaign."
            );
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var configuration =
            await db.BlokeRaidConfigurations.SingleOrDefaultAsync(
                value => value.HostId == hostId,
                cancellationToken
            ) ?? NewConfiguration(hostId, now);
        var campaign = NewCampaign(hostId, command.OperationKey, configuration, now);
        _ = db.BlokeRaidCampaigns.Add(campaign);
        _ = await db.SaveChangesAsync(cancellationToken);
        AddEvent(
            db,
            campaign,
            BlokeRaidEventKind.CampaignStarted,
            $"campaign-start:{command.OperationKey}",
            new
            {
                campaign = campaign.PublicId,
                boss = campaign.BossName,
                health = campaign.MaximumHealth,
                ward = campaign.MaximumWard,
                phase = 1,
                endsAtUtc = campaign.EndsAtUtc,
                response = configuration.PhaseOneResponse,
            },
            now
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BlokeRaidCampaignOutcome.Succeeded(ToCampaignView(campaign));
    }

    private async Task<BlokeRaidCampaignOutcome> EndAttemptAsync(
        int hostId,
        BlokeRaidCampaignCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ImmediateTransaction.StartAsync(db, cancellationToken);
        if (!await FeatureIsEnabledAsync(db, hostId, cancellationToken))
        {
            return new BlokeRaidCampaignOutcome.FeatureDisabled();
        }

        var eventKey = $"campaign-end:{command.OperationKey}";
        var prior = await db.BlokeRaidEvents.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.OperationKey == eventKey,
            cancellationToken
        );
        if (prior is not null)
        {
            var priorCampaign = await CampaignQuery(db, hostId)
                .SingleAsync(value => value.Id == prior.CampaignId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new BlokeRaidCampaignOutcome.Succeeded(ToCampaignView(priorCampaign), true);
        }

        var campaign = await CampaignQuery(db, hostId)
            .SingleOrDefaultAsync(
                value => value.Status == BlokeRaidCampaignStatus.Active,
                cancellationToken
            );
        if (campaign is null)
        {
            return new BlokeRaidCampaignOutcome.NoActiveCampaign();
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        campaign.Status = BlokeRaidCampaignStatus.Ended;
        campaign.CompletedAtUtc = now;
        campaign.Revision++;
        AddEvent(
            db,
            campaign,
            BlokeRaidEventKind.CampaignEnded,
            eventKey,
            new { campaign = campaign.PublicId, endedAtUtc = now },
            now
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BlokeRaidCampaignOutcome.Succeeded(ToCampaignView(campaign));
    }

    private async Task<BlokeRaidCampaignOutcome> ResetAttemptAsync(
        int hostId,
        BlokeRaidCampaignCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ImmediateTransaction.StartAsync(db, cancellationToken);
        if (!await FeatureIsEnabledAsync(db, hostId, cancellationToken))
        {
            return new BlokeRaidCampaignOutcome.FeatureDisabled();
        }

        var resetKey = $"campaign-reset:{command.OperationKey}";
        var prior = await db.BlokeRaidEvents.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.OperationKey == resetKey,
            cancellationToken
        );
        if (prior is not null)
        {
            var priorCampaign = await CampaignQuery(db, hostId)
                .SingleAsync(value => value.Id == prior.CampaignId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new BlokeRaidCampaignOutcome.Succeeded(ToCampaignView(priorCampaign), true);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var active = await CampaignQuery(db, hostId)
            .SingleOrDefaultAsync(
                value => value.Status == BlokeRaidCampaignStatus.Active,
                cancellationToken
            );
        if (active is not null)
        {
            active.Status = BlokeRaidCampaignStatus.Ended;
            active.CompletedAtUtc = now;
            active.Revision++;
            AddEvent(
                db,
                active,
                BlokeRaidEventKind.CampaignEnded,
                $"campaign-reset-ended:{command.OperationKey}",
                new
                {
                    campaign = active.PublicId,
                    endedAtUtc = now,
                    reason = "reset",
                },
                now
            );
        }

        var configuration =
            await db.BlokeRaidConfigurations.SingleOrDefaultAsync(
                value => value.HostId == hostId,
                cancellationToken
            ) ?? NewConfiguration(hostId, now);
        var campaign = NewCampaign(hostId, $"reset:{command.OperationKey}", configuration, now);
        _ = db.BlokeRaidCampaigns.Add(campaign);
        _ = await db.SaveChangesAsync(cancellationToken);
        AddEvent(
            db,
            campaign,
            BlokeRaidEventKind.CampaignReset,
            resetKey,
            new
            {
                campaign = campaign.PublicId,
                previousCampaign = active?.PublicId,
                resetAtUtc = now,
                boss = campaign.BossName,
            },
            now
        );
        AddEvent(
            db,
            campaign,
            BlokeRaidEventKind.CampaignStarted,
            $"campaign-start:reset:{command.OperationKey}",
            new
            {
                campaign = campaign.PublicId,
                boss = campaign.BossName,
                health = campaign.MaximumHealth,
                ward = campaign.MaximumWard,
                phase = 1,
                endsAtUtc = campaign.EndsAtUtc,
                response = configuration.PhaseOneResponse,
            },
            now
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BlokeRaidCampaignOutcome.Succeeded(ToCampaignView(campaign));
    }

    private async Task<BlokeRaidActionOutcome> ActAttemptAsync(
        int hostId,
        BlokeRaidActionCommand command,
        Func<int, int, int> resolveOutcome,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ImmediateTransaction.StartAsync(db, cancellationToken);
        if (!await FeatureIsEnabledAsync(db, hostId, cancellationToken))
        {
            return new BlokeRaidActionOutcome.FeatureDisabled();
        }

        var prior = await db.BlokeRaidActions.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.OperationKey == command.OperationKey,
            cancellationToken
        );
        if (prior is not null)
        {
            var priorCampaign = await CampaignQuery(db, hostId)
                .SingleAsync(value => value.Id == prior.CampaignId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new BlokeRaidActionOutcome.Succeeded(
                ToActionView(prior),
                ToCampaignView(priorCampaign),
                true
            );
        }

        var campaign = await CampaignQuery(db, hostId)
            .SingleOrDefaultAsync(
                value => value.Status == BlokeRaidCampaignStatus.Active,
                cancellationToken
            );
        if (campaign is null)
        {
            return new BlokeRaidActionOutcome.NoActiveCampaign();
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (campaign.EndsAtUtc <= now)
        {
            return new BlokeRaidActionOutcome.NoActiveCampaign();
        }
        var configuration =
            await db.BlokeRaidConfigurations.SingleOrDefaultAsync(
                value => value.HostId == hostId,
                cancellationToken
            ) ?? NewConfiguration(hostId, now);
        var rule = Rule(configuration, command.Kind);
        var lastAction = await db
            .BlokeRaidActions.Where(value =>
                value.HostId == hostId
                && value.CampaignId == campaign.Id
                && value.ViewerTwitchUserId == command.Viewer.TwitchUserId
                && value.Kind == command.Kind
                && value.Source == BlokeRaidActionSource.Chat
            )
            .OrderByDescending(value => value.OccurredAtUtc)
            .Select(value => (DateTime?)value.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (lastAction is { } last)
        {
            var availableAt = last.AddSeconds(rule.CooldownSeconds);
            if (availableAt > now)
            {
                return new BlokeRaidActionOutcome.Cooldown(availableAt - now);
            }
        }
        var streamCount = await db.BlokeRaidActions.CountAsync(
            value =>
                value.HostId == hostId
                && value.CampaignId == campaign.Id
                && value.ViewerTwitchUserId == command.Viewer.TwitchUserId
                && value.Kind == command.Kind
                && value.Source == BlokeRaidActionSource.Chat
                && value.StreamKey == command.StreamKey,
            cancellationToken
        );
        if (streamCount >= rule.PerStreamLimit)
        {
            return new BlokeRaidActionOutcome.PerStreamLimitReached();
        }
        if (
            command.Kind == BlokeRaidActionKind.Mend
            && campaign.CurrentWard >= campaign.MaximumWard
        )
        {
            return new BlokeRaidActionOutcome.Invalid("The raid ward is already full.");
        }

        var pointCost = rule.PointCost;
        if (!pointCost.IsZero)
        {
            var balance = await db.PointBalances.SingleOrDefaultAsync(
                value => value.HostId == hostId && value.Login == command.Viewer.Login,
                cancellationToken
            );
            var current = balance is null
                ? PointAmount.Zero
                : PointAmount.ParseAbsolute(balance.Amount);
            if (current < pointCost)
            {
                return new BlokeRaidActionOutcome.InsufficientPoints(current, pointCost);
            }
            var next = current.Subtract(pointCost);
            balance!.Amount = next.ToString();
            balance.UpdatedAtUtc = now;
            _ = db.PointLedgerEntries.Add(
                new PointLedgerEntry
                {
                    HostId = hostId,
                    CreatedAtUtc = now,
                    Kind = PointLedgerKind.BlokeRaidSpecialSpend,
                    Login = command.Viewer.Login,
                    Delta = (-pointCost.Value).ToString(CultureInfo.InvariantCulture),
                    BalanceAfter = next.ToString(),
                    Note = $"BlokeRaid special against {campaign.BossName}",
                    OperationKey = $"blokeraid:special:{command.OperationKey}",
                }
            );
        }

        var rolled = resolveOutcome(rule.Minimum, rule.Maximum);
        var beforeHealth = campaign.CurrentHealth;
        var beforeWard = campaign.CurrentWard;
        var applied =
            command.Kind == BlokeRaidActionKind.Mend
                ? Math.Min(rolled, campaign.MaximumWard - campaign.CurrentWard)
                : Math.Min(rolled, campaign.CurrentHealth);
        if (command.Kind == BlokeRaidActionKind.Mend)
        {
            campaign.CurrentWard += applied;
        }
        else
        {
            campaign.CurrentHealth -= applied;
        }

        var previousPhase = campaign.CurrentPhase;
        campaign.CurrentPhase = Phase(configuration, campaign);
        campaign.Revision++;
        var contribution = Contribution(campaign, command.Viewer, now);
        contribution.ActionCount++;
        contribution.LastContributedAtUtc = now;
        if (command.Kind == BlokeRaidActionKind.Mend)
        {
            contribution.WardRestored += applied;
        }
        else
        {
            contribution.Damage += applied;
            if (command.Kind == BlokeRaidActionKind.Special)
            {
                contribution.SpecialCount++;
            }
        }

        var response = Response(configuration, command.Kind, applied, campaign, previousPhase);
        var action = new BlokeRaidAction
        {
            HostId = hostId,
            CampaignId = campaign.Id,
            OperationKey = command.OperationKey,
            Kind = command.Kind,
            Source = BlokeRaidActionSource.Chat,
            ViewerTwitchUserId = command.Viewer.TwitchUserId,
            ViewerLogin = command.Viewer.Login,
            ViewerDisplayName = command.Viewer.DisplayName,
            StreamKey = command.StreamKey,
            Outcome = applied,
            PointCost = pointCost.ToString(),
            BossHealthBefore = beforeHealth,
            BossHealthAfter = campaign.CurrentHealth,
            WardBefore = beforeWard,
            WardAfter = campaign.CurrentWard,
            PhaseAfter = campaign.CurrentPhase,
            Response = response,
            OccurredAtUtc = now,
        };
        _ = db.BlokeRaidActions.Add(action);
        AddActionEvents(db, campaign, action, previousPhase, response, now);
        if (campaign.CurrentHealth == 0)
        {
            var rewarded = await CompleteVictoryAsync(
                db,
                campaign,
                configuration,
                now,
                cancellationToken
            );
            if (!rewarded)
            {
                return new BlokeRaidActionOutcome.PointCapacityExceeded();
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BlokeRaidActionOutcome.Succeeded(ToActionView(action), ToCampaignView(campaign));
    }

    private async Task<BlokeRaidActionOutcome> ApplyGuessingAttemptAsync(
        int hostId,
        BlokeRaidGuessingResult source,
        IReadOnlyList<BlokeRaidViewer> correctGuessers,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ImmediateTransaction.StartAsync(db, cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(
            value => value.Id == hostId,
            cancellationToken
        );
        if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.CooperativeGame))
        {
            return new BlokeRaidActionOutcome.FeatureDisabled();
        }
        if (
            host.BlokeRaidAcceptWorkAfterUtc is { } acceptAfter
            && source.OccurredAtUtc.UtcDateTime < acceptAfter
        )
        {
            return new BlokeRaidActionOutcome.SourceSuppressed();
        }

        var operationKey = $"guess:{source.RoundId}";
        var prior = await db.BlokeRaidActions.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.OperationKey == operationKey,
            cancellationToken
        );
        if (prior is not null)
        {
            var priorCampaign = await CampaignQuery(db, hostId)
                .SingleAsync(value => value.Id == prior.CampaignId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new BlokeRaidActionOutcome.Succeeded(
                ToActionView(prior),
                ToCampaignView(priorCampaign),
                true
            );
        }

        var campaign = await CampaignQuery(db, hostId)
            .SingleOrDefaultAsync(
                value => value.Status == BlokeRaidCampaignStatus.Active,
                cancellationToken
            );
        if (
            campaign is null
            || source.OccurredAtUtc.UtcDateTime < campaign.StartedAtUtc
            || source.OccurredAtUtc.UtcDateTime >= campaign.EndsAtUtc
        )
        {
            return new BlokeRaidActionOutcome.NoActiveCampaign();
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var configuration =
            await db.BlokeRaidConfigurations.SingleOrDefaultAsync(
                value => value.HostId == hostId,
                cancellationToken
            ) ?? NewConfiguration(hostId, now);
        var beforeHealth = campaign.CurrentHealth;
        var remaining = campaign.CurrentHealth;
        foreach (var viewer in correctGuessers)
        {
            var applied = Math.Min(configuration.CorrectGuessDamage, remaining);
            var contribution = Contribution(campaign, viewer, now);
            contribution.ActionCount++;
            contribution.CorrectGuessCount++;
            contribution.Damage += applied;
            contribution.LastContributedAtUtc = now;
            remaining -= applied;
        }
        campaign.CurrentHealth = remaining;
        var totalDamage = beforeHealth - remaining;
        var previousPhase = campaign.CurrentPhase;
        campaign.CurrentPhase = Phase(configuration, campaign);
        campaign.Revision++;
        var response = Response(
            configuration,
            BlokeRaidActionKind.CorrectGuess,
            totalDamage,
            campaign,
            previousPhase
        );
        var action = new BlokeRaidAction
        {
            HostId = hostId,
            CampaignId = campaign.Id,
            OperationKey = operationKey,
            Kind = BlokeRaidActionKind.CorrectGuess,
            Source = BlokeRaidActionSource.Guessing,
            StreamKey = $"guessing:{source.RoundId}",
            Outcome = totalDamage,
            PointCost = "0",
            BossHealthBefore = beforeHealth,
            BossHealthAfter = campaign.CurrentHealth,
            WardBefore = campaign.CurrentWard,
            WardAfter = campaign.CurrentWard,
            PhaseAfter = campaign.CurrentPhase,
            GuessRoundId = source.RoundId,
            Response = response,
            OccurredAtUtc = source.OccurredAtUtc.UtcDateTime,
        };
        _ = db.BlokeRaidActions.Add(action);
        AddActionEvents(db, campaign, action, previousPhase, response, now);
        if (campaign.CurrentHealth == 0)
        {
            var rewarded = await CompleteVictoryAsync(
                db,
                campaign,
                configuration,
                now,
                cancellationToken
            );
            if (!rewarded)
            {
                return new BlokeRaidActionOutcome.PointCapacityExceeded();
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BlokeRaidActionOutcome.Succeeded(ToActionView(action), ToCampaignView(campaign));
    }

    private async Task<DueWorkOutcome> ProcessDueWorkAttemptAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ImmediateTransaction.StartAsync(db, cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(
            value => value.Id == hostId,
            cancellationToken
        );
        if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.CooperativeGame))
        {
            return new(false, false);
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var configuration = await db.BlokeRaidConfigurations.SingleOrDefaultAsync(
            value => value.HostId == hostId,
            cancellationToken
        );
        var active = await CampaignQuery(db, hostId)
            .SingleOrDefaultAsync(
                value => value.Status == BlokeRaidCampaignStatus.Active,
                cancellationToken
            );
        var changed = false;
        if (
            configuration
                is { ResetPolicy: BlokeRaidResetPolicy.Weekly, NextWeeklyResetAtUtc: { } resetAt }
            && resetAt <= now
        )
        {
            configuration.NextWeeklyResetAtUtc = NextWeeklyReset(
                now,
                (DayOfWeek)configuration.WeeklyResetDay,
                configuration.WeeklyResetHourUtc
            );
            configuration.Revision++;
            configuration.UpdatedAtUtc = now;
            if (host.BlokeRaidAcceptWorkAfterUtc is not { } acceptAfter || resetAt >= acceptAfter)
            {
                if (active is not null)
                {
                    active.Status = BlokeRaidCampaignStatus.Ended;
                    active.CompletedAtUtc = now;
                    active.Revision++;
                    AddEvent(
                        db,
                        active,
                        BlokeRaidEventKind.CampaignEnded,
                        $"weekly-ended:{resetAt.Ticks}",
                        new
                        {
                            campaign = active.PublicId,
                            endedAtUtc = now,
                            reason = "weekly-reset",
                        },
                        now
                    );
                }
                var campaign = NewCampaign(hostId, $"weekly:{resetAt.Ticks}", configuration, now);
                _ = db.BlokeRaidCampaigns.Add(campaign);
                _ = await db.SaveChangesAsync(cancellationToken);
                AddEvent(
                    db,
                    campaign,
                    BlokeRaidEventKind.CampaignReset,
                    $"weekly-reset:{resetAt.Ticks}",
                    new
                    {
                        campaign = campaign.PublicId,
                        previousCampaign = active?.PublicId,
                        resetAtUtc = now,
                        boss = campaign.BossName,
                    },
                    now
                );
                AddEvent(
                    db,
                    campaign,
                    BlokeRaidEventKind.CampaignStarted,
                    $"weekly-start:{resetAt.Ticks}",
                    new
                    {
                        campaign = campaign.PublicId,
                        boss = campaign.BossName,
                        health = campaign.MaximumHealth,
                        ward = campaign.MaximumWard,
                        phase = 1,
                        endsAtUtc = campaign.EndsAtUtc,
                        response = configuration.PhaseOneResponse,
                    },
                    now
                );
            }
            changed = true;
        }
        else if (active is not null && active.EndsAtUtc <= now)
        {
            active.Status = BlokeRaidCampaignStatus.Expired;
            active.CompletedAtUtc = now;
            active.Revision++;
            AddEvent(
                db,
                active,
                BlokeRaidEventKind.CampaignExpired,
                $"campaign-expired:{active.PublicId:N}",
                new
                {
                    campaign = active.PublicId,
                    expiredAtUtc = now,
                    response = configuration?.ExpiryResponse
                        ?? NewConfiguration(hostId, now).ExpiryResponse,
                },
                now
            );
            changed = true;
        }

        if (!changed)
        {
            return new(false, false);
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, false);
    }

    private static async Task<bool> CompleteVictoryAsync(
        BlokeBotDbContext db,
        BlokeRaidCampaign campaign,
        BlokeRaidConfiguration configuration,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (campaign.Status != BlokeRaidCampaignStatus.Active)
        {
            return true;
        }

        var reward = PointAmount.ParseAbsolute(campaign.VictoryPointReward);
        foreach (
            var contribution in campaign.Contributions.OrderBy(value => value.ViewerTwitchUserId)
        )
        {
            if (reward.IsZero)
            {
                continue;
            }
            var balance = await db.PointBalances.SingleOrDefaultAsync(
                value => value.HostId == campaign.HostId && value.Login == contribution.ViewerLogin,
                cancellationToken
            );
            if (balance is null)
            {
                balance = new PointBalance
                {
                    HostId = campaign.HostId,
                    Login = contribution.ViewerLogin,
                    Amount = "0",
                    UpdatedAtUtc = now,
                };
                _ = db.PointBalances.Add(balance);
            }
            var current = PointAmount.ParseAbsolute(balance.Amount);
            if (
                !await PointCreditCapacity.CanCreditAsync(
                    db,
                    campaign.HostId,
                    contribution.ViewerLogin,
                    current,
                    reward.Value,
                    cancellationToken
                )
            )
            {
                return false;
            }
            var next = current.Add(reward);
            balance.Amount = next.ToString();
            balance.UpdatedAtUtc = now;
            _ = db.PointLedgerEntries.Add(
                new PointLedgerEntry
                {
                    HostId = campaign.HostId,
                    CreatedAtUtc = now,
                    Kind = PointLedgerKind.BlokeRaidVictoryReward,
                    Login = contribution.ViewerLogin,
                    Delta = reward.ToString(),
                    BalanceAfter = next.ToString(),
                    Note = $"BlokeRaid victory over {campaign.BossName}",
                    OperationKey =
                        $"blokeraid:victory:{campaign.PublicId:N}:{contribution.ViewerTwitchUserId}",
                }
            );
        }
        campaign.Status = BlokeRaidCampaignStatus.Victory;
        campaign.CompletedAtUtc = now;
        campaign.VictoryRewardedAtUtc = now;
        AddEvent(
            db,
            campaign,
            BlokeRaidEventKind.CampaignVictorious,
            $"campaign-victory:{campaign.PublicId:N}",
            new
            {
                campaign = campaign.PublicId,
                boss = campaign.BossName,
                contributors = campaign.Contributions.Count,
                reward = campaign.VictoryPointReward,
                defeatedAtUtc = now,
                response = configuration.VictoryResponse,
            },
            now
        );
        return true;
    }

    private static void AddActionEvents(
        BlokeBotDbContext db,
        BlokeRaidCampaign campaign,
        BlokeRaidAction action,
        int previousPhase,
        string response,
        DateTime now
    )
    {
        AddEvent(
            db,
            campaign,
            BlokeRaidEventKind.ActionResolved,
            $"action:{action.OperationKey}",
            new
            {
                campaign = campaign.PublicId,
                action = action.Kind.ToString(),
                source = action.Source.ToString(),
                viewer = action.ViewerLogin,
                outcome = action.Outcome,
                pointCost = action.PointCost,
                health = action.BossHealthAfter,
                ward = action.WardAfter,
                phase = action.PhaseAfter,
                guessRound = action.GuessRoundId,
                response,
            },
            now
        );
        if (campaign.CurrentPhase != previousPhase)
        {
            AddEvent(
                db,
                campaign,
                BlokeRaidEventKind.PhaseChanged,
                $"phase:{campaign.PublicId:N}:{campaign.CurrentPhase}",
                new
                {
                    campaign = campaign.PublicId,
                    phase = campaign.CurrentPhase,
                    health = campaign.CurrentHealth,
                    response,
                },
                now
            );
        }
    }

    private static void AddEvent(
        BlokeBotDbContext db,
        BlokeRaidCampaign campaign,
        BlokeRaidEventKind kind,
        string operationKey,
        object payload,
        DateTime occurredAtUtc
    ) =>
        _ = db.BlokeRaidEvents.Add(
            new BlokeRaidDomainEvent
            {
                HostId = campaign.HostId,
                CampaignId = campaign.Id,
                Kind = kind,
                OperationKey = operationKey,
                PublicPayload = JsonSerializer.Serialize(payload),
                OccurredAtUtc = occurredAtUtc,
            }
        );

    private static BlokeRaidContribution Contribution(
        BlokeRaidCampaign campaign,
        BlokeRaidViewer viewer,
        DateTime now
    )
    {
        var contribution = campaign.Contributions.SingleOrDefault(value =>
            value.ViewerTwitchUserId == viewer.TwitchUserId || value.ViewerLogin == viewer.Login
        );
        if (contribution is not null)
        {
            if (contribution.ViewerTwitchUserId.StartsWith("login:", StringComparison.Ordinal))
            {
                contribution.ViewerTwitchUserId = viewer.TwitchUserId;
            }
            contribution.ViewerLogin = viewer.Login;
            contribution.ViewerDisplayName = viewer.DisplayName;
            return contribution;
        }

        contribution = new()
        {
            HostId = campaign.HostId,
            CampaignId = campaign.Id,
            ViewerTwitchUserId = viewer.TwitchUserId,
            ViewerLogin = viewer.Login,
            ViewerDisplayName = viewer.DisplayName,
            LastContributedAtUtc = now,
        };
        campaign.Contributions.Add(contribution);
        return contribution;
    }

    private static BlokeRaidActionRuleView Rule(
        BlokeRaidConfiguration configuration,
        BlokeRaidActionKind kind
    ) =>
        kind switch
        {
            BlokeRaidActionKind.Attack => new(
                configuration.AttackMinimum,
                configuration.AttackMaximum,
                configuration.AttackCooldownSeconds,
                configuration.AttackPerStreamLimit,
                PointAmount.Zero
            ),
            BlokeRaidActionKind.Mend => new(
                configuration.MendMinimum,
                configuration.MendMaximum,
                configuration.MendCooldownSeconds,
                configuration.MendPerStreamLimit,
                PointAmount.Zero
            ),
            BlokeRaidActionKind.Special => new(
                configuration.SpecialMinimum,
                configuration.SpecialMaximum,
                configuration.SpecialCooldownSeconds,
                configuration.SpecialPerStreamLimit,
                PointAmount.ParseAbsolute(configuration.SpecialPointCost)
            ),
            _ => throw new InvalidOperationException(
                "Correct guesses use the guessing integration."
            ),
        };

    private static int Phase(BlokeRaidConfiguration configuration, BlokeRaidCampaign campaign)
    {
        var percent =
            campaign.MaximumHealth == 0 ? 0 : campaign.CurrentHealth * 100 / campaign.MaximumHealth;
        return percent <= configuration.PhaseThreeHealthPercent ? 3
            : percent <= configuration.PhaseTwoHealthPercent ? 2
            : 1;
    }

    private static string Response(
        BlokeRaidConfiguration configuration,
        BlokeRaidActionKind kind,
        int outcome,
        BlokeRaidCampaign campaign,
        int previousPhase
    ) =>
        campaign.CurrentHealth == 0 ? configuration.VictoryResponse
        : campaign.CurrentPhase != previousPhase
            ? campaign.CurrentPhase == 2 ? configuration.PhaseTwoResponse
                : configuration.PhaseThreeResponse
        : kind switch
        {
            BlokeRaidActionKind.Attack => $"Attack dealt {outcome} damage.",
            BlokeRaidActionKind.Mend => $"The raid ward recovered {outcome}.",
            BlokeRaidActionKind.Special => $"The point-funded special dealt {outcome} damage.",
            BlokeRaidActionKind.CorrectGuess =>
                $"Correct guessers dealt {outcome} damage to the boss.",
            _ => string.Empty,
        };

    private static BlokeRaidCampaign NewCampaign(
        int hostId,
        string operationKey,
        BlokeRaidConfiguration configuration,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            StartOperationKey = operationKey,
            Status = BlokeRaidCampaignStatus.Active,
            BossName = configuration.BossName,
            MaximumHealth = configuration.MaximumHealth,
            CurrentHealth = configuration.MaximumHealth,
            MaximumWard = configuration.MaximumWard,
            CurrentWard = 0,
            CurrentPhase = 1,
            VictoryPointReward = configuration.VictoryPointReward,
            ResetPolicy = configuration.ResetPolicy,
            StartedAtUtc = now,
            EndsAtUtc = now.AddHours(configuration.CampaignDurationHours),
        };

    private static BlokeRaidConfiguration NewConfiguration(int hostId, DateTime now) =>
        new() { HostId = hostId, UpdatedAtUtc = now };

    private static void Apply(
        BlokeRaidConfiguration configuration,
        BlokeRaidConfigurationDraft draft
    )
    {
        configuration.BossName = draft.BossName.Trim();
        configuration.MaximumHealth = draft.MaximumHealth;
        configuration.MaximumWard = draft.MaximumWard;
        configuration.CampaignDurationHours = draft.CampaignDurationHours;
        configuration.AttackMinimum = draft.AttackMinimum;
        configuration.AttackMaximum = draft.AttackMaximum;
        configuration.AttackCooldownSeconds = draft.AttackCooldownSeconds;
        configuration.AttackPerStreamLimit = draft.AttackPerStreamLimit;
        configuration.MendMinimum = draft.MendMinimum;
        configuration.MendMaximum = draft.MendMaximum;
        configuration.MendCooldownSeconds = draft.MendCooldownSeconds;
        configuration.MendPerStreamLimit = draft.MendPerStreamLimit;
        configuration.SpecialMinimum = draft.SpecialMinimum;
        configuration.SpecialMaximum = draft.SpecialMaximum;
        configuration.SpecialCooldownSeconds = draft.SpecialCooldownSeconds;
        configuration.SpecialPerStreamLimit = draft.SpecialPerStreamLimit;
        configuration.SpecialPointCost = draft.SpecialPointCost.ToString();
        configuration.CorrectGuessDamage = draft.CorrectGuessDamage;
        configuration.VictoryPointReward = draft.VictoryPointReward.ToString();
        configuration.PhaseTwoHealthPercent = draft.PhaseTwoHealthPercent;
        configuration.PhaseThreeHealthPercent = draft.PhaseThreeHealthPercent;
        configuration.PhaseOneResponse = draft.PhaseOneResponse.Trim();
        configuration.PhaseTwoResponse = draft.PhaseTwoResponse.Trim();
        configuration.PhaseThreeResponse = draft.PhaseThreeResponse.Trim();
        configuration.VictoryResponse = draft.VictoryResponse.Trim();
        configuration.ExpiryResponse = draft.ExpiryResponse.Trim();
        configuration.ResetPolicy = draft.ResetPolicy;
        configuration.WeeklyResetDay = (int)draft.WeeklyResetDay;
        configuration.WeeklyResetHourUtc = draft.WeeklyResetHourUtc;
    }

    private static BlokeRaidConfigurationView ToConfigurationView(
        BlokeRaidConfiguration configuration
    ) =>
        new(
            configuration.Revision,
            configuration.BossName,
            configuration.MaximumHealth,
            configuration.MaximumWard,
            configuration.CampaignDurationHours,
            Rule(configuration, BlokeRaidActionKind.Attack),
            Rule(configuration, BlokeRaidActionKind.Mend),
            Rule(configuration, BlokeRaidActionKind.Special),
            configuration.CorrectGuessDamage,
            PointAmount.ParseAbsolute(configuration.VictoryPointReward),
            configuration.PhaseTwoHealthPercent,
            configuration.PhaseThreeHealthPercent,
            configuration.PhaseOneResponse,
            configuration.PhaseTwoResponse,
            configuration.PhaseThreeResponse,
            configuration.VictoryResponse,
            configuration.ExpiryResponse,
            configuration.ResetPolicy,
            (DayOfWeek)configuration.WeeklyResetDay,
            configuration.WeeklyResetHourUtc,
            configuration.NextWeeklyResetAtUtc
        );

    private static BlokeRaidCampaignView ToCampaignView(BlokeRaidCampaign campaign) =>
        new(
            campaign.PublicId,
            campaign.Status,
            campaign.BossName,
            campaign.MaximumHealth,
            campaign.CurrentHealth,
            campaign.MaximumWard,
            campaign.CurrentWard,
            campaign.CurrentPhase,
            PointAmount.ParseAbsolute(campaign.VictoryPointReward),
            campaign.ResetPolicy,
            campaign.StartedAtUtc,
            campaign.EndsAtUtc,
            campaign.CompletedAtUtc,
            campaign.VictoryRewardedAtUtc is not null,
            campaign.Revision,
            [
                .. campaign
                    .Contributions.OrderByDescending(value => value.Damage + value.WardRestored)
                    .ThenBy(value => value.ViewerLogin, StringComparer.Ordinal)
                    .Select(value => new BlokeRaidContributionView(
                        new(value.ViewerTwitchUserId, value.ViewerLogin, value.ViewerDisplayName),
                        value.Damage,
                        value.WardRestored,
                        value.ActionCount,
                        value.SpecialCount,
                        value.CorrectGuessCount,
                        value.LastContributedAtUtc
                    )),
            ],
            [
                .. campaign
                    .Actions.OrderByDescending(value => value.OccurredAtUtc)
                    .ThenByDescending(value => value.Id)
                    .Take(20)
                    .Select(ToActionView),
            ]
        );

    private static BlokeRaidActionView ToActionView(BlokeRaidAction action) =>
        new(
            action.Id,
            action.Kind,
            action.Source,
            action.ViewerTwitchUserId is null
                ? null
                : new(
                    action.ViewerTwitchUserId,
                    action.ViewerLogin ?? string.Empty,
                    action.ViewerDisplayName ?? action.ViewerLogin ?? string.Empty
                ),
            action.Outcome,
            PointAmount.ParseAbsolute(action.PointCost),
            action.BossHealthBefore,
            action.BossHealthAfter,
            action.WardBefore,
            action.WardAfter,
            action.PhaseAfter,
            action.GuessRoundId,
            action.Response,
            action.OccurredAtUtc
        );

    private static IQueryable<BlokeRaidCampaign> CampaignQuery(BlokeBotDbContext db, int hostId) =>
        db
            .BlokeRaidCampaigns.AsSplitQuery()
            .Include(value => value.Contributions)
            .Include(value => value.Actions)
            .Where(value => value.HostId == hostId);

    private static BlokeRaidConfigurationOutcome.Invalid? Validate(
        BlokeRaidConfigurationDraft draft
    ) =>
        string.IsNullOrWhiteSpace(draft.BossName) || draft.BossName.Trim().Length > 120
            ? new("Boss name is required and may contain at most 120 characters.")
        : draft.MaximumHealth is < BlokeRaidLimits.MinimumHealth or > BlokeRaidLimits.MaximumHealth
            ? new(
                $"Maximum health must be between {BlokeRaidLimits.MinimumHealth:N0} and {BlokeRaidLimits.MaximumHealth:N0}."
            )
        : draft.MaximumWard is < 0 or > BlokeRaidLimits.MaximumWard
            ? new($"Maximum ward must be between 0 and {BlokeRaidLimits.MaximumWard:N0}.")
        : draft.CampaignDurationHours is < 1 or > BlokeRaidLimits.MaximumCampaignDurationHours
            ? new("Campaign duration must be between 1 hour and 365 days.")
        : !ValidRule(
            draft.AttackMinimum,
            draft.AttackMaximum,
            draft.AttackCooldownSeconds,
            draft.AttackPerStreamLimit
        )
        || !ValidRule(
            draft.MendMinimum,
            draft.MendMaximum,
            draft.MendCooldownSeconds,
            draft.MendPerStreamLimit
        )
        || !ValidRule(
            draft.SpecialMinimum,
            draft.SpecialMaximum,
            draft.SpecialCooldownSeconds,
            draft.SpecialPerStreamLimit
        )
            ? new("Action ranges, cooldowns, or per-stream limits are outside supported bounds.")
        : draft.CorrectGuessDamage is < 1 or > BlokeRaidLimits.MaximumActionOutcome
            ? new("Correct-guess damage is outside the supported range.")
        : draft.PhaseThreeHealthPercent is < 1 or > 98
        || draft.PhaseTwoHealthPercent is < 2 or > 99
        || draft.PhaseThreeHealthPercent >= draft.PhaseTwoHealthPercent
            ? new("Phase three must be below phase two, and both must be between 1% and 99%.")
        : draft.WeeklyResetHourUtc is < 0 or > 23
        || !Enum.IsDefined(draft.WeeklyResetDay)
        || !Enum.IsDefined(draft.ResetPolicy)
            ? new("Weekly reset day, hour, or policy is invalid.")
        : new[]
        {
            draft.PhaseOneResponse,
            draft.PhaseTwoResponse,
            draft.PhaseThreeResponse,
            draft.VictoryResponse,
            draft.ExpiryResponse,
        }.Any(value => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 500)
            ? new(
                "Every deterministic response is required and may contain at most 500 characters."
            )
        : null;

    private static bool ValidRule(int minimum, int maximum, int cooldown, int perStream) =>
        minimum is >= 1 and <= BlokeRaidLimits.MaximumActionOutcome
        && maximum >= minimum
        && maximum <= BlokeRaidLimits.MaximumActionOutcome
        && cooldown is >= 0 and <= BlokeRaidLimits.MaximumCooldownSeconds
        && perStream is >= 1 and <= BlokeRaidLimits.MaximumPerStreamLimit;

    private static BlokeRaidCampaignOutcome.Invalid? Validate(BlokeRaidCampaignCommand command) =>
        !ValidOperationKey(command.OperationKey) ? new("A bounded operation identity is required.")
        : string.IsNullOrWhiteSpace(command.Actor.TwitchUserId)
        || string.IsNullOrWhiteSpace(command.Actor.Login)
            ? new("A moderator identity is required.")
        : command.PrivateReason.Length > 1_000
            ? new("Private reasons may contain at most 1,000 characters.")
        : null;

    private static BlokeRaidActionOutcome.Invalid? Validate(BlokeRaidActionCommand command) =>
        !ValidOperationKey(command.OperationKey) ? new("A bounded operation identity is required.")
        : command.Kind
            is not (
                BlokeRaidActionKind.Attack
                or BlokeRaidActionKind.Mend
                or BlokeRaidActionKind.Special
            )
            ? new("Choose attack, mend, or special.")
        : !IsValid(Normalize(command.Viewer)) ? new("A valid viewer identity is required.")
        : string.IsNullOrWhiteSpace(command.StreamKey) || command.StreamKey.Length > 160
            ? new("A bounded stream identity is required.")
        : null;

    private static bool ValidOperationKey(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 160;

    private static BlokeRaidViewer Normalize(BlokeRaidViewer viewer) =>
        new(
            viewer.TwitchUserId.Trim(),
            CommunityInput.NormalizeLogin(viewer.Login),
            viewer.DisplayName.Trim()
        );

    private static bool IsValid(BlokeRaidViewer viewer) =>
        viewer.TwitchUserId.Length is >= 1 and <= 128
        && CommunityInput.IsValidLogin(viewer.Login)
        && viewer.DisplayName.Length is >= 1 and <= 128;

    private static DateTime NextWeeklyReset(DateTime now, DayOfWeek day, int hourUtc)
    {
        var candidate = new DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc);
        var days = ((int)day - (int)candidate.DayOfWeek + 7) % 7;
        candidate = candidate.AddDays(days);
        return candidate <= now ? candidate.AddDays(7) : candidate;
    }

    private async Task PublishChangedAsync(bool pointsChanged, CancellationToken cancellationToken)
    {
        _ = await events.PublishAsync(AppEventKind.BlokeRaidChanged, cancellationToken);
        if (pointsChanged)
        {
            _ = await events.PublishAsync(AppEventKind.PointsChanged, cancellationToken);
        }
    }

    private static Task<bool> FeatureIsEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        db.Hosts.AnyAsync(
            value =>
                value.Id == hostId
                && (value.EnabledFeatures & HostFeatureFlags.CooperativeGame)
                    == HostFeatureFlags.CooperativeGame,
            cancellationToken
        );

    private static async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception)
                when (attempt < _persistenceRetryCount && IsPersistenceCollision(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), cancellationToken);
            }
        }
    }

    private static bool IsPersistenceCollision(Exception exception) =>
        exception switch
        {
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
            } => true,
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT,
                SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE,
            } => true,
            DbUpdateException { InnerException: { } inner } => IsPersistenceCollision(inner),
            _ => false,
        };

    private sealed record DueWorkOutcome(bool Changed, bool PointsChanged);

    private sealed class ImmediateTransaction(
        SqliteTransaction providerTransaction,
        IDbContextTransaction contextTransaction
    ) : IAsyncDisposable
    {
        public static async Task<ImmediateTransaction> StartAsync(
            BlokeBotDbContext db,
            CancellationToken cancellationToken
        )
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            var connection =
                db.Database.GetDbConnection() as SqliteConnection
                ?? throw new InvalidOperationException("BlokeRaid persistence requires SQLite.");
            var providerTransaction = connection.BeginTransaction(deferred: false);
            try
            {
                var contextTransaction =
                    await db.Database.UseTransactionAsync(providerTransaction, cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The immediate SQLite transaction could not be attached."
                    );
                return new(providerTransaction, contextTransaction);
            }
            catch
            {
                await providerTransaction.DisposeAsync();
                throw;
            }
        }

        public Task CommitAsync(CancellationToken cancellationToken) =>
            contextTransaction.CommitAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await contextTransaction.DisposeAsync();
            await providerTransaction.DisposeAsync();
        }
    }
}
