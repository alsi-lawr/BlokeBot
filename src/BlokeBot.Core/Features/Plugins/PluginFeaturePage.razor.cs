using BlokeBot.Core.Components.Layout;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginFeaturePage
{
    private ElementReference _iframe;
    private PageLoadState _loadState = new PageLoadState.Loading("Loading plugin page.");
    private PageSaveFeedback? _feedback;
    private PluginPageDocument? _document;
    private PluginPageEndpoint? _endpoint;
    private PluginPageSession? _session;
    private IJSObjectReference? _bridge;
    private DotNetObjectReference<PluginFeaturePage>? _bridgeReference;
    private bool _bridgePending;
    private string _pluginName = "Plugin";
    private string _title = "Plugin page";
    private string _description = "Plugin-provided tools for the selected channel.";
    private string? _embeddedUrl;
    private string? _stateTitle;
    private string _stateDescription = string.Empty;
    private string? _stateActionRoute;
    private string _stateActionLabel = string.Empty;

    [Inject]
    private PluginPageCatalog _pages { get; set; } = default!;

    [Inject]
    private PluginPageSessionRegistry _sessions { get; set; } = default!;

    [Inject]
    private IPluginDispatchInvoker _invoker { get; set; } = default!;

    [Inject]
    private IPluginDispatchSnapshotProvider _dispatch { get; set; } = default!;

    [Inject]
    private IJSRuntime _javascript { get; set; } = default!;

    [Inject]
    private NavigationManager _navigation { get; set; } = default!;

    [Parameter]
    public string PluginIdValue { get; set; } = string.Empty;

    [Parameter]
    public string FeatureIdValue { get; set; } = string.Empty;

    [Parameter]
    public string RouteValue { get; set; } = string.Empty;

    private string _statusClass =>
        _endpoint is null ? "status-pill status-pill--amber" : "status-pill status-pill--green";

    private string _statusLabel => _endpoint is null ? "Unavailable" : "Ready";

    protected override async Task OnParametersSetAsync()
    {
        await ResetPageAsync();
        _ = await LoadPageContextAsync();
        if (
            Host is null
            || !PluginId.TryCreate(PluginIdValue, out var pluginId)
            || !PluginFeatureId.TryCreate(FeatureIdValue, out var featureId)
            || !PluginHostId.TryCreate(Host.Id, out var hostId)
        )
        {
            Failure("Choose a channel and a valid plugin page.");
            return;
        }
        await RunSelectedHostMutationAsync(
            Host.Id,
            () => LoadCoreAsync(pluginId, featureId, hostId)
        );
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_bridgePending || _session is null)
        {
            return;
        }
        _bridgePending = false;
        _bridgeReference = DotNetObjectReference.Create(this);
        var module = await _javascript.InvokeAsync<IJSObjectReference>(
            "import",
            "./js/plugin-page-bridge.js"
        );
        _bridge = await module.InvokeAsync<IJSObjectReference>(
            "initializePluginPageBridge",
            _iframe,
            _bridgeReference,
            _session.Id.Value.ToString("D"),
            _session.MessageOrigins,
            PluginContractLimits.MaximumPageMessageBytes
        );
        await module.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        RemoveSession();
        await DisposeBridgeAsync();
        Dispose();
    }

    private async Task DisposeBridgeAsync()
    {
        if (_bridge is not null)
        {
            try
            {
                await _bridge.InvokeVoidAsync("dispose");
                await _bridge.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            _bridge = null;
        }
        _bridgeReference?.Dispose();
        _bridgeReference = null;
    }

    private async Task ResetPageAsync()
    {
        RemoveSession();
        await DisposeBridgeAsync();
        _loadState = new PageLoadState.Loading("Loading plugin page.");
        _feedback = null;
        _document = null;
        _endpoint = null;
        _embeddedUrl = null;
        _stateTitle = null;
        _stateActionRoute = null;
        _bridgePending = false;
    }

    private void RemoveSession()
    {
        if (_session is not null)
        {
            _sessions.Remove(_session.Id);
            _session = null;
        }
    }

    private void Failure(string message) =>
        _loadState = new PageLoadState.Failure(message, OnParametersSetAsync);
}
