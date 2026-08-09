using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public partial class ShoutoutsPage
{
    private string _targetLogin = string.Empty;
    private bool _historyOpen;

    private string _historySummary =>
        State is not { History.Count: > 0 } history
            ? "No shoutouts recorded yet"
            : $"Sent {history.History.Count(item => item.Direction == ShoutoutDirection.Sent)}"
                + $" · received {history.History.Count(item => item.Direction == ShoutoutDirection.Received)}"
                + $" · last: @{history.History[0].TargetLogin} {history.History[0].OccurredAtUtc.ToLocalTime():MMM d}";

    private string _cooldownText =>
        State switch
        {
            {
                GlobalEligibleAtUtc: { } global,
                TargetCooldown: ShoutoutTargetCooldownReadiness.EligibleAt target
            } =>
                $"Next global shoutout: {global.ToLocalTime():g}. @{_targetLogin} is eligible at {target.Value.ToLocalTime():g}.",
            { GlobalEligibleAtUtc: { } global } =>
                $"You can send another shoutout after {global.ToLocalTime():g}. No separate time is available yet for @{_targetLogin}.",
            { TargetCooldown: ShoutoutTargetCooldownReadiness.EligibleAt target } =>
                $"@{_targetLogin} can be shouted out after {target.Value.ToLocalTime():g}. The overall next-send time is not available yet.",
            _ => "No cooldown time is available yet. Try sending when you are ready.",
        };

    protected override HostFeatureFlags Feature => HostFeatureFlags.Shoutouts;

    protected override async Task<ShoutoutDashboardState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    ) => await _shoutouts.LoadAsync(hostId, _targetLogin, cancellationToken);

    // The shoutout load spans provider calls, so honour a switch turned off during that window.
    protected override async Task<bool> ConfirmEnabledAfterLoadAsync(
        ShoutoutDashboardState? state
    ) =>
        state is not null
        && await NativeTwitch.IsEnabledAsync(
            HostId,
            HostFeatureFlags.Shoutouts,
            CancellationToken.None
        );

    private Task SendAsync() =>
        MutateAsync(async hostId =>
        {
            var outcome = await _shoutouts.SendAsync(hostId, _targetLogin, CancellationToken.None);
            var (message, success) = outcome switch
            {
                ShoutoutOperationOutcome.Sent sent => (
                    $"Shoutout sent to @{sent.TargetLogin}.",
                    true
                ),
                ShoutoutOperationOutcome.TargetNotFound missing => (
                    $"Twitch user @{missing.TargetLogin} was not found.",
                    false
                ),
                ShoutoutOperationOutcome.SelfTarget => (
                    "You cannot shout out the selected channel.",
                    false
                ),
                ShoutoutOperationOutcome.TargetOffline offline => (
                    $"@{offline.TargetLogin} must be live with viewers.",
                    false
                ),
                ShoutoutOperationOutcome.NotReady => (
                    "Connect the bot account to Twitch, then try again.",
                    false
                ),
                ShoutoutOperationOutcome.CooldownActive cooldown => (
                    $"Try again after {cooldown.EligibleAtUtc.ToLocalTime():g}.",
                    false
                ),
                ShoutoutOperationOutcome.CooldownUnknown => (
                    "Twitch did not confirm the cooldown state.",
                    false
                ),
                ShoutoutOperationOutcome.ProviderRejected => (
                    "Twitch could not send this shoutout. Check the channel name and try again.",
                    false
                ),
                _ => throw new UnreachableException(),
            };
            Publish(message, success);
        });

    private Task RunAutomaticRaidMutationAsync(int hostId, Func<Task> mutation) =>
        RunSelectedHostMutationAsync(hostId, mutation);
}
