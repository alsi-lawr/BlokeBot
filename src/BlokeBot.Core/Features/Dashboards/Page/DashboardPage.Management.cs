using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardPage
{
    private bool _managementOpen;
    private bool _focusManagement;

    private async Task ToggleManagement()
    {
        if (_managementOpen)
        {
            await CloseManagementAsync();
            return;
        }
        _managementOpen = true;
        _focusManagement = true;
    }

    private async Task CloseManagementAsync()
    {
        _managementOpen = false;
        if (_grid is not null)
        {
            await _grid.InvokeVoidAsync("focusManage");
        }
    }

    private Task ManagementKeyAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CloseManagementAsync() : Task.CompletedTask;

    private async Task EditNameAsync()
    {
        await CloseManagementAsync();
        if (_grid is not null)
        {
            await _grid.InvokeVoidAsync("focusName");
        }
    }

    private async Task DeleteDashboardAsync()
    {
        if (!_editing || _busy || _snapshot is not { } snapshot)
        {
            return;
        }
        var generation = _loadGeneration;
        var host = new DashboardHostId(HostId);
        if (
            !await _js.InvokeAsync<bool>(
                "confirm",
                $"Delete dashboard ‘{snapshot.Name}’? Any unsaved edits will be discarded. This deletes only the layout, not its automations, values, timers or other targets."
            )
        )
        {
            await CloseManagementAsync();
            return;
        }
        if (_disposed || generation != _loadGeneration || _snapshot != snapshot)
        {
            return;
        }
        _busy = true;
        try
        {
            _ = await LoadPageContextAsync();
            if (_disposed || generation != _loadGeneration || HostId != host.Value)
            {
                return;
            }
            var outcome = await _configuration.DeleteAsync(
                PageContext.Session,
                host,
                snapshot.Id,
                snapshot.Revision,
                CancellationToken.None
            );
            if (_disposed || generation != _loadGeneration || HostId != host.Value)
            {
                return;
            }
            if (outcome is DashboardResult<DashboardId>.Succeeded)
            {
                await CancelGestureAsync();
                _edit?.Cancel();
                _managementOpen = false;
                _feedback = "Dashboard deleted. Its targets are unchanged.";
                _focusPicker = true;
                _navigation.NavigateTo("/dashboards/editor");
            }
            else if (outcome is DashboardResult<DashboardId>.Rejected rejected)
            {
                _feedback =
                    rejected.Reason == DashboardRejection.Conflict
                        ? "Dashboard not deleted: someone else saved it. Your unsaved edits are still here. Reload the saved dashboard before trying again."
                        : $"Dashboard not deleted: {rejected.Reason}. Your unsaved edits are still here.";
                await CloseManagementAsync();
            }
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(DeleteDashboardAsync), exception);
            if (!_disposed && generation == _loadGeneration)
            {
                _feedback =
                    "Deletion could not be confirmed. Reload to check the saved dashboard before trying again. Your local edits are still here.";
            }
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _busy = false;
            }
        }
    }
}
