using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class ExpandedAutomationRuntime
{
    public async Task StreamEndedAsync(
        EventSubStreamOfflineEvent stream,
        CancellationToken cancellation
    )
    {
        if (string.IsNullOrWhiteSpace(stream.BroadcasterUserId))
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var hostId = await db
            .Hosts.Where(h => h.TwitchUserId == stream.BroadcasterUserId)
            .Select(h => (int?)h.Id)
            .SingleOrDefaultAsync(cancellation);
        var observedConnection = hostId is { } observedHost
            ? ConnectionId(observedHost)
            : Guid.Empty;
        // An older offline delivery must not erase a subsequently observed stream.
        var deleted = await db
            .AutomationStreamObservations.Where(s =>
                s.HostId == hostId && s.StartedAtUtc <= stream.MessageTimestamp.UtcDateTime
            )
            .ExecuteDeleteAsync(cancellation);
        if (deleted > 0 && hostId is { } id)
        {
            ObserveStreamReadiness(
                id,
                observedConnection,
                "The current stream ended. Waiting for a new live stream observation."
            );
            var host = await db.Hosts.AsNoTracking().SingleAsync(h => h.Id == id, cancellation);
            await PublishObservationReadinessAsync(host);
        }
    }

    private async Task UptimeAsync(
        BotHost host,
        ImmutableArray<AutomationConfiguration> sources,
        Guid observedConnection,
        CancellationToken ct
    )
    {
        var liveness = await LiveAsync(host, ct);
        if (liveness is HostStreamLivenessOutcome.Offline)
        {
            ObserveStreamReadiness(
                host.Id,
                observedConnection,
                "The channel is confirmed offline. Uptime and live-only occurrences are skipped, not deferred."
            );
            await using var gone = await dbFactory.CreateDbContextAsync(ct);
            _ = await gone
                .AutomationStreamObservations.Where(s => s.HostId == host.Id)
                .ExecuteDeleteAsync(ct);
            return;
        }
        if (liveness is not HostStreamLivenessOutcome.Live live)
        {
            ObserveStreamReadiness(
                host.Id,
                observedConnection,
                "Twitch live status is unavailable. No offline status or uptime trigger is inferred; retry when the provider connection recovers."
            );
            return;
        }
        var observation = await ObserveStreamAsync(host, live, ct);
        if (observation is null)
        {
            ObserveStreamReadiness(
                host.Id,
                observedConnection,
                "The current Twitch stream could not be confirmed. Try again."
            );
            return;
        }
        ObserveStreamReadiness(host.Id, observedConnection, null);
        var now = clock.GetUtcNow();
        foreach (var uptime in sources.OfType<UptimeSourceConfiguration>())
        {
            var elapsed = now - live.StartedAtUtc;
            if (elapsed < uptime.Duration)
            {
                continue;
            }
            var due =
                uptime.Kind == UptimeKind.Threshold
                    ? live.StartedAtUtc + uptime.Duration
                    : live.StartedAtUtc.AddTicks(
                        elapsed.Ticks / uptime.Duration.Ticks * uptime.Duration.Ticks
                    );
            if (observation.SuppressUptimeBeforeUtc is { } suppress && due.UtcDateTime <= suppress)
            {
                continue;
            }
            if (
                uptime.Kind == UptimeKind.Interval
                && (
                    now - due > TimeSpan.FromSeconds(2)
                    || observation.ObservedAtUtc >= due.UtcDateTime
                )
            )
            {
                continue;
            }
            var context = Create(
                host,
                AutomationDefinitionIds.UptimeSource,
                $"{live.StreamId}:{uptime.Kind}:{uptime.Duration.Ticks}:{due.UtcTicks}",
                due,
                now,
                [
                    Text("stream-id", live.StreamId, true),
                    Number("uptime-seconds", (decimal)elapsed.TotalSeconds),
                ],
                stream: new(live.StreamId, null, null, live.StartedAtUtc)
            );
            _ = await runtime.DispatchExpandedAsync(
                context,
                c => c == uptime,
                ct,
                async (current, token) =>
                    await current.AutomationStreamObservations.AnyAsync(
                        s => s.HostId == host.Id && s.StreamId == live.StreamId,
                        token
                    )
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        _ = await db
            .AutomationStreamObservations.Where(s =>
                s.HostId == host.Id && s.StreamId == live.StreamId
            )
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ObservedAtUtc, now.UtcDateTime), ct);
    }

    private async Task<AutomationStreamObservation?> ObserveStreamAsync(
        BotHost host,
        HostStreamLivenessOutcome.Live live,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        _ = await MainDatabaseStatements.LockHostAsync(db, host.Id, ct);
        if (
            !await db.Hosts.AnyAsync(
                h => h.Id == host.Id && (h.EnabledFeatures & HostFeatureFlags.Automations) != 0,
                ct
            )
        )
        {
            return null;
        }
        var observation = await db.AutomationStreamObservations.SingleOrDefaultAsync(
            s => s.HostId == host.Id,
            ct
        );
        if (observation is null)
        {
            observation = new() { HostId = host.Id };
            _ = db.AutomationStreamObservations.Add(observation);
        }
        if (observation.StreamId != live.StreamId)
        {
            if (
                string.IsNullOrWhiteSpace(live.StreamId)
                || observation.StartedAtUtc >= live.StartedAtUtc.UtcDateTime
            )
            {
                return null;
            }
            _ = await db
                .AutomationSeenViewers.Where(v => v.HostId == host.Id)
                .ExecuteDeleteAsync(ct);
            observation.StreamId = live.StreamId;
            observation.StartedAtUtc = live.StartedAtUtc.UtcDateTime;
            observation.ObservedAtUtc = clock.GetUtcNow().UtcDateTime;
            observation.SuppressUptimeBeforeUtc = null;
        }
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return observation;
    }

    public async Task ChatReceivedAsync(
        EventSubObservedChatEvent message,
        CancellationToken cancellation
    )
    {
        if (
            message.SourceBroadcasterId != message.BroadcasterId
            || string.IsNullOrWhiteSpace(message.ViewerId)
            || string.IsNullOrWhiteSpace(message.MessageId)
        )
        {
            return;
        }
        var host = await HostAsync(message.BroadcasterId, HostFeatureFlags.None, cancellation);
        if (host is null || !await AcceptEventAsync(host.Id, message.Timestamp, cancellation))
        {
            return;
        }
        var sources = await SourcesAsync(host, cancellation);
        if (!sources.OfType<ChatMatchSourceConfiguration>().Any())
        {
            return;
        }
        var account = (
            await accounts.GetBotAccount(host.Login).ExecuteAsync(cancellation)
        ).Match<BotAccount?>(a => a, _ => null);
        if (
            account is null
            || string.Equals(account.Login, message.ViewerLogin, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }
        var observedConnection = CaptureStreamConnection(host);
        if (
            sources
                .OfType<ChatMatchSourceConfiguration>()
                .Any(s => s.Kind == ChatMatchKind.FirstObserved)
            && await LiveAsync(host, cancellation) is HostStreamLivenessOutcome.Live live
        )
        {
            var observation = await ObserveStreamAsync(host, live, cancellation);
            if (observation is not null)
            {
                ObserveStreamReadiness(host.Id, observedConnection, null);
            }
            await runtime.DispatchObservedAsync(
                new(host.Id),
                async (db, ct) =>
                {
                    if (
                        !await db.Hosts.AnyAsync(
                            h =>
                                h.Id == host.Id
                                && (h.EnabledFeatures & HostFeatureFlags.Automations) != 0,
                            ct
                        )
                        || await db.AutomationSourceAdmissions.AnyAsync(
                            source =>
                                source.HostId == host.Id
                                && source.AcceptEventsAfterUtc >= message.Timestamp.UtcDateTime,
                            ct
                        )
                    )
                    {
                        return [];
                    }
                    var first = false;
                    if (
                        await db.AutomationStreamObservations.AnyAsync(
                            stream => stream.HostId == host.Id && stream.StreamId == live.StreamId,
                            ct
                        )
                        && !await db.AutomationSeenViewers.AnyAsync(
                            viewer =>
                                viewer.HostId == host.Id && viewer.ViewerId == message.ViewerId,
                            ct
                        )
                    )
                    {
                        _ = db.AutomationSeenViewers.Add(
                            new() { HostId = host.Id, ViewerId = message.ViewerId }
                        );
                        _ = await db.SaveChangesAsync(ct);
                        first = true;
                    }
                    return [Trigger(first, live.StreamId)];
                },
                cancellation
            );
            await PublishObservationReadinessAsync(host);
            return;
        }
        var trigger = Trigger(false, "");
        _ = await runtime.DispatchExpandedAsync(trigger.Context, trigger.Matches, cancellation);

        AutomationObservedTrigger Trigger(bool first, string streamId) =>
            new(
                Create(
                    host,
                    AutomationDefinitionIds.ChatMatchSource,
                    message.MessageId,
                    message.Timestamp,
                    clock.GetUtcNow(),
                    [
                        Text("message-text", message.Text, true),
                        Text("message-id", message.MessageId, true),
                        Text("stream-id", streamId, true),
                    ],
                    new(message.ViewerId, message.ViewerLogin, message.ViewerName)
                ),
                c =>
                    c is ChatMatchSourceConfiguration match
                    && match.Kind switch
                    {
                        ChatMatchKind.Keyword => KeywordMatches(message.Text, match.Match),
                        ChatMatchKind.Phrase => message.Text.Contains(
                            match.Match,
                            StringComparison.OrdinalIgnoreCase
                        ),
                        ChatMatchKind.Emote => message.EmoteIds.Contains(match.Match),
                        ChatMatchKind.FirstObserved => first,
                    }
            );
    }

    internal static bool KeywordMatches(string text, string keyword)
    {
        if (keyword.Length == 0)
        {
            return false;
        }
        var from = 0;
        while (from <= text.Length - keyword.Length)
        {
            var at = text.IndexOf(keyword, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return false;
            }
            var end = at + keyword.Length;
            if ((at == 0 || !Word(text[at - 1])) && (end == text.Length || !Word(text[end])))
            {
                return true;
            }
            from = at + 1;
        }
        return false;
    }

    private static bool Word(char value) =>
        char.IsLetterOrDigit(value)
        || value == '_'
        || char.GetUnicodeCategory(value)
            is System.Globalization.UnicodeCategory.NonSpacingMark
                or System.Globalization.UnicodeCategory.SpacingCombiningMark;

    public async Task MetadataChangedAsync(
        EventSubChannelUpdateEvent metadata,
        CancellationToken cancellation
    )
    {
        var host = await HostAsync(metadata.BroadcasterUserId, HostFeatureFlags.None, cancellation);
        if (host is null)
        {
            return;
        }
        if (!await AcceptEventAsync(host.Id, metadata.MessageTimestamp, cancellation))
        {
            return;
        }
        MetadataObservation? previous;
        lock (_observationGate)
        {
            previous = _metadata.GetValueOrDefault(host.Id);
            if (previous is not null && metadata.MessageTimestamp <= previous.At)
            {
                return;
            }
            _metadata[host.Id] = new(
                metadata.StreamTitle,
                metadata.CategoryId,
                metadata.MessageTimestamp,
                ConnectionId(host.Id)
            );
        }
        if (previous is null || previous.Connection != ConnectionId(host.Id))
        {
            return;
        }
        var title = previous.Title != metadata.StreamTitle;
        var category = previous.CategoryId != metadata.CategoryId;
        if (!title && !category)
        {
            return;
        }
        _ = await runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.MetadataSource,
                metadata.MessageId,
                metadata.MessageTimestamp,
                clock.GetUtcNow(),
                [
                    Text(
                        "changed-field",
                        title && category ? "TitleAndCategory"
                            : title ? "Title"
                            : "Category"
                    ),
                    Text("title", metadata.StreamTitle),
                    Text("category-id", metadata.CategoryId),
                    Text("category-name", metadata.CategoryName),
                ]
            ),
            c =>
                c is MetadataSourceConfiguration s
                && (
                    s.Field == MetadataField.Any
                    || (s.Field == MetadataField.Title && title)
                    || (s.Field == MetadataField.Category && category)
                )
                && (s.CategoryId is null || s.CategoryId == metadata.CategoryId),
            cancellation
        );
    }
}
