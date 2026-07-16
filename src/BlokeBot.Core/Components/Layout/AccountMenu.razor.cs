using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Core.Components.Layout;

public partial class AccountMenu
{
    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    private BotHostSelection? _selection =>
        Session.State.Match<BotHostSelection?>(
            _ => null,
            selected => selected.Selection,
            _ => null
        );

    private string _currentPath => "/" + _navigation.ToBaseRelativePath(_navigation.Uri);

    private bool _isAdminEditing => Session.IsAdminEditing;

    private string _role => Session.DisplayRole;

    private string? AccountImageUrl()
    {
        return _isAdminEditing && !string.IsNullOrWhiteSpace(_selection?.Current.ProfileImageUrl)
            ? _selection.Current.ProfileImageUrl
            : Session.ProfileImageUrl;
    }

    private string IdentityText()
    {
        if (
            _selection?.Current.Role == AuthRole.Admin
            && !string.IsNullOrWhiteSpace(Session.AdminEditingLogin)
        )
        {
            return $"#{_selection.Current.DisplayName} ({Session.AdminEditingLogin})";
        }

        return Session.DisplayText;
    }

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

        return $"account-menu__role-badge inline-flex h-6 w-[5.75rem] items-center justify-center rounded-full px-2 text-center text-xs font-semibold ring-1 {color}";
    }
}
