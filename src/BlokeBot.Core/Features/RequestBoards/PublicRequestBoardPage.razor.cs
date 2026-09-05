using BlokeBot.Core.Auth.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Features.RequestBoards;

public partial class PublicRequestBoardPage
{
    [Inject]
    private BlokeBot.Core.Features.ViewerPortal.Boundary.PublicViewerGate _publicGate { get; set; } =
        null!;

    private readonly Dictionary<string, string> _fieldValues = new(StringComparer.Ordinal);
    private RequestBoardPage? _page;
    private RequestActor? _actor;
    private RequestBoardSelfView? _self;
    private string _title = string.Empty;
    private string _category = string.Empty;
    private string _tags = string.Empty;
    private string _feedback = string.Empty;
    private bool _operationFailed;

    [CascadingParameter]
    private Task<AuthenticationState> _authenticationState { get; set; } =
        Task.FromResult(new AuthenticationState(new()));

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    [Parameter]
    public string BoardSlug { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        _page = null;
        _self = null;
        var authentication = await _authenticationState;
        _actor = RequestActor.FromSession(AuthenticatedSession.FromPrincipal(authentication.User));
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (!await _publicGate.TryReadAsync(Channel, CancellationToken.None))
        {
            return;
        }
        _page = await _boards.GetPublicPageAsync(Channel, BoardSlug, CancellationToken.None);
        _self =
            _page is not null && _actor is not null
                ? await _boards.GetSelfAsync(
                    _page.Board.HostId,
                    _page.Board.Slug,
                    _actor,
                    _page.Submissions.Select(row => row.Id).ToArray(),
                    CancellationToken.None
                )
                : null;
        _fieldValues.Clear();
        if (_page is not null)
        {
            foreach (var field in _page.Board.Fields)
            {
                _fieldValues[field.Key] = string.Empty;
            }
        }
    }

    private async Task SubmitAsync()
    {
        if (_page is null || _actor is null)
        {
            return;
        }

        if (!await _publicGate.TryActionAsync(_page.Board.HostId))
        {
            return;
        }
        var result = await _boards.SubmitAsync(
            _page.Board.HostId,
            _page.Board.Slug,
            new SubmitRequestCommand(
                Guid.NewGuid(),
                _actor,
                _title,
                _category,
                RequestBoardInput.ParseTags(_tags),
                _fieldValues
            ),
            CancellationToken.None
        );
        _feedback = result.Match(
            static succeeded => $"Request #{succeeded.Value.Id} submitted.",
            static rejected => rejected.Reason.Message
        );
        _operationFailed = result is RequestBoardResult<PublicRequestSubmissionView>.Rejected;
        if (!_operationFailed)
        {
            _title = string.Empty;
            _category = string.Empty;
            _tags = string.Empty;
            await LoadAsync();
        }
    }

    private async Task VoteAsync(long submissionId)
    {
        if (_page is null || _actor is null)
        {
            return;
        }

        if (!await _publicGate.TryActionAsync(_page.Board.HostId))
        {
            return;
        }
        var result = await _boards.VoteAsync(
            _page.Board.HostId,
            submissionId,
            _actor,
            CancellationToken.None
        );
        _feedback = result.Match(
            static succeeded =>
                succeeded.WasIdempotent
                    ? "Your vote was already recorded."
                    : "Your vote was recorded.",
            static rejected => rejected.Reason.Message
        );
        _operationFailed = result is RequestBoardResult<PublicRequestSubmissionView>.Rejected;
        await LoadAsync();
    }

    private async Task WithdrawAsync(long submissionId)
    {
        if (_page is null || _actor is null)
        {
            return;
        }

        if (!await _publicGate.TryActionAsync(_page.Board.HostId))
        {
            return;
        }
        var result = await _boards.WithdrawAsync(
            _page.Board.HostId,
            submissionId,
            _actor,
            CancellationToken.None
        );
        _feedback = result.Match(
            static _ => "Your request was withdrawn.",
            static rejected => rejected.Reason.Message
        );
        _operationFailed = result is RequestBoardResult<PublicRequestSubmissionView>.Rejected;
        await LoadAsync();
    }
}
