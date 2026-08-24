using System.Collections.Immutable;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class NavMenuPluginFeatures
{
    private readonly CancellationTokenSource _stopping = new();
    private ImmutableArray<PluginNavigationItem> _plugins = [];
    private Task? _declarationWatch;
    private Task? _stateWatch;

    [Parameter]
    public PluginHostId? HostId { get; set; }

    [Parameter, EditorRequired]
    public required NavMenuRouteBindings Routes { get; set; }

    protected override void OnInitialized()
    {
        _declarationWatch = WatchDeclarationsAsync();
        _stateWatch = WatchStatesAsync();
    }

    protected override void OnParametersSet() => Refresh();

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        try
        {
            await Task.WhenAll(
                _declarationWatch ?? Task.CompletedTask,
                _stateWatch ?? Task.CompletedTask
            );
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        _stopping.Dispose();
    }

    private static string ReadinessLabel(PluginFeatureReadiness readiness) =>
        readiness.Match(_ => "Setup", _ => "Needs attention", _ => "Ready");

    private void Refresh() =>
        _plugins = PluginFeatureNavigation.Project(
            HostId,
            DeclarationProvider.Current,
            SnapshotProvider.Current
        );

    private async Task WatchDeclarationsAsync()
    {
        var version = DeclarationProvider.CurrentVersion;
        while (!_stopping.IsCancellationRequested)
        {
            version = await DeclarationProvider.WaitForChangeAsync(version, _stopping.Token);
            await InvokeAsync(RefreshAndRender);
        }
    }

    private async Task WatchStatesAsync()
    {
        var version = SnapshotProvider.CurrentVersion;
        while (!_stopping.IsCancellationRequested)
        {
            version = await SnapshotProvider.WaitForChangeAsync(version, _stopping.Token);
            await InvokeAsync(RefreshAndRender);
        }
    }

    private void RefreshAndRender()
    {
        Refresh();
        StateHasChanged();
    }
}
