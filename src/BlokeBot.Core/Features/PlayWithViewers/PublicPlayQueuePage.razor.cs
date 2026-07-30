using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.PlayWithViewers;

public partial class PublicPlayQueuePage
{
    [Parameter]
    public string Channel { get; set; } = string.Empty;

    [Parameter]
    public string QueueSlug { get; set; } = string.Empty;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private PublicPlayQueueSnapshot? _page;
    private string _login = string.Empty;
    private string _feedback = string.Empty;
    private bool _failed;
    private bool _loading = true;

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _page = await _queues.GetPublicPageAsync(Channel, QueueSlug, CancellationToken.None);
        _loading = false;
    }

    private string Value(string key)
    {
        return _values.GetValueOrDefault(key, string.Empty);
    }

    private void SetValue(string key, string value)
    {
        _values[key] = value;
    }

    private PlayQueueViewerIdentity Identity()
    {
        return new(_login);
    }

    private async Task JoinAsync()
    {
        if (_page is null)
        {
            return;
        }
        var result = await _queues.JoinAsync(
            _page.Queue.HostId,
            _page.Queue.Slug,
            new JoinPlayQueueCommand(Identity(), 0, _values),
            CancellationToken.None
        );
        _feedback = result.Match(
            succeeded => $"You are position {succeeded.Value.Position}.",
            rejected => rejected.Reason.Message
        );
        _failed = result is PlayQueueResult<PublicPlayQueueEntryView>.Rejected;
        await ReloadAsync();
    }

    private Task LeaveAsync()
    {
        return MutateAsync(
            (hostId, slug, viewer) =>
                _queues.LeaveAsync(hostId, slug, viewer, CancellationToken.None),
            "You left the queue."
        );
    }

    private Task ReadyAsync()
    {
        return MutateAsync(
            (hostId, slug, viewer) =>
                _queues.ReadyAsync(hostId, slug, viewer, CancellationToken.None),
            "You are ready."
        );
    }

    private Task PositionAsync()
    {
        return MutateAsync(
            (hostId, slug, viewer) =>
                _queues.GetPositionAsync(hostId, slug, viewer, CancellationToken.None),
            "Position checked.",
            value =>
                value.Status == BlokeBot.Persistence.Models.PlayQueueEntryStatus.Selected
                    ? "You are in the current party."
                    : $"You are position {value.Position} ({value.Status})."
        );
    }

    private async Task MutateAsync(
        Func<
            int,
            string,
            PlayQueueViewerIdentity,
            Task<PlayQueueResult<PublicPlayQueueEntryView>>
        > mutation,
        string success,
        Func<PublicPlayQueueEntryView, string>? message = null
    )
    {
        if (_page is null)
        {
            return;
        }
        var result = await mutation(_page.Queue.HostId, _page.Queue.Slug, Identity());
        _feedback = result.Match(
            succeeded => message?.Invoke(succeeded.Value) ?? success,
            rejected => rejected.Reason.Message
        );
        _failed = result is PlayQueueResult<PublicPlayQueueEntryView>.Rejected;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _page = await _queues.GetPublicPageAsync(Channel, QueueSlug, CancellationToken.None);
    }
}
