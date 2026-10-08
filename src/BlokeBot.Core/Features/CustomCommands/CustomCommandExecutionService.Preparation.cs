using System.Data.Common;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomCommandExecutionService
{
    private sealed record PreparedCommandInvocation(
        string? Text,
        string? LegacyTemplate,
        long? Count,
        CustomCommandComputedResult? ComputedResult,
        bool IsReplay
    );

    private sealed record CommandPreparationFailure(
        CustomCommandExecutionOutcome Outcome,
        string? Message = null
    );

    private async Task<
        Result<PreparedCommandInvocation, CommandPreparationFailure>
    > PrepareInvocationAsync(
        BlokeBotDbContext db,
        BotHost host,
        string hostLogin,
        CustomCommand command,
        CustomMessageLibraryEntry? messageEntry,
        OverlayCueCustomCommandAction? cueAction,
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        string? reply = null;
        string? selectedMessage = null;
        var valueReply =
            messageEntry?.Variants.Any(x =>
                CustomCommandTemplateRenderer.ContainsStoredToken(x.Text)
            ) == true;
        long? count = null;
        var invocationId = context.Message.Tags.GetValueOrDefault("id", string.Empty);
        var viewerId = context.Message.Tags.GetValueOrDefault("user-id", string.Empty);
        var invocationAvailable = !string.IsNullOrWhiteSpace(invocationId);
        if (
            valueReply
            && command.InvocationLimit
                is CustomCommandInvocationLimit.OncePerUser
                    or CustomCommandInvocationLimit.OncePerStreamPerUser
            && !CustomValueIdentity.Valid(viewerId)
        )
        {
            return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                new(
                    new CustomCommandExecutionOutcome.Handled(),
                    TemplateRenderFailure.ViewerIdentity.ChatMessage()
                )
            );
        }
        var renderFailed = false;
        CustomCommandComputedResult? computedResult = null;
        try
        {
            await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
                db,
                ct
            );
            await db.Entry(command).ReloadAsync(ct);
            if (
                !command.Enabled
                || !CustomCommandAccessPolicy.Allows(hostLogin, command, context.Message)
            )
            {
                return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                    new(new CustomCommandExecutionOutcome.Handled())
                );
            }
            var prior = invocationAvailable
                ? await db
                    .CustomCommandComputedResults.AsNoTracking()
                    .SingleOrDefaultAsync(
                        x =>
                            x.HostId == host.Id
                            && x.InvocationHash == CustomValueIdentity.Hash(invocationId),
                        ct
                    )
                : null;
            if (prior is not null)
            {
                return
                    prior.InvocationId != invocationId
                    || prior.CommandId != command.Id
                    || prior.ViewerId != viewerId
                    ? Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                        new(new CustomCommandExecutionOutcome.Handled())
                    )
                    : Result<PreparedCommandInvocation, CommandPreparationFailure>.Success(
                        new(prior.Reply, null, null, prior, true)
                    );
            }
            else
            {
                using var valueCooldown = valueReply
                    ? await cooldowns.ReserveAsync(
                        command.Id,
                        command.CooldownScope,
                        context.Message.Login,
                        Cooldown(command),
                        ct
                    )
                    : null;
                if (
                    valueReply
                        ? valueCooldown is null
                        : !cooldowns.TryRecord(
                            command.Id,
                            command.CooldownScope,
                            context.Message.Login,
                            Cooldown(command)
                        )
                )
                {
                    return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                        new(new CustomCommandExecutionOutcome.Cooldown())
                    );
                }
                var streamRequired = RequiresStream(command.InvocationLimit);
                var streamId = await StreamIdAsync(streamRequired, hostLogin, ct);
                if (streamRequired && streamId is StreamIdentity.Offline)
                {
                    return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                        new(new CustomCommandExecutionOutcome.StreamOffline())
                    );
                }
                if (streamRequired && streamId is StreamIdentity.Unavailable unavailable)
                {
                    return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                        new(
                            new CustomCommandExecutionOutcome.StreamUnavailable(unavailable.Failure)
                        )
                    );
                }
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
                    return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                        new(new CustomCommandExecutionOutcome.AlreadyUsed())
                    );
                }
                if (command.Action is CounterCustomCommandAction counterAction)
                {
                    await db.Entry(counterAction).Reference(x => x.Counter).LoadAsync(ct);
                    count = IncrementCounter(counterAction);
                    if (count is null)
                    {
                        return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                            new(new CustomCommandExecutionOutcome.Handled())
                        );
                    }
                }
                if (messageEntry is not null)
                {
                    await db.Entry(messageEntry).ReloadAsync(ct);
                }
                selectedMessage = SelectMessage(messageEntry);
                if (selectedMessage is null && cueAction is null)
                {
                    return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                        new(new CustomCommandExecutionOutcome.Handled())
                    );
                }
                if (selectedMessage is not null && valueReply)
                {
                    var session = await CustomStoredValueSession.LoadAsync(
                        db,
                        host.Id,
                        viewerId,
                        invocationAvailable,
                        ct
                    );
                    var rendered = await templates.RenderCommandAsync(
                        selectedMessage,
                        new(host.Id, host.Login, host.TwitchUserId ?? string.Empty),
                        context,
                        args,
                        count,
                        ct,
                        session
                    );
                    TemplateRenderFailure? failure = null;
                    reply = rendered.Match(
                        text => text,
                        error =>
                        {
                            failure = error;
                            return error.ChatMessage();
                        }
                    );
                    renderFailed = failure is not null;
                    if (!renderFailed && invocationAvailable)
                    {
                        computedResult = new()
                        {
                            HostId = host.Id,
                            CommandId = command.Id,
                            InvocationId = invocationId,
                            InvocationHash = CustomValueIdentity.Hash(invocationId),
                            ViewerId = viewerId,
                            Reply = reply,
                            ReplyEligible = cueAction?.ReplyOrder != OverlayCueReplyOrder.After,
                        };
                        _ = db.CustomCommandComputedResults.Add(computedResult);
                    }
                }
                if (!renderFailed)
                {
                    _ = await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    valueCooldown?.Commit();
                }
            }
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                new(
                    new CustomCommandExecutionOutcome.Handled(),
                    "The command save could not be confirmed. Check database storage and retry with the same message ID."
                )
            );
        }
        return renderFailed
            ? Result<PreparedCommandInvocation, CommandPreparationFailure>.Error(
                new(new CustomCommandExecutionOutcome.Handled(), reply)
            )
            : Result<PreparedCommandInvocation, CommandPreparationFailure>.Success(
                new(reply, valueReply ? null : selectedMessage, count, computedResult, false)
            );
    }
}
