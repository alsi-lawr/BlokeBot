using BlokeBot.Core.Features.TwitchOperations.Predictions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PredictionTemplateDraftTests
{
    [Test]
    public void TwitchBounds_ValidatingTemplate_EnforceProviderContract()
    {
        new PredictionTemplateDraft("Will it happen?", ["Yes", "No"], 30)
            .Validate()
            .ShouldBeOfType<PredictionTemplateValidationOutcome.Valid>();
        new PredictionTemplateDraft(new string('x', 46), ["Yes", "No"], 30)
            .Validate()
            .ShouldBeOfType<PredictionTemplateValidationOutcome.Invalid>();
        new PredictionTemplateDraft("Title", ["Yes"], 30)
            .Validate()
            .ShouldBeOfType<PredictionTemplateValidationOutcome.Invalid>();
        new PredictionTemplateDraft("Title", ["Yes", "No"], 29)
            .Validate()
            .ShouldBeOfType<PredictionTemplateValidationOutcome.Invalid>();
    }
}
