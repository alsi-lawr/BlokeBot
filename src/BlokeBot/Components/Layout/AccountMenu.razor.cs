using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
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
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Components.Layout;

public partial class AccountMenu
{
    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    private BotHostSelection? Selection => Session.HostSelection;

    private string CurrentReturnUrl
    {
        get
        {
            var path = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
            return Uri.EscapeDataString(path);
        }
    }

    private string ExitImpersonationHref => $"/auth/exit-admin?returnUrl={CurrentReturnUrl}";

    private bool IsAdminEditing => Session.IsAdminEditing;

    private string Role => Session.DisplayRole;

    private string? AccountImageUrl()
    {
        return IsAdminEditing && !string.IsNullOrWhiteSpace(Selection?.Current.ProfileImageUrl)
            ? Selection.Current.ProfileImageUrl
            : Session.ProfileImageUrl;
    }

    private string IdentityText()
    {
        if (
            Selection?.Current.Role == AuthRole.Admin
            && !string.IsNullOrWhiteSpace(Session.AdminEditingLogin)
        )
        {
            return $"{Selection.Current.DisplayName} ({Session.AdminEditingLogin})";
        }

        return Session.DisplayText;
    }

    private static string RoleBadgeClass(string role)
    {
        var color = role.ToLowerInvariant() switch
        {
            "streamer" => "bg-emerald-50 text-emerald-700 ring-emerald-200",
            "admin" => "bg-red-50 text-red-700 ring-red-200",
            "bot" => "bg-purple-50 text-purple-700 ring-purple-200",
            _ => "bg-sky-50 text-sky-700 ring-sky-200",
        };

        return $"inline-flex h-6 w-[5.75rem] items-center justify-center rounded-full px-2 text-center text-xs font-semibold ring-1 {color}";
    }
}
