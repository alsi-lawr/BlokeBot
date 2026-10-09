using System.Data.Common;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomCommandExecutionService
{
    private async Task<CustomCommandExecutionOutcome> DeliverPreparedAsync(
        BlokeBotDbContext db,
        BotHost host,
        CustomCommand command,
        OverlayCueCustomCommandAction? cueAction,
        ChatCommandContext context,
        IReadOnlyList<string> args,
        PreparedCommandInvocation prepared,
        CancellationToken ct
    )
    {
        if (prepared.IsReplay)
        {
            if (prepared.ComputedResult!.ReplyEligible)
            {
                await context.ReplyAsync(prepared.Text!, ct);
            }
            return new CustomCommandExecutionOutcome.Handled();
        }
        var reply = prepared.Text;
        var selectedMessage = prepared.LegacyTemplate;
        var count = prepared.Count;
        var computedResult = prepared.ComputedResult;
        if (
            command.Action is CounterCustomCommandAction completedCounter
            && completedCounter.Counter is not null
            && count is { } counterValue
            && context.Message.Tags.TryGetValue("id", out var counterInvocationId)
            && !string.IsNullOrWhiteSpace(counterInvocationId)
        )
        {
            var viewer = context.Message.Tags.TryGetValue("user-id", out var counterViewerId)
                ? new BingoViewer(
                    counterViewerId,
                    context.Message.Login,
                    context.Message.Tags.GetValueOrDefault("display-name", context.Message.Login)
                )
                : null;
            foreach (var observer in _bingoCounters)
            {
                await observer.CounterChangedAsync(
                    host.Id,
                    counterInvocationId,
                    completedCounter.Counter.Id,
                    completedCounter.Counter.Name,
                    counterValue,
                    viewer,
                    clock.GetUtcNow(),
                    ct
                );
            }
        }

        if (selectedMessage is not null)
        {
            reply = (
                await templates.RenderCommandAsync(
                    selectedMessage,
                    new(host.Id, host.Login, host.TwitchUserId ?? string.Empty),
                    context,
                    args,
                    count,
                    ct
                )
            ).Match(static text => text, static failure => failure.ChatMessage());
        }

        if (
            cueAction is not null
            && reply is not null
            && cueAction.ReplyOrder == OverlayCueReplyOrder.Before
        )
        {
            await context.ReplyAsync(reply, ct);
        }

        if (cueAction is not null)
        {
            var admission = await overlayCues.AdmitAsync(
                Request(host.Id, cueAction, context.Message, OverlayCueAdmissionOrigin.Command),
                ct
            );
            if (
                reply is not null
                && cueAction.ReplyOrder == OverlayCueReplyOrder.After
                && AdmissionAccepted(admission)
            )
            {
                if (computedResult is not null)
                {
                    try
                    {
                        await using var deliveryStatus =
                            await MainDatabaseWriteTransaction.StartImmediateAsync(db, ct);
                        computedResult.ReplyEligible = true;
                        _ = await db.SaveChangesAsync(ct);
                        await deliveryStatus.CommitAsync(ct);
                    }
                    catch (Exception exception) when (exception is DbException or DbUpdateException)
                    {
                        await context.ReplyAsync(
                            "Command values were saved, but reply delivery status could not be saved. Check database storage. The cue will not be submitted again for this message ID.",
                            ct
                        );
                        return new CustomCommandExecutionOutcome.OverlayCue(admission);
                    }
                }
                await context.ReplyAsync(reply, ct);
            }
            return new CustomCommandExecutionOutcome.OverlayCue(admission);
        }

        await context.ReplyAsync(reply!, ct);
        return new CustomCommandExecutionOutcome.Handled();
    }

    internal static IReadOnlyList<string> SingleArgument(string text)
    {
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }
        while (start < text.Length && !char.IsWhiteSpace(text[start]))
        {
            start++;
        }
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }
        return start == text.Length ? [] : [text[start..]];
    }

    public async Task<OverlayCueAdmissionOutcome> TestCueAsync(
        int hostId,
        OverlayCueCustomCommandActionEditor action,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var features = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId)
            .Select(host => (HostFeatureFlags?)host.EnabledFeatures)
            .SingleOrDefaultAsync(ct);
        if (features is null || !HasCustomCommands(features.Value) || !HasOverlays(features.Value))
        {
            return new OverlayCueAdmissionOutcome.ParentDisabledOrCancelled();
        }

        var storedShape = new OverlayCueCustomCommandAction
        {
            TargetOverlayPublicId = action.TargetOverlayPublicId,
            CuePublicId = action.CuePublicId,
            QueuePolicy = action.QueuePolicy,
            ReplyOrder = action.ReplyOrder,
        };
        var references = await overlayCues.ResolveReferencesAsync(
            ReferenceRequest(hostId, storedShape),
            ct
        );
        return references is not OverlayCueReferenceOutcome.Available
            ? AdmissionOutcome(references)
            : await overlayCues.AdmitAsync(
                Request(hostId, storedShape, null, OverlayCueAdmissionOrigin.OwnerTest),
                ct
            );
    }

    private static OverlayCueAdmissionRequest Request(
        int hostId,
        OverlayCueCustomCommandAction action,
        ChatMessage? message,
        OverlayCueAdmissionOrigin origin
    )
    {
        var displayName =
            message is not null
            && message.Tags.TryGetValue("display-name", out var taggedDisplayName)
                ? taggedDisplayName
                : message?.Login ?? string.Empty;
        return new(
            hostId,
            action.TargetOverlayPublicId,
            action.CuePublicId,
            action.QueuePolicy,
            origin,
            new OverlayCueSafeContext(message?.Login ?? string.Empty, displayName)
        );
    }

    private static bool AdmissionAccepted(OverlayCueAdmissionOutcome admission) =>
        admission
            is OverlayCueAdmissionOutcome.Running
                or OverlayCueAdmissionOutcome.Queued
                or OverlayCueAdmissionOutcome.Disconnected;

    private static OverlayCueReferenceRequest ReferenceRequest(
        int hostId,
        OverlayCueCustomCommandAction action
    ) => new(hostId, action.TargetOverlayPublicId, action.CuePublicId);

    private static OverlayCueAdmissionOutcome AdmissionOutcome(
        OverlayCueReferenceOutcome references
    ) =>
        references switch
        {
            OverlayCueReferenceOutcome.Disabled { Part: OverlayCueReferencePart.Parent } =>
                new OverlayCueAdmissionOutcome.ParentDisabledOrCancelled(),
            OverlayCueReferenceOutcome.Disabled => new OverlayCueAdmissionOutcome.Disabled(),
            OverlayCueReferenceOutcome.Missing => new OverlayCueAdmissionOutcome.Missing(),
            _ => throw new InvalidOperationException(
                "An available cue reference does not map to a failed admission."
            ),
        };
}
