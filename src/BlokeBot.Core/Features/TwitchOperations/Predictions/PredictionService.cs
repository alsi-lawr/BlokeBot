using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public sealed class PredictionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBroadcasterTokenStatusProvider broadcasters,
    HelixClient helix,
    BotSettings settings,
    EventBus<AppEventKind> events,
    DurableAlertService alerts
) : IPredictionEventObserver
{
    private const int ResultsToKeep = 100;
    private const string NotReadyMessage =
        "Reconnect the selected broadcaster with Twitch operations permissions.";

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
            .Take(ResultsToKeep)
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
            return new PredictionOperationOutcome.InvalidTemplate(invalid.Message);
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

    public async Task<PredictionOperationOutcome> StartAsync(
        int hostId,
        int templateId,
        CancellationToken ct
    )
    {
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
            return new PredictionOperationOutcome.NotReady(NotReadyMessage);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        var template = await db
            .TwitchPredictionTemplates.Include(x => x.Outcomes)
            .SingleOrDefaultAsync(x => x.Id == templateId && x.HostId == hostId, ct);
        if (host?.TwitchUserId is not { Length: > 0 } || template is null)
            return new PredictionOperationOutcome.TemplateNotFound();
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
            return new PredictionOperationOutcome.ActivePredictionExists();
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
            return new PredictionOperationOutcome.ActivePredictionExists();
        if (provider is not HelixPredictionCreateOutcome.Created created)
            return new PredictionOperationOutcome.ProviderRejected(
                "Twitch did not permit creating this prediction."
            );
        var prediction = Upsert(db, hostId, created.Prediction, false).Prediction;
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
        return new PredictionOperationOutcome.Started(View(prediction));
    }

    public Task<PredictionOperationOutcome> LockAsync(
        int hostId,
        bool confirmed,
        CancellationToken ct
    ) => EndAsync(hostId, HelixPredictionEndStatus.Locked, null, confirmed, ct);

    public Task<PredictionOperationOutcome> CancelAsync(
        int hostId,
        bool confirmed,
        CancellationToken ct
    ) => EndAsync(hostId, HelixPredictionEndStatus.Canceled, null, confirmed, ct);

    public Task<PredictionOperationOutcome> ResolveAsync(
        int hostId,
        string winningOutcomeId,
        bool confirmed,
        CancellationToken ct
    ) => EndAsync(hostId, HelixPredictionEndStatus.Resolved, winningOutcomeId, confirmed, ct);

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
            return new PredictionOperationOutcome.NotReady(NotReadyMessage);
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
            return new PredictionOperationOutcome.ProviderRejected(
                "There is no active prediction."
            );
        if (!confirmed)
            return new PredictionOperationOutcome.ConfirmationRequired();
        var outcomes =
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(active.OutcomesJson) ?? [];
        if (status is HelixPredictionEndStatus.Resolved && !outcomes.Any(x => x.Id == outcomeId))
            return new PredictionOperationOutcome.InvalidOutcome();
        var provider = await helix.EndPredictionAsync(
            new(settings.Identity.ClientId, token),
            host.TwitchUserId,
            active.ProviderPredictionId,
            status,
            outcomeId,
            ct
        );
        if (provider is null)
            return new PredictionOperationOutcome.ProviderRejected(
                "Twitch did not permit updating this prediction."
            );
        var prediction = Upsert(db, hostId, provider, active.IsExternallyStarted).Prediction;
        if (Terminal(prediction.Status))
            await TrimAsync(db, hostId, ct);
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
        return new PredictionOperationOutcome.Updated(View(prediction));
    }

    public async Task ReconcileChannelAsync(string channel, CancellationToken ct)
    {
        var login = Login.Normalize(channel);
        if (login.Length == 0)
            return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await db
            .Hosts.Where(x => x.Login == login)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is { } id)
            await ReconcileAsync(id, ct);
    }

    public async Task ReconcileAsync(int hostId, CancellationToken ct)
    {
        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
            return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host?.TwitchUserId is not { Length: > 0 } id)
            return;
        var provider = await helix.GetLatestPredictionAsync(
            new(settings.Identity.ClientId, token),
            id,
            ct
        );
        var changed = provider switch
        {
            HelixPredictionLookupOutcome.Found found => Upsert(
                db,
                hostId,
                found.Prediction,
                true
            ).Changed,
            HelixPredictionLookupOutcome.NoPrediction => ArchiveMissingActive(db, hostId),
            HelixPredictionLookupOutcome.Unavailable => false,
            _ => false,
        };
        if (!changed)
            return;
        if (provider is HelixPredictionLookupOutcome.NoPrediction)
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
        if (host is null)
            return;
        var upsert = Upsert(db, host.Id, prediction.ToHelix(), true);
        if (!upsert.Changed)
            return;
        if (Terminal(upsert.Prediction.Status))
            await TrimAsync(db, host.Id, ct);
        await db.SaveChangesAsync(ct);
        await ChangedAsync(ct);
    }

    private async Task<PredictionAuthorizationReadiness> ReadinessAsync(
        int hostId,
        CancellationToken ct
    ) =>
        await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            ct
        ) is TokenStatus.Ready
            ? new PredictionAuthorizationReadiness.Ready()
            : await NeedsAuthorizationAsync(hostId, ct);

    private async Task<string?> ReadyTokenAsync(int hostId, CancellationToken ct) =>
        await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            ct
        )
            is TokenStatus.Ready ready
            ? ready.AccessToken
            : await MissingTokenAsync(hostId, ct);

    private async Task<PredictionAuthorizationReadiness> NeedsAuthorizationAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await AlertAsync(hostId, ct);
        return new PredictionAuthorizationReadiness.NeedsBroadcasterAuthorization(NotReadyMessage);
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
            return false;
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
        var status = ToPersisted(prediction.Status);
        if (record is not null && Terminal(record.Status))
            return new(record, false);
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
        if (
            record.Status == status
            && record.OutcomesJson == JsonSerializer.Serialize(prediction.Outcomes)
        )
            return new(record, false);
        record.Title = prediction.Title;
        record.OutcomesJson = JsonSerializer.Serialize(
            prediction
                .Outcomes.Select(x => new PredictionOutcomeView(
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
                .ToArray()
        );
        record.Status = status;
        record.CreatedAtUtc = prediction.CreatedAt.UtcDateTime;
        record.LocksAtUtc = prediction.LocksAt?.UtcDateTime;
        record.EndedAtUtc = Terminal(status)
            ? prediction.EndedAt?.UtcDateTime ?? DateTime.UtcNow
            : null;
        record.UpdatedAtUtc = DateTime.UtcNow;
        return new(record, true);
    }

    private static TwitchPredictionStatus ToPersisted(HelixPredictionStatus value) =>
        value switch
        {
            HelixPredictionStatus.Active => TwitchPredictionStatus.Active,
            HelixPredictionStatus.Locked => TwitchPredictionStatus.Locked,
            HelixPredictionStatus.Resolved => TwitchPredictionStatus.Resolved,
            HelixPredictionStatus.Canceled => TwitchPredictionStatus.Canceled,
            _ => TwitchPredictionStatus.Archived,
        };

    private static bool Terminal(TwitchPredictionStatus value) =>
        value is not TwitchPredictionStatus.Active and not TwitchPredictionStatus.Locked;

    private static async Task TrimAsync(BlokeBotDbContext db, int hostId, CancellationToken ct)
    {
        var excess = await db
            .TwitchPredictions.Where(x =>
                x.HostId == hostId
                && x.Status != TwitchPredictionStatus.Active
                && x.Status != TwitchPredictionStatus.Locked
            )
            .OrderByDescending(x => x.EndedAtUtc)
            .Skip(ResultsToKeep)
            .ToArrayAsync(ct);
        db.TwitchPredictions.RemoveRange(excess);
    }

    private static PredictionTemplateView View(TwitchPredictionTemplate template) =>
        new(
            template.Id,
            template.Title,
            template.Outcomes.OrderBy(x => x.Position).Select(x => x.Title).ToArray(),
            template.PredictionWindowSeconds
        );

    private static PredictionView View(TwitchPrediction value) =>
        new(
            value.ProviderPredictionId,
            value.Title,
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(value.OutcomesJson) ?? [],
            value.Status.ToString(),
            value.IsExternallyStarted,
            value.CreatedAtUtc,
            value.LocksAtUtc,
            value.EndedAtUtc
        );

    private sealed record PredictionUpsertOutcome(TwitchPrediction Prediction, bool Changed);
}
