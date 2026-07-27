using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.Home;

public partial class HomePage
{
    private bool _blockedBySelectedChannel;
    private bool _loaded;
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
        _blockedBySelectedChannel = context.Session.State.Match(
            _ => false,
            _ => !context.Session.CanManageSelectedHostConfig,
            _ => false
        );
        if (_blockedBySelectedChannel)
        {
            _state = null;
            _loaded = true;
            return;
        }

        var result = await _hostConfig.Load(context.Session).ExecuteAsync(CancellationToken.None);
        _state = result.Match(
            option => option.Match<HostConfigState?>(state => state, () => null),
            _ => throw new UnreachableException()
        );
        _loaded = true;
    }
}
