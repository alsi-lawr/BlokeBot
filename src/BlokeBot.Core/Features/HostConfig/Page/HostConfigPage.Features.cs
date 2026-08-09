using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private const string _featureIconBase =
        "inline-flex h-9 w-9 flex-none items-center justify-center rounded-lg border border-[var(--app-border)] bg-[var(--app-surface-muted)]";

    private string _chatToolsStageSummary =>
        _state is null
            ? string.Empty
            : $"{_state.Features.Count(static feature => feature.Enabled)} of {_state.Features.Count} on";

    private static string FeatureBadgeClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? "inline-flex h-5 shrink-0 items-center gap-1.5 rounded-full bg-emerald-50 px-2 text-[0.68rem] font-bold text-emerald-700 ring-1 ring-emerald-200"
            : "inline-flex h-5 shrink-0 items-center gap-1.5 rounded-full bg-slate-100 px-2 text-[0.68rem] font-bold text-slate-600 ring-1 ring-slate-200";

    private static string FeatureCardClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? "feature-toggle-card w-full rounded-[14px] border border-[var(--app-focus-border)] bg-[var(--app-surface-solid)] p-3 text-left shadow-[var(--app-shadow-sm)] transition hover:-translate-y-0.5 hover:border-[var(--app-control-hover-border)] hover:bg-[var(--app-control-hover)] hover:shadow-[var(--app-shadow-surface)] focus-visible:border-[var(--app-focus-border)] focus-visible:shadow-[var(--app-focus-shadow)] focus-visible:outline-none"
            : "feature-toggle-card w-full rounded-[14px] border border-[var(--app-control-border)] bg-[var(--app-control-bg)] p-3 text-left transition hover:-translate-y-0.5 hover:border-[var(--app-control-hover-border)] hover:bg-[var(--app-control-hover)] hover:shadow-[var(--app-shadow-surface)] focus-visible:border-[var(--app-focus-border)] focus-visible:shadow-[var(--app-focus-shadow)] focus-visible:outline-none";

    private static string FeatureDotClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
            : "h-1.5 w-1.5 rounded-full bg-slate-400";

    private static string FeatureIconClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? feature.Feature switch
            {
                HostFeatureFlags.Points => $"{_featureIconBase} text-emerald-600",
                HostFeatureFlags.CustomCommands => $"{_featureIconBase} text-violet-600",
                HostFeatureFlags.Shoutouts
                or HostFeatureFlags.Polls
                or HostFeatureFlags.ClipsAndMarkers
                or HostFeatureFlags.RewardsAndRedemptions
                or HostFeatureFlags.Predictions => $"{_featureIconBase} text-purple-700",
                HostFeatureFlags.RequestBoards => $"{_featureIconBase} text-sky-700",
                HostFeatureFlags.PlayWithViewers => $"{_featureIconBase} text-emerald-700",
                HostFeatureFlags.Moments => $"{_featureIconBase} text-violet-700",
                HostFeatureFlags.Overlays => $"{_featureIconBase} text-blue-600",
                _ => $"{_featureIconBase} text-blue-600",
            }
            : $"{_featureIconBase} text-slate-500";

    private static MarkupString FeatureIcon(HostFeatureFlags feature) =>
        new(
            feature switch
            {
                HostFeatureFlags.Guessing => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M8.5 4h7l4 4v8l-4 4h-7l-4-4V8l4-4Z" />
                    <path d="M9 9h.01" />
                    <path d="M15 9h.01" />
                    <path d="M12 12h.01" />
                    <path d="M9 15h.01" />
                    <path d="M15 15h.01" />
                </svg>
                """,
                HostFeatureFlags.Points => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M12 3v18" />
                    <path d="M17 7.5c0-1.4-1.6-2.5-5-2.5S7 6.1 7 7.5 8.6 10 12 10s5 1.1 5 2.5-1.6 2.5-5 2.5-5-1.1-5-2.5" />
                </svg>
                """,
                HostFeatureFlags.CustomCommands => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M4 7h16" />
                    <path d="M4 12h10" />
                    <path d="M4 17h7" />
                    <path d="m16 14 3 3-3 3" />
                </svg>
                """,
                HostFeatureFlags.Shoutouts
                or HostFeatureFlags.Polls
                or HostFeatureFlags.ClipsAndMarkers
                or HostFeatureFlags.RewardsAndRedemptions
                or HostFeatureFlags.Predictions => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="m13 2-7 11h6l-1 9 7-12h-6l1-8Z" />
                </svg>
                """,
                HostFeatureFlags.RequestBoards => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M5 6h14M5 12h14M5 18h14" />
                </svg>
                """,
                HostFeatureFlags.PlayWithViewers => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <circle cx="12" cy="7" r="3" /><path d="M6 20c0-4 2-6 6-6s6 2 6 6" />
                </svg>
                """,
                HostFeatureFlags.Moments => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="m12 3 3 6 6 3-6 3-3 6-3-6-6-3 6-3 3-6Z" />
                </svg>
                """,
                HostFeatureFlags.Overlays => """
                <svg class="h-5 w-5 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:1.9]" viewBox="0 0 24 24" aria-hidden="true">
                    <rect x="3" y="5" width="18" height="14" rx="2" />
                    <path d="m7 15 3-3 2.5 2.5L16 11l2 2" />
                    <path d="M8 9h.01" />
                </svg>
                """,
                _ => string.Empty,
            }
        );

    private Task SetFeatureEnabledAsync(int hostId, HostFeatureFlags feature, bool enabled) =>
        ObserveUiOperationAsync(
            nameof(SetFeatureEnabledAsync),
            () =>
                RunSelectedHostMutationAsync(
                    hostId,
                    () => SetFeatureEnabledCoreAsync(hostId, feature, enabled)
                )
        );

    private async Task SetFeatureEnabledCoreAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled
    )
    {
        var startupMessageEnabled = _startupMessageEnabled;
        var startupMessageText = _startupMessageText;
        try
        {
            if (enabled)
            {
                await _features.EnableAsync(hostId, feature, CancellationToken.None);
            }
            else
            {
                await _features.DisableAsync(hostId, feature, CancellationToken.None);
            }

            await LoadCoreAsync();
            ToastFeatureChange(feature, enabled);
        }
        finally
        {
            if (_state?.HostId == hostId)
            {
                _startupMessageEnabled = startupMessageEnabled;
                _startupMessageText = startupMessageText;
            }
        }
    }

    private void ToastFeatureChange(HostFeatureFlags feature, bool enabled)
    {
        var featureName = FeatureName(feature);
        var channelName = _state is { Login.Length: > 0 } ? $"#{_state.Login}" : "this channel";
        var stateText = enabled ? "enabled" : "disabled";
        var impactText = FeatureImpact(feature, enabled);

        var message = $"{featureName} is now {stateText} for {channelName}. {impactText}";
        var title = $"{featureName} {stateText}";
        if (enabled)
        {
            _ = _toasts.Publish(
                ToastRequest<PositiveStatusToastStrategy>.WithTitle(message, title)
            );
        }
        else
        {
            _ = _toasts.Publish(ToastRequest<CautionStatusToastStrategy>.WithTitle(message, title));
        }
    }

    private static string FeatureImpact(HostFeatureFlags feature, bool enabled) =>
        feature switch
        {
            HostFeatureFlags.Overlays when enabled =>
                "Its dashboard and Browser Sources are available again.",
            HostFeatureFlags.Overlays =>
                "Its dashboard and Browser Sources are unavailable until you enable it again.",
            _ when enabled => "Its chat commands and pages are available again.",
            _ => "Its chat commands and pages are unavailable until you enable it again.",
        };

    private static string FeatureName(HostFeatureFlags feature) =>
        feature switch
        {
            HostFeatureFlags.Guessing => "Guessing game",
            HostFeatureFlags.Points => "Points",
            HostFeatureFlags.CustomCommands => "Custom commands",
            HostFeatureFlags.Shoutouts => "Shoutouts",
            HostFeatureFlags.Polls => "Polls",
            HostFeatureFlags.ClipsAndMarkers => "Clips & markers",
            HostFeatureFlags.RewardsAndRedemptions => "Rewards & redemptions",
            HostFeatureFlags.Predictions => "Predictions",
            HostFeatureFlags.RequestBoards => "Request boards",
            HostFeatureFlags.PlayWithViewers => "Play with viewers",
            HostFeatureFlags.Moments => "Moments",
            HostFeatureFlags.Overlays => "Overlays",
            _ => "Feature",
        };
}
