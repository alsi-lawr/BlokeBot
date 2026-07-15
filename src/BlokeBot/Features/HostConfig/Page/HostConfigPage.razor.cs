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

    private readonly SemaphoreSlim _allowModsByDefaultSaveGate = new(1, 1);
    private readonly HostModAccessSaveSequence _allowModsByDefaultSaves = new();
    private string _newBlacklistLogin = string.Empty;
    private string _newWhitelistLogin = string.Empty;
    private bool _blockedByMode;
    private PendingRuntimeTransition? _pendingRuntimeTransition;
    private IReadOnlyList<AccessListEntryProfile> _blacklistEntries = [];
    private HostConfigState? _state;
    private IReadOnlyList<AccessListEntryProfile> _whitelistEntries = [];

    private HostedChannelRuntimeLifecycle? _runtimeLifecycle => _state?.RuntimeStatus?.Lifecycle;

    private bool _canStart =>
        _state?.RuntimeStatus is not null
        && _state.IsChannelBotAuthorized
        && _state.RuntimeStatus.ChannelBotAuthorizationScopesCurrent
        && _botAccountCanStart
        && _state.RuntimeStatus.Lifecycle is HostedChannelRuntimeLifecycle.Stopped;

    private bool _canStop =>
        _runtimeLifecycle
            is HostedChannelRuntimeLifecycle.Started
                or HostedChannelRuntimeLifecycle.Starting;

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
        _runtimeLifecycle?.Match(
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-orange-50 px-2.5 text-xs font-bold text-orange-700 ring-1 ring-orange-200",
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-purple-50 px-2.5 text-xs font-bold text-purple-700 ring-1 ring-purple-200"
        )
        ?? "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200";

    private string _runtimeDotClass =>
        _runtimeLifecycle?.Match(
            static _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
            static _ => "h-1.5 w-1.5 rounded-full bg-orange-500",
            static _ => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            static _ => "h-1.5 w-1.5 rounded-full bg-purple-500"
        ) ?? "h-1.5 w-1.5 rounded-full bg-slate-400";

    private string _runtimeText =>
        _runtimeLifecycle?.Match(
            static _ => "offline",
            static _ => "starting",
            static _ => "online",
            static _ => "stopping"
        ) ?? "offline";

    private string _runtimeStatusMessage =>
        _runtimeLifecycle?.Match(
            static _ => "The bot is offline.",
            static _ => "The bot is starting.",
            static _ => "The bot is in chat.",
            static _ => "The bot is leaving chat."
        ) ?? "The bot is offline.";

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
        : _state.RuntimeStatus?.Lifecycle is not HostedChannelRuntimeLifecycle.Stopped
            ? "Wait for the bot to stop before starting it again."
        : "The bot cannot be started right now.";

    private string _stopRuntimeDisabledTooltip =>
        _runtimeLifecycle is HostedChannelRuntimeLifecycle.Stopping
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

    private Task AddAccessAsync(int hostId, AccessListEntryKind kind)
    {
        return ObserveUiOperationAsync(
            nameof(AddAccessAsync),
            () => AddAccessCoreAsync(hostId, kind)
        );
    }

    private async Task AddAccessCoreAsync(int hostId, AccessListEntryKind kind)
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

        await LoadCoreAsync();
    }

    private Task LoadAsync()
    {
        return ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
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
        if (_allowModsByDefaultSaves.HasPendingSubmission)
        {
            return;
        }

        var previousPendingRuntimeTransition = _pendingRuntimeTransition;
        await LoadAsync();

        if (previousPendingRuntimeTransition is null)
        {
            return;
        }

        var currentRuntimeTransition = PendingTransition(_runtimeLifecycle);
        if (currentRuntimeTransition == previousPendingRuntimeTransition)
        {
            return;
        }

        TrackPendingRuntimeTransition();
        if (_runtimeLifecycle is not null)
        {
            _toasts.Status(_runtimeStatusMessage);
        }
    }

    private Task RemoveAccessAsync(int hostId, AccessListEntryKind kind, string login)
    {
        return ObserveUiOperationAsync(
            nameof(RemoveAccessAsync),
            () => RemoveAccessCoreAsync(hostId, kind, login)
        );
    }

    private async Task RemoveAccessCoreAsync(int hostId, AccessListEntryKind kind, string login)
    {
        await _modAccess.RemoveEntryAsync(hostId, kind, login, CancellationToken.None);
        await LoadCoreAsync();
    }

    private Task SetModsEnabledAsync(int hostId, ChangeEventArgs args)
    {
        return ObserveUiOperationAsync(
            nameof(SetModsEnabledAsync),
            () => SetModsEnabledCoreAsync(hostId, args)
        );
    }

    private async Task SetModsEnabledCoreAsync(int hostId, ChangeEventArgs args)
    {
        if (args.Value is true)
        {
            await _modAccess.EnableModeratorAccessAsync(hostId, CancellationToken.None);
        }
        else
        {
            await _modAccess.DisableModeratorAccessAsync(hostId, CancellationToken.None);
        }

        await LoadCoreAsync();
    }

    private void SetAllowModsByDefault(int hostId, bool allowByDefault)
    {
        if (_state is null || _state.ModAccess.AllowModsByDefault == allowByDefault)
        {
            return;
        }

        HostModAccessSaveValidator
            .Validate(hostId, HostModeratorAccessMode.FromAllowModsByDefault(allowByDefault))
            .Match(
                command =>
                {
                    BeginAllowModsByDefaultSave(command, allowByDefault);
                    return true;
                },
                errors =>
                {
                    _toasts.Error(errors[0].Message, "Mod help not saved");
                    return false;
                }
            );
    }

    private void BeginAllowModsByDefaultSave(HostModAccessSaveCommand command, bool allowByDefault)
    {
        if (_state is null)
        {
            return;
        }

        var previousAccess = _state.ModAccess;
        var submission = _allowModsByDefaultSaves.Begin(command, previousAccess);
        _state = _state with
        {
            ModAccess = previousAccess with { AllowModsByDefault = allowByDefault },
        };
        _ = PersistAllowModsByDefaultAsync(submission);
    }

    private async Task PersistAllowModsByDefaultAsync(HostModAccessSaveSubmission submission)
    {
        var cancellationToken = submission.CancellationToken;
        try
        {
            await Task.Delay(_accessModeSaveDebounce, _timeProvider, cancellationToken);
            await _allowModsByDefaultSaveGate.WaitAsync(cancellationToken);
            try
            {
                var result = await _modAccess
                    .SaveModeratorAccess(submission.Command)
                    .ExecuteAsync(cancellationToken);
                await result.Match(
                    _ => ApplyAllowModsByDefaultSuccessAsync(submission),
                    failure => ApplyAllowModsByDefaultFailureAsync(submission, failure)
                );
            }
            finally
            {
                _allowModsByDefaultSaveGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportUiFault(nameof(PersistAllowModsByDefaultAsync), exception);
            await DispatchExceptionAsync(exception);
        }
        finally
        {
            _allowModsByDefaultSaves.Complete(submission);
        }
    }

    private Task ApplyAllowModsByDefaultSuccessAsync(HostModAccessSaveSubmission submission)
    {
        return _allowModsByDefaultSaves.IsCurrent(submission)
            ? InvokeAsync(LoadCoreAsync)
            : Task.CompletedTask;
    }

    private Task ApplyAllowModsByDefaultFailureAsync(
        HostModAccessSaveSubmission submission,
        HostModAccessSaveFailure failure
    )
    {
        if (!_allowModsByDefaultSaves.IsCurrent(submission))
        {
            return Task.CompletedTask;
        }

        return InvokeAsync(() =>
        {
            if (_state is not null)
            {
                _state = _state with { ModAccess = submission.PreviousAccess };
            }

            _toasts.Error(failure.Message, "Mod help not saved");
            StateHasChanged();
        });
    }

    private Task SetFeatureEnabledAsync(int hostId, HostFeatureFlags feature, bool enabled)
    {
        return ObserveUiOperationAsync(
            nameof(SetFeatureEnabledAsync),
            () => SetFeatureEnabledCoreAsync(hostId, feature, enabled)
        );
    }

    private async Task SetFeatureEnabledCoreAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled
    )
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

    private Task SetBotOverrideEnabledAsync(int hostId, bool enabled)
    {
        return ObserveUiOperationAsync(
            nameof(SetBotOverrideEnabledAsync),
            () => SetBotOverrideEnabledCoreAsync(hostId, enabled)
        );
    }

    private async Task SetBotOverrideEnabledCoreAsync(int hostId, bool enabled)
    {
        var runtimeWasActive =
            _runtimeLifecycle
            is HostedChannelRuntimeLifecycle.Starting
                or HostedChannelRuntimeLifecycle.Started;
        if (enabled)
        {
            await _hostBotAccounts.UseCustomBotAsync(hostId, CancellationToken.None);
        }
        else
        {
            await _hostBotAccounts.UseMainBotAsync(hostId, CancellationToken.None);
        }

        await LoadCoreAsync();
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

    private Task SetWhisperResponsesEnabledAsync(int hostId, bool enabled)
    {
        return ObserveUiOperationAsync(
            nameof(SetWhisperResponsesEnabledAsync),
            () => SetWhisperResponsesEnabledCoreAsync(hostId, enabled)
        );
    }

    private async Task SetWhisperResponsesEnabledCoreAsync(int hostId, bool enabled)
    {
        var outcome = enabled
            ? await _hostBotAccounts.EnableWhisperResponsesAsync(hostId, CancellationToken.None)
            : await _hostBotAccounts.DisableWhisperResponsesAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();

        outcome
            .Match<Action>(_ => ShowSavedStatus, _ => ShowRejectedStatus, _ => ShowRejectedStatus)
            .Invoke();

        void ShowRejectedStatus()
        {
            _toasts.Error(
                "Turn on custom bot before enabling whisper responses.",
                "Whisper responses not saved"
            );
        }

        void ShowSavedStatus()
        {
            _toasts.Status(
                enabled
                    ? "Command replies will use custom-bot whispers when available."
                    : "Command replies will use public chat.",
                enabled ? "Whisper responses on" : "Whisper responses off",
                enabled ? ToastTone.Positive : ToastTone.Caution
            );
        }
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

    private Task ClearChannelAuthorizationAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(ClearChannelAuthorizationAsync),
            () => ClearChannelAuthorizationCoreAsync(hostId)
        );
    }

    private async Task ClearChannelAuthorizationCoreAsync(int hostId)
    {
        await _channelBotAuthorization.ClearAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();
        _toasts.Status("The channel has been disconnected from Twitch chat.");
    }

    private Task ClearBotOverrideAuthorizationAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(ClearBotOverrideAuthorizationAsync),
            () => ClearBotOverrideAuthorizationCoreAsync(hostId)
        );
    }

    private async Task ClearBotOverrideAuthorizationCoreAsync(int hostId)
    {
        await _hostBotAccounts.ClearAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();
        _toasts.Status(
            "The custom bot account has been disconnected.",
            "Custom bot disconnected",
            ToastTone.Caution
        );
    }

    private Task StartAsync(int hostId)
    {
        return ObserveUiOperationAsync(nameof(StartAsync), () => StartCoreAsync(hostId));
    }

    private async Task StartCoreAsync(int hostId)
    {
        var result = await _runtime.StartAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();
        if (result.Succeeded)
        {
            TrackPendingRuntimeTransition();
        }

        _toasts.Publish(
            result.Succeeded ? ToastKind.Status : ToastKind.Error,
            result.Succeeded ? _runtimeStatusMessage : result.Message
        );
    }

    private Task StopAsync(int hostId)
    {
        return ObserveUiOperationAsync(nameof(StopAsync), () => StopCoreAsync(hostId));
    }

    private async Task StopCoreAsync(int hostId)
    {
        var result = await _runtime.StopAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();
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
        _pendingRuntimeTransition = PendingTransition(_runtimeLifecycle);
    }

    private static PendingRuntimeTransition? PendingTransition(
        HostedChannelRuntimeLifecycle? lifecycle
    )
    {
        return lifecycle?.Match<PendingRuntimeTransition?>(
            static _ => null,
            static _ => PendingRuntimeTransition.Starting,
            static _ => null,
            static _ => PendingRuntimeTransition.Stopping
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _allowModsByDefaultSaves.Dispose();
        }

        base.Dispose(disposing);
    }

    private enum PendingRuntimeTransition
    {
        Starting,
        Stopping,
    }
}
