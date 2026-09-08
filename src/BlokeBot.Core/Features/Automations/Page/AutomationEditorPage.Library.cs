namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private AutomationLibraryKind _libraryKind;
    private AutomationSubflowLibraryPage _subflowPage = new([], null);
    private string _subflowSearch = string.Empty;
    private int _subflowOffset;
    private int _libraryHost;
    private long _subflowQuery;
    private long _librarySelection;
    private bool _subflowLoading;

    private void ResetLibraryForHost()
    {
        if (_libraryHost == HostId)
        {
            return;
        }
        _libraryHost = HostId;
        _subflowQuery++;
        _librarySelection++;
        _subflowPage = new([], null);
        _subflowSearch = string.Empty;
        _subflowOffset = 0;
        _subflowLoading = false;
        _libraryKind = AutomationLibraryKind.Flows;
    }

    private async Task ChangeLibraryAsync(AutomationLibraryKind kind)
    {
        _libraryKind = kind;
        if (kind == AutomationLibraryKind.Subflows)
        {
            await LoadSubflowPageAsync(_subflowOffset);
        }
    }

    private async Task LoadSubflowPageAsync(int offset)
    {
        var request = ++_subflowQuery;
        var host = HostId;
        _subflowLoading = true;
        var page = await _subflowsService.ListAsync(
            new(host),
            new(_subflowSearch, offset),
            CancellationToken.None
        );
        if (request != _subflowQuery || host != HostId)
        {
            return;
        }
        _subflowLoading = false;
        _subflowOffset = offset;
        _subflowPage = page;
    }

    private async Task SearchSubflowsAsync(string search)
    {
        _subflowSearch = search;
        await LoadSubflowPageAsync(0);
    }

    private Task SelectLibrarySubflowAsync(AutomationSubflowId id) =>
        _editor?.Subflow?.Id == id
            ? Task.CompletedTask
            : RequestTransitionAsync(async () =>
            {
                var editor = _editor;
                var host = HostId;
                var version = _draftRevision;
                var request = ++_librarySelection;
                var loaded = await _subflowsService.LoadCurrentAsync(
                    new(host),
                    id,
                    CancellationToken.None
                );
                if (
                    request != _librarySelection
                    || host != HostId
                    || version != _draftRevision
                    || !ReferenceEquals(editor, _editor)
                )
                {
                    return;
                }
                if (loaded is null)
                {
                    _feedback = "This subflow is unavailable.";
                    _hasChanges = _editor is not null && _history.IsDirty(_editor);
                    return;
                }
                if (!LoadSubflowEditor(loaded))
                {
                    _hasChanges = _editor is not null && _history.IsDirty(_editor);
                }
            });
}
