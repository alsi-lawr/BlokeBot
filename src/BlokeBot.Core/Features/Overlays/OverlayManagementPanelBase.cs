using BlokeBot.Core.Components;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays;

public abstract class OverlayManagementPanelBase : AuthenticatedPageComponent
{
    [Inject]
    protected HostFeatureService Features { get; set; } = default!;

    protected bool OverlayEnabled;
    protected bool IsLoading = true;
    protected bool IsBusy;
    protected bool HasFailed;
    protected string Feedback = string.Empty;

    protected abstract Task LoadAsync();

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    protected async Task LoadOverlayAsync(Func<Task> load, string failureMessage)
    {
        IsLoading = true;
        try
        {
            OverlayEnabled =
                Host is not null
                && await Features.IsEnabledAsync(
                    HostId,
                    HostFeatureFlags.Overlays,
                    CancellationToken.None
                );
            if (!OverlayEnabled)
            {
                return;
            }

            await load();
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(LoadAsync), exception);
            Fail(failureMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RunSelectedHostMutationAsync(HostId, operation);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected void Success(string message)
    {
        Feedback = message;
        HasFailed = false;
    }

    protected void Fail(string message)
    {
        Feedback = message;
        HasFailed = true;
    }
}
