using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Hosts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components.Layout;

public partial class AccountMenu
{
    private bool _preferencesEnabled = true;

    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    [Inject]
    private IJSRuntime _js { get; set; } = default!;

    [Inject]
    private IOptions<PrivacyNoticeOptions> _privacy { get; set; } = default!;

    private string? _noticeUrl => _privacy.Value.NoticeUri?.ToString();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _preferencesEnabled = await _js.InvokeAsync<bool>("blokeBotPreferences.enabled");
        StateHasChanged();
    }

    private async Task TogglePreferenceSavingAsync() =>
        _preferencesEnabled = await _js.InvokeAsync<bool>(
            _preferencesEnabled ? "blokeBotPreferences.disable" : "blokeBotPreferences.enable"
        );

    private BotHostSelection? _selection =>
        Session.State.Match<BotHostSelection?>(
            static _ => null,
            static selected => selected.Selection,
            static _ => null
        );

    private string _currentPath => "/" + _navigation.ToBaseRelativePath(_navigation.Uri);

    private bool _isAdminEditing => Session.IsAdminEditing;

    private string _role => Session.DisplayRole;

    private string? AccountImageUrl() =>
        _isAdminEditing && !string.IsNullOrWhiteSpace(_selection?.Current.ProfileImageUrl)
            ? _selection.Current.ProfileImageUrl
            : Session.ProfileImageUrl;

    private string IdentityText() =>
        _selection?.Current.Role == AuthRole.Admin
        && !string.IsNullOrWhiteSpace(Session.AdminEditingLogin)
            ? $"#{_selection.Current.DisplayName} ({Session.AdminEditingLogin})"
            : Session.DisplayText;

    private string AccountInitial()
    {
        var identity = IdentityText();
        return string.IsNullOrWhiteSpace(identity) ? "?" : identity[..1].ToUpperInvariant();
    }

    private static string RoleBadgeClass(string role)
    {
        var color = role.ToLowerInvariant() switch
        {
            "streamer" => "bg-emerald-50 text-emerald-700 ring-emerald-200",
            "moderator" => "app-blue-badge",
            "admin" => "bg-red-50 text-red-700 ring-red-200",
            "bot" => "bg-purple-50 text-purple-700 ring-purple-200",
            _ => "bg-sky-50 text-sky-700 ring-sky-200",
        };

        return $"account-menu__role-badge inline-flex h-6 shrink-0 items-center justify-center whitespace-nowrap rounded-full px-2 text-center text-xs font-semibold ring-1 {color}";
    }
}
