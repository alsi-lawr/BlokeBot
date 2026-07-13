using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.AccessLists;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private static readonly TimeSpan _accessModeSaveDebounce = TimeSpan.FromMilliseconds(180);

    private CancellationTokenSource? _allowModsByDefaultSaveCts;
    private string _newBlacklistLogin = string.Empty;
    private string _newWhitelistLogin = string.Empty;
    private int _allowModsByDefaultSaveVersion;
    private bool _blockedByMode;
    private BotChannelRuntimeState? _pendingRuntimeState;
    private IReadOnlyList<AccessListEntryProfile> _blacklistEntries = [];
    private HostConfigState? _state;
    private IReadOnlyList<AccessListEntryProfile> _whitelistEntries = [];

    private bool _canStart =>
        _state?.RuntimeStatus is not null
        && _state.IsChannelBotAuthorized
        && _state.RuntimeStatus.ChannelBotAuthorizationScopesCurrent
        && _botAccountCanStart
        && _state.RuntimeStatus.RuntimeState is BotChannelRuntimeState.Stopped;

    private bool _canStop =>
        _state?.RuntimeStatus?.RuntimeState
            is BotChannelRuntimeState.Started
                or BotChannelRuntimeState.Starting;

    private string _authorizationBadgeClass =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200"
            : "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200";

    private string _authorizationDotClass =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
            : "h-1.5 w-1.5 rounded-full bg-amber-500";

    private string _authorizationText =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "connected"
        : _state?.IsChannelBotAuthorized == true ? "needs update"
        : "not connected";

    private string _runtimeBadgeClass =>
        _state?.RuntimeStatus?.RuntimeState switch
        {
            BotChannelRuntimeState.Starting =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-orange-50 px-2.5 text-xs font-bold text-orange-700 ring-1 ring-orange-200",
            BotChannelRuntimeState.Started =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            BotChannelRuntimeState.Stopping =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-purple-50 px-2.5 text-xs font-bold text-purple-700 ring-1 ring-purple-200",
            _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
        };

    private string _runtimeDotClass =>
        _state?.RuntimeStatus?.RuntimeState switch
        {
            BotChannelRuntimeState.Starting => "h-1.5 w-1.5 rounded-full bg-orange-500",
            BotChannelRuntimeState.Started => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            BotChannelRuntimeState.Stopping => "h-1.5 w-1.5 rounded-full bg-purple-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string _runtimeText =>
        _state?.RuntimeStatus?.RuntimeState switch
        {
            BotChannelRuntimeState.Starting => "starting",
            BotChannelRuntimeState.Started => "online",
            BotChannelRuntimeState.Stopping => "stopping",
            _ => "offline",
        };

    private string _runtimeStatusMessage =>
        _state?.RuntimeStatus?.RuntimeState switch
        {
            BotChannelRuntimeState.Starting => "The bot is starting.",
            BotChannelRuntimeState.Started => "The bot is in chat.",
            BotChannelRuntimeState.Stopping => "The bot is leaving chat.",
            _ => "The bot is offline.",
        };

    private string _startRuntimeTooltip =>
        _canStart ? "Start the bot for this channel." : _startRuntimeDisabledTooltip;

    private string _stopRuntimeTooltip =>
        _canStop ? "Stop the bot for this channel." : _stopRuntimeDisabledTooltip;

    private string _startRuntimeDisabledTooltip =>
        _state is null ? "Wait for the channel to load before starting the bot."
        : _state.IsChannelBotAuthorized != true ? "Connect the channel before starting the bot."
        : _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent != true
            ? "Reconnect the channel before starting the bot."
        : !_botAccountCanStart ? "Connect the custom bot account before starting the bot."
        : _state.RuntimeStatus?.RuntimeState is not BotChannelRuntimeState.Stopped
            ? "Wait for the bot to stop before starting it again."
        : "The bot cannot be started right now.";

    private string _stopRuntimeDisabledTooltip =>
        _state?.RuntimeStatus?.RuntimeState is BotChannelRuntimeState.Stopping
            ? "The bot is already stopping."
            : "The bot is not running right now.";

    private bool _botAccountCanStart =>
        _state?.BotOverride.Enabled != true
        || _state.BotOverride.Status.State == BotAccountAuthorizationState.Ready;

    private string _botAccountStatusReloadKey =>
        _state is null
            ? string.Empty
            : string.Join(
                ":",
                _state.Login,
                _state.BotOverride.Enabled,
                _state.BotOverride.Status.State,
                _state.BotOverride.Status.AuthorizedLogin ?? string.Empty,
                string.Join(",", _state.BotOverride.Status.GrantedScopes)
            );

    private string _whisperQuotaBadgeClass =>
        _state?.BotOverride.WhisperQuota.Exhausted == true
        || _state?.BotOverride.WhisperQuota.Remaining == 0
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200"
            : "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200";

    private string _whisperQuotaDotClass =>
        _state?.BotOverride.WhisperQuota.Exhausted == true
        || _state?.BotOverride.WhisperQuota.Remaining == 0
            ? "h-1.5 w-1.5 rounded-full bg-amber-500"
            : "h-1.5 w-1.5 rounded-full bg-emerald-500";

    private string _whisperQuotaText =>
        _state?.BotOverride.WhisperQuota is { } quota
            ? $"{quota.RecipientCount} of {quota.Limit}"
            : "0 of 40";

    private string _accessModeSegmentClass =>
        _state?.ModAccess.AllowModsByDefault == false
            ? "segmented-motion segmented-motion--second"
            : "segmented-motion";

    private static string AccessModeTabClass(bool active)
    {
        return active
            ? "segmented-motion__tab segmented-motion__tab--active"
            : "segmented-motion__tab";
    }

    private static string FeatureBadgeClass(HostFeatureCardState feature)
    {
        return feature.Enabled
            ? "inline-flex h-5 shrink-0 items-center gap-1.5 rounded-full bg-emerald-50 px-2 text-[0.68rem] font-bold text-emerald-700 ring-1 ring-emerald-200"
            : "inline-flex h-5 shrink-0 items-center gap-1.5 rounded-full bg-slate-100 px-2 text-[0.68rem] font-bold text-slate-600 ring-1 ring-slate-200";
    }

    private static string FeatureCardClass(HostFeatureCardState feature)
    {
        return feature.Enabled
            ? "feature-toggle-card grid min-h-24 grid-cols-[2.25rem_minmax(0,1fr)] items-center gap-3 rounded-lg p-3 text-left"
            : "feature-toggle-card feature-toggle-card--disabled grid min-h-24 grid-cols-[2.25rem_minmax(0,1fr)] items-center gap-3 rounded-lg p-3 text-left";
    }

    private static string FeatureDotClass(HostFeatureCardState feature)
    {
        return feature.Enabled
            ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
            : "h-1.5 w-1.5 rounded-full bg-slate-400";
    }

    private static string FeatureIconClass(HostFeatureCardState feature)
    {
        return feature.Enabled
            ? feature.Feature switch
            {
                HostFeatureFlags.Points => "feature-toggle-card__icon text-emerald-600",
                HostFeatureFlags.CustomCommands => "feature-toggle-card__icon text-violet-600",
                _ => "feature-toggle-card__icon text-blue-600",
            }
            : "feature-toggle-card__icon text-slate-500";
    }

    private static MarkupString FeatureIcon(HostFeatureFlags feature)
    {
        return new(
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
                _ => string.Empty,
            }
        );
    }

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                InvokeAsync,
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task AddAccessAsync(int hostId, AccessListEntryKind kind)
    {
        var login = kind == AccessListEntryKind.Whitelist ? _newWhitelistLogin : _newBlacklistLogin;
        await _modAccess.AddEntryAsync(hostId, kind, login, CancellationToken.None);
        if (kind == AccessListEntryKind.Whitelist)
        {
            _newWhitelistLogin = string.Empty;
        }
        else
        {
            _newBlacklistLogin = string.Empty;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var pageContext = await LoadPageContextAsync();
        var session = pageContext.Session;
        var selection = pageContext.HostSelection;
        if (pageContext.IsBotAccount)
        {
            _blockedByMode = true;
            _state = null;
            ClearAccessEntries();
            return;
        }

        _state = await _hostConfig.LoadAsync(session, CancellationToken.None);

        _blockedByMode =
            selection is not null
            && !session.CurrentHostRoleIs(AuthRole.Streamer)
            && _state?.IsHostCreated == true;
        if (_blockedByMode)
        {
            _state = null;
            ClearAccessEntries();
            return;
        }

        if (_state is { IsHostCreated: true } loadedState)
        {
            await LoadAccessEntriesAsync(loadedState.ModAccess);
        }
        else
        {
            ClearAccessEntries();
        }
    }

    private async Task ReloadForEventAsync()
    {
        var previousPendingRuntimeState = _pendingRuntimeState;
        await LoadAsync();

        if (previousPendingRuntimeState is null)
        {
            return;
        }

        var currentRuntimeState = _state?.RuntimeStatus?.RuntimeState;
        if (currentRuntimeState == previousPendingRuntimeState)
        {
            return;
        }

        TrackPendingRuntimeTransition();
        if (currentRuntimeState is not null)
        {
            _toasts.Status(_runtimeStatusMessage);
        }
    }

    private async Task RemoveAccessAsync(int hostId, AccessListEntryKind kind, string login)
    {
        await _modAccess.RemoveEntryAsync(hostId, kind, login, CancellationToken.None);
        await LoadAsync();
    }

    private async Task SetModsEnabledAsync(int hostId, ChangeEventArgs args)
    {
        await _modAccess.SetModsEnabledAsync(hostId, args.Value is true, CancellationToken.None);
        await LoadAsync();
    }

    private void SetAllowModsByDefault(int hostId, bool allowByDefault)
    {
        if (_state is null || _state.ModAccess.AllowModsByDefault == allowByDefault)
        {
            return;
        }

        var previousAccess = _state.ModAccess;
        var version = ++_allowModsByDefaultSaveVersion;
        _state = _state with
        {
            ModAccess = previousAccess with { AllowModsByDefault = allowByDefault },
        };

        _allowModsByDefaultSaveCts?.Cancel();
        var saveCts = new CancellationTokenSource();
        _allowModsByDefaultSaveCts = saveCts;

        _ = PersistAllowModsByDefaultAsync(
            hostId,
            allowByDefault,
            previousAccess,
            version,
            saveCts
        );
    }

    private async Task PersistAllowModsByDefaultAsync(
        int hostId,
        bool allowByDefault,
        HostModAccessState previousAccess,
        int version,
        CancellationTokenSource saveCts
    )
    {
        var cancellationToken = saveCts.Token;
        try
        {
            await Task.Delay(_accessModeSaveDebounce, cancellationToken);
            await _modAccess.SetAllowModsByDefaultAsync(hostId, allowByDefault, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (version == _allowModsByDefaultSaveVersion)
            {
                await InvokeAsync(LoadAsync);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            if (version != _allowModsByDefaultSaveVersion)
            {
                return;
            }

            await InvokeAsync(() =>
            {
                if (_state is not null)
                {
                    _state = _state with { ModAccess = previousAccess };
                }

                _toasts.Error(
                    "Who can help could not be saved. Your previous setting has been restored.",
                    "Mod help not saved"
                );
                StateHasChanged();
            });
        }
        finally
        {
            if (ReferenceEquals(_allowModsByDefaultSaveCts, saveCts))
            {
                _allowModsByDefaultSaveCts = null;
            }

            saveCts.Dispose();
        }
    }

    private async Task SetFeatureEnabledAsync(int hostId, HostFeatureFlags feature, bool enabled)
    {
        await _features.SetEnabledAsync(hostId, feature, enabled, CancellationToken.None);
        await LoadAsync();
        ToastFeatureChange(feature, enabled);
    }

    private async Task SetBotOverrideEnabledAsync(int hostId, bool enabled)
    {
        var runtimeWasActive =
            _state?.RuntimeStatus?.RuntimeState
            is BotChannelRuntimeState.Starting
                or BotChannelRuntimeState.Started;
        await _hostBotAccounts.SetOverrideEnabledAsync(hostId, enabled, CancellationToken.None);
        await LoadAsync();
        if (runtimeWasActive)
        {
            TrackPendingRuntimeTransition();
        }

        _toasts.Status(
            enabled
                ? "Custom bot is turned on for this channel. Connect the account before starting the bot."
                : "Custom bot is turned off. This channel will use the main bot account.",
            enabled ? "Custom bot on" : "Custom bot off",
            enabled ? ToastTone.Positive : ToastTone.Caution
        );
    }

    private async Task SetWhisperResponsesEnabledAsync(int hostId, bool enabled)
    {
        var saved = await _hostBotAccounts.SetWhisperResponsesEnabledAsync(
            hostId,
            enabled,
            CancellationToken.None
        );
        await LoadAsync();

        if (!saved && enabled)
        {
            _toasts.Error(
                "Turn on custom bot before enabling whisper responses.",
                "Whisper responses not saved"
            );
            return;
        }

        _toasts.Status(
            enabled
                ? "Command replies will use custom-bot whispers when available."
                : "Command replies will use public chat.",
            enabled ? "Whisper responses on" : "Whisper responses off",
            enabled ? ToastTone.Positive : ToastTone.Caution
        );
    }

    private async Task LoadAccessEntriesAsync(HostModAccessState access)
    {
        _whitelistEntries = await _accessListProfiles.ResolveAsync(
            access.Whitelist,
            CancellationToken.None
        );
        _blacklistEntries = await _accessListProfiles.ResolveAsync(
            access.Blacklist,
            CancellationToken.None
        );
    }

    private void ClearAccessEntries()
    {
        _whitelistEntries = [];
        _blacklistEntries = [];
    }

    private void ToastFeatureChange(HostFeatureFlags feature, bool enabled)
    {
        var featureName = FeatureName(feature);
        var channelName = _state is { Login.Length: > 0 } ? $"#{_state.Login}" : "this channel";
        var stateText = enabled ? "enabled" : "disabled";
        var impactText = enabled
            ? "Its chat commands and pages are available again."
            : "Its chat commands and pages are unavailable until you enable it again.";

        _toasts.Status(
            $"{featureName} is now {stateText} for {channelName}. {impactText}",
            $"{featureName} {stateText}",
            enabled ? ToastTone.Positive : ToastTone.Caution
        );
    }

    private static string FeatureName(HostFeatureFlags feature)
    {
        return feature switch
        {
            HostFeatureFlags.Guessing => "Guessing game",
            HostFeatureFlags.Points => "Points",
            HostFeatureFlags.CustomCommands => "Custom commands",
            _ => "Feature",
        };
    }

    private async Task ClearChannelAuthorizationAsync(int hostId)
    {
        await _channelBotAuthorization.ClearAsync(hostId, CancellationToken.None);
        await LoadAsync();
        _toasts.Status("The channel has been disconnected from Twitch chat.");
    }

    private async Task ClearBotOverrideAuthorizationAsync(int hostId)
    {
        await _hostBotAccounts.ClearAsync(hostId, CancellationToken.None);
        await LoadAsync();
        _toasts.Status(
            "The custom bot account has been disconnected.",
            "Custom bot disconnected",
            ToastTone.Caution
        );
    }

    private async Task StartAsync(int hostId)
    {
        var result = await _runtime.StartAsync(hostId, CancellationToken.None);
        await LoadAsync();
        if (result.Succeeded)
        {
            TrackPendingRuntimeTransition();
        }

        _toasts.Publish(
            result.Succeeded ? ToastKind.Status : ToastKind.Error,
            result.Succeeded ? _runtimeStatusMessage : result.Message
        );
    }

    private async Task StopAsync(int hostId)
    {
        var result = await _runtime.StopAsync(hostId, CancellationToken.None);
        await LoadAsync();
        if (result.Succeeded)
        {
            TrackPendingRuntimeTransition();
        }

        _toasts.Publish(
            result.Succeeded ? ToastKind.Status : ToastKind.Error,
            result.Succeeded ? _runtimeStatusMessage : result.Message
        );
    }

    private void TrackPendingRuntimeTransition()
    {
        var runtimeState = _state?.RuntimeStatus?.RuntimeState;
        _pendingRuntimeState = IsRuntimeTransitionPending(runtimeState) ? runtimeState : null;
    }

    private static bool IsRuntimeTransitionPending(BotChannelRuntimeState? runtimeState)
    {
        return runtimeState is BotChannelRuntimeState.Starting or BotChannelRuntimeState.Stopping;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _allowModsByDefaultSaveCts?.Cancel();
            _allowModsByDefaultSaveCts = null;
        }

        base.Dispose(disposing);
    }
}
