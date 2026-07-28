namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public abstract record PredictionOperationOutcome
{
    private PredictionOperationOutcome() { }

    public sealed record Started(PredictionView Prediction) : PredictionOperationOutcome;

    public sealed record Updated(PredictionView Prediction) : PredictionOperationOutcome;

    public sealed record TemplateSaved(PredictionTemplateView Template)
        : PredictionOperationOutcome;

    public sealed record ActivePredictionExists : PredictionOperationOutcome;

    public sealed record TemplateNotFound : PredictionOperationOutcome;

    public sealed record TemplateDeleted : PredictionOperationOutcome;

    public sealed record ConfirmationRequired : PredictionOperationOutcome;

    public sealed record InvalidOutcome : PredictionOperationOutcome;

    public sealed record NotReady(string Message) : PredictionOperationOutcome;

    public sealed record Ineligible(string Message) : PredictionOperationOutcome;

    public sealed record Unavailable(string Message) : PredictionOperationOutcome;

    public sealed record InvalidTemplate(string Message) : PredictionOperationOutcome;

    public sealed record ProviderRejected(string Message) : PredictionOperationOutcome;
}
