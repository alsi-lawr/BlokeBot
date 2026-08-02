using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Admin.HostedChannels;

public partial class HostedChannelsSection
{
    [Parameter, EditorRequired]
    public Func<Task> CreateHost { get; set; } = () => Task.CompletedTask;

    [Parameter]
    public bool CanEditHosts { get; set; } = true;

    [Parameter, EditorRequired]
    public IReadOnlyList<HostedChannelAdminView> Hosts { get; set; } = [];

    [Parameter]
    public string NewHostLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewHostLoginChanged { get; set; }

    [Parameter, EditorRequired]
    public Func<int, Task> RemoveHost { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StartBot { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StopBot { get; set; } = _ => Task.CompletedTask;

    private async Task OnInput(ChangeEventArgs args)
    {
        NewHostLogin = args.Value?.ToString() ?? string.Empty;
        await NewHostLoginChanged.InvokeAsync(NewHostLogin);
    }
}
