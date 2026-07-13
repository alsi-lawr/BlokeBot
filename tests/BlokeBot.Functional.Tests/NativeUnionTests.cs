using System.Reflection;
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

    [Test]
    public void UnionContract_Inspecting_IsClosedAndHandlerComplete()
    {
        var unionType = typeof(SubmissionOutcome);
        var directCases = unionType
            .Assembly.GetTypes()
            .Where(type => type.BaseType == unionType)
            .OrderBy(type => type.Name)
            .ToArray();
        var seal =
            unionType.GetMethod("Seal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The union seal contract is missing.");
        var match =
            unionType.GetMethod(nameof(SubmissionOutcome.Match))
            ?? throw new InvalidOperationException("The exhaustive Match contract is missing.");
        var handledCases = match
            .GetParameters()
            .Select(parameter => parameter.ParameterType.GetGenericArguments()[0])
            .OrderBy(type => type.Name)
            .ToArray();

        unionType.IsAbstract.ShouldBeTrue();
        unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();
        seal.IsAbstract.ShouldBeTrue();
        seal.IsFamilyAndAssembly.ShouldBeTrue();
        directCases.Select(type => type.Name).ShouldBe(["Accepted", "Deferred", "Rejected"]);
        handledCases.ShouldBe(directCases);

        foreach (var caseType in directCases)
        {
            caseType.IsSealed.ShouldBeTrue();
            var caseSeal =
                caseType.GetMethod(
                    "Seal",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )
                ?? throw new InvalidOperationException(
                    $"{caseType.Name} does not implement the seal."
                );
            caseSeal.GetBaseDefinition().ShouldBe(seal);
        }
    }
}
