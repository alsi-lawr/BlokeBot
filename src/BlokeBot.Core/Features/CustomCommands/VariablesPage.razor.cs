using System.Data.Common;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public partial class VariablesPage
{
    private static readonly IReadOnlyList<SegmentedTabItem> _tabs =
    [
        new("variables", "Variables"),
        new("dictionaries", "Dictionaries"),
    ];
    private IReadOnlyList<CustomValueDefinitionDraft> _definitions = [];
    private IReadOnlyList<CustomValueSnapshot> _entries = [];
    private CustomValueDefinitionDraft? _selected;
    private CustomValueSnapshot? _original;
    private CustomCommandViewer? _viewer;
    private string _viewerLogin = string.Empty;
    private string _tab = "variables";
    private string _name = string.Empty;
    private string _default = "0";
    private CustomValueScope _scope = CustomValueScope.User;
    private CustomValueKind _kind = CustomValueKind.Number;
    private CustomValueKind _editKind = CustomValueKind.Number;
    private string _input = string.Empty;
    private string _newKey = string.Empty;
    private string _loadedChannel = string.Empty;
    private int _loadedHostId;
    private bool _creating;
    private bool _adding;
    private bool _loading;
    private string? _feedback;
    private string? _loadError;
    private Func<Task>? _switch;
    private string? _confirmation;
    private string? _confirmationAction;
    private Func<Task>? _confirmed;

    private bool _hasTarget => _selected?.Scope == CustomValueScope.Global || _viewer is not null;
    private bool _dirty =>
        _creating
            ? _name.Length != 0
            : (_selected is not null && (_name != _selected.Name || _default != _selected.Default))
                || (
                    _original is not null
                    && (_input != _original.Value || _editKind != _original.Kind)
                );
    private string _contextLabel =>
        _creating ? "Definition · no live values"
        : _selected is null ? string.Empty
        : $"{_selected.Scope} · {_selected.Kind}";
    private string _tokenExample =>
        _selected is null ? string.Empty
        : _selected.Kind == CustomValueKind.Dictionary
            ? $"{{dict_get|{_selected.Scope.ToString().ToLowerInvariant()}|{CustomCommandTemplateRenderer.NameOperand(_selected.Name)}|{{arg1}}}}"
        : $"{{var_get|{_selected.Scope.ToString().ToLowerInvariant()}|{CustomCommandTemplateRenderer.NameOperand(_selected.Name)}}}";
    private IReadOnlyList<StudioRailGroup> _railGroups =>
        [
            new(
                _tab == "dictionaries" ? "Dictionaries" : "Variables",
                _definitions
                    .Where(x => x.Kind == CustomValueKind.Dictionary == (_tab == "dictionaries"))
                    .Select(x => new StudioRailItem
                    {
                        Key = x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Label = x.Name,
                        Sub = $"{x.Scope} · {x.Kind}",
                        Selected = _selected?.Id == x.Id,
                        On = true,
                        Select = EventCallback.Factory.Create(
                            this,
                            () => RequestSwitch(() => SelectAsync(x))
                        ),
                    })
                    .ToArray(),
                "No definitions yet."
            ),
        ];

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            _ = await LoadPageContextAsync();
            var definitions = HostId == 0 ? [] : await _values.DefinitionsAsync(HostId, default);
            _loadedHostId = HostId;
            _loadedChannel = HostLogin;
            _definitions = definitions;
            _selected = null;
            _original = null;
            _viewer = null;
            _creating = false;
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(LoadAsync), exception);
            _loadError =
                "Variables could not load. Your edits are retained. Check the connection and retry.";
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SelectAsync(CustomValueDefinitionDraft definition)
    {
        _entries = [];
        _selected = definition;
        _name = definition.Name;
        _default = definition.Default;
        _creating = false;
        _adding = false;
        _original = null;
        _confirmation = null;
        _feedback = null;
        await LoadValuesAsync();
    }

    private async Task LoadValuesAsync()
    {
        if (_selected is null)
        {
            return;
        }
        _entries = await _values.ValuesAsync(
            _loadedHostId,
            _selected.Id,
            _viewer?.TwitchUserId ?? string.Empty,
            default
        );
        if (_selected.Kind != CustomValueKind.Dictionary)
        {
            Edit(_entries.SingleOrDefault());
        }
    }

    private void Edit(CustomValueSnapshot? snapshot)
    {
        _original = snapshot;
        _input = snapshot?.Value ?? string.Empty;
        _editKind = snapshot?.Kind ?? CustomValueKind.Text;
        _adding = false;
    }

    private Task RequestSwitch(Func<Task> action)
    {
        if (_dirty)
        {
            _switch = action;
            return Task.CompletedTask;
        }
        return action();
    }

    private void KeepEditing() => _switch = null;

    private async Task DiscardAndSwitch()
    {
        var action = _switch;
        _switch = null;
        if (action is not null)
        {
            await action();
        }
    }

    private Task ChangeTab(string tab) =>
        RequestSwitch(() =>
        {
            _tab = tab;
            _selected = null;
            _original = null;
            _creating = false;
            return Task.CompletedTask;
        });

    private Task RequestNew() =>
        RequestSwitch(() =>
        {
            _selected = null;
            _original = null;
            _creating = true;
            _adding = false;
            _name = string.Empty;
            _scope = CustomValueScope.User;
            _kind = _tab == "dictionaries" ? CustomValueKind.Dictionary : CustomValueKind.Number;
            _default = _kind == CustomValueKind.Number ? "0" : string.Empty;
            return Task.CompletedTask;
        });

    private Task RequestEntry(CustomValueSnapshot snapshot) =>
        RequestSwitch(() =>
        {
            Edit(snapshot);
            return Task.CompletedTask;
        });

    private Task RequestAddEntry() =>
        RequestSwitch(() =>
        {
            _original = null;
            _adding = true;
            _newKey = string.Empty;
            return Task.CompletedTask;
        });

    private Task RequestViewer() => RequestSwitch(SelectViewerAsync);

    private async Task SelectViewerAsync()
    {
        CustomCommandViewerResolution resolution;
        try
        {
            resolution = await _viewers.ResolveAsync(_viewerLogin, default);
        }
        catch (HttpRequestException)
        {
            _feedback = "Viewer lookup is unavailable. Your edits are retained. Try again.";
            return;
        }
        if (resolution is not CustomCommandViewerResolution.Found found)
        {
            _feedback = "Viewer not found. Enter an existing Twitch login.";
            return;
        }
        if (_selected is null)
        {
            return;
        }
        try
        {
            var entries = await _values.ValuesAsync(
                _loadedHostId,
                _selected.Id,
                found.Viewer.TwitchUserId,
                default
            );
            _viewer = found.Viewer;
            _entries = entries;
            _adding = false;
            Edit(_selected.Kind == CustomValueKind.Dictionary ? null : entries.SingleOrDefault());
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            _feedback =
                "Saved values could not load. Your edit is retained. Check database storage and retry.";
        }
    }

    private async Task OpenEntryAsync()
    {
        if (_selected is null || !_hasTarget || !CustomValueIdentity.Valid(_newKey))
        {
            _feedback = "Select a viewer and supply a nonempty key.";
            return;
        }
        Edit(
            await _values.ValueAsync(
                _loadedHostId,
                new(
                    _selected.Id,
                    _selected.Scope == CustomValueScope.Global
                        ? string.Empty
                        : _viewer!.TwitchUserId,
                    _newKey
                ),
                default
            )
        );
    }

    private async Task ReloadValueAsync()
    {
        if (_original is not null)
        {
            Edit(await _values.ValueAsync(_loadedHostId, _original.Target, default));
        }
        await LoadValuesAsync();
    }

    private Task SaveAsync() =>
        _creating
            ? SaveDefinitionAsync()
            : MutateAsync(async () =>
            {
                if (_original is null)
                {
                    return;
                }
                var result = await _values.SaveValueAsync(
                    _loadedHostId,
                    _original,
                    _editKind,
                    _input,
                    default
                );
                _feedback = result.Message;
                if (result.Status == CustomValueEditStatus.Saved)
                {
                    await ReloadValueAsync();
                }
            });

    private Task SaveDefinitionAsync() =>
        MutateAsync(async () =>
        {
            var draft = _creating
                ? new CustomValueDefinitionDraft(0, _name, _scope, _kind, _default, Guid.Empty)
                : _selected! with
                {
                    Name = _name,
                    Default = _default,
                };
            var result = await _values.SaveDefinitionAsync(_loadedHostId, draft, default);
            _feedback = result.Message;
            if (result.Status != CustomValueEditStatus.Saved)
            {
                return;
            }
            var original = _original;
            var input = _input;
            var editKind = _editKind;
            _definitions = await _values.DefinitionsAsync(_loadedHostId, default);
            await SelectAsync(
                _definitions.Single(x => x.Scope == draft.Scope && x.Name == draft.Name)
            );
            if (original is not null)
            {
                _original = original with { DefinitionRevision = _selected!.Revision };
                _input = input;
                _editKind = editKind;
            }
            _feedback = result.Message;
        });

    private Task MutateAsync(Func<Task> mutation) =>
        RunSelectedHostMutationAsync(_loadedHostId, mutation);

    private void RequestReset()
    {
        if (_original is null)
        {
            return;
        }
        var snapshot = _original;
        _confirmationAction =
            _selected?.Kind == CustomValueKind.Dictionary ? "delete entry" : "reset value";
        _confirmation =
            $"{_confirmationAction} for {TargetLabel(snapshot)}? Other viewers, keys and channels are not changed.";
        _confirmed = () =>
            MutateAsync(async () =>
            {
                var result = await _values.ResetValueAsync(_loadedHostId, snapshot, default);
                _feedback = result.Message;
                if (result.Status == CustomValueEditStatus.Saved)
                {
                    _original = null;
                    await LoadValuesAsync();
                }
            });
    }

    private async Task RequestDeleteAsync()
    {
        if (_selected is null)
        {
            return;
        }
        var selected = _selected;
        var count = await _values.AffectedValuesAsync(_loadedHostId, selected.Id, default);
        _confirmationAction = "delete definition";
        _confirmation =
            $"Delete {selected.Scope} {selected.Name} in {_loadedChannel} and {count.Count} saved values across its viewers? Commands that reference this definition will fail until you update them. Other definitions and channels are not changed.";
        _confirmed = () =>
            MutateAsync(async () =>
            {
                var result = await _values.DeleteDefinitionAsync(
                    _loadedHostId,
                    selected,
                    count,
                    default
                );
                _feedback = result.Message;
                if (result.Status == CustomValueEditStatus.Saved)
                {
                    _definitions = await _values.DefinitionsAsync(_loadedHostId, default);
                    _selected = null;
                    _original = null;
                    _entries = [];
                }
            });
    }

    private string TargetLabel(CustomValueSnapshot snapshot) =>
        $"{_selected?.Name} · {_loadedChannel} · {(_selected?.Scope == CustomValueScope.Global ? "Global" : $"viewer {_viewer?.DisplayName} ({snapshot.Target.ViewerId})")} {(snapshot.Target.Key.Length == 0 ? string.Empty : $"· key {snapshot.Target.Key}")}";

    private void CancelConfirmation()
    {
        _confirmation = null;
        _confirmed = null;
    }

    private async Task ConfirmAsync()
    {
        var action = _confirmed;
        CancelConfirmation();
        if (action is not null)
        {
            await action();
        }
    }
}
