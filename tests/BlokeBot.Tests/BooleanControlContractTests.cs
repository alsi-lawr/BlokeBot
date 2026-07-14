using System.Reflection;
using BlokeBot.Features.HostedChannels.Authorization;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class BooleanControlContractTests
{
    [Test]
    public void NativeUnions_Inspecting_HaveDeclaredDirectCasesAndCompleteMatchHandlers()
    {
        AssertUnionContract(typeof(BotRuntimeStatus), ["Authorized", "Connected", "Unauthorized"]);
        AssertUnionContract(
            typeof(WhisperResponseConfigurationOutcome),
            ["Configured", "CustomBotRequired", "HostNotFound"]
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
        var constructors = unionType.GetConstructors(
            BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly
        );
        var constructor = constructors.Single(candidate => candidate.GetParameters().Length == 0);
        var recordCopyConstructor = constructors.Single(candidate =>
            candidate.GetParameters() is [var parameter] && parameter.ParameterType == unionType
        );
        var handlers = match.GetParameters();

        unionType.IsAbstract.ShouldBeTrue();
        unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();
        constructors.Length.ShouldBe(2);
        constructor.IsPrivate.ShouldBeTrue();
        recordCopyConstructor.IsFamily.ShouldBeTrue();
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
