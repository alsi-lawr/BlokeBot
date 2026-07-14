using System.Reflection;
using BlokeBot.Features.Commands;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class AppCommandRouteStateTests
{
    [Test]
    public void HostAndGuessingProfileRoutes_Matching_DispatchToTypedHandlers()
    {
        AppCommandRouteState host = new AppCommandRouteState.Host(7);
        AppCommandRouteState guessingProfile = new AppCommandRouteState.GuessingProfile(7, 11);

        host.Match(
                static route => $"host:{route.HostId}",
                static route => $"profile:{route.HostId}:{route.ProfileId}"
            )
            .ShouldBe("host:7");
        guessingProfile
            .Match(
                static route => $"host:{route.HostId}",
                static route => $"profile:{route.HostId}:{route.ProfileId}"
            )
            .ShouldBe("profile:7:11");
    }

    [Test]
    public void RouteCases_Comparing_UseCaseAndIdentifierValueEquality()
    {
        new AppCommandRouteState.Host(7).ShouldBe(new AppCommandRouteState.Host(7));
        new AppCommandRouteState.Host(7).ShouldNotBe(new AppCommandRouteState.Host(8));
        new AppCommandRouteState.GuessingProfile(7, 11).ShouldBe(
            new AppCommandRouteState.GuessingProfile(7, 11)
        );
        new AppCommandRouteState.GuessingProfile(7, 11).ShouldNotBe(
            new AppCommandRouteState.GuessingProfile(8, 11)
        );
        new AppCommandRouteState.GuessingProfile(7, 11).ShouldNotBe(
            new AppCommandRouteState.GuessingProfile(7, 12)
        );
        AppCommandRouteState host = new AppCommandRouteState.Host(7);
        host.ShouldNotBe(new AppCommandRouteState.GuessingProfile(7, 11));
    }

    [Test]
    public void InvalidIdentifiers_ConstructingRoutes_Rejects()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AppCommandRouteState.Host(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new AppCommandRouteState.Host(-1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new AppCommandRouteState.GuessingProfile(0, 1)
        );
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new AppCommandRouteState.GuessingProfile(1, 0)
        );
    }

    [Test]
    public void AppCommandRouteState_Inspecting_HasClosedNativeUnionContract()
    {
        var unionType = typeof(AppCommandRouteState);
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
        var parameterlessConstructor = constructors.Single(candidate =>
            candidate.GetParameters().Length == 0
        );
        var recordCopyConstructor = constructors.Single(candidate =>
            candidate.GetParameters() is [var parameter] && parameter.ParameterType == unionType
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
        recordCopyConstructor.IsFamily.ShouldBeTrue();
        directCases.Select(type => type.Name).ShouldBe(["GuessingProfile", "Host"]);
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
