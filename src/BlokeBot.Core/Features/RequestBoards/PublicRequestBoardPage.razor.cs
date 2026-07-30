using BlokeBot.Core.Auth.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Features.RequestBoards;

public partial class PublicRequestBoardPage
{
    private readonly Dictionary<string, string> _fieldValues = new(StringComparer.Ordinal);
    private RequestBoardPage? _page;
    private string _viewerLogin = string.Empty;
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
        var authentication = await _authenticationState;
        _viewerLogin = RequestBoardInput.NormalizeLogin(
            AuthenticatedSession.FromPrincipal(authentication.User).Login
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _page = await _boards.GetPublicPageAsync(Channel, BoardSlug, CancellationToken.None);
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
        if (_page is null || string.IsNullOrWhiteSpace(_viewerLogin))
        {
            return;
        }

        var result = await _boards.SubmitAsync(
            _page.Board.HostId,
            _page.Board.Slug,
            new SubmitRequestCommand(
                Guid.NewGuid(),
                _viewerLogin,
                _title,
                _category,
                RequestBoardInput.ParseTags(_tags),
                _fieldValues
            ),
            CancellationToken.None
        );
        _feedback = result.Match(
            succeeded => $"Request #{succeeded.Value.Id} submitted.",
            rejected => rejected.Reason.Message
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
        if (_page is null)
        {
            return;
        }

        var result = await _boards.VoteAsync(
            _page.Board.HostId,
            submissionId,
            _viewerLogin,
            CancellationToken.None
        );
        _feedback = result.Match(
            succeeded =>
                succeeded.WasIdempotent
                    ? "Your vote was already recorded."
                    : "Your vote was recorded.",
            rejected => rejected.Reason.Message
        );
        _operationFailed = result is RequestBoardResult<PublicRequestSubmissionView>.Rejected;
        await LoadAsync();
    }

    private async Task WithdrawAsync(long submissionId)
    {
        if (_page is null)
        {
            return;
        }

        var result = await _boards.WithdrawAsync(
            _page.Board.HostId,
            submissionId,
            _viewerLogin,
            CancellationToken.None
        );
        _feedback = result.Match(
            _ => "Your request was withdrawn.",
            rejected => rejected.Reason.Message
        );
        _operationFailed = result is RequestBoardResult<PublicRequestSubmissionView>.Rejected;
        await LoadAsync();
    }
}
