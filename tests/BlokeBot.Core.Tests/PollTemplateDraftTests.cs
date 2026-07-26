using BlokeBot.Core.Features.TwitchOperations.Polls;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PollTemplateDraftTests
{
    [Test]
    public void Validation_ValidBroadcasterPollTemplate_AcceptsFullTwitchLimits()
    {
        Valid().Validate().ShouldBeOfType<PollTemplateValidationOutcome.Valid>();
    }

    [Test]
    public void Validation_TitleOutsideTwitchLimit_ReturnsTypedInvalid()
    {
        new PollTemplateDraft(new string('x', 61), ["Yes", "No"], 60, false, null)
            .Validate()
            .ShouldBeOfType<PollTemplateValidationOutcome.Invalid>();
    }

    [Test]
    public void Validation_ChoiceCountOutsideTwitchLimit_ReturnsTypedInvalid()
    {
        new PollTemplateDraft("Question", ["Only"], 60, false, null)
            .Validate()
            .ShouldBeOfType<PollTemplateValidationOutcome.Invalid>();
    }

    [Test]
    public void Validation_DurationOutsideTwitchLimit_ReturnsTypedInvalid()
    {
        new PollTemplateDraft("Question", ["Yes", "No"], 14, false, null)
            .Validate()
            .ShouldBeOfType<PollTemplateValidationOutcome.Invalid>();
    }

    [Test]
    public void Validation_ChannelPointsVotingWithoutCost_ReturnsTypedInvalid()
    {
        new PollTemplateDraft("Question", ["Yes", "No"], 60, true, null)
            .Validate()
            .ShouldBeOfType<PollTemplateValidationOutcome.Invalid>();
    }

    [Test]
    public void Validation_ChannelPointsVotingWithCost_AcceptsReusableTemplate()
    {
        new PollTemplateDraft("Question", ["Yes", "No"], 60, true, 100)
            .Validate()
            .ShouldBeOfType<PollTemplateValidationOutcome.Valid>();
    }

    private static PollTemplateDraft Valid()
    {
        return new("Question", ["Yes", "No"], 60, false, null);
    }
}
