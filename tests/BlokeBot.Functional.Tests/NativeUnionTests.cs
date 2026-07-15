using BlokeBot.Functional.Tests.Examples;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Functional.Tests;

public sealed class NativeUnionTests
{
    [Test]
    public void Accepted_Describing_UsesAcceptedPayload()
    {
        SubmissionOutcome outcome = new SubmissionOutcome.Accepted("receipt-42");

        SubmissionOutcomeDescription.Describe(outcome).ShouldBe("Accepted: receipt-42");
    }

    [Test]
    public void Deferred_Describing_UsesDeferredPayload()
    {
        SubmissionOutcome outcome = new SubmissionOutcome.Deferred(TimeSpan.FromMinutes(5));

        SubmissionOutcomeDescription.Describe(outcome).ShouldBe("Deferred: 5 minutes");
    }

    [Test]
    public void Rejected_Describing_UsesRejectedPayload()
    {
        SubmissionOutcome outcome = new SubmissionOutcome.Rejected("not eligible");

        SubmissionOutcomeDescription.Describe(outcome).ShouldBe("Rejected: not eligible");
    }

    [Test]
    public void InvalidCasePayloads_Constructing_AreRejectedByOwningCases()
    {
        Should.Throw<ArgumentException>(() => new SubmissionOutcome.Accepted(" "));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SubmissionOutcome.Deferred(TimeSpan.Zero)
        );
        Should.Throw<ArgumentException>(() => new SubmissionOutcome.Rejected(string.Empty));
    }

    [Test]
    public void EquivalentCases_Comparing_HaveValueSemantics()
    {
        new SubmissionOutcome.Accepted("receipt-42").ShouldBe(
            new SubmissionOutcome.Accepted("receipt-42")
        );
        new SubmissionOutcome.Deferred(TimeSpan.FromMinutes(5)).ShouldBe(
            new SubmissionOutcome.Deferred(TimeSpan.FromMinutes(5))
        );
        new SubmissionOutcome.Rejected("not eligible").ShouldBe(
            new SubmissionOutcome.Rejected("not eligible")
        );
        new SubmissionOutcome.Accepted("receipt-42").ShouldNotBe(
            new SubmissionOutcome.Accepted("receipt-41")
        );
    }
}
