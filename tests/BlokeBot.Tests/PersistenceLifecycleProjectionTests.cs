using System.Reflection;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PersistenceLifecycleProjectionTests
{
    [Test]
    public void LifecycleUnions_Inspecting_HaveDeclaredDirectCasesAndCompleteMatchHandlers()
    {
        AssertUnionContract(typeof(GuessRoundLifecycle), ["Closed", "Completed", "Open"]);
        AssertUnionContract(
            typeof(PointsGiveawayLifecycle),
            ["Active", "Cancelled", "Completed", "Expired"]
        );
        AssertUnionContract(
            typeof(HostedChannelRuntimeLifecycle),
            ["Started", "Starting", "Stopped", "Stopping"]
        );
    }

    [Test]
    public void GuessRoundStates_MappingPersistence_ProduceClosedLifecycleCases()
    {
        var started = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        var closed = started.AddMinutes(5);

        GuessRoundLifecycle
            .FromPersistence(GuessRoundStatus.Open, started, null, null)
            .ShouldBeOfType<GuessRoundLifecycle.Open>();
        GuessRoundLifecycle
            .FromPersistence(GuessRoundStatus.Closed, started, closed, null)
            .ShouldBeOfType<GuessRoundLifecycle.Closed>()
            .ClosedAtUtc.ShouldBe(closed);
        GuessRoundLifecycle
            .FromPersistence(GuessRoundStatus.Completed, started, closed, "blue")
            .ShouldBeOfType<GuessRoundLifecycle.Completed>()
            .WinningName.ShouldBe("blue");

        Should.Throw<PersistenceDataIntegrityException>(() =>
            GuessRoundLifecycle.FromPersistence(GuessRoundStatus.Completed, started, closed, null)
        );
    }

    [Test]
    public void GiveawayStates_MappingPersistence_RequireTerminalCompletionTime()
    {
        var completed = new DateTime(2026, 7, 14, 10, 5, 0, DateTimeKind.Utc);

        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Active, completed.AddMinutes(-5), null)
            .ShouldBeOfType<PointsGiveawayLifecycle.Active>();
        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Completed, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Completed>();
        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Cancelled, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Cancelled>();
        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Expired, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Expired>();

        Should.Throw<PersistenceDataIntegrityException>(() =>
            PointsGiveawayLifecycle.FromPersistence(
                PointsGiveawayStatus.Completed,
                completed.AddMinutes(-5),
                null
            )
        );
    }

    [Test]
    public void RuntimeStates_MappingPersistence_RequireTransitionTimesWhenActive()
    {
        var changed = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);

        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Stopped, null)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Stopped>();
        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Starting, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Starting>();
        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Started, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Started>();
        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Stopping, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Stopping>();

        Should.Throw<PersistenceDataIntegrityException>(() =>
            HostedChannelRuntimeLifecycle.FromPersistence(BotChannelRuntimeState.Started, null)
        );
    }

    private static void AssertUnionContract(Type unionType, string[] expectedCaseNames)
    {
        var directCases = unionType
            .Assembly.GetTypes()
            .Where(type => type.BaseType == unionType)
            .OrderBy(type => type.Name)
            .ToArray();
        var match = unionType
            .GetMethods(
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            )
            .Single(method => method.Name == "Match");
        var resultType = match.GetGenericArguments().ShouldHaveSingleItem();
        var constructor =
            unionType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            ) ?? throw new InvalidOperationException("The private union constructor is missing.");
        var handlers = match.GetParameters();

        unionType.IsAbstract.ShouldBeTrue();
        unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();
        constructor.IsPrivate.ShouldBeTrue();
        directCases.Select(type => type.Name).ShouldBe(expectedCaseNames);
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
