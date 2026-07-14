using System.Reflection;
using BlokeBot.Auth.Web;
using BlokeBot.Commands;
using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Auth;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class P6BoundaryContractTests
{
    [Test]
    public void GuessingQueries_Inspecting_ExposeOnlyNamedProjectionAndReplySourceOperations()
    {
        AssertExactPublicMethods(
            typeof(GuessRoundProfileQueryExtensions),
            new(
                "LoadDefaultProfileIdAsync",
                typeof(Task<int>),
                typeof(IQueryable<GuessRoundProfile>),
                typeof(int),
                typeof(CancellationToken)
            ),
            new(
                "LoadProfileIdByNameAsync",
                typeof(Task<int?>),
                typeof(IQueryable<GuessRoundProfile>),
                typeof(int),
                typeof(string),
                typeof(CancellationToken)
            ),
            new(
                "LoadDefaultProfileAsync",
                typeof(Task<GuessRoundProfileDetails>),
                typeof(IQueryable<GuessRoundProfile>),
                typeof(int),
                typeof(CancellationToken)
            ),
            new(
                "LoadDefaultProfileWithOptionsAsync",
                typeof(Task<GuessRoundProfileDetailsWithOptions>),
                typeof(IQueryable<GuessRoundProfile>),
                typeof(int),
                typeof(CancellationToken)
            ),
            new(
                "LoadProfileAsync",
                typeof(Task<GuessRoundProfileDetails>),
                typeof(IQueryable<GuessRoundProfile>),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            ),
            new(
                "LoadProfileWithOptionsAsync",
                typeof(Task<GuessRoundProfileDetailsWithOptions>),
                typeof(IQueryable<GuessRoundProfile>),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            )
        );
        AssertExactPublicMethods(
            typeof(GuessingReplySettingsQueries),
            new(
                "LoadForRoundAsync",
                typeof(Task<GuessingReplySettingsResolution>),
                typeof(BlokeBotDbContext),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            ),
            new(
                "LoadForProfileAsync",
                typeof(Task<GuessingReplySettingsResolution>),
                typeof(BlokeBotDbContext),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            ),
            new(
                "LoadForDefaultAsync",
                typeof(Task<GuessingReplySettingsResolution>),
                typeof(BlokeBotDbContext),
                typeof(int),
                typeof(CancellationToken)
            )
        );
    }

    [Test]
    public void ExplicitModeBoundaries_Inspecting_HaveNoRetiredOverloads()
    {
        AssertExactPublicMethods(
            typeof(HostBotAccountOAuthService),
            new(
                "CreateAuthorizationUriForDefaultScopes",
                typeof(OAuthAuthorizationStartOutcome),
                typeof(string)
            ),
            new(
                "CreateAuthorizationUriForScopes",
                typeof(OAuthAuthorizationStartOutcome),
                typeof(string),
                typeof(OAuthAuthorizationScopeSet)
            ),
            new(
                "CompleteAsync",
                typeof(Task<OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>>),
                typeof(string),
                typeof(CancellationToken)
            ),
            new("RequestedScopes", typeof(OAuthAuthorizationScopeSet))
        );
        AssertExactPublicMethods(
            typeof(CommandAliasRegistry),
            new(
                "ReplaceAliasesAsync",
                typeof(Task),
                typeof(BlokeBotDbContext),
                typeof(int),
                typeof(IReadOnlySet<AppCommandKind>),
                typeof(IEnumerable<CommandAliasDraft>),
                typeof(CommandAliasScope),
                typeof(CancellationToken)
            ),
            new(
                "JoinAliases",
                typeof(string),
                typeof(IEnumerable<CommandAlias>),
                typeof(AppCommandKind),
                typeof(CommandAliasScope)
            )
        );
        AssertExactPublicMethods(
            typeof(LoginPage),
            new("Render", typeof(string)),
            new("RenderError", typeof(string), typeof(string))
        );

        typeof(AppCommandAliasResolution)
            .GetProperty(nameof(AppCommandAliasResolution.Scope))!
            .PropertyType.ShouldBe(typeof(CommandAliasScope));
        typeof(AuthorizationUriRequest)
            .GetProperty(nameof(AuthorizationUriRequest.Scopes))!
            .PropertyType.ShouldBe(typeof(OAuthAuthorizationScopeSet));
        typeof(AuthorizationUriRequest)
            .GetProperty(nameof(AuthorizationUriRequest.Verification))!
            .PropertyType.ShouldBe(typeof(AuthorizationVerificationPolicy));
    }

    [Test]
    public void GiveawayFormatter_Inspecting_AcceptsOnlyTypedOutcomes()
    {
        var replyMethods = DeclaredPublicMethods(typeof(PointsGiveawayMessageFormatter))
            .Where(method => method.Name == "Reply")
            .ToArray();

        AssertExactMethods(
            replyMethods,
            new(
                "Reply",
                typeof(PointOperationOutcome),
                typeof(PointsGiveawayStartOutcome),
                typeof(ReplyDeliveryMap)
            ),
            new(
                "Reply",
                typeof(PointOperationOutcome),
                typeof(PointsGiveawayJoinOutcome),
                typeof(ReplyDeliveryMap)
            ),
            new(
                "Reply",
                typeof(PointOperationOutcome),
                typeof(PointsGiveawayDrawOutcome),
                typeof(ReplyDeliveryMap)
            ),
            new(
                "Reply",
                typeof(PointOperationOutcome),
                typeof(PointsGiveawayCancelOutcome),
                typeof(ReplyDeliveryMap)
            )
        );
    }

    private static void AssertExactPublicMethods(Type type, params MethodContract[] expected)
    {
        AssertExactMethods(DeclaredPublicMethods(type), expected);
    }

    private static void AssertExactMethods(MethodInfo[] methods, params MethodContract[] expected)
    {
        methods.Length.ShouldBe(expected.Length);
        foreach (var contract in expected)
        {
            var method = methods.Single(candidate =>
                candidate.Name == contract.Name
                && candidate
                    .GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .SequenceEqual(contract.ParameterTypes)
            );
            method.ReturnType.ShouldBe(contract.ReturnType);
            method.GetParameters().ShouldAllBe(parameter => !parameter.IsOptional);
        }
    }

    private static MethodInfo[] DeclaredPublicMethods(Type type)
    {
        return type.GetMethods(
            BindingFlags.Public
                | BindingFlags.Static
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly
        );
    }

    private sealed record MethodContract(
        string Name,
        Type ReturnType,
        params Type[] ParameterTypes
    );
}
