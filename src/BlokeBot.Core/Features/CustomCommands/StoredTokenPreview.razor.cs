using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CustomCommands;

public partial class StoredTokenPreview
{
    [Parameter, EditorRequired]
    public required string Id { get; set; }

    [Parameter]
    public int HostId { get; set; }

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    [Parameter]
    public string Text { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> TextChanged { get; set; }
    private IReadOnlyList<CustomValueDefinitionDraft> _definitions = [];
    private CustomStoredValueSession? _sandbox;
    private int _loadedHost = -1;
    private int _definitionId;
    private string _nameOperand = string.Empty;
    private string _operation = "get";
    private string _key = "{arg1}";
    private string _operand = "{args}";
    private string _viewerLogin = string.Empty;
    private string _args = "game";
    private bool _singleArgument;
    private string _sampleContext =
        "User defaults · Global saved values · sample viewer not selected";
    private string? _error;
    private CustomValueDefinitionDraft? _selected =>
        _definitions.SingleOrDefault(x => x.Id == _definitionId);
    private string _token =>
        _selected is not { } definition
            ? string.Empty
            : $"{{{(definition.Kind == CustomValueKind.Dictionary ? "dict" : "var")}_{_operation}|{definition.Scope.ToString().ToLowerInvariant()}|{_nameOperand}{(definition.Kind == CustomValueKind.Dictionary ? $"|{_key}" : string.Empty)}{(_operation == "get" ? string.Empty : $"|{_operand}")}}}";
    private string _preview =>
        CustomCommandTemplateRenderer.RenderCommandPreview(
            Text,
            new()
            {
                Message = new(
                    _viewerLogin.Length == 0 ? "viewer" : _viewerLogin,
                    Channel,
                    string.Empty,
                    string.Empty,
                    new Dictionary<string, string>()
                ),
                CommandName = "preview",
                Responder = static (_, _) => ValueTask.CompletedTask,
            },
            _singleArgument
                ? (string.IsNullOrWhiteSpace(_args) ? [] : [_args])
                : _args.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            null,
            _sandbox
        );

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedHost == HostId)
        {
            return;
        }
        _loadedHost = HostId;
        _definitions = [];
        _sandbox = null;
        _definitionId = 0;
        _nameOperand = string.Empty;
        _viewerLogin = string.Empty;
        _sampleContext = $"Channel {Channel} · User defaults · sample viewer not selected";
        try
        {
            _definitions = await _values.DefinitionsAsync(HostId, default);
            _sandbox = await _values.SandboxAsync(HostId, string.Empty, default);
            _error = null;
        }
        catch (Exception exception)
            when (exception
                    is System.Data.Common.DbException
                        or Microsoft.EntityFrameworkCore.DbUpdateException
            )
        {
            _error = "Sample values could not load. Your message is retained.";
        }
    }

    private void SelectDefinition() =>
        _nameOperand = _selected is null
            ? string.Empty
            : CustomCommandTemplateRenderer.NameOperand(_selected.Name);

    private Task InsertAsync() => TextChanged.InvokeAsync(Text + _token);

    private async Task SelectViewerAsync()
    {
        CustomCommandViewerResolution resolved;
        try
        {
            resolved = await _viewers.ResolveAsync(_viewerLogin, default);
        }
        catch (HttpRequestException)
        {
            _error = "Viewer lookup is unavailable. Your message is retained. Try again.";
            return;
        }
        if (resolved is not CustomCommandViewerResolution.Found found)
        {
            _error = "Viewer not found. Enter an existing Twitch login.";
            return;
        }
        try
        {
            _sandbox = await _values.SandboxAsync(HostId, found.Viewer.TwitchUserId, default);
        }
        catch (Exception exception)
            when (exception
                    is System.Data.Common.DbException
                        or Microsoft.EntityFrameworkCore.DbUpdateException
            )
        {
            _error =
                "Sample values could not load. Your message is retained. Check database storage and retry.";
            return;
        }
        _sampleContext =
            $"Channel {Channel} · {found.Viewer.DisplayName} · viewer ID {found.Viewer.TwitchUserId} · copy of saved values";
        _error = null;
    }
}
