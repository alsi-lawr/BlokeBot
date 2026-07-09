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

public partial class TopBarControls
{
    private static bool ShowsHostSelector(AuthenticatedSession session) =>
        !session.IsAdminEditing && !session.IsBotAccount;

    private static string ControlsGridClass(
        BotHostSelection? selection,
        bool isAdminEditing,
        bool isBotAccount,
        bool showHostSelector
    )
    {
        if (isBotAccount)
            return "grid items-center gap-3 md:grid-cols-[minmax(18rem,auto)]";

        if (selection is null)
        {
            return showHostSelector
                ? "grid items-center gap-3 md:grid-cols-[auto_minmax(18rem,auto)]"
                : "grid items-center gap-3 md:grid-cols-[minmax(18rem,auto)]";
        }

        if (isAdminEditing || !showHostSelector)
            return "grid items-center gap-3 md:grid-cols-[auto_minmax(18rem,auto)]";

        return "grid items-center gap-3 md:grid-cols-[auto_auto_minmax(18rem,auto)]";
    }
}
