using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
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
    DurableAlertService alerts
) : IPollEventObserver
{
    private const int _resultsToKeep = 100;

    public async Task<PollDashboardState> LoadAsync(int hostId, CancellationToken ct)
    {
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
        var validation = draft.Validate();
        if (validation is PollTemplateValidationOutcome.Invalid invalid)
        {
            return new PollOperationOutcome.InvalidTemplate(invalid.Message);
        }
        var valid = ((PollTemplateValidationOutcome.Valid)validation).Draft;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
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
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new PollOperationOutcome.NotReady(
                "Reconnect the selected broadcaster with Twitch operations permissions."
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
        if (
            await db.TwitchPolls.AnyAsync(
                x => x.HostId == hostId && x.Status == TwitchPollStatus.Active,
                ct
            )
        )
        {
            return new PollOperationOutcome.ActivePollExists();
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
        if (provider is null)
        {
            return new PollOperationOutcome.ActivePollExists();
        }
        var poll = Upsert(db, hostId, provider, false);
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
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new PollOperationOutcome.NotReady(
                "Reconnect the selected broadcaster with Twitch operations permissions."
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
        if (active.IsExternallyStarted && !confirmedExternal)
        {
            return new PollOperationOutcome.ConfirmationRequired();
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
        var poll = Upsert(db, hostId, provider, active.IsExternallyStarted);
        await TrimResultsAsync(db, hostId, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return new PollOperationOutcome.Ended(View(poll));
    }

    public async Task ReconcileAsync(int hostId, CancellationToken ct)
    {
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host?.TwitchUserId is not { Length: > 0 } broadcasterId)
        {
            return;
        }
        var provider = await helix.GetActivePollAsync(
            new HelixRequestContext(settings.Identity.ClientId, token),
            broadcasterId,
            ct
        );
        if (provider is null)
        {
            return;
        }
        Upsert(db, hostId, provider, true);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    public async Task RecordProviderUpdateAsync(int hostId, HelixPoll poll, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Upsert(db, hostId, poll, true);
        if (poll.Status is not HelixPollStatus.Active)
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
        if (host is null)
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
        Upsert(
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
        if (status is not HelixPollStatus.Active)
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
            "Reconnect the selected broadcaster with Twitch operations permissions."
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
                "Reconnect broadcaster for Twitch operations",
                "Twitch operations needs the selected broadcaster to reconnect and approve all requested permissions.",
                "/twitch-operations"
            )
            .ExecuteAsync(ct);
    }

    private static TwitchPoll Upsert(
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
        if (record is null)
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
        record.Status = poll.Status switch
        {
            HelixPollStatus.Active => TwitchPollStatus.Active,
            HelixPollStatus.Completed => TwitchPollStatus.Completed,
            HelixPollStatus.Terminated => TwitchPollStatus.Terminated,
            _ => TwitchPollStatus.Archived,
        };
        record.StartedAtUtc = poll.StartedAt.UtcDateTime;
        record.EndsAtUtc = poll.EndsAt?.UtcDateTime;
        record.EndedAtUtc = record.Status is TwitchPollStatus.Active ? null : DateTime.UtcNow;
        record.UpdatedAtUtc = DateTime.UtcNow;
        return record;
    }

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
