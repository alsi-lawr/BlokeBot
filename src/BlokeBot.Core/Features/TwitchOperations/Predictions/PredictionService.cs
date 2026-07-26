using System.Collections.Concurrent;
using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public sealed class PredictionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBroadcasterTokenStatusProvider broadcasters,
    HelixClient helix,
    BotSettings settings,
    EventBus<AppEventKind> events,
    DurableAlertService alerts,
    ILogger<PredictionService> logger
) : IPredictionEventObserver
{
    private const int _resultsToKeep = 100;
    private const string _notReadyMessage =
        "Reconnect the selected broadcaster with Twitch operations permissions.";
    private const string _ineligibleMessage =
        "Twitch Predictions are available only to Affiliate or Partner broadcasters.";
    private readonly ConcurrentDictionary<int, PendingProgress> _pendingProgress = new();

    public async Task<PredictionDashboardState> LoadAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var readiness = await ReadinessAsync(hostId, ct);
        var active = await db
            .TwitchPredictions.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && (
                    x.Status == TwitchPredictionStatus.Active
                    || x.Status == TwitchPredictionStatus.Locked
                )
            )
            .SingleOrDefaultAsync(ct);
        var templates = await db
            .TwitchPredictionTemplates.AsNoTracking()
            .Include(x => x.Outcomes)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Id)
            .ToArrayAsync(ct);
        var results = await db
            .TwitchPredictions.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && x.Status != TwitchPredictionStatus.Active
                && x.Status != TwitchPredictionStatus.Locked
            )
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

    public async Task<PredictionOperationOutcome> SaveTemplateAsync(
        int hostId,
        PredictionTemplateDraft draft,
        CancellationToken ct
    )
    {
        if (draft.Validate() is PredictionTemplateValidationOutcome.Invalid invalid)
        {
            return new PredictionOperationOutcome.InvalidTemplate(invalid.Message);
        }
        var valid = ((PredictionTemplateValidationOutcome.Valid)draft.Validate()).Draft;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var template = new TwitchPredictionTemplate
        {
            HostId = hostId,
            Title = valid.Title,
            PredictionWindowSeconds = valid.PredictionWindowSeconds,
            CreatedAtUtc = DateTime.UtcNow,
            Outcomes = valid
                .Outcomes.Select(
                    (title, position) =>
                        new TwitchPredictionTemplateOutcome { Title = title, Position = position }
                )
                .ToArray(),
        };
        db.TwitchPredictionTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
        return new PredictionOperationOutcome.TemplateSaved(View(template));
    }

    public async Task<PredictionOperationOutcome> DeleteTemplateAsync(
        int hostId,
        int templateId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var template = await db.TwitchPredictionTemplates.SingleOrDefaultAsync(
            x => x.Id == templateId && x.HostId == hostId,
            ct
        );
        if (template is null)
        {
            return new PredictionOperationOutcome.TemplateNotFound();
        }
        db.TwitchPredictionTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
        return new PredictionOperationOutcome.TemplateDeleted();
    }

    public async Task<PredictionOperationOutcome> StartAsync(
        int hostId,
        int templateId,
        CancellationToken ct
    )
    {
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        if (
            await EligibilityAsync(token, ct)
            is { } eligibility
                and not HelixPredictionEligibilityOutcome.Eligible
        )
        {
            return EligibilityOutcome(eligibility);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        var template = await db
            .TwitchPredictionTemplates.Include(x => x.Outcomes)
            .SingleOrDefaultAsync(x => x.Id == templateId && x.HostId == hostId, ct);
        if (host?.TwitchUserId is not { Length: > 0 } || template is null)
        {
            return new PredictionOperationOutcome.TemplateNotFound();
        }
        if (
            await db.TwitchPredictions.AnyAsync(
                x =>
                    x.HostId == hostId
                    && (
                        x.Status == TwitchPredictionStatus.Active
                        || x.Status == TwitchPredictionStatus.Locked
                    ),
                ct
            )
        )
        {
            return new PredictionOperationOutcome.ActivePredictionExists();
        }
        var provider = await helix.CreatePredictionAsync(
            new(settings.Identity.ClientId, token),
            host.TwitchUserId,
            new(
                template.Title,
                template.Outcomes.OrderBy(x => x.Position).Select(x => x.Title).ToArray(),
                template.PredictionWindowSeconds
            ),
            ct
        );
        if (provider is HelixPredictionCreateOutcome.ActivePredictionExists)
        {
            await ReconcileAsync(hostId, ct);
            return new PredictionOperationOutcome.ActivePredictionExists();
        }
        if (provider is HelixPredictionCreateOutcome.Unauthorized)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        if (provider is HelixPredictionCreateOutcome.Ineligible)
        {
            return new PredictionOperationOutcome.Ineligible(_ineligibleMessage);
        }
        if (provider is HelixPredictionCreateOutcome.InvalidRequest)
        {
            return new PredictionOperationOutcome.ProviderRejected(
                "Twitch rejected this prediction request."
            );
        }
        if (provider is not HelixPredictionCreateOutcome.Created created)
        {
            return new PredictionOperationOutcome.Unavailable(
                "Twitch is temporarily unavailable; the prediction was not started locally."
            );
        }
        var prediction = Upsert(db, hostId, created.Prediction, false).Prediction;
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
        return new PredictionOperationOutcome.Started(View(prediction));
    }

    public Task<PredictionOperationOutcome> LockAsync(
        int hostId,
        bool confirmed,
        CancellationToken ct
    )
    {
        return EndAsync(hostId, HelixPredictionEndStatus.Locked, null, confirmed, ct);
    }

    public Task<PredictionOperationOutcome> CancelAsync(
        int hostId,
        bool confirmed,
        CancellationToken ct
    )
    {
        return EndAsync(hostId, HelixPredictionEndStatus.Canceled, null, confirmed, ct);
    }

    public Task<PredictionOperationOutcome> ResolveAsync(
        int hostId,
        string winningOutcomeId,
        bool confirmed,
        CancellationToken ct
    )
    {
        return EndAsync(hostId, HelixPredictionEndStatus.Resolved, winningOutcomeId, confirmed, ct);
    }

    private async Task<PredictionOperationOutcome> EndAsync(
        int hostId,
        HelixPredictionEndStatus status,
        string? outcomeId,
        bool confirmed,
        CancellationToken ct
    )
    {
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        var active = await db.TwitchPredictions.SingleOrDefaultAsync(
            x =>
                x.HostId == hostId
                && (
                    x.Status == TwitchPredictionStatus.Active
                    || x.Status == TwitchPredictionStatus.Locked
                ),
            ct
        );
        if (host?.TwitchUserId is not { Length: > 0 } || active is null)
        {
            return new PredictionOperationOutcome.ProviderRejected(
                "There is no active prediction."
            );
        }
        if (!confirmed)
        {
            return new PredictionOperationOutcome.ConfirmationRequired();
        }
        var outcomes =
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(active.OutcomesJson) ?? [];
        if (status is HelixPredictionEndStatus.Resolved && !outcomes.Any(x => x.Id == outcomeId))
        {
            return new PredictionOperationOutcome.InvalidOutcome();
        }
        var provider = await helix.EndPredictionAsync(
            new(settings.Identity.ClientId, token),
            host.TwitchUserId,
            active.ProviderPredictionId,
            status,
            outcomeId,
            ct
        );
        if (provider is HelixPredictionEndOutcome.Unauthorized)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        if (provider is HelixPredictionEndOutcome.Ineligible)
        {
            return new PredictionOperationOutcome.Ineligible(_ineligibleMessage);
        }
        if (provider is HelixPredictionEndOutcome.InvalidRequest)
        {
            return new PredictionOperationOutcome.ProviderRejected(
                "Twitch did not permit that prediction transition."
            );
        }
        if (provider is not HelixPredictionEndOutcome.Updated updated)
        {
            return new PredictionOperationOutcome.Unavailable(
                "Twitch is temporarily unavailable; the prediction was not changed locally."
            );
        }
        var prediction = Upsert(
            db,
            hostId,
            updated.Prediction,
            active.IsExternallyStarted
        ).Prediction;
        if (Terminal(prediction.Status))
        {
            await TrimAsync(db, hostId, ct);
        }
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
        return new PredictionOperationOutcome.Updated(View(prediction));
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
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host?.TwitchUserId is not { Length: > 0 } id)
        {
            return;
        }
        var provider = await helix.GetLatestPredictionAsync(
            new(settings.Identity.ClientId, token),
            id,
            ct
        );
        var changed = false;
        if (provider is HelixPredictionLookupOutcome.Found found)
        {
            foreach (var prediction in found.Predictions)
            {
                changed |= Upsert(db, hostId, prediction, true).Changed;
            }
            if (
                !found.Predictions.Any(x =>
                    x.Status is HelixPredictionStatus.Active or HelixPredictionStatus.Locked
                )
            )
            {
                changed |= ArchiveMissingActive(db, hostId);
            }
        }
        else if (provider is HelixPredictionLookupOutcome.NoPrediction)
        {
            changed = ArchiveMissingActive(db, hostId);
        }
        if (!changed)
        {
            return;
        }
        await TrimAsync(db, hostId, ct);
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
    }

    public async Task PredictionReceivedAsync(
        EventSubPredictionEvent prediction,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(
            x =>
                x.TwitchUserId == prediction.BroadcasterUserId
                || x.Login == Login.Normalize(prediction.BroadcasterUserLogin),
            ct
        );
        if (host is null || prediction.ToHelix().Status is HelixPredictionStatus.Unknown)
        {
            return;
        }
        if (prediction.ToHelix().Status is HelixPredictionStatus.Active)
        {
            QueueProgress(host.Id, prediction.ToHelix());
            return;
        }
        _pendingProgress.TryRemove(host.Id, out _);
        var upsert = Upsert(db, host.Id, prediction.ToHelix(), true);
        if (!upsert.Changed)
        {
            return;
        }
        if (Terminal(upsert.Prediction.Status))
        {
            await TrimAsync(db, host.Id, ct);
        }
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
    }

    private void QueueProgress(int hostId, HelixPrediction prediction)
    {
        while (true)
        {
            if (_pendingProgress.TryGetValue(hostId, out var current))
            {
                var merged = current with
                {
                    Prediction = MergeProgress(current.Prediction, prediction),
                };
                if (_pendingProgress.TryUpdate(hostId, merged, current))
                {
                    return;
                }
                continue;
            }
            if (_pendingProgress.TryAdd(hostId, new PendingProgress(prediction)))
            {
                _ = FlushProgressAsync(hostId);
                return;
            }
        }
    }

    private async Task FlushProgressAsync(int hostId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            if (!_pendingProgress.TryRemove(hostId, out var pending))
            {
                return;
            }
            await using var db = await dbFactory.CreateDbContextAsync();
            var upsert = Upsert(db, hostId, pending.Prediction, true);
            if (!upsert.Changed)
            {
                return;
            }
            await db.SaveChangesAsync();
            await ChangedAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Prediction progress debounce failed for host {HostId}.",
                hostId
            );
        }
    }

    private async Task<PredictionAuthorizationReadiness> ReadinessAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (await ReadyTokenAsync(hostId, ct) is not { } token)
        {
            return new PredictionAuthorizationReadiness.NeedsBroadcasterAuthorization(
                _notReadyMessage
            );
        }
        return await EligibilityAsync(token, ct) switch
        {
            HelixPredictionEligibilityOutcome.Eligible =>
                new PredictionAuthorizationReadiness.Ready(),
            HelixPredictionEligibilityOutcome.Ineligible =>
                new PredictionAuthorizationReadiness.Ineligible(_ineligibleMessage),
            HelixPredictionEligibilityOutcome.Unauthorized =>
                new PredictionAuthorizationReadiness.NeedsBroadcasterAuthorization(
                    _notReadyMessage
                ),
            _ => new PredictionAuthorizationReadiness.Unavailable(
                "Twitch eligibility could not be checked right now."
            ),
        };
    }

    private async Task<HelixPredictionEligibilityOutcome> EligibilityAsync(
        string token,
        CancellationToken ct
    )
    {
        return await helix.GetPredictionEligibilityAsync(
            new(settings.Identity.ClientId, token),
            ct
        );
    }

    private static PredictionOperationOutcome EligibilityOutcome(
        HelixPredictionEligibilityOutcome outcome
    )
    {
        return outcome switch
        {
            HelixPredictionEligibilityOutcome.Ineligible =>
                new PredictionOperationOutcome.Ineligible(_ineligibleMessage),
            HelixPredictionEligibilityOutcome.Unauthorized =>
                new PredictionOperationOutcome.NotReady(_notReadyMessage),
            _ => new PredictionOperationOutcome.Unavailable(
                "Twitch eligibility could not be checked right now."
            ),
        };
    }

    private async Task<string?> ReadyTokenAsync(int hostId, CancellationToken ct)
    {
        return
            await broadcasters.GetTokenStatusAsync(
                hostId,
                HostBroadcasterAuthorizationService.MilestoneScopes,
                ct
            )
                is TokenStatus.Ready ready
            ? ready.AccessToken
            : await MissingTokenAsync(hostId, ct);
    }

    private async Task<string?> MissingTokenAsync(int hostId, CancellationToken ct)
    {
        await AlertAsync(hostId, ct);
        return null;
    }

    private async Task AlertAsync(int hostId, CancellationToken ct)
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

    private async Task ChangedAsync(CancellationToken ct)
    {
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    private static bool ArchiveMissingActive(BlokeBotDbContext db, int hostId)
    {
        var active = db.TwitchPredictions.SingleOrDefault(x =>
            x.HostId == hostId
            && (
                x.Status == TwitchPredictionStatus.Active
                || x.Status == TwitchPredictionStatus.Locked
            )
        );
        if (active is null)
        {
            return false;
        }
        active.Status = TwitchPredictionStatus.Archived;
        active.EndedAtUtc = active.UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    private static PredictionUpsertOutcome Upsert(
        BlokeBotDbContext db,
        int hostId,
        HelixPrediction prediction,
        bool external
    )
    {
        var record =
            db.TwitchPredictions.Local.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderPredictionId == prediction.Id
            )
            ?? db.TwitchPredictions.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderPredictionId == prediction.Id
            );
        if (prediction.Status is HelixPredictionStatus.Unknown)
        {
            return record is null
                ? new PredictionUpsertOutcome(new TwitchPrediction(), false)
                : new PredictionUpsertOutcome(record, false);
        }
        var status = ToPersisted(prediction.Status);
        if (
            record is not null
            && (Terminal(record.Status) || StateRank(status) < StateRank(record.Status))
        )
        {
            return new(record, false);
        }
        if (record is null)
        {
            record = new TwitchPrediction
            {
                HostId = hostId,
                ProviderPredictionId = prediction.Id,
                IsExternallyStarted = external,
            };
            db.TwitchPredictions.Add(record);
        }
        var outcomes = ToProjection(prediction.Outcomes);
        var outcomesJson = JsonSerializer.Serialize(outcomes);
        var previous =
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(record.OutcomesJson) ?? [];
        if (record.Status == status && record.OutcomesJson == outcomesJson)
        {
            return new(record, false);
        }
        if (
            status is TwitchPredictionStatus.Active
            && HasParticipationRegression(previous, outcomes)
        )
        {
            return new(record, false);
        }
        record.Title = prediction.Title;
        record.OutcomesJson = outcomesJson;
        record.Status = status;
        record.CreatedAtUtc = prediction.CreatedAt.UtcDateTime;
        record.LocksAtUtc = prediction.LocksAt?.UtcDateTime;
        record.EndedAtUtc = Terminal(status)
            ? prediction.EndedAt?.UtcDateTime ?? DateTime.UtcNow
            : null;
        record.UpdatedAtUtc = DateTime.UtcNow;
        return new(record, true);
    }

    private static HelixPrediction MergeProgress(HelixPrediction current, HelixPrediction incoming)
    {
        if (
            current.Status is not HelixPredictionStatus.Active
            || incoming.Status is not HelixPredictionStatus.Active
        )
        {
            return incoming;
        }
        var outcomes = current
            .Outcomes.Select(old =>
                incoming.Outcomes.FirstOrDefault(next => next.Id == old.Id) is { } next
                && next.Users >= old.Users
                && next.ChannelPoints >= old.ChannelPoints
                    ? next
                    : old
            )
            .ToArray();
        return incoming with { Outcomes = outcomes };
    }

    private static PredictionOutcomeView[] ToProjection(
        IReadOnlyList<HelixPredictionOutcome> outcomes
    )
    {
        return outcomes
            .Select(x => new PredictionOutcomeView(
                x.Id,
                x.Title,
                x.Color,
                x.Users,
                x.ChannelPoints,
                x.TopPredictors.Select(p => new PredictionTopPredictorView(
                        p.UserLogin,
                        p.UserName,
                        p.ChannelPointsUsed,
                        p.ChannelPointsWon
                    ))
                    .ToArray()
            ))
            .ToArray();
    }

    private static bool HasParticipationRegression(
        IReadOnlyList<PredictionOutcomeView> previous,
        IReadOnlyList<PredictionOutcomeView> current
    )
    {
        return previous.Any(old =>
            current.FirstOrDefault(next => next.Id == old.Id) is not { } next
            || next.Users < old.Users
            || next.ChannelPoints < old.ChannelPoints
        );
    }

    private static TwitchPredictionStatus ToPersisted(HelixPredictionStatus value)
    {
        return value switch
        {
            HelixPredictionStatus.Active => TwitchPredictionStatus.Active,
            HelixPredictionStatus.Locked => TwitchPredictionStatus.Locked,
            HelixPredictionStatus.Resolved => TwitchPredictionStatus.Resolved,
            HelixPredictionStatus.Canceled => TwitchPredictionStatus.Canceled,
            _ => TwitchPredictionStatus.Archived,
        };
    }

    private static int StateRank(TwitchPredictionStatus value)
    {
        return value switch
        {
            TwitchPredictionStatus.Active => 1,
            TwitchPredictionStatus.Locked => 2,
            TwitchPredictionStatus.Resolved or TwitchPredictionStatus.Canceled => 3,
            _ => 4,
        };
    }

    private static bool Terminal(TwitchPredictionStatus value)
    {
        return value is not TwitchPredictionStatus.Active and not TwitchPredictionStatus.Locked;
    }

    private static async Task TrimAsync(BlokeBotDbContext db, int hostId, CancellationToken ct)
    {
        var excess = await db
            .TwitchPredictions.Where(x =>
                x.HostId == hostId
                && x.Status != TwitchPredictionStatus.Active
                && x.Status != TwitchPredictionStatus.Locked
            )
            .OrderByDescending(x => x.EndedAtUtc)
            .Skip(_resultsToKeep)
            .ToArrayAsync(ct);
        db.TwitchPredictions.RemoveRange(excess);
    }

    private static PredictionTemplateView View(TwitchPredictionTemplate template)
    {
        return new(
            template.Id,
            template.Title,
            template.Outcomes.OrderBy(x => x.Position).Select(x => x.Title).ToArray(),
            template.PredictionWindowSeconds
        );
    }

    private static PredictionView View(TwitchPrediction value)
    {
        return new(
            value.ProviderPredictionId,
            value.Title,
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(value.OutcomesJson) ?? [],
            value.Status.ToString(),
            value.IsExternallyStarted,
            value.CreatedAtUtc,
            value.LocksAtUtc,
            value.EndedAtUtc
        );
    }

    private sealed record PredictionUpsertOutcome(TwitchPrediction Prediction, bool Changed);

    private sealed record PendingProgress(HelixPrediction Prediction);
}
