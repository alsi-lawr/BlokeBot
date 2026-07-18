using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private static readonly TimeSpan _accessModeSaveDebounce = TimeSpan.FromMilliseconds(180);

    private readonly SemaphoreSlim _allowModsByDefaultSaveGate = new(1, 1);
    private readonly HostModAccessSaveSequence _allowModsByDefaultSaves = new();
    private string _newBlacklistLogin = string.Empty;
    private string _newWhitelistLogin = string.Empty;
    private IReadOnlyList<AccessListEntryProfile> _blacklistEntries = [];
    private IReadOnlyList<AccessListEntryProfile> _whitelistEntries = [];
    private string _accessModeSegmentClass =>
        _state?.ModAccess.AllowModsByDefault == false
            ? "segmented-motion segmented-motion--second"
            : "segmented-motion";

    private static string AccessModeTabClass(bool active)
    {
        return active
            ? "segmented-motion__tab segmented-motion__tab--active"
            : "segmented-motion__tab";
    }

    private Task AddAccessAsync(int hostId, AccessListEntryKind kind)
    {
        return ObserveUiOperationAsync(
            nameof(AddAccessAsync),
            () => RunSelectedHostMutationAsync(hostId, () => AddAccessCoreAsync(hostId, kind))
        );
    }

    private async Task AddAccessCoreAsync(int hostId, AccessListEntryKind kind)
    {
        var login = kind == AccessListEntryKind.Whitelist ? _newWhitelistLogin : _newBlacklistLogin;
        await _modAccess.AddEntryAsync(hostId, kind, login, CancellationToken.None);
        if (kind == AccessListEntryKind.Whitelist)
        {
            _newWhitelistLogin = string.Empty;
        }
        else
        {
            _newBlacklistLogin = string.Empty;
        }

        await LoadCoreAsync();
    }

    private Task RemoveAccessAsync(int hostId, AccessListEntryKind kind, string login)
    {
        return ObserveUiOperationAsync(
            nameof(RemoveAccessAsync),
            () =>
                RunSelectedHostMutationAsync(
                    hostId,
                    () => RemoveAccessCoreAsync(hostId, kind, login)
                )
        );
    }

    private async Task RemoveAccessCoreAsync(int hostId, AccessListEntryKind kind, string login)
    {
        await _modAccess.RemoveEntryAsync(hostId, kind, login, CancellationToken.None);
        await LoadCoreAsync();
    }

    private Task SetModsEnabledAsync(int hostId, ChangeEventArgs args)
    {
        return ObserveUiOperationAsync(
            nameof(SetModsEnabledAsync),
            () => RunSelectedHostMutationAsync(hostId, () => SetModsEnabledCoreAsync(hostId, args))
        );
    }

    private async Task SetModsEnabledCoreAsync(int hostId, ChangeEventArgs args)
    {
        if (args.Value is true)
        {
            await _modAccess.EnableModeratorAccessAsync(hostId, CancellationToken.None);
        }
        else
        {
            await _modAccess.DisableModeratorAccessAsync(hostId, CancellationToken.None);
        }

        await LoadCoreAsync();
    }

    private Task SetAllowModsByDefaultAsync(int hostId, bool allowByDefault)
    {
        if (_state is null || _state.ModAccess.AllowModsByDefault == allowByDefault)
        {
            return Task.CompletedTask;
        }

        return HostModAccessSaveValidator
            .Validate(hostId, HostModeratorAccessMode.FromAllowModsByDefault(allowByDefault))
            .Match(
                command =>
                    RunSelectedHostMutationAsync(
                        hostId,
                        () => BeginAllowModsByDefaultSaveAsync(command, allowByDefault)
                    ),
                errors =>
                {
                    _toasts.Publish(
                        ToastRequest<ErrorToastStrategy>.WithTitle(
                            errors[0].Message,
                            "Mod help not saved"
                        )
                    );
                    return Task.CompletedTask;
                }
            );
    }

    private Task BeginAllowModsByDefaultSaveAsync(
        HostModAccessSaveCommand command,
        bool allowByDefault
    )
    {
        if (_state is null || _state.ModAccess.AllowModsByDefault == allowByDefault)
        {
            return Task.CompletedTask;
        }

        var previousAccess = _state.ModAccess;
        var submission = _allowModsByDefaultSaves.Begin(command, previousAccess);
        _state = _state with
        {
            ModAccess = previousAccess with { AllowModsByDefault = allowByDefault },
        };
        _ = PersistAllowModsByDefaultAsync(submission);
        return Task.CompletedTask;
    }

    private async Task PersistAllowModsByDefaultAsync(HostModAccessSaveSubmission submission)
    {
        var cancellationToken = submission.CancellationToken;
        try
        {
            await Task.Delay(_accessModeSaveDebounce, _timeProvider, cancellationToken);
            await _allowModsByDefaultSaveGate.WaitAsync(cancellationToken);
            try
            {
                var result = await _modAccess
                    .SaveModeratorAccess(submission.Command)
                    .ExecuteAsync(cancellationToken);
                await result.Match(
                    _ => Task.CompletedTask,
                    failure => ApplyAllowModsByDefaultFailureAsync(submission, failure)
                );
            }
            finally
            {
                _allowModsByDefaultSaveGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportUiFault(nameof(PersistAllowModsByDefaultAsync), exception);
            await DispatchExceptionAsync(exception);
        }
        finally
        {
            _allowModsByDefaultSaves.Complete(submission);
        }
    }

    private Task ApplyAllowModsByDefaultFailureAsync(
        HostModAccessSaveSubmission submission,
        HostModAccessSaveFailure failure
    )
    {
        return InvokeAsync(() =>
        {
            if (!_allowModsByDefaultSaves.IsCurrent(submission))
            {
                return;
            }

            if (_state is not null)
            {
                _state = _state with { ModAccess = submission.PreviousAccess };
            }

            _toasts.Publish(
                ToastRequest<ErrorToastStrategy>.WithTitle(failure.Message, "Mod help not saved")
            );
            StateHasChanged();
        });
    }

    private async Task LoadAccessEntriesAsync(HostModAccessState access)
    {
        _whitelistEntries = await _accessListProfiles.ResolveAsync(
            access.Whitelist,
            CancellationToken.None
        );
        _blacklistEntries = await _accessListProfiles.ResolveAsync(
            access.Blacklist,
            CancellationToken.None
        );
    }

    private void ClearAccessEntries()
    {
        _whitelistEntries = [];
        _blacklistEntries = [];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _allowModsByDefaultSaves.Dispose();
        }

        base.Dispose(disposing);
    }
}
