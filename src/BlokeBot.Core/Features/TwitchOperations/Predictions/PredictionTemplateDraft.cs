namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public sealed record PredictionTemplateDraft(
    string Title,
    IReadOnlyList<string> Outcomes,
    int PredictionWindowSeconds
)
{
    public PredictionTemplateValidationOutcome Validate()
    {
        var title = Title.Trim();
        var outcomes = Outcomes.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        var titleInvalid = title.Length is < 1 or > 45;
        var outcomesInvalid = outcomes.Length is < 2 or > 10 || outcomes.Any(x => x.Length > 25);
        var windowInvalid = PredictionWindowSeconds is < 30 or > 1800;
        return titleInvalid switch
        {
            true => new PredictionTemplateValidationOutcome.Invalid(
                "Prediction titles must be 1–45 characters."
            ),
            false when outcomesInvalid => new PredictionTemplateValidationOutcome.Invalid(
                "Predictions need 2–10 outcomes, each no longer than 25 characters."
            ),
            false when windowInvalid => new PredictionTemplateValidationOutcome.Invalid(
                "Prediction windows must be 30–1800 seconds."
            ),
            _ => new PredictionTemplateValidationOutcome.Valid(
                new(title, outcomes, PredictionWindowSeconds)
            ),
        };
    }
}

public abstract record PredictionTemplateValidationOutcome
{
    private PredictionTemplateValidationOutcome() { }

    public sealed record Valid(PredictionTemplateDraft Draft) : PredictionTemplateValidationOutcome;

    public sealed record Invalid(string Message) : PredictionTemplateValidationOutcome;
}
