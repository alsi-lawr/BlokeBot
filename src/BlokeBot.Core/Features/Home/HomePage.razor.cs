using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.Home;

public partial class HomePage
{
    private HostConfigState? _state;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private Task LoadAsync()
    {
        return ObserveRouteLoadAsync(LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        var context = await LoadPageContextAsync();
        var result = await _hostConfig.Load(context.Session).ExecuteAsync(CancellationToken.None);
        _state = result.Match(
            option => option.Match<HostConfigState?>(state => state, () => null),
            _ => throw new UnreachableException()
        );
    }
}
