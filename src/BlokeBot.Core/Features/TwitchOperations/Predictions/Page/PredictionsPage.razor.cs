using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.Predictions.Page;

public partial class PredictionsPage
{
    private string _title = string.Empty;
    private string _outcomes = string.Empty;
    private string _window = "60";

    protected override HostFeatureFlags Feature => HostFeatureFlags.Predictions;

    protected override async Task<PredictionDashboardState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    ) => await _predictions.LoadAsync(hostId, cancellationToken);

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

    private Task DeleteTemplateAsync(int templateId) =>
        MutateAsync(async hostId =>
            Publish(
                await _predictions.DeleteTemplateAsync(hostId, templateId, CancellationToken.None)
            )
        );

    private Task StartPredictionAsync(int templateId) =>
        MutateAsync(async hostId =>
            Publish(await _predictions.StartAsync(hostId, templateId, CancellationToken.None))
        );

    private Task LockPredictionAsync() =>
        ConfirmedMutationAsync(
            "Lock this Twitch Prediction?",
            (hostId, confirmed) => _predictions.LockAsync(hostId, confirmed, CancellationToken.None)
        );

    private Task CancelPredictionAsync() =>
        ConfirmedMutationAsync(
            "Cancel this Twitch Prediction? Twitch refunds viewers.",
            (hostId, confirmed) =>
                _predictions.CancelAsync(hostId, confirmed, CancellationToken.None)
        );

    private Task ResolvePredictionAsync(string outcomeId) =>
        ConfirmedMutationAsync(
            "Resolve this Twitch Prediction and pay winners?",
            (hostId, confirmed) =>
                _predictions.ResolveAsync(hostId, outcomeId, confirmed, CancellationToken.None)
        );

    private async Task ConfirmedMutationAsync(
        string prompt,
        Func<int, bool, Task<PredictionOperationOutcome>> operation
    )
    {
        var confirmed = await _js.InvokeAsync<bool>("confirm", [prompt]);
        await MutateAsync(async hostId => Publish(await operation(hostId, confirmed)));
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
        Publish(message, success);
    }
}
