using System.Globalization;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferContractTests
{
    [Test]
    public async Task AutomationBindings_InvalidBoundsDuplicatesAndExpressionShapesArePreviewIssues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "binding-validation",
                DisplayName = "Binding validation",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var cases = new[]
        {
            (
                Bindings:
                [
                    new("message", AutomationInputBindingMode.Fixed),
                    new("message", AutomationInputBindingMode.Fixed),
                ],
                Message: "duplicated"
            ),
            (
                Bindings:
                [
                    new("message", AutomationInputBindingMode.Expression, null, "actor.login"),
                ],
                Message: "require one expression and language version"
            ),
            (
                Bindings: [new("message", AutomationInputBindingMode.Fixed, 1, "actor.login")],
                Message: "must omit both"
            ),
            (
                Bindings: Enumerable
                    .Range(0, ConfigurationDocumentCodec.MaximumRecordsPerCollection + 1)
                    .Select(index => new AutomationInputBindingV1(
                        $"field-{index}",
                        AutomationInputBindingMode.Fixed
                    ))
                    .ToArray(),
                Message: "record limit"
            ),
        };

        foreach (var (bindings, message) in cases)
        {
            var outcome = await new ConfigurationImportPreviewService(database).PreviewAsync(
                AutomationDocument(AutomationNode(bindings)),
                new(
                    hostId,
                    [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
                    new HashSet<HostFeatureFlags>()
                ),
                CancellationToken.None
            );

            outcome
                .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
                .Preview.Sections.Single()
                .Issues.ShouldHaveSingleItem()
                .Message.ShouldContain(message, Case.Insensitive);
        }
    }

    [Test]
    public async Task AutomationReferencePayload_WrongJsonTypeIsDeterministicNonBlockingPreview()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "reference-validation",
                DisplayName = "Reference validation",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var malformedReferences = new[]
        {
            (
                AutomationDefinitionIds.CustomCommandSource.Value,
                JsonSerializer.SerializeToElement(
                    new Dictionary<string, int> { ["custom-command-id"] = 42 }
                )
            ),
            (
                AutomationDefinitionIds.PlayOverlayCueAction.Value,
                JsonSerializer.SerializeToElement(
                    new Dictionary<string, object> { ["target-id"] = 42, ["cue-id"] = "cue" }
                )
            ),
            (
                AutomationDefinitionIds.RewardRedemptionSource.Value,
                JsonSerializer.SerializeToElement(
                    new Dictionary<string, object>
                    {
                        ["reward-id"] = new { unexpected = true },
                        ["completion-policy"] = "none",
                    }
                )
            ),
            (
                AutomationDefinitionIds.CustomCommandSource.Value,
                JsonSerializer.SerializeToElement(
                    new Dictionary<string, string?> { ["custom-command-id"] = null }
                )
            ),
        };
        var automation = ConfigurationTransferAutomationTestServices.Create(database);
        var adapter = new AutomationConfigurationTransferAdapter(
            automation.Flows,
            automation.Catalog,
            TimeProvider.System
        );

        foreach (var (definitionId, configuration) in malformedReferences)
        {
            var outcome = await new ConfigurationImportPreviewService(
                database,
                UnavailableOverlayConfigurationTransferAdapter.Instance,
                adapter
            ).PreviewAsync(
                AutomationDocument(AutomationNode([], definitionId, configuration)),
                new(
                    hostId,
                    [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
                    new HashSet<HostFeatureFlags>()
                ),
                CancellationToken.None
            );

            var issues = outcome
                .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
                .Preview.Sections.Single()
                .Issues;
            issues.ShouldNotBeEmpty();
            issues.ShouldAllBe(issue => !issue.BlocksApply);
        }
    }

    [Test]
    public async Task OverlayExport_IndependentlySelectsExactUrlsAndDocumentLinksAndReportsEmptyCues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "source-id",
                Login = "source",
                DisplayName = "Source",
                EnabledFeatures = HostFeatureFlags.Overlays,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            var document = new OverlayMediaDocument
            {
                Id = Guid.NewGuid(),
                ContentType = "video/mp4",
                ByteLength = 12,
                StorageKey = new string('a', 32),
                State = OverlayMediaDocumentState.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            var media = new OverlayMediaAsset
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                Name = "Clip",
                ContentRevision = 1,
                Document = document,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _ = db.OverlayMediaDocuments.Add(document);
            _ = db.OverlayMediaAssets.Add(media);
            _ = await db.SaveChangesAsync();
            _ = db.OverlayInstances.Add(
                new()
                {
                    PublicId = Guid.NewGuid(),
                    HostId = hostId,
                    Name = "Cue player",
                    Type = OverlayType.CuePlayer,
                    IsEnabled = true,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    AccessKeyDigest = Enumerable.Repeat((byte)1, 32).ToArray(),
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            var configuration = """
                {"schemaVersion":1,"layers":[
                  {"type":"uploadedMedia","assetId":"__MEDIA_ID__","mediaKind":"video","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":0,"volume":1,"fit":"contain","rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}},
                  {"type":"remoteMedia","url":"https://example.test/video.mp4?token=secret%2Bvalue","mediaKind":"video","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":1,"volume":1,"fit":"contain","rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}
                ]}
                """.Replace("__MEDIA_ID__", media.PublicId.ToString("D"), StringComparison.Ordinal);
            _ = db.OverlayCues.Add(
                new()
                {
                    PublicId = Guid.NewGuid(),
                    HostId = hostId,
                    Name = "Mixed cue",
                    IsEnabled = true,
                    DurationMilliseconds = 1000,
                    QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                    ConfigurationJson = configuration,
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var automation = ConfigurationTransferAutomationTestServices.Create(database);
        var exporter = new ConfigurationDocumentExporter(
            database,
            new(),
            automation.Catalog,
            automation.Flows,
            NullLogger<ConfigurationDocumentExporter>.Instance,
            TimeProvider.System
        );

        var both = await ExportOverlaysAsync(exporter, hostId, urls: true, media: true);
        both.Version.ShouldBe(1);
        both.Sections.Overlays!.Cues.ShouldHaveSingleItem().Layers.Count.ShouldBe(2);
        _ = both.Sections.Overlays.MediaReferences.ShouldHaveSingleItem();
        both.Sections.Overlays.Cues[0]
            .Layers[1]
            .Url.ShouldBe("https://example.test/video.mp4?token=secret%2Bvalue");

        var urls = await ExportOverlaysAsync(exporter, hostId, urls: true, media: false);
        urls.Sections.Overlays!.MediaReferences.ShouldBeEmpty();
        urls.Sections.Overlays.Cues.ShouldHaveSingleItem()
            .Layers.ShouldHaveSingleItem()
            .Type.ShouldBe(OverlayCueLayerTypeV1.RemoteMedia);

        var mediaOnly = await ExportOverlaysAsync(exporter, hostId, urls: false, media: true);
        mediaOnly
            .Sections.Overlays!.Cues.ShouldHaveSingleItem()
            .Layers.ShouldHaveSingleItem()
            .Type.ShouldBe(OverlayCueLayerTypeV1.UploadedMedia);

        var neither = await ExportOverlaysAsync(exporter, hostId, urls: false, media: false);
        neither.Sections.Overlays!.Cues.ShouldBeEmpty();
        neither.Sections.Overlays.OmittedCueNames.ShouldBe(["Mixed cue"]);

        _ = (
            await exporter.ExportAsync(
                hostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Overlays },
                    new(true, false, false)
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationExportOutcome.Unsupported>();
    }

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
    }

    [Test]
    public void TypedCodec_MissingRequiredNestedValues_ReturnsStructuralFailure()
    {
        const string Json = """
            {
              "format": "blokebot.channel-configuration",
              "version": 1,
              "exportedAtUtc": "2026-08-20T12:00:00Z",
              "source": { "channelLogin": "source" },
              "sections": {
                "customCommands": {
                  "timeZoneId": "UTC",
                  "replies": [],
                  "counters": [],
                  "commands": [{
                    "id": "command-1",
                    "name": "hello",
                    "aliases": [],
                    "allowEveryone": true,
                    "allowModerators": false,
                    "cooldownSeconds": 0,
                    "cooldownScope": "user",
                    "invocationLimit": "unlimited",
                    "action": { "type": "message" }
                  }]
                }
              }
            }
            """;

        var invalid = new ConfigurationDocumentCodec()
            .Parse(Json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();

        invalid.Issue.Location.ShouldContain("customCommands.commands");
        invalid.Issue.Message.ShouldContain("Enabled");
    }

    [Test]
    public void PastedJson_ExceedingUtf8Limit_IsRejectedBeforeByteParsing()
    {
        var pasted = string.Concat(
            Enumerable.Repeat("😀", ConfigurationDocumentCodec.MaximumBytes / 3)
        );
        pasted.Length.ShouldBeLessThan(ConfigurationDocumentCodec.MaximumBytes);

        var invalid = new ConfigurationDocumentCodec()
            .Parse(pasted)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();

        invalid.Issue.Message.ShouldContain("2 MB limit");
    }

    [Test]
    public void TypedCodec_MissingRequiredObjectCollectionOrScheduleValue_IsRejected()
    {
        var malformed = new[]
        {
            (
                Json: """
                {
                  "format": "blokebot.channel-configuration",
                  "version": 1,
                  "exportedAtUtc": "2026-08-20T12:00:00Z",
                  "sections": {}
                }
                """,
                Expected: "Source"
            ),
            (
                Json: """
                {
                  "format": "blokebot.channel-configuration",
                  "version": 1,
                  "exportedAtUtc": "2026-08-20T12:00:00Z",
                  "source": { "channelLogin": "source" },
                  "sections": {
                    "customCommands": { "timeZoneId": "UTC", "replies": [], "counters": [] }
                  }
                }
                """,
                Expected: "Commands"
            ),
            (
                Json: """
                {
                  "format": "blokebot.channel-configuration",
                  "version": 1,
                  "exportedAtUtc": "2026-08-20T12:00:00Z",
                  "source": { "channelLogin": "source" },
                  "sections": {
                    "announcements": {
                      "replies": [{
                        "id": "reply", "name": "reply", "selectionMode": "sequential",
                        "variants": ["hello"]
                      }],
                      "items": [{
                        "id": "weekly", "name": "weekly", "enabled": true,
                        "messageReplyId": "reply", "deliveryType": "chatMessage",
                        "announcementColor": "primary", "retryDelaySeconds": 2,
                        "occurrenceLifetimeSeconds": 30,
                        "schedule": { "type": "weekly", "time": "12:00:00" }
                      }]
                    }
                  }
                }
                """,
                Expected: "UTC weekday"
            ),
        };

        foreach (var (json, expected) in malformed)
        {
            var invalid = new ConfigurationDocumentCodec()
                .Parse(json)
                .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
            invalid.Issue.Message.ShouldContain(expected);
        }
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

    private static ConfigurationDocumentV1 AutomationDocument(AutomationNodeV1 node) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.Parse("2026-08-20T12:00:00Z", CultureInfo.InvariantCulture),
            new("source", "0.12.0"),
            new(
                Automations: new(
                    [
                        new(
                            "flow-1",
                            "Flow",
                            false,
                            AutomationFlowSchema.CurrentVersion,
                            AutomationFlowOrientation.Horizontal,
                            AutomationEdgeStyle.Angular,
                            [node],
                            []
                        ),
                    ],
                    []
                )
            )
        );

    private static AutomationNodeV1 AutomationNode(
        IReadOnlyList<AutomationInputBindingV1> bindings,
        string definitionId = "send-chat",
        JsonElement configuration = default
    ) =>
        new(
            "node-1",
            definitionId,
            1,
            configuration.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(
                    new Dictionary<string, string> { ["message"] = "Hello" }
                )
                : configuration,
            1,
            AutomationNodeFailurePolicy.Stop,
            bindings,
            0,
            0
        );

    private static async Task<ConfigurationDocumentV1> ExportOverlaysAsync(
        ConfigurationDocumentExporter exporter,
        int hostId,
        bool urls,
        bool media
    ) =>
        (
            await exporter.ExportAsync(
                hostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Overlays },
                    new(urls, media, urls)
                ),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<ConfigurationExportOutcome.Success>()
            .Document;
}
