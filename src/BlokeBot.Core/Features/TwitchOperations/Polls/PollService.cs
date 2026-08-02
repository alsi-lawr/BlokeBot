using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Polls;

public sealed class PollService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBroadcasterTokenStatusProvider broadcasters,
    HelixClient helix,
    BotSettings settings,
    EventBus<AppEventKind> events,
    DurableAlertService alerts,
    NativeTwitchFeatureGate nativeTwitch
) : IPollEventObserver, IPollDashboardOperations
{
    private const int _resultsToKeep = 100;

    public async Task<PollDashboardState> LoadAsync(int hostId, CancellationToken ct)
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return new(new PollAuthorizationReadiness.Disabled(), null, [], []);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var readiness = await ReadinessAsync(hostId, ct);
        var active = await db
            .TwitchPolls.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Status == TwitchPollStatus.Active)
            .SingleOrDefaultAsync(ct);
        var templates = await db
            .TwitchPollTemplates.AsNoTracking()
            .Include(x => x.Choices)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Id)
            .ToArrayAsync(ct);
        var results = await db
            .TwitchPolls.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Status != TwitchPollStatus.Active)
            .OrderByDescending(x => x.EndedAtUtc)
            .Take(_resultsToKeep)
            .ToArrayAsync(ct);
        return new(
            readiness,
            active is null ? null : View(active),
            templates.Select(View).ToArray(),
            results.Select(View).ToArray()
        );
    }

    public async Task<PollOperationOutcome> SaveTemplateAsync(
        int hostId,
        PollTemplateDraft draft,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var validation = draft.Validate();
        if (validation is PollTemplateValidationOutcome.Invalid invalid)
        {
            return new PollOperationOutcome.InvalidTemplate(invalid.Message);
        }
        var valid = ((PollTemplateValidationOutcome.Valid)validation).Draft;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (
            !await db.Hosts.AnyAsync(
                host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.Polls) == HostFeatureFlags.Polls,
                ct
            )
        )
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var template = new TwitchPollTemplate
        {
            HostId = hostId,
            Title = valid.Title.Trim(),
            DurationSeconds = valid.DurationSeconds,
            ChannelPointsVotingEnabled = valid.ChannelPointsVotingEnabled,
            ChannelPointsPerVote = valid.ChannelPointsVotingEnabled
                ? valid.ChannelPointsPerVote
                : null,
            CreatedAtUtc = DateTime.UtcNow,
            Choices = valid
                .Choices.Select(
                    (title, index) =>
                        new TwitchPollTemplateChoice { Position = index, Title = title }
                )
                .ToArray(),
        };
        db.TwitchPollTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new PollOperationOutcome.TemplateSaved(View(template));
    }

    public async Task<PollOperationOutcome> StartAsync(
        int hostId,
        int templateId,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new PollOperationOutcome.NotReady(
                "Reconnect the selected channel's Twitch integration."
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        var template = await db
            .TwitchPollTemplates.Include(x => x.Choices)
            .SingleOrDefaultAsync(x => x.Id == templateId && x.HostId == hostId, ct);
        if (host is null || template is null)
        {
            return new PollOperationOutcome.TemplateNotFound();
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Polls))
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }
        if (
            await db.TwitchPolls.AnyAsync(
                x => x.HostId == hostId && x.Status == TwitchPollStatus.Active,
                ct
            )
        )
        {
            return new PollOperationOutcome.ActivePollExists();
        }
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var provider = await helix.CreatePollAsync(
            new HelixRequestContext(settings.Identity.ClientId, token),
            host.TwitchUserId!,
            new(
                template.Title,
                template.Choices.OrderBy(x => x.Position).Select(x => x.Title).ToArray(),
                template.DurationSeconds,
                template.ChannelPointsVotingEnabled,
                template.ChannelPointsPerVote
            ),
            ct
        );
        if (provider is HelixPollCreateOutcome.ActivePollExists)
        {
            return new PollOperationOutcome.ActivePollExists();
        }
        if (provider is not HelixPollCreateOutcome.Created created)
        {
            return new PollOperationOutcome.ProviderRejected(
                "Twitch did not permit creating this poll."
            );
        }

        var poll = Upsert(db, hostId, created.Poll, false).Poll;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new PollOperationOutcome.Started(View(poll));
    }

    public async Task<PollOperationOutcome> EndAsync(
        int hostId,
        bool confirmedExternal,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new PollOperationOutcome.NotReady(
                "Reconnect the selected channel's Twitch integration."
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        var active = await db.TwitchPolls.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.Status == TwitchPollStatus.Active,
            ct
        );
        if (host is null || active is null)
        {
            return new PollOperationOutcome.ProviderRejected("There is no active poll to end.");
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Polls))
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }
        if (active.IsExternallyStarted && !confirmedExternal)
        {
            return new PollOperationOutcome.ConfirmationRequired();
        }
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return new PollOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var provider = await helix.EndPollAsync(
            new HelixRequestContext(settings.Identity.ClientId, token),
            host.TwitchUserId!,
            active.ProviderPollId,
            HelixPollEndStatus.Terminated,
            ct
        );
        if (provider is null)
        {
            return new PollOperationOutcome.ProviderRejected(
                "Twitch did not permit ending this poll."
            );
        }
        var poll = Upsert(db, hostId, provider, active.IsExternallyStarted).Poll;
        await TrimResultsAsync(db, hostId, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new PollOperationOutcome.Ended(View(poll));
    }

    public async Task ReconcileChannelAsync(string channel, CancellationToken ct)
    {
        var login = Login.Normalize(channel);
        if (login.Length == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await db
            .Hosts.Where(host => host.Login == login)
            .Select(host => (int?)host.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is not { } id)
        {
            return;
        }

        await ReconcileAsync(id, ct);
    }

    public async Task ReconcileAsync(int hostId, CancellationToken ct)
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return;
        }

        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (
            host?.TwitchUserId is not { Length: > 0 } broadcasterId
            || !host.EnabledFeatures.Contains(HostFeatureFlags.Polls)
        )
        {
            return;
        }

        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return;
        }

        var provider = await helix.GetLatestPollAsync(
            new HelixRequestContext(settings.Identity.ClientId, token),
            broadcasterId,
            ct
        );
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return;
        }

        var changed = provider switch
        {
            HelixPollLookupOutcome.Found found => Upsert(db, hostId, found.Poll, true).Changed,
            HelixPollLookupOutcome.NoPoll => ArchiveMissingActivePoll(db, hostId),
            HelixPollLookupOutcome.Unavailable => false,
            _ => throw new InvalidOperationException("Unknown Twitch poll lookup outcome."),
        };
        if (!changed)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        if (provider is HelixPollLookupOutcome.NoPoll)
        {
            await TrimResultsAsync(db, hostId, ct);
            await db.SaveChangesAsync(ct);
        }
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    public async Task RecordProviderUpdateAsync(int hostId, HelixPoll poll, CancellationToken ct)
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, ct))
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (
            !await db.Hosts.AnyAsync(
                host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.Polls) == HostFeatureFlags.Polls,
                ct
            )
        )
        {
            return;
        }

        var upsert = Upsert(db, hostId, poll, true);
        if (!upsert.Changed)
        {
            return;
        }

        if (upsert.Poll.Status is not TwitchPollStatus.Active)
        {
            await TrimResultsAsync(db, hostId, ct);
        }
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    public async Task PollReceivedAsync(EventSubPollEvent poll, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(
            x =>
                x.TwitchUserId == poll.BroadcasterUserId
                || x.Login == Login.Normalize(poll.BroadcasterUserLogin),
            ct
        );
        if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.Polls))
        {
            return;
        }
        var status = poll.Status switch
        {
            "ACTIVE" => HelixPollStatus.Active,
            "COMPLETED" => HelixPollStatus.Completed,
            "TERMINATED" => HelixPollStatus.Terminated,
            _ => HelixPollStatus.Archived,
        };
        if (!await nativeTwitch.IsEnabledAsync(host.Id, HostFeatureFlags.Polls, ct))
        {
            return;
        }

        var upsert = Upsert(
            db,
            host.Id,
            new(
                poll.PollId,
                poll.BroadcasterUserId,
                poll.Title,
                poll.Choices.Select(x => new HelixPollChoice(
                        x.Id,
                        x.Title,
                        x.Votes,
                        x.ChannelPointsVotes
                    ))
                    .ToArray(),
                status,
                poll.StartedAt,
                poll.EndsAt
            ),
            true
        );
        if (!upsert.Changed)
        {
            return;
        }

        if (upsert.Poll.Status is not TwitchPollStatus.Active)
        {
            await TrimResultsAsync(db, host.Id, ct);
        }
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    private async Task<PollAuthorizationReadiness> ReadinessAsync(int hostId, CancellationToken ct)
    {
        var status = await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            ct
        );
        if (status is TokenStatus.Ready)
        {
            return new PollAuthorizationReadiness.Ready();
        }

        await EnsureBroadcasterAuthorizationAlertAsync(hostId, ct);
        return new PollAuthorizationReadiness.NeedsBroadcasterAuthorization(
            "Reconnect the selected channel's Twitch integration."
        );
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

        await EnsureBroadcasterAuthorizationAlertAsync(hostId, ct);
        return null;
    }

    private async Task EnsureBroadcasterAuthorizationAlertAsync(int hostId, CancellationToken ct)
    {
        await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "twitch-broadcaster-authorization",
                "reauthorize-v1",
                "Reconnect Twitch integration",
                "Reconnect the selected channel's Twitch integration and approve all requested permissions.",
                "/twitch-operations"
            )
            .ExecuteAsync(ct);
    }

    private static bool ArchiveMissingActivePoll(BlokeBotDbContext db, int hostId)
    {
        var active = db.TwitchPolls.SingleOrDefault(poll =>
            poll.HostId == hostId && poll.Status == TwitchPollStatus.Active
        );
        if (active is null)
        {
            return false;
        }

        active.Status = TwitchPollStatus.Archived;
        active.EndedAtUtc = DateTime.UtcNow;
        active.UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    private static PollUpsertOutcome Upsert(
        BlokeBotDbContext db,
        int hostId,
        HelixPoll poll,
        bool external
    )
    {
        var record =
            db.TwitchPolls.Local.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderPollId == poll.Id
            )
            ?? db.TwitchPolls.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderPollId == poll.Id
            );
        var status = ToPersistedStatus(poll.Status);
        if (record is not null)
        {
            if (record.Status is not TwitchPollStatus.Active)
            {
                return new PollUpsertOutcome(record, false);
            }

            if (status is TwitchPollStatus.Active && !HasAdvancedVotes(record, poll))
            {
                return new PollUpsertOutcome(record, false);
            }
        }
        else
        {
            record = new TwitchPoll
            {
                HostId = hostId,
                ProviderPollId = poll.Id,
                IsExternallyStarted = external,
            };
            db.TwitchPolls.Add(record);
        }

        record.Title = poll.Title;
        record.ChoicesJson = JsonSerializer.Serialize(poll.Choices);
        record.Status = status;
        record.StartedAtUtc = poll.StartedAt.UtcDateTime;
        record.EndsAtUtc = poll.EndsAt?.UtcDateTime;
        record.EndedAtUtc = status is TwitchPollStatus.Active ? null : DateTime.UtcNow;
        record.UpdatedAtUtc = DateTime.UtcNow;
        return new PollUpsertOutcome(record, true);
    }

    private static bool HasAdvancedVotes(TwitchPoll record, HelixPoll poll)
    {
        var current = (
            JsonSerializer.Deserialize<HelixPollChoice[]>(record.ChoicesJson) ?? []
        ).ToDictionary(choice => choice.Id, StringComparer.Ordinal);
        return poll.Choices.All(choice =>
                current.TryGetValue(choice.Id, out var existing)
                && choice.Votes >= existing.Votes
                && choice.ChannelPointsVotes >= existing.ChannelPointsVotes
            )
            && poll.Choices.Any(choice =>
                choice.Votes > current[choice.Id].Votes
                || choice.ChannelPointsVotes > current[choice.Id].ChannelPointsVotes
            );
    }

    private static TwitchPollStatus ToPersistedStatus(HelixPollStatus status)
    {
        return status switch
        {
            HelixPollStatus.Active => TwitchPollStatus.Active,
            HelixPollStatus.Completed => TwitchPollStatus.Completed,
            HelixPollStatus.Terminated => TwitchPollStatus.Terminated,
            _ => TwitchPollStatus.Archived,
        };
    }

    private sealed record PollUpsertOutcome(TwitchPoll Poll, bool Changed);

    private static async Task TrimResultsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var excess = await db
            .TwitchPolls.Where(x => x.HostId == hostId && x.Status != TwitchPollStatus.Active)
            .OrderByDescending(x => x.EndedAtUtc)
            .Skip(_resultsToKeep)
            .ToArrayAsync(ct);
        db.TwitchPolls.RemoveRange(excess);
    }

    private static PollTemplateView View(TwitchPollTemplate template)
    {
        return new(
            template.Id,
            template.Title,
            template.Choices.OrderBy(x => x.Position).Select(x => x.Title).ToArray(),
            template.DurationSeconds,
            template.ChannelPointsVotingEnabled,
            template.ChannelPointsPerVote
        );
    }

    private static PollView View(TwitchPoll poll)
    {
        return new(
            poll.ProviderPollId,
            poll.Title,
            JsonSerializer.Deserialize<PollChoiceView[]>(poll.ChoicesJson) ?? [],
            poll.Status.ToString(),
            poll.IsExternallyStarted,
            poll.StartedAtUtc,
            poll.EndsAtUtc,
            poll.EndedAtUtc
        );
    }
}
