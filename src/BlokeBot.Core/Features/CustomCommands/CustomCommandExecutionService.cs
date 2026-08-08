using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandExecutionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IOptions<BlokeBotOptions> options,
    CustomCommandCooldownStore cooldowns,
    CustomMessageSelector messageSelector,
    CustomCommandTemplateRenderer templates,
    CustomCommandInvocationClaimStore claims,
    IHostStreamLivenessProvider streams,
    IOverlayCueAdmissionService overlayCues,
    TimeProvider clock
)
{
    public async ValueTask<CustomCommandExecutionOutcome> ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = LoginName.Parse(context.Message.Channel).Value;
        var alias = CommandAliasNormalizer.Normalize(context.CommandName);
        if (hostLogin.Length == 0 || alias.Length == 0)
        {
            return new CustomCommandExecutionOutcome.Unhandled();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == hostLogin)
            .Select(x => new { x.Id, x.EnabledFeatures })
            .SingleOrDefaultAsync(ct);
        if (host is null || !HasCustomCommands(host.EnabledFeatures))
        {
            return new CustomCommandExecutionOutcome.Unhandled();
        }

        var commandId = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x => x.HostId == host.Id && x.Alias == alias)
            .Select(x => (int?)x.CustomCommandId)
            .FirstOrDefaultAsync(ct);
        if (commandId is null)
        {
            return new CustomCommandExecutionOutcome.Unhandled();
        }

        var command = await db
            .CustomCommands.Include(x => x.Action)
            .Include(x => x.AllowedUsers)
            .SingleOrDefaultAsync(x => x.HostId == host.Id && x.Id == commandId.Value, ct);
        if (command is null || !command.Enabled)
        {
            return new CustomCommandExecutionOutcome.Handled();
        }

        if (!CustomCommandAccessPolicy.Allows(hostLogin, command, context.Message))
        {
            return new CustomCommandExecutionOutcome.Handled();
        }

        var cueAction = command.Action as OverlayCueCustomCommandAction;
        if (cueAction is not null && !HasOverlays(host.EnabledFeatures))
        {
            return new CustomCommandExecutionOutcome.OverlayCue(
                new OverlayCueAdmissionOutcome.ParentDisabledOrCancelled()
            );
        }
        if (cueAction is not null)
        {
            var references = await overlayCues.ResolveReferencesAsync(
                ReferenceRequest(host.Id, cueAction),
                ct
            );
            if (references is not OverlayCueReferenceOutcome.Available)
            {
                return new CustomCommandExecutionOutcome.OverlayCue(AdmissionOutcome(references));
            }
        }

        var replyId = command.Action.ReplyIdForArgumentCount(args.Count);
        if (replyId is null && cueAction is null)
        {
            return new CustomCommandExecutionOutcome.Handled();
        }

        var messageEntry = replyId is null
            ? null
            : await db
                .CustomMessageLibraryEntries.Include(x => x.Variants)
                .SingleOrDefaultAsync(x => x.HostId == host.Id && x.Id == replyId.Value, ct);
        if (replyId is not null && messageEntry is null)
        {
            return new CustomCommandExecutionOutcome.Handled();
        }

        if (
            !cooldowns.TryRecord(
                command.Id,
                command.CooldownScope,
                context.Message.Login,
                Cooldown(command)
            )
        )
        {
            return new CustomCommandExecutionOutcome.Cooldown();
        }

        var streamId = await StreamIdAsync(command.InvocationLimit, hostLogin, ct);
        if (streamId is StreamIdentity.Offline)
        {
            return new CustomCommandExecutionOutcome.StreamOffline();
        }

        if (streamId is StreamIdentity.Unavailable unavailable)
        {
            return new CustomCommandExecutionOutcome.StreamUnavailable(unavailable.Failure);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var claim =
            command.InvocationLimit == CustomCommandInvocationLimit.Unlimited
                ? new CustomCommandInvocationClaimOutcome.Claimed()
                : await claims.TryClaimAsync(
                    db,
                    ClaimRequest(host.Id, command, context.Message, streamId),
                    ct
                );
        if (claim is CustomCommandInvocationClaimOutcome.AlreadyUsed)
        {
            return new CustomCommandExecutionOutcome.AlreadyUsed();
        }

        long? count = null;
        if (command.Action is CounterCustomCommandAction counterAction)
        {
            await db.Entry(counterAction).Reference(x => x.Counter).LoadAsync(ct);
            count = IncrementCounter(counterAction);
            if (count is null)
            {
                return new CustomCommandExecutionOutcome.Handled();
            }
        }

        var selectedMessage = SelectMessage(messageEntry);
        if (selectedMessage is null && cueAction is null)
        {
            return new CustomCommandExecutionOutcome.Handled();
        }

        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var reply = selectedMessage is null
            ? null
            : templates.Render(selectedMessage, context, args, count);
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
                await context.ReplyAsync(reply, ct);
            }
            return new CustomCommandExecutionOutcome.OverlayCue(admission);
        }

        await context.ReplyAsync(reply!, ct);
        return new CustomCommandExecutionOutcome.Handled();
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

    private async Task<StreamIdentity> StreamIdAsync(
        CustomCommandInvocationLimit limit,
        string hostLogin,
        CancellationToken ct
    )
    {
        if (
            limit
            is not (
                CustomCommandInvocationLimit.OncePerStream
                or CustomCommandInvocationLimit.OncePerStreamPerUser
            )
        )
        {
            return new StreamIdentity.NotRequired();
        }

        var result = await streams.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
        var outcome = result.Match(value => value, _ => throw new UnreachableException());
        return outcome switch
        {
            HostStreamLivenessOutcome.Live live => new StreamIdentity.Available(live.StreamId),
            HostStreamLivenessOutcome.Offline => new StreamIdentity.Offline(),
            HostStreamLivenessOutcome.Unavailable unavailable => new StreamIdentity.Unavailable(
                unavailable
            ),
            _ => throw new UnreachableException("Unknown stream-liveness outcome."),
        };
    }

    private static CustomCommandInvocationClaimRequest ClaimRequest(
        int hostId,
        CustomCommand command,
        ChatMessage message,
        StreamIdentity stream
    )
    {
        CustomCommandInvocationScope scope = command.InvocationLimit switch
        {
            CustomCommandInvocationLimit.OncePerStream =>
                new CustomCommandInvocationScope.OncePerStream(
                    ((StreamIdentity.Available)stream).StreamId
                ),
            CustomCommandInvocationLimit.OncePerUser =>
                new CustomCommandInvocationScope.OncePerUser(message.Tags["user-id"]),
            CustomCommandInvocationLimit.OncePerStreamPerUser =>
                new CustomCommandInvocationScope.OncePerStreamPerUser(
                    ((StreamIdentity.Available)stream).StreamId,
                    message.Tags["user-id"]
                ),
            _ => throw new UnreachableException("Unlimited commands do not create claims."),
        };
        return new(hostId, command.Id, scope);
    }

    private TimeSpan Cooldown(CustomCommand command)
    {
        var seconds = Math.Max(
            Math.Max(0, command.CooldownSeconds),
            Math.Max(0, options.Value.CustomCommands.MinimumCooldownSeconds)
        );
        return TimeSpan.FromSeconds(seconds);
    }

    private string? SelectMessage(CustomMessageLibraryEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        var snapshot = new CustomMessageSelectionSnapshot(
            entry.SelectionMode,
            entry.CurrentVariantIndex,
            entry.Variants.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => x.Text)
        );
        return messageSelector
            .Select(snapshot)
            .Match<string?>(
                selected =>
                {
                    if (entry.SelectionMode is CustomMessageSelectionMode.Sequential)
                    {
                        entry.CurrentVariantIndex = selected.NextVariantIndex;
                        entry.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
                    }

                    return selected.Text;
                },
                static () => null
            );
    }

    private long? IncrementCounter(CounterCustomCommandAction action)
    {
        if (action.Counter is null)
        {
            return null;
        }

        action.Counter.Value++;
        action.Counter.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        return action.Counter.Value;
    }

    private static bool HasCustomCommands(HostFeatureFlags features) =>
        (features & HostFeatureFlags.CustomCommands) == HostFeatureFlags.CustomCommands;

    private static bool HasOverlays(HostFeatureFlags features) =>
        (features & HostFeatureFlags.Overlays) == HostFeatureFlags.Overlays;

    private abstract record StreamIdentity
    {
        private StreamIdentity() { }

        public sealed record NotRequired : StreamIdentity;

        public sealed record Available(string StreamId) : StreamIdentity;

        public sealed record Offline : StreamIdentity;

        public sealed record Unavailable(HostStreamLivenessOutcome.Unavailable Failure)
            : StreamIdentity;
    }
}
