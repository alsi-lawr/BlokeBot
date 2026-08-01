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

namespace BlokeBot.Core.Components;

public partial class TextAreaField
{
    private ElementReference _input;

    [Parameter]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public required string Id { get; set; }

    [Parameter]
    public string? ErrorMessage { get; set; }

    [Parameter]
    public long FocusRequest { get; set; }

    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    private bool _hasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private string _inputId => Id;

    private string? _errorId => _hasError ? $"{_inputId}-error" : null;

    public ValueTask FocusAsync() => _input.FocusAsync();

    private long _handledFocusRequest;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (FocusRequest <= _handledFocusRequest)
        {
            return;
        }

        _handledFocusRequest = FocusRequest;
        await FocusAsync();
    }

    private Task OnInput(ChangeEventArgs e) =>
        ValueChanged.InvokeAsync(e.Value?.ToString() ?? string.Empty);
}
