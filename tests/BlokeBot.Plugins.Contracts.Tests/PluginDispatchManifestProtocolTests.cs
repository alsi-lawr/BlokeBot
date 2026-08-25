using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginDispatchManifestProtocolTests
{
    [Test]
    public void DispatchDeclarations_RoundTripThroughTheReviewedManifestProtocol()
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("handle", out var operation).ShouldBeTrue();
        PluginEventHandlerId.TryCreate("stream-online", out var eventHandler).ShouldBeTrue();
        PluginScheduleHandlerId.TryCreate("refresh", out var scheduleHandler).ShouldBeTrue();
        var validated = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(
                    accepted.Manifest with
                    {
                        Features = accepted.Manifest.Features.Replace(
                            feature,
                            feature with
                            {
                                Twitch = feature.Twitch with
                                {
                                    EventSubTypes = feature.Twitch.EventSubTypes.Add(
                                        "stream.online"
                                    ),
                                },
                                Dispatch = new(
                                    [
                                        new(
                                            "plugin-route",
                                            module,
                                            operation,
                                            PluginCallbackRequirements.Independent
                                        ),
                                    ],
                                    [
                                        new(
                                            eventHandler,
                                            new PluginEventSource.Twitch(
                                                PluginTwitchEventKind.StreamOnline
                                            ),
                                            module,
                                            operation,
                                            PluginCallbackRequirements.Twitch
                                        ),
                                    ],
                                    [
                                        new(
                                            scheduleHandler,
                                            module,
                                            operation,
                                            PluginCallbackRequirements.Independent
                                        ),
                                    ]
                                ),
                            }
                        ),
                    },
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;

        var roundTripped = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginManifestJson.Serialize(validated),
                    PluginContractFixtures.CompatibleHost()
                )
        )
            .Manifest.Manifest.Features.Single(item => item.Id == feature.Id)
            .DispatchDeclarations;

        roundTripped.Commands.ShouldHaveSingleItem().Route.ShouldBe("plugin-route");
        _ = roundTripped
            .Events.ShouldHaveSingleItem()
            .Source.ShouldBeOfType<PluginEventSource.Twitch>();
        roundTripped.Schedules.ShouldHaveSingleItem().Id.ShouldBe(scheduleHandler);
    }

    [Test]
    public void TwitchEventHandler_MissingMismatchedOrIndependentDeclaration_IsRejected()
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("handle", out var operation).ShouldBeTrue();
        PluginEventHandlerId.TryCreate("stream-online", out var eventHandler).ShouldBeTrue();
        var handler = new PluginEventHandlerDescriptor(
            eventHandler,
            new PluginEventSource.Twitch(PluginTwitchEventKind.StreamOnline),
            module,
            operation,
            PluginCallbackRequirements.Twitch
        );

        var missing = PluginManifestValidator.Validate(
            accepted.Manifest with
            {
                Features = accepted.Manifest.Features.Replace(
                    feature,
                    feature with
                    {
                        Dispatch = new([], [handler], []),
                    }
                ),
            },
            PluginContractFixtures.CompatibleHost()
        );
        var mismatched = PluginManifestValidator.Validate(
            accepted.Manifest with
            {
                Features = accepted.Manifest.Features.Replace(
                    feature,
                    feature with
                    {
                        Twitch = feature.Twitch with
                        {
                            EventSubTypes = feature.Twitch.EventSubTypes.Add("stream.offline"),
                        },
                        Dispatch = new([], [handler], []),
                    }
                ),
            },
            PluginContractFixtures.CompatibleHost()
        );
        var independent = PluginManifestValidator.Validate(
            accepted.Manifest with
            {
                Features = accepted.Manifest.Features.Replace(
                    feature,
                    feature with
                    {
                        Twitch = feature.Twitch with
                        {
                            EventSubTypes = feature.Twitch.EventSubTypes.Add("stream.online"),
                        },
                        Dispatch = new(
                            [],
                            [
                                handler with
                                {
                                    Requirements = PluginCallbackRequirements.Independent,
                                },
                            ],
                            []
                        ),
                    }
                ),
            },
            PluginContractFixtures.CompatibleHost()
        );

        Errors(missing)
            .ShouldContain(error =>
                error.Code == PluginManifestErrorCode.InvalidDispatchDeclaration
            );
        Errors(mismatched)
            .ShouldContain(error =>
                error.Code == PluginManifestErrorCode.InvalidDispatchDeclaration
            );
        Errors(independent)
            .ShouldContain(error =>
                error.Code == PluginManifestErrorCode.InvalidDispatchDeclaration
            );
    }

    [Test]
    public void ArbitraryEventSubType_RequiresAndRoundTripsOneExactRawHandler()
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
        var module = accepted.Manifest.LuaModules[0].Id;
        PluginHostOperationId.TryCreate("channel_ban", out var operation).ShouldBeTrue();
        PluginEventHandlerId.TryCreate("channel-ban", out var eventHandler).ShouldBeTrue();
        var withArbitraryType = feature with
        {
            Twitch = feature.Twitch with
            {
                EventSubTypes = feature.Twitch.EventSubTypes.Add("channel.ban"),
            },
        };
        var raw = withArbitraryType with
        {
            Dispatch = new(
                [],
                [
                    new(
                        eventHandler,
                        new PluginEventSource.TwitchRaw("channel.ban", "1"),
                        module,
                        operation,
                        PluginCallbackRequirements.Twitch
                    ),
                ],
                []
            ),
        };

        var orphan = PluginManifestValidator.Validate(
            accepted.Manifest with
            {
                Features = accepted.Manifest.Features.Replace(feature, withArbitraryType),
            },
            PluginContractFixtures.CompatibleHost()
        );
        var validated = PluginManifestValidator.Validate(
            accepted.Manifest with
            {
                Features = accepted.Manifest.Features.Replace(feature, raw),
            },
            PluginContractFixtures.CompatibleHost()
        );

        Errors(orphan)
            .ShouldContain(error =>
                error.Code == PluginManifestErrorCode.InvalidDispatchDeclaration
            );
        var manifest = validated
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        var roundTripped = PluginManifestJson
            .Validate(
                PluginManifestJson.Serialize(manifest),
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest.Manifest.Features.Single(item => item.Id == feature.Id)
            .DispatchDeclarations.Events.ShouldHaveSingleItem()
            .Source.ShouldBeOfType<PluginEventSource.TwitchRaw>();
        roundTripped.EventSubType.ShouldBe("channel.ban");
        roundTripped.Version.ShouldBe("1");
    }

    private static IReadOnlyList<PluginManifestError> Errors(
        PluginManifestValidationOutcome outcome
    ) => outcome.ShouldBeOfType<PluginManifestValidationOutcome.Rejected>().Errors;
}
