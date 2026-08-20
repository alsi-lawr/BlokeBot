using System.Globalization;
using System.Text;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferContractTests
{
    [Test]
    [Arguments(HostFeatureFlags.None)]
    [Arguments(HostFeatureFlags.Polls)]
    [Arguments(
        HostFeatureFlags.Automations
            | HostFeatureFlags.RewardsAndRedemptions
            | HostFeatureFlags.Bingo
            | HostFeatureFlags.CustomCommands
    )]
    [Arguments(HostFeatureFlags.All)]
    public void Enablement_RoundTripsEveryIndependentFlag(HostFeatureFlags flags)
    {
        var mapped = ChannelToolEnablementMapper.FromFlags(flags);

        ChannelToolEnablementMapper.ToFlags(mapped).ShouldBe(flags);
        HostFeatureCatalog.Features.Count.ShouldBe(20);
        HostFeatureFlags.NativeTwitchFeatures.ShouldBe(
            HostFeatureFlags.Polls
                | HostFeatureFlags.ClipsAndMarkers
                | HostFeatureFlags.RewardsAndRedemptions
                | HostFeatureFlags.Predictions
                | HostFeatureFlags.RaidCollaboration
        );
    }

    [Test]
    public void TypedCodec_RoundTripsDeterministicallyAndRejectsUnknownMembers()
    {
        var codec = new ConfigurationDocumentCodec();
        var document = Document(ChannelToolEnablementMapper.FromFlags(HostFeatureFlags.Polls));

        var first = codec.Serialize(document);
        var second = codec.Serialize(document);

        first.ShouldBe(second);
        ((ConfigurationDocumentParseOutcome.Valid)codec.Parse(first)).Document.ShouldBe(document);

        var withUnknown = Encoding
            .UTF8.GetString(first)
            .Replace(
                "\"format\":",
                "\"unexpected\": true,\n  \"format\":",
                StringComparison.Ordinal
            );
        var invalid = codec
            .Parse(Encoding.UTF8.GetBytes(withUnknown))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
        invalid.Issue.Location.ShouldBe("$.unexpected");
    }

    [Test]
    public void VersionAdapter_MigratesV0AndRejectsFutureVersion()
    {
        var codec = new ConfigurationDocumentCodec();
        const string V0 = """
            {
              "format": "blokebot.channel-configuration",
              "version": 0,
              "exportedAtUtc": "2026-08-20T12:00:00Z",
              "channelLogin": "source",
              "sections": {}
            }
            """;
        const string Future = """
            {
              "format": "blokebot.channel-configuration",
              "version": 99,
              "exportedAtUtc": "2026-08-20T12:00:00Z",
              "source": { "channelLogin": "source" },
              "sections": {}
            }
            """;

        var migrated = codec
            .Parse(Encoding.UTF8.GetBytes(V0))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>();
        migrated.Document.Version.ShouldBe(1);
        migrated.Document.Source.ChannelLogin.ShouldBe("source");

        var rejected = codec
            .Parse(Encoding.UTF8.GetBytes(Future))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
        rejected.Issue.Location.ShouldBe("version");
        rejected.Issue.Message.ShouldContain("newer");
    }

    [Test]
    public void LocalReferenceValidation_RejectsMissingReplyWithRecordLocation()
    {
        var document = Document(
            commands: new(
                "UTC",
                [],
                [],
                [
                    new(
                        "command-0001",
                        "hello",
                        true,
                        ["hello"],
                        true,
                        true,
                        [],
                        0,
                        CustomCommandCooldownScope.User,
                        CustomCommandInvocationLimit.Unlimited,
                        new(CustomCommandActionTypeV1.Message, ZeroArgumentReplyId: "missing")
                    ),
                ]
            )
        );

        var invalid = new ConfigurationDocumentCodec()
            .Parse(new ConfigurationDocumentCodec().Serialize(document))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();

        invalid.Issue.Location.ShouldBe("sections.customCommands.commands[command-0001].action");
        invalid.Issue.Message.ShouldContain("missing");
    }

    [Test]
    public void FutureEnablementMember_IsRejectedInsteadOfDropped()
    {
        var codec = new ConfigurationDocumentCodec();
        var json = Encoding
            .UTF8.GetString(
                codec.Serialize(
                    Document(
                        new ChannelToolEnablementV1(
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false
                        )
                    )
                )
            )
            .Replace(
                "\"customCommands\": false",
                "\"customCommands\": false,\n      \"futureTool\": true",
                StringComparison.Ordinal
            );

        var invalid = codec
            .Parse(Encoding.UTF8.GetBytes(json))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();

        invalid.Issue.Location.ShouldBe("$.sections.channelToolEnablement.futureTool");
    }

    private static ConfigurationDocumentV1 Document(
        ChannelToolEnablementV1? enablement = null,
        CustomCommandsSectionV1? commands = null
    ) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.Parse("2026-08-20T12:00:00Z", CultureInfo.InvariantCulture),
            new("source", "0.12.0"),
            new(commands, ChannelToolEnablement: enablement)
        );
}
