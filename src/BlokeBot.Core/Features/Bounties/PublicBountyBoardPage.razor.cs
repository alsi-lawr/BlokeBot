using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Points.Balances;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Features.Bounties;

public partial class PublicBountyBoardPage
{
    private readonly Dictionary<Guid, string> _pledges = [];
    private IReadOnlyList<BountyView> _items = [];
    private AuthenticatedSession _session = AuthenticatedSession.Anonymous;
    private bool _loaded;
    private bool _operationFailed;
    private string _feedback = string.Empty;

    [CascadingParameter]
    private Task<AuthenticationState> _authenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private IReadOnlyList<BountyView> _activeItems =>
        [.. _items.Where(value => !BountyPresentation.IsTerminal(value.Status))];

    private IReadOnlyList<BountyView> _settledItems =>
        [.. _items.Where(value => BountyPresentation.IsTerminal(value.Status))];

    protected override async Task OnParametersSetAsync()
    {
        _session = AuthenticatedSession.FromPrincipal((await _authenticationState).User);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _items = await _bounties.GetPublicBoardAsync(Channel, CancellationToken.None);
        _loaded = true;
    }

    private async Task PledgeAsync(BountyView bounty)
    {
        var amount = PointAmount
            .ParseNonNegativeAbsolute(PledgeFor(bounty.PublicId))
            .Match<PointAmount?>(static value => value, static _ => null);
        if (amount is null || amount.Value.IsZero)
        {
            _feedback = "Pledge must be a positive whole number.";
            _operationFailed = true;
            return;
        }

        var result = await _bounties.PledgeAsync(
            bounty.HostId,
            new PledgeBountyCommand(
                Guid.NewGuid(),
                bounty.PublicId,
                new BountyActor(_session.UserId, _session.Login),
                amount.Value
            ),
            CancellationToken.None
        );
        _feedback = result.Match(
            succeeded =>
                succeeded.WasIdempotent
                    ? "That pledge was already recorded."
                    : $"Pledged {succeeded.Value.ReservedAmount.ToDisplayString()} points.",
            static rejected => rejected.Reason.Message
        );
        _operationFailed = result is BountyResult<BountyPledgeView>.Rejected;
        await LoadAsync();
    }

    private string PledgeFor(Guid id) => _pledges.GetValueOrDefault(id, "10");

    private void SetPledge(Guid id, string value) => _pledges[id] = value;
}
