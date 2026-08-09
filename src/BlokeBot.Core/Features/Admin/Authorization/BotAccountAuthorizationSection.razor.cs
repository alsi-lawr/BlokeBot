using BlokeBot.Core.Features.HostedChannels.Authorization;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Admin.Authorization;

public partial class BotAccountAuthorizationSection
{
    [Parameter]
    public BotAccountAuthorizationStatus? Status { get; set; }

    [Parameter]
    public string AuthorizeButtonText { get; set; } = "Connect bot account";

    [Parameter]
    public string AuthorizationStartUrl { get; set; } = "/oauth/start";

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public string? DisabledMessage { get; set; }

    [Parameter]
    public string ConfiguredAccountFallbackText { get; set; } = "not set";

    [Parameter, EditorRequired]
    public Func<Task> Clear { get; set; } = static () => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<Task> Refresh { get; set; } = static () => Task.CompletedTask;

    private string _authorizedAccountText =>
        Status?.AuthorizedLogin is { Length: > 0 } login
            ? $"@{login}"
            : "No Twitch account connected";

    private string _configuredAccountText =>
        Status?.ConfiguredBotLogin is { Length: > 0 } login
            ? $"@{login}"
            : ConfiguredAccountFallbackText;

    private bool _connectedChatterScopeMissing =>
        Status?.MissingScopes.Contains(Scopes.ModeratorReadChatters, StringComparer.Ordinal)
        == true;

    private string _containerClass =>
        Disabled
            ? "surface-muted rounded-lg p-4 opacity-60 grayscale-[45%]"
            : "surface-muted rounded-lg p-4";

    private string _statusBadgeClass =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled =>
                "status-pill bg-slate-100 text-slate-600 ring-1 ring-slate-200",
            BotAccountAuthorizationState.Ready =>
                "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200",
            BotAccountAuthorizationState.WrongAccount
            or BotAccountAuthorizationState.MissingScopes =>
                "status-pill bg-amber-50 text-amber-700 ring-1 ring-amber-200",
            _ => "status-pill bg-slate-100 text-slate-600 ring-1 ring-slate-200",
        };

    private string _statusDotClass =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled => "status-pill__dot bg-slate-400",
            BotAccountAuthorizationState.Ready => "status-pill__dot bg-emerald-500",
            BotAccountAuthorizationState.WrongAccount
            or BotAccountAuthorizationState.MissingScopes => "status-pill__dot bg-amber-500",
            _ => "status-pill__dot bg-slate-400",
        };

    private string _statusText =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled => "disabled",
            BotAccountAuthorizationState.Ready => "ready",
            BotAccountAuthorizationState.WrongAccount => "wrong account",
            BotAccountAuthorizationState.MissingScopes => "needs more access",
            BotAccountAuthorizationState.NotAuthorized => "not connected",
            _ => "unknown",
        };

    private string _connectionHelpText =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled =>
                "Turn this on when you want to connect this account.",
            BotAccountAuthorizationState.Ready => "This account has everything BlokeBot needs.",
            BotAccountAuthorizationState.WrongAccount =>
                "Reconnect with the Twitch account shown above.",
            BotAccountAuthorizationState.MissingScopes =>
                "Reconnect this account so Twitch can give BlokeBot the access it needs.",
            BotAccountAuthorizationState.NotAuthorized => "Connect a Twitch account to continue.",
            _ => "Refresh to check this Twitch connection.",
        };
}
