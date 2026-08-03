using BlokeBot.Core.Auth.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Features.Moments;

public partial class PublicMomentRecapPage
{
    [CascadingParameter]
    private Task<AuthenticationState> _authenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    [Parameter]
    public string? StreamIdentity { get; set; }

    private MomentRecapPage? _page;
    private AuthenticatedSession _session = AuthenticatedSession.Anonymous;
    private string _login = string.Empty;
    private string _feedback = string.Empty;
    private bool _failed;
    private bool _loading = true;

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _session = AuthenticatedSession.FromPrincipal((await _authenticationState).User);
        await ReloadAsync();
        _loading = false;
    }

    private async Task ReloadAsync() =>
        _page = string.IsNullOrWhiteSpace(StreamIdentity)
            ? await _moments.GetWeeklyRecapAsync(Channel, DateTime.UtcNow, CancellationToken.None)
            : await _moments.GetStreamRecapAsync(Channel, StreamIdentity, CancellationToken.None);

    private MomentViewerIdentity Identity() =>
        _session.IsAuthenticated
            ? new(_session.Login, _session.UserId, _session.DisplayName)
            : new(MomentInput.NormalizeLogin(_login));

    private async Task VoteAsync(Guid publicId)
    {
        if (_page is null || _page.Moments.Count == 0)
        {
            return;
        }
        var result = await _moments.VoteAsync(
            _page.Moments[0].HostId,
            publicId,
            Identity(),
            CancellationToken.None
        );
        _feedback = result.Match(
            static succeeded =>
                succeeded.WasIdempotent ? "Your vote was already recorded." : "Vote recorded.",
            static rejected => rejected.Reason.Message
        );
        _failed = result is MomentResult<MomentView>.Rejected;
        await ReloadAsync();
    }
}
