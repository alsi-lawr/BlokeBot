using System.Reflection;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Giveaways;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PointsOutcomeContractTests
{
    [Test]
    public void NativePointOutcomes_Inspecting_HaveClosedExhaustiveContracts()
    {
        AssertUnion(typeof(PointOperationOutcome), ["Failed", "Succeeded"]);
        AssertUnion(
            typeof(PointBalanceMutationFailure),
            ["CapExceeded", "InsufficientBalance", "InvalidAmount", "UnknownUser"]
        );
        AssertUnion(typeof(PointGambleOutcome), ["Lost", "Won"]);
        AssertUnion(
            typeof(PointsGiveawayStartOutcome),
            [
                "AlreadyActive",
                "Cooldown",
                "FollowerEligibilityUnavailable",
                "Started",
                "StreamLivenessUnavailable",
                "StreamOffline",
            ]
        );
        AssertUnion(
            typeof(PointsGiveawayJoinOutcome),
            [
                "DuplicateJoin",
                "FollowerEligibilityUnavailable",
                "Joined",
                "NotActive",
                "NotEligible",
            ]
        );
        AssertUnion(
            typeof(PointsGiveawayDrawOutcome),
            ["Missing", "NoEntrants", "NotActive", "PayoutFailed", "Winners"]
        );
        AssertUnion(typeof(GuessingWinnerDeclarationOutcome), ["Completed", "PayoutFailed"]);
        AssertUnion(typeof(PointsGiveawayCancelOutcome), ["Cancelled", "NotActive"]);
    }

    [Test]
    public void WonAndLostGambles_Matching_DispatchToNamedHandlers()
    {
        PointGambleOutcome won = new PointGambleOutcome.Won();
        PointGambleOutcome lost = new PointGambleOutcome.Lost();

        won.Match(static _ => "won", static _ => "lost").ShouldBe("won");
        lost.Match(static _ => "won", static _ => "lost").ShouldBe("lost");
    }

    [Test]
    public void EmptyWinnerCollection_ConstructingWinnersOutcome_Rejects()
    {
        Should.Throw<ArgumentException>(() => new PointsGiveawayDrawOutcome.Winners(new(), []));
    }

    private static void AssertUnion(Type unionType, string[] expectedCases)
    {
        var directCases = unionType
            .Assembly.GetTypes()
            .Where(type => type.BaseType == unionType)
            .OrderBy(type => type.Name)
            .ToArray();
        var constructors = unionType.GetConstructors(
            BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly
        );
        var parameterlessConstructor = constructors.Single(constructor =>
            constructor.GetParameters().Length == 0
        );
        var copyConstructor = constructors.Single(constructor =>
            constructor.GetParameters() is [var parameter] && parameter.ParameterType == unionType
        );
        var match = unionType
            .GetMethods(
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            )
            .Single(method => method.Name == "Match");
        var resultType = match.GetGenericArguments().ShouldHaveSingleItem();
        var handlers = match.GetParameters();

        unionType.IsAbstract.ShouldBeTrue();
        constructors.Length.ShouldBe(2);
        parameterlessConstructor.IsPrivate.ShouldBeTrue();
        copyConstructor.IsFamily.ShouldBeTrue();
        directCases.Select(type => type.Name).ShouldBe(expectedCases);
        directCases.ShouldAllBe(type => type.DeclaringType == unionType);
        directCases.ShouldAllBe(type => type.IsSealed);
        match.IsGenericMethodDefinition.ShouldBeTrue();
        match.ReturnType.ShouldBe(resultType);
        handlers.Length.ShouldBe(directCases.Length);
        handlers.ShouldAllBe(parameter =>
            parameter.ParameterType.IsGenericType
            && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Func<,>)
            && parameter.ParameterType.GetGenericArguments()[1] == resultType
        );
        handlers
            .Select(parameter => parameter.ParameterType.GetGenericArguments()[0])
            .OrderBy(type => type.Name)
            .ShouldBe(directCases);
        unionType
            .GetMethods(
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            )
            .ShouldNotContain(method => method.Name == "Seal");
    }
}
