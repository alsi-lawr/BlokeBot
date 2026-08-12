using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.RaidCollaboration;

public partial class AutomaticShoutoutOutcomeList
{
    [Parameter]
    public IReadOnlyList<AutomaticRaidShoutoutOutcomeView> Outcomes { get; set; } = [];

    private static string OutcomePillClass(AutomaticRaidShoutoutOutcomeView outcome) =>
        outcome.ResultCode == AutomaticRaidShoutoutResultCode.Delivered
            ? "status-pill bg-[var(--app-affirmative-surface)] text-[var(--app-affirmative)]"
            : "status-pill bg-[var(--app-surface-muted)] text-[var(--app-text-muted)] ring-1 ring-[var(--app-border)]";

    private static string OutcomeTitle(AutomaticRaidShoutoutOutcomeView outcome) =>
        outcome.ResultCode switch
        {
            AutomaticRaidShoutoutResultCode.Delivered => "Delivered",
            AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong =>
                "Message exceeded the rendered limit",
            AutomaticRaidShoutoutResultCode.NotReady => "Account was not connected",
            AutomaticRaidShoutoutResultCode.AuthorityRequired => "Reconnect the Twitch account",
            AutomaticRaidShoutoutResultCode.Cooldown => "Skipped during Twitch cooldown",
            AutomaticRaidShoutoutResultCode.Invalid => "Raid was not eligible",
            AutomaticRaidShoutoutResultCode.Rejected => "Twitch did not send the shoutout",
            AutomaticRaidShoutoutResultCode.RateLimited => "Chat was too busy to send",
            AutomaticRaidShoutoutResultCode.PartialFailure => "Message sent, pin failed",
            AutomaticRaidShoutoutResultCode.Unexpected => "Shoutout failed",
            AutomaticRaidShoutoutResultCode.Ambiguous => "Check Twitch for the result",
            _ when outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Processing =>
                "Sending shoutout",
            _ => "Shoutout was not sent",
        };

    private static string OutcomeDescription(AutomaticRaidShoutoutOutcomeView outcome) =>
        outcome.ResultCode switch
        {
            AutomaticRaidShoutoutResultCode.Delivered => "BlokeBot sent the shoutout you selected.",
            AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong =>
                "Live Twitch values pushed the chat message over 500 characters. Nothing was sent.",
            AutomaticRaidShoutoutResultCode.NotReady =>
                "Reconnect the account used by this shoutout. BlokeBot did not switch to another mode.",
            AutomaticRaidShoutoutResultCode.AuthorityRequired =>
                "Reconnect the account shown in Channel setup before the next raid.",
            AutomaticRaidShoutoutResultCode.Cooldown =>
                "Twitch’s native shoutout cooldown was still active, so nothing was sent.",
            AutomaticRaidShoutoutResultCode.Invalid =>
                "The raiding channel or saved shoutout choice could not be used.",
            AutomaticRaidShoutoutResultCode.Rejected =>
                "Twitch did not send this shoutout. BlokeBot did not switch to another mode.",
            AutomaticRaidShoutoutResultCode.RateLimited =>
                "Chat stayed busy until this raid’s send window ended, so nothing was sent.",
            AutomaticRaidShoutoutResultCode.PartialFailure =>
                "The chat message was sent once, but the later pin step failed. BlokeBot will not resend or switch modes.",
            AutomaticRaidShoutoutResultCode.Unexpected =>
                "Open Alerts for the failure details before the next raid.",
            AutomaticRaidShoutoutResultCode.Ambiguous =>
                "BlokeBot cannot safely tell whether Twitch completed the request, so it will not retry.",
            _ => "BlokeBot is waiting for Twitch or chat to finish this shoutout.",
        };
}
