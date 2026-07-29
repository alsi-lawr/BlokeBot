using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.TwitchOperations.Predictions.Page;

public partial class PredictionsPage
{
    private PredictionDashboardState? _state;
    private string _title = string.Empty;
    private string _outcomes = string.Empty;
    private string _window = "60";
    private bool _nativeTwitchEnabled;
    private bool _loading = true;
    private bool _loadFailed;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.TwitchOperationsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            await LoadPageContextAsync();
            _nativeTwitchEnabled =
                HostId != 0 && await _nativeTwitch.IsEnabledAsync(HostId, CancellationToken.None);
            _state = _nativeTwitchEnabled
                ? await _predictions.LoadAsync(HostId, CancellationToken.None)
                : null;
        }
        catch (Exception exception)
        {
            _state = null;
            _nativeTwitchEnabled = false;
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveTemplateAsync()
    {
        if (!int.TryParse(_window, out var window))
        {
            Warn("Prediction window must be a number.");
            return;
        }
        await MutateAsync(async hostId =>
            Publish(
                await _predictions.SaveTemplateAsync(
                    hostId,
                    new(_title, _outcomes.Split('\n'), window),
                    CancellationToken.None
                )
            )
        );
    }

    private Task DeleteTemplateAsync(int templateId)
    {
        return MutateAsync(async hostId =>
            Publish(
                await _predictions.DeleteTemplateAsync(hostId, templateId, CancellationToken.None)
            )
        );
    }

    private Task StartPredictionAsync(int templateId)
    {
        return MutateAsync(async hostId =>
            Publish(await _predictions.StartAsync(hostId, templateId, CancellationToken.None))
        );
    }

    private Task LockPredictionAsync()
    {
        return ConfirmedMutationAsync(
            "Lock this Twitch Prediction?",
            (hostId, confirmed) => _predictions.LockAsync(hostId, confirmed, CancellationToken.None)
        );
    }

    private Task CancelPredictionAsync()
    {
        return ConfirmedMutationAsync(
            "Cancel this Twitch Prediction? Twitch refunds viewers.",
            (hostId, confirmed) =>
                _predictions.CancelAsync(hostId, confirmed, CancellationToken.None)
        );
    }

    private Task ResolvePredictionAsync(string outcomeId)
    {
        return ConfirmedMutationAsync(
            "Resolve this Twitch Prediction and pay winners?",
            (hostId, confirmed) =>
                _predictions.ResolveAsync(hostId, outcomeId, confirmed, CancellationToken.None)
        );
    }

    private async Task ConfirmedMutationAsync(
        string prompt,
        Func<int, bool, Task<PredictionOperationOutcome>> operation
    )
    {
        var confirmed = await _js.InvokeAsync<bool>("confirm", [prompt]);
        await MutateAsync(async hostId => Publish(await operation(hostId, confirmed)));
    }

    private async Task MutateAsync(Func<int, Task> operation)
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                await operation(hostId);
                await LoadAsync();
            }
        );
    }

    private void Publish(PredictionOperationOutcome outcome)
    {
        var (message, success) = outcome switch
        {
            PredictionOperationOutcome.Started => ("Prediction started.", true),
            PredictionOperationOutcome.Updated => ("Prediction updated.", true),
            PredictionOperationOutcome.TemplateSaved => ("Prediction template saved.", true),
            PredictionOperationOutcome.TemplateDeleted => ("Prediction template deleted.", true),
            PredictionOperationOutcome.ActivePredictionExists => (
                "Twitch already has an active Prediction.",
                false
            ),
            PredictionOperationOutcome.TemplateNotFound => (
                "That Prediction template is no longer available.",
                false
            ),
            PredictionOperationOutcome.ConfirmationRequired => (
                "Confirm this Prediction change before continuing.",
                false
            ),
            PredictionOperationOutcome.InvalidOutcome => (
                "That Prediction outcome is no longer available.",
                false
            ),
            PredictionOperationOutcome.NotReady => (
                "Reconnect this channel to Twitch, then try again.",
                false
            ),
            PredictionOperationOutcome.Ineligible => (
                "Predictions are available after this channel becomes a Twitch Affiliate or Partner.",
                false
            ),
            PredictionOperationOutcome.Unavailable => (
                "Predictions are not available right now. Reload the page before trying again.",
                false
            ),
            PredictionOperationOutcome.InvalidTemplate invalid => (invalid.Message, false),
            PredictionOperationOutcome.ProviderRejected => (
                "Twitch could not complete that Prediction action. Reload the page before trying again.",
                false
            ),
            _ => throw new UnreachableException(),
        };
        if (success)
        {
            _toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
        }
        else
        {
            Warn(message);
        }
    }

    private void Warn(string message)
    {
        _toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
    }
}
