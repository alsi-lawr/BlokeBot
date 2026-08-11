using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.MomentAttachments;

public partial class MomentAttachmentsSection
{
    private MomentAttachmentSectionView? _view;
    private Guid? _selectedMomentId;
    private bool _pickerOpen;
    private bool _failed;
    private string _feedback = string.Empty;
    private string _search = string.Empty;

    [Inject]
    private MomentAttachmentService _attachments { get; set; } = null!;

    [Parameter]
    [EditorRequired]
    public int SelectedHostId { get; set; }

    [Parameter]
    [EditorRequired]
    public string SelectedHostLogin { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public MomentAttachmentDestination Destination { get; set; } =
        new MomentAttachmentDestination.Bounty(Guid.Empty);

    private string _choiceName => $"moment-attachment-{_destinationKey}";

    private string _searchId => $"moment-attachment-search-{_destinationKey}";

    private string _destinationKey =>
        Destination switch
        {
            MomentAttachmentDestination.Bounty value => $"bounty-{value.Id:N}",
            MomentAttachmentDestination.Achievement value => $"achievement-{value.Id.Value:N}",
            MomentAttachmentDestination.TournamentResult value => $"result-{value.Id.Value:N}",
            _ => "unknown",
        };

    protected override async Task OnParametersSetAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    private async Task LoadAsync() =>
        _view = await _attachments.GetManagementAsync(
            SelectedHostId,
            Destination,
            CancellationToken.None
        );

    private void OpenPicker()
    {
        _pickerOpen = true;
        _feedback = string.Empty;
        _selectedMomentId = _view?.Discoverable.FirstOrDefault(moment => !moment.IsAttached)?.Id;
    }

    private void ClosePicker()
    {
        _pickerOpen = false;
        _search = string.Empty;
        _selectedMomentId = null;
    }

    private async Task AttachAsync()
    {
        if (_selectedMomentId is not { } momentId)
        {
            return;
        }

        var outcome = await _attachments.AttachAsync(
            PageContext.Session,
            SelectedHostId,
            Destination,
            momentId,
            CancellationToken.None
        );
        await FinishAsync(outcome, "Moment attached.");
        if (!_failed)
        {
            ClosePicker();
        }
    }

    private async Task DetachAsync(MomentAttachmentMomentView moment)
    {
        var outcome = await _attachments.DetachAsync(
            PageContext.Session,
            SelectedHostId,
            Destination,
            moment.Id,
            CancellationToken.None
        );
        await FinishAsync(outcome, "Moment removed from this destination.");
    }

    private async Task FinishAsync(MomentAttachmentMutationOutcome outcome, string success)
    {
        switch (outcome)
        {
            case MomentAttachmentMutationOutcome.Succeeded:
                _failed = false;
                _feedback = success;
                await LoadAsync();
                break;
            case MomentAttachmentMutationOutcome.Rejected rejected:
                _failed = true;
                _feedback = rejected.Reason.Message;
                break;
        }
    }

    private IReadOnlyList<MomentAttachmentMomentView> PickerMoments()
    {
        var query = _search.Trim();
        return
        [
            .. (_view?.Discoverable ?? []).Where(moment =>
                query.Length == 0
                || moment.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || moment.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            ),
        ];
    }

    private static string MomentMeta(MomentAttachmentMomentView moment) =>
        $"{moment.Category} · {moment.CapturedAtUtc.ToString("d MMM", CultureInfo.InvariantCulture)} · {moment.StreamIdentity}";
}
