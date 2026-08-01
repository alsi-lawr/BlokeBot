using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;

namespace BlokeBot.Core.Features.CustomCommands;

public abstract record CustomCommandExecutionOutcome
{
    private CustomCommandExecutionOutcome() { }

    public TResult Match<TResult>(
        Func<Unhandled, TResult> unhandled,
        Func<Handled, TResult> handled,
        Func<Cooldown, TResult> cooldown,
        Func<AlreadyUsed, TResult> alreadyUsed,
        Func<StreamOffline, TResult> streamOffline,
        Func<StreamUnavailable, TResult> streamUnavailable,
        Func<OverlayCue, TResult> overlayCue
    ) =>
        this switch
        {
            Unhandled value => unhandled(value),
            Handled value => handled(value),
            Cooldown value => cooldown(value),
            AlreadyUsed value => alreadyUsed(value),
            StreamOffline value => streamOffline(value),
            StreamUnavailable value => streamUnavailable(value),
            OverlayCue value => overlayCue(value),
            _ => throw new UnreachableException("Unknown custom-command execution outcome."),
        };

    public sealed record Unhandled : CustomCommandExecutionOutcome;

    public sealed record Handled : CustomCommandExecutionOutcome;

    public sealed record Cooldown : CustomCommandExecutionOutcome;

    public sealed record AlreadyUsed : CustomCommandExecutionOutcome;

    public sealed record StreamOffline : CustomCommandExecutionOutcome;

    public sealed record StreamUnavailable(HostStreamLivenessOutcome.Unavailable Failure)
        : CustomCommandExecutionOutcome;

    public sealed record OverlayCue(OverlayCueAdmissionOutcome Admission)
        : CustomCommandExecutionOutcome;
}
