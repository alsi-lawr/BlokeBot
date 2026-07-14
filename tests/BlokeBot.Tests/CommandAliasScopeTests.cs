using System.Reflection;
using BlokeBot.Features.Commands;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CommandAliasScopeTests
{
    [Test]
    public void GlobalAndProfileScopes_Matching_DispatchToTheirTypedHandlers()
    {
        CommandAliasScope global = new CommandAliasScope.Global();
        CommandAliasScope profile = new CommandAliasScope.Profile(42);

        global
            .Match(static _ => "global", static value => $"profile:{value.ProfileId}")
            .ShouldBe("global");
        profile
            .Match(static _ => "global", static value => $"profile:{value.ProfileId}")
            .ShouldBe("profile:42");
    }

    [Test]
    public void InvalidProfileIds_ConstructingProfileScope_Rejects()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandAliasScope.Profile(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandAliasScope.Profile(-1));
    }

    [Test]
    public void CommandAliasScope_Inspecting_HasClosedNativeUnionContract()
    {
        var unionType = typeof(CommandAliasScope);
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
        directCases.Select(type => type.Name).ShouldBe(["Global", "Profile"]);
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
