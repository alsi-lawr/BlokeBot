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
    private static readonly TimeSpan AccessModeSaveDebounce = TimeSpan.FromMilliseconds(180);

    private CancellationTokenSource? allowModsByDefaultSaveCts;
    private string newBlacklistLogin = string.Empty;
    private string newWhitelistLogin = string.Empty;
    private int allowModsByDefaultSaveVersion;
    private bool blockedByMode;
    private BotChannelRuntimeState? pendingRuntimeState;
    private IReadOnlyList<AccessListEntryProfile> blacklistEntries = [];
    private HostConfigState? state;
    private IReadOnlyList<AccessListEntryProfile> whitelistEntries = [];

    private bool CanStart =>
        state?.RuntimeStatus is not null
        && state.IsChannelBotAuthorized
        && state.RuntimeStatus.ChannelBotAuthorizationScopesCurrent
        && BotAccountCanStart
        && state.RuntimeStatus.RuntimeState is BotChannelRuntimeState.Stopped;

    private bool CanStop =>
        state?.RuntimeStatus?.RuntimeState
            is BotChannelRuntimeState.Started
                or BotChannelRuntimeState.Starting;

    private string AuthorizationBadgeClass =>
        state?.IsChannelBotAuthorized == true
        && state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200"
            : "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200";

    private string AuthorizationDotClass =>
        state?.IsChannelBotAuthorized == true
        && state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
            : "h-1.5 w-1.5 rounded-full bg-amber-500";

    private string AuthorizationText =>
        state?.IsChannelBotAuthorized == true
        && state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "channel authorized"
        : state?.IsChannelBotAuthorized == true ? "needs update"
        : "channel needs auth";

    private string RuntimeBadgeClass =>
        state?.RuntimeStatus?.RuntimeState switch
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

    private string RuntimeDotClass =>
        state?.RuntimeStatus?.RuntimeState switch
        {
            BotChannelRuntimeState.Starting => "h-1.5 w-1.5 rounded-full bg-orange-500",
            BotChannelRuntimeState.Started => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            BotChannelRuntimeState.Stopping => "h-1.5 w-1.5 rounded-full bg-purple-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string RuntimeText =>
        state?.RuntimeStatus?.RuntimeState switch
        {
            BotChannelRuntimeState.Starting => "bot starting",
            BotChannelRuntimeState.Started => "bot started",
            BotChannelRuntimeState.Stopping => "bot stopping",
            _ => "bot stopped",
        };

    private string RuntimeStatusMessage =>
        state?.RuntimeStatus?.RuntimeState switch
        {
            BotChannelRuntimeState.Starting => "Bot starting.",
            BotChannelRuntimeState.Started => "Bot started.",
            BotChannelRuntimeState.Stopping => "Bot stopping.",
            _ => "Bot stopped.",
        };

    private bool BotAccountCanStart =>
        state?.BotOverride.Enabled != true
        || state.BotOverride.Status.State == BotAccountAuthorizationState.Ready;

    private string BotAccountStatusReloadKey =>
        state is null
            ? string.Empty
            : string.Join(
                ":",
                state.Login,
                state.BotOverride.Enabled,
                state.BotOverride.Status.State,
                state.BotOverride.Status.AuthorizedLogin ?? string.Empty,
                string.Join(",", state.BotOverride.Status.GrantedScopes)
            );

    private string AccessModeSegmentClass =>
        state?.ModAccess.AllowModsByDefault == false
            ? "segmented-motion segmented-motion--second"
            : "segmented-motion";

    private static string AccessModeTabClass(bool active) =>
        active ? "segmented-motion__tab segmented-motion__tab--active" : "segmented-motion__tab";

    private static string FeatureBadgeClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? "inline-flex h-5 shrink-0 items-center gap-1.5 rounded-full bg-emerald-50 px-2 text-[0.68rem] font-bold text-emerald-700 ring-1 ring-emerald-200"
            : "inline-flex h-5 shrink-0 items-center gap-1.5 rounded-full bg-slate-100 px-2 text-[0.68rem] font-bold text-slate-600 ring-1 ring-slate-200";

    private static string FeatureCardClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? "feature-toggle-card grid min-h-24 grid-cols-[2.25rem_minmax(0,1fr)] items-center gap-3 rounded-lg p-3 text-left"
            : "feature-toggle-card feature-toggle-card--disabled grid min-h-24 grid-cols-[2.25rem_minmax(0,1fr)] items-center gap-3 rounded-lg p-3 text-left";

    private static string FeatureDotClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
            : "h-1.5 w-1.5 rounded-full bg-slate-400";

    private static string FeatureIconClass(HostFeatureCardState feature) =>
        feature.Enabled
            ? feature.Feature switch
            {
                HostFeatureFlags.Points => "feature-toggle-card__icon text-emerald-600",
                _ => "feature-toggle-card__icon text-blue-600",
            }
            : "feature-toggle-card__icon text-slate-500";

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
                _ => string.Empty,
            }
        );

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                work => InvokeAsync(work),
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task AddAccessAsync(int hostId, AccessListEntryKind kind)
    {
        var login = kind == AccessListEntryKind.Whitelist ? newWhitelistLogin : newBlacklistLogin;
        await ModAccess.AddEntryAsync(hostId, kind, login, CancellationToken.None);
        if (kind == AccessListEntryKind.Whitelist)
            newWhitelistLogin = string.Empty;
        else
            newBlacklistLogin = string.Empty;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var pageContext = await LoadPageContextAsync();
        var session = pageContext.Session;
        var selection = pageContext.HostSelection;
        if (pageContext.IsBotAccount)
        {
            blockedByMode = true;
            state = null;
            ClearAccessEntries();
            return;
        }

        state = await HostConfig.LoadAsync(session, CancellationToken.None);

        blockedByMode =
            selection is not null
            && !session.CurrentHostRoleIs(AuthRole.Streamer)
            && state?.IsHostCreated == true;
        if (blockedByMode)
        {
            state = null;
            ClearAccessEntries();
            return;
        }

        if (state is { IsHostCreated: true } loadedState)
            await LoadAccessEntriesAsync(loadedState.ModAccess);
        else
            ClearAccessEntries();
    }

    private async Task ReloadForEventAsync()
    {
        var previousPendingRuntimeState = pendingRuntimeState;
        await LoadAsync();

        if (previousPendingRuntimeState is null)
            return;

        var currentRuntimeState = state?.RuntimeStatus?.RuntimeState;
        if (currentRuntimeState == previousPendingRuntimeState)
            return;

        TrackPendingRuntimeTransition();
        if (currentRuntimeState is not null)
            Toasts.Status(RuntimeStatusMessage);
    }

    private async Task RemoveAccessAsync(int hostId, AccessListEntryKind kind, string login)
    {
        await ModAccess.RemoveEntryAsync(hostId, kind, login, CancellationToken.None);
        await LoadAsync();
    }

    private async Task SetModsEnabledAsync(int hostId, ChangeEventArgs args)
    {
        await ModAccess.SetModsEnabledAsync(hostId, args.Value is true, CancellationToken.None);
        await LoadAsync();
    }

    private void SetAllowModsByDefault(int hostId, bool allowByDefault)
    {
        if (state is null || state.ModAccess.AllowModsByDefault == allowByDefault)
            return;

        var previousAccess = state.ModAccess;
        var version = ++allowModsByDefaultSaveVersion;
        state = state with
        {
            ModAccess = previousAccess with { AllowModsByDefault = allowByDefault },
        };

        allowModsByDefaultSaveCts?.Cancel();
        var saveCts = new CancellationTokenSource();
        allowModsByDefaultSaveCts = saveCts;

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
            await Task.Delay(AccessModeSaveDebounce, cancellationToken);
            await ModAccess.SetAllowModsByDefaultAsync(hostId, allowByDefault, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (version == allowModsByDefaultSaveVersion)
                await InvokeAsync(LoadAsync);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            if (version != allowModsByDefaultSaveVersion)
                return;

            await InvokeAsync(() =>
            {
                if (state is not null)
                    state = state with { ModAccess = previousAccess };

                Toasts.Error(
                    "Moderator access mode could not be saved. Your previous setting has been restored.",
                    "Moderator access not saved"
                );
                StateHasChanged();
            });
        }
        finally
        {
            if (ReferenceEquals(allowModsByDefaultSaveCts, saveCts))
                allowModsByDefaultSaveCts = null;

            saveCts.Dispose();
        }
    }

    private async Task SetFeatureEnabledAsync(int hostId, HostFeatureFlags feature, bool enabled)
    {
        await Features.SetEnabledAsync(hostId, feature, enabled, CancellationToken.None);
        await LoadAsync();
        ToastFeatureChange(feature, enabled);
    }

    private async Task SetBotOverrideEnabledAsync(int hostId, bool enabled)
    {
        var runtimeWasActive =
            state?.RuntimeStatus?.RuntimeState
            is BotChannelRuntimeState.Starting
                or BotChannelRuntimeState.Started;
        await HostBotAccounts.SetOverrideEnabledAsync(hostId, enabled, CancellationToken.None);
        await LoadAsync();
        if (runtimeWasActive)
            TrackPendingRuntimeTransition();

        Toasts.Status(
            enabled
                ? "Custom bot override is enabled for this channel. Authorize the account before starting the bot."
                : "Custom bot override is disabled for this channel. The global bot account will be used.",
            enabled ? "Bot override enabled" : "Bot override disabled",
            enabled ? ToastTone.Positive : ToastTone.Caution
        );
    }

    private async Task LoadAccessEntriesAsync(HostModAccessState access)
    {
        whitelistEntries = await AccessListProfiles.ResolveAsync(
            access.Whitelist,
            CancellationToken.None
        );
        blacklistEntries = await AccessListProfiles.ResolveAsync(
            access.Blacklist,
            CancellationToken.None
        );
    }

    private void ClearAccessEntries()
    {
        whitelistEntries = [];
        blacklistEntries = [];
    }

    private void ToastFeatureChange(HostFeatureFlags feature, bool enabled)
    {
        var featureName = FeatureName(feature);
        var channelName = state is { Login.Length: > 0 } ? $"#{state.Login}" : "this channel";
        var stateText = enabled ? "enabled" : "disabled";
        var impactText = enabled
            ? "Its chat commands and pages are available again."
            : "Its chat commands and pages are unavailable until you enable it again.";

        Toasts.Status(
            $"{featureName} is now {stateText} for {channelName}. {impactText}",
            $"{featureName} {stateText}",
            enabled ? ToastTone.Positive : ToastTone.Caution
        );
    }

    private static string FeatureName(HostFeatureFlags feature) =>
        feature switch
        {
            HostFeatureFlags.Guessing => "Guessing game",
            HostFeatureFlags.Points => "Points",
            _ => "Feature",
        };

    private async Task ClearChannelAuthorizationAsync(int hostId)
    {
        await ChannelBotAuthorization.ClearAsync(hostId, CancellationToken.None);
        await LoadAsync();
        Toasts.Status("Channel authorization cleared.");
    }

    private async Task ClearBotOverrideAuthorizationAsync(int hostId)
    {
        await HostBotAccounts.ClearAsync(hostId, CancellationToken.None);
        await LoadAsync();
        Toasts.Status(
            "Custom bot authorization cleared.",
            "Bot override updated",
            ToastTone.Caution
        );
    }

    private async Task StartAsync(int hostId)
    {
        var result = await Runtime.StartAsync(hostId, CancellationToken.None);
        await LoadAsync();
        if (result.Succeeded)
            TrackPendingRuntimeTransition();
        Toasts.Publish(
            result.Succeeded ? ToastKind.Status : ToastKind.Error,
            result.Succeeded ? RuntimeStatusMessage : result.Message
        );
    }

    private async Task StopAsync(int hostId)
    {
        var result = await Runtime.StopAsync(hostId, CancellationToken.None);
        await LoadAsync();
        if (result.Succeeded)
            TrackPendingRuntimeTransition();
        Toasts.Publish(
            result.Succeeded ? ToastKind.Status : ToastKind.Error,
            result.Succeeded ? RuntimeStatusMessage : result.Message
        );
    }

    private void TrackPendingRuntimeTransition()
    {
        var runtimeState = state?.RuntimeStatus?.RuntimeState;
        pendingRuntimeState = IsRuntimeTransitionPending(runtimeState) ? runtimeState : null;
    }

    private static bool IsRuntimeTransitionPending(BotChannelRuntimeState? runtimeState) =>
        runtimeState is BotChannelRuntimeState.Starting or BotChannelRuntimeState.Stopping;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            allowModsByDefaultSaveCts?.Cancel();
            allowModsByDefaultSaveCts = null;
        }

        base.Dispose(disposing);
    }
}
