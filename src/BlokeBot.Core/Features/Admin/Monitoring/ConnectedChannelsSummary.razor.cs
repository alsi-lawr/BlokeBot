namespace BlokeBot.Core.Features.Admin.Monitoring;

public partial class ConnectedChannelsSummary
{
    private string _connectedChannelsText =>
        _botStatus.Current.Match(
            static _ => "No channels connected.",
            static _ => "No channels connected.",
            static connected =>
                $"Connected: {string.Join(", ", connected.Channels.Select(static channel => $"#{channel}"))}"
        );

    protected override void OnInitialized() => _botStatus.Changed += OnBotStatusChanged;

    public void Dispose() => _botStatus.Changed -= OnBotStatusChanged;

    private void OnBotStatusChanged() => _ = InvokeAsync(StateHasChanged);
}
