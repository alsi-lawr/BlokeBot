using System.Collections.Immutable;
using System.Security.Cryptography;
using BlokeBot.Announcements;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.DatabaseCutover;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Tests;

internal sealed partial class DatabaseCutoverIntegrationFixture
{
    internal const string PriorReleaseSqliteMigration =
        "20260822192152_v0.12.0_GuessingSharedAliases";
    internal const string CurrentSqliteMigration = "20261008185949_SeparateStoredValueNamespaces";
    internal const string CurrentPostgreSqlMigration =
        "20261008190031_SeparateStoredValueNamespaces";
    internal static readonly string[] CurrentPostgreSqlMigrations =
    [
        "20260901145930_20260901_v0_14_0_Baseline",
        "20260905033659_RequestsStableIdentity",
        "20260906180403_AutomationScenarios",
        "20260907001120_AutomationTraces",
        "20260907005350_AutomationSubflows",
        "20260907093259_CurrentSubflowCallers",
        "20261001153219_FullOverlays",
        "20261001164846_FullOverlayWidgets",
        "20261002151335_FullOverlayProtectedKeys",
        "20261003213012_AutomationSourceLifecycle",
        "20261006192903_WatchTimePoints",
        "20261008174636_ScopedCustomCommandValues",
        CurrentPostgreSqlMigration,
    ];
    internal const int SeedHostId = 900;
    internal const long PendingOutboxId = 700;
    internal const string PendingDeduplicationKey =
        "ad42bceab72645c7a931673b69c39971ad42bceab72645c7a931673b69c39971";
    internal const long MergedSubmissionId = 100;
    internal const long TargetSubmissionId = 200;
    internal const long MergedCandidateId = 300;
    internal const long TargetCandidateId = 400;
    internal static readonly Guid FlowId = Guid.Parse("c0edc830-c63f-4ec9-92ad-27632794c855");

    private static readonly Guid _subflowId = Guid.Parse("c91e5d1a-f49c-4868-846d-8b9745d9e503");
    private static readonly Guid _subflowRevisionId = Guid.Parse(
        "5227d29b-41f9-4887-9722-087681d756af"
    );
    private static readonly Guid _subflowCallerId = Guid.Parse(
        "5648f98e-b4c5-45d1-b14f-748a3b5cbba5"
    );

    private const string _tracePrivateValue = "synthetic-cutover-private-value";
    private static readonly Guid _traceId = Guid.Parse("8a28148b-9cc9-4554-9576-9d4a7d4ad99c");
    private static readonly Guid _scenarioId = Guid.Parse("25c3fa68-dd1d-4f99-98c1-87b4c0f46ade");
    private static readonly Guid _scenarioSourceId = Guid.Parse(
        "4a48eebc-dab7-4e89-bf61-906b37d71760"
    );

    // Real rows carry 100 ns ticks that PostgreSQL truncates to microseconds.
    internal static readonly DateTime SeedTime = new DateTime(
        2026,
        8,
        31,
        19,
        23,
        41,
        DateTimeKind.Utc
    ).AddTicks(1234567);

    private static readonly AutomationScenarioFixture _scenarioFixture = new(
        new(_scenarioSourceId),
        AutomationDefinitionIds.CustomCommandSource,
        new(1),
        new(
            new(Guid.Empty, AutomationDefinitionIds.CustomCommandSource),
            new("scenario-viewer", "scenario_viewer", "Scenario Viewer"),
            new(new(SeedHostId), "seed-user", "cutover_seed", "Cutover Seed"),
            null,
            new(new(SeedTime), new(SeedTime.AddSeconds(1))),
            [new(0, "cutover rehearsal")],
            new([])
        ),
        new(SeedTime),
        17,
        [],
        [],
        []
    );

    private static readonly AutomationTraceEventData[] _traceEvents =
    [
        AutomationTraceRedaction.Event(
            _traceId,
            AutomationTraceEventKind.Outputs,
            SeedTime,
            new(
                _scenarioSourceId,
                AutomationDefinitionIds.CustomCommandSource.Value,
                1,
                """{"custom-command-id":7}""",
                "{}",
                AutomationExpressionLanguage.CurrentVersion.Value,
                false
            ),
            values: new Dictionary<AutomationPortId, AutomationResolvedValue>
            {
                [new("arguments")] = new(
                    new AutomationValue.Arguments([
                        new(0, _tracePrivateValue, [AutomationValueProvenance.PublicChat]),
                    ]),
                    [AutomationValueProvenance.PublicChat]
                ),
            }
        ),
        AutomationTraceRedaction.Event(
            _traceId,
            AutomationTraceEventKind.Terminal,
            SeedTime.AddSeconds(1),
            outcome: AutomationTraceOutcome.Succeeded
        ),
    ];

    private const long _fullOverlayId = 600;
    private static readonly Guid _fullOverlayPublicId = Guid.Parse(
        "b99e5307-79c3-4577-bc36-9868d00df37a"
    );
    private static readonly FullOverlayDocument _publishedOverlayDocument = new(
        Guid.Parse("9ff2a062-01b1-414c-9ee3-ff0712652928"),
        "<main>Published cutover overlay</main>",
        "main { color: #abcdef; }",
        [],
        []
    );
    private static readonly FullOverlayDocument _draftOverlayDocument =
        _publishedOverlayDocument with
        {
            Html = "<main>Unpublished draft edits</main>",
        };
    private static readonly byte[] _overlayAccessKeyDigest = SHA256.HashData(
        "synthetic-cutover-access-key"u8
    );
    private const string _protectedOverlayAccessKey = "synthetic-cutover-protected-access-key";
    private const string _storedValueName = "profile";
    private const string _storedValueViewerId = "cutover-viewer";
    private const string _dictionaryEntryKey = "game";
    private const long _storedNumber = 9_007_199_254_740_993;
    private const string _storedText = "Celeste — preserved";
    private const string _computedInvocationId = "cutover-computed-invocation";
    private const string _computedReply = "9007199254740993/Celeste — preserved";
    private const string _storedValueTemplate =
        "{var_get|user|profile}/{dict_get|user|profile|game}";
    private static readonly Guid _scalarRevision = Guid.Parse(
        "bc829d09-5d6f-45bd-8902-67a0a52b4890"
    );
    private static readonly Guid _dictionaryRevision = Guid.Parse(
        "099afc78-d603-48fc-90d8-2f7458c756d7"
    );

    private static readonly AutomationSubflowRevision _subflowRevision = CreateSubflowRevision();

    internal string ReceiptPath => new CutoverReceiptStore(StateDirectory).Path;

    private async Task InitializeAsync()
    {
        await using (
            var db = BlokeBotDatabaseConfiguration.Sqlite(SqliteDatabasePath).CreateDbContext()
        )
        {
            await db.Database.MigrateAsync(PriorReleaseSqliteMigration);
        }

        await SeedPriorReleaseRowsAsync();
        await CreateLocalStateAsync();
        await SelectTargetAsync(Primary);
    }

    private static async Task InitializeDatabaseAsync(BlokeBotDatabaseConfiguration configuration)
    {
        var services = new ServiceCollection();
        _ = services.AddBlokeBotPersistence(configuration);
        await using var provider = services.BuildServiceProvider();
        await provider
            .GetRequiredService<BlokeBotDatabaseInitializer>()
            .InitializeAsync(CancellationToken.None);
    }

    private async Task SeedPriorReleaseRowsAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.Sqlite(SqliteDatabasePath);
        await using var db = configuration.CreateDbContext();
        var host = new BotHost
        {
            Id = SeedHostId,
            TwitchUserId = "seed-user",
            Login = "cutover_seed",
            DisplayName = "Cutover Seed",
            BotRuntimeState = BotChannelRuntimeState.Stopped,
            EnabledFeatures = HostFeatureFlags.Automations,
            AutomationGeneration = 17,
            TimeZoneId = "Europe/London",
            StartupMessageEnabled = true,
            CommandsAliasesConfigured = true,
            CreatedAtUtc = SeedTime,
        };
        var board = new RequestBoard
        {
            Id = 10,
            HostId = SeedHostId,
            Slug = "cutover",
            Title = "Cutover board",
            Description = "Preserved request data",
            IsOpen = true,
            PointCost = "12.5",
            RefundPolicy = RequestBoardRefundPolicy.RejectedOrWithdrawn,
            CreatedAtUtc = SeedTime,
            UpdatedAtUtc = SeedTime.AddMinutes(1),
        };
        var targetCandidate = Candidate(
            TargetCandidateId,
            Guid.Parse("c01bd29d-420c-492c-a9de-e58a989a13c6"),
            MomentCandidateState.Approved,
            SeedTime.AddMinutes(4)
        );
        var mergedCandidate = Candidate(
            MergedCandidateId,
            Guid.Parse("d53352f7-e4cb-4624-87b0-22f8f4027269"),
            MomentCandidateState.Merged,
            SeedTime.AddMinutes(5)
        );
        mergedCandidate.MergedIntoCandidateId = TargetCandidateId;
        mergedCandidate.MergedIntoCandidate = targetCandidate;

        _ = db.Add(host);
        _ = db.Add(
            new RequestBoardField
            {
                Id = 11,
                BoardId = board.Id,
                Board = board,
                Position = 1,
                Key = "rating",
                Label = "Rating",
                Kind = RequestBoardFieldKind.Number,
                IsRequired = true,
                MinimumNumber = 12.5m,
                MaximumNumber = 98.75m,
            }
        );
        db.AddRange(targetCandidate, mergedCandidate);
        // Both announcement rows leave AnnouncementColor at its database default.
        _ = db.Add(
            new AutomaticRaidShoutoutSettings { HostId = SeedHostId, UpdatedAtUtc = SeedTime }
        );
        _ = db.Add(
            new CustomAnnouncement
            {
                HostId = SeedHostId,
                Name = "Cutover announcement",
                MessageLibraryEntry = new CustomMessageLibraryEntry
                {
                    HostId = SeedHostId,
                    Name = "Cutover library entry",
                    Variants = [new CustomMessageVariant { SortOrder = 0, Text = "Announced" }],
                    CreatedAtUtc = SeedTime,
                    UpdatedAtUtc = SeedTime,
                },
                DeliveryPolicy = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
                {
                    HostId = SeedHostId,
                    RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(30)),
                    OccurrenceLifetime = new AnnouncementOccurrenceLifetime(
                        TimeSpan.FromSeconds(45)
                    ),
                },
                Schedule = new IntervalCustomAnnouncementSchedule { HostId = SeedHostId },
                CreatedAtUtc = SeedTime,
                UpdatedAtUtc = SeedTime,
            }
        );
        _ = db.Add(
            new PublicChatOutboxMessage
            {
                Id = PendingOutboxId,
                Channel = "cutover-channel",
                Message = "pending once",
                DeduplicationKey = PendingDeduplicationKey,
                CreatedAtUtc = SeedTime,
                ExpiresAtUtc = SeedTime.AddYears(10),
                NextAttemptAtUtc = SeedTime,
                Status = PublicChatOutboxStatus.Pending,
            }
        );
        _ = await db.SaveChangesAsync();
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO request_submissions (
                "Id", "HostId", "BoardId", "OperationId", "SubmitterLogin", "Title", "NormalizedTitle", "Status",
                "Category", "Tags", "Priority", "QueuePosition", "VoteCount", "PublicNote", "PrivateModeratorNote",
                "PrivateRejectionReason", "PointReservationState", "CreatedAtUtc", "UpdatedAtUtc", "MergedIntoSubmissionId"
            ) VALUES (
                {TargetSubmissionId}, {SeedHostId}, {board.Id}, {Guid.Parse(
                "25f76bd0-e548-483d-9585-f0e4f995d322"
            )},
                'target', 'Target request', 'target request', 'Approved', '', '', 0, 0, 0, '', '', '', 'None',
                {SeedTime}, {SeedTime}, NULL
            ), (
                {MergedSubmissionId}, {SeedHostId}, {board.Id}, {Guid.Parse(
                "407ec677-8e17-478a-9a4a-cec0b5cc2b70"
            )},
                'merged', 'Merged request', 'merged request', 'Merged', '', '', 0, 0, 0, '', '', '', 'None',
                {SeedTime.AddMinutes(2)}, {SeedTime.AddMinutes(3)}, {TargetSubmissionId}
            )
            """
        );
    }

    internal async Task SeedCurrentReleaseRowsAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.Sqlite(SqliteDatabasePath);
        await using var db = configuration.CreateDbContext();
        _ = db.Add(
            new AutomationFlow
            {
                Id = FlowId,
                HostId = SeedHostId,
                Name = "Cutover flow",
                SchemaVersion = 3,
                IsEnabled = true,
                UseVerticalLayout = true,
                UseSmoothEdges = false,
                CreatedAtUtc = SeedTime,
                UpdatedAtUtc = SeedTime.AddSeconds(1),
                Nodes =
                [
                    new()
                    {
                        Id = _scenarioSourceId,
                        DefinitionId = AutomationDefinitionIds.CustomCommandSource.Value,
                        DefinitionSchemaVersion = 1,
                        ConfigurationJson = """{"custom-command-id":7}""",
                        InputBindingsJson = "{}",
                        ExpressionLanguageVersion = AutomationExpressionLanguage
                            .CurrentVersion
                            .Value,
                    },
                ],
            }
        );
        _ = db.Add(
            new AutomationScenario
            {
                Id = _scenarioId,
                FlowId = FlowId,
                Slot = 0,
                Name = "Cutover rehearsal",
                FixtureJson = AutomationScenarioSerialization.Serialize(_scenarioFixture),
            }
        );
        _ = db.Add(
            new PluginInstallationConfigurationRecord
            {
                PluginId = "example.cutover",
                ValuesJson = "[{\"settingId\":\"mode\",\"value\":\"safe\"}]",
                Revision = 42,
            }
        );
        _ = db.Add(
            new PluginInstallationSecretRecord
            {
                PluginId = "example.cutover",
                SettingId = "token",
                ProtectedValue = [0x00, 0x7F, 0x80, 0xFF],
            }
        );
        _ = db.AutomationSubflows.Add(
            new()
            {
                HostId = SeedHostId,
                Id = _subflowId,
                LastRevision = _subflowRevision.Revision,
            }
        );
        _ = db.AutomationSubflowRevisions.Add(
            new()
            {
                HostId = SeedHostId,
                Id = _subflowRevisionId,
                SubflowId = _subflowId,
                Revision = _subflowRevision.Revision,
                SnapshotJson = AutomationSubflowSerialization.Serialize(_subflowRevision),
            }
        );
        _ = db.AutomationFlowNodes.Add(
            AutomationFlowService.Persist(
                FlowId,
                new AutomationFlowDraftNode(
                    new(_subflowCallerId),
                    AutomationSubflowDefinitions.Invocation(_subflowRevision),
                    AutomationExpressionLanguage.CurrentVersion,
                    AutomationNodeFailurePolicy.Stop,
                    ImmutableDictionary<
                        AutomationConfigurationFieldId,
                        AutomationInputBinding
                    >.Empty
                )
            )
        );
        _ = db.AutomationSubflowCallers.Add(
            new()
            {
                NodeId = _subflowCallerId,
                HostId = SeedHostId,
                SubflowId = _subflowId,
            }
        );
        SeedCurrentOverlayAndStoredValueRows(db);
        _ = await db.SaveChangesAsync();
        await AutomationTraceStore.CreateAsync(
            db,
            _traceId,
            SeedHostId,
            FlowId,
            null,
            SeedTime,
            CancellationToken.None
        );
        foreach (var data in _traceEvents)
        {
            await AutomationTraceStore.AppendAsync(
                db,
                _traceId,
                data,
                data.TimeUtc.UtcDateTime,
                CancellationToken.None
            );
        }
    }

    private static void SeedCurrentOverlayAndStoredValueRows(BlokeBotDbContext db)
    {
        _ = db.FullOverlays.Add(
            new()
            {
                Id = _fullOverlayId,
                PublicId = _fullOverlayPublicId,
                HostId = SeedHostId,
                Name = "Cutover full overlay",
                DraftDocumentJson = FullOverlayDocuments.Serialize(_draftOverlayDocument),
                AccessKeyDigest = _overlayAccessKeyDigest,
                ProtectedAccessKey = _protectedOverlayAccessKey,
                Revision = 9,
                PublicationSequence = 3,
                PublishedVersion = 3,
                CreatedAtUtc = SeedTime,
                UpdatedAtUtc = SeedTime.AddMinutes(1),
            }
        );
        _ = db.FullOverlayPublications.Add(
            new()
            {
                OverlayId = _fullOverlayId,
                Version = 3,
                DocumentJson = FullOverlayDocuments.Serialize(_publishedOverlayDocument),
                AuthorUserId = "seed-user",
                AuthorLogin = "cutover_seed",
                PublishedAtUtc = SeedTime,
            }
        );
        db.CustomValueDefinitions.AddRange(
            new CustomValueDefinition
            {
                Id = 710,
                HostId = SeedHostId,
                Name = _storedValueName,
                NameHash = CustomValueIdentity.Hash(_storedValueName),
                Scope = CustomValueScope.User,
                Kind = CustomValueKind.Number,
                DefaultNumber = -7,
                Revision = _scalarRevision,
            },
            new CustomValueDefinition
            {
                Id = 711,
                HostId = SeedHostId,
                Name = _storedValueName,
                NameHash = CustomValueIdentity.Hash(_storedValueName),
                Scope = CustomValueScope.User,
                Kind = CustomValueKind.Dictionary,
                DefaultText = "unknown game",
                Revision = _dictionaryRevision,
            }
        );
        db.CustomStoredValues.AddRange(
            new CustomStoredValue
            {
                Id = 720,
                HostId = SeedHostId,
                DefinitionId = 710,
                ViewerId = _storedValueViewerId,
                TargetHash = CustomValueIdentity.Target(_storedValueViewerId, ""),
                Kind = CustomValueKind.Number,
                Number = _storedNumber,
                Revision = _scalarRevision,
            },
            new CustomStoredValue
            {
                Id = 721,
                HostId = SeedHostId,
                DefinitionId = 711,
                ViewerId = _storedValueViewerId,
                EntryKey = _dictionaryEntryKey,
                TargetHash = CustomValueIdentity.Target(_storedValueViewerId, _dictionaryEntryKey),
                Kind = CustomValueKind.Text,
                Text = _storedText,
                Revision = _dictionaryRevision,
            }
        );
        _ = db.CustomCommands.Add(
            new()
            {
                Id = 7,
                HostId = SeedHostId,
                Name = "cutover_profile",
                CreatedAtUtc = SeedTime,
                UpdatedAtUtc = SeedTime,
                Action = new MessageCustomCommandAction
                {
                    HostId = SeedHostId,
                    ZeroArgumentMessageLibraryEntry = new()
                    {
                        HostId = SeedHostId,
                        Name = "Cutover stored-value reply",
                        CreatedAtUtc = SeedTime,
                        UpdatedAtUtc = SeedTime,
                        Variants = [new() { Text = _storedValueTemplate }],
                    },
                },
            }
        );
        _ = db.CustomCommandComputedResults.Add(
            new()
            {
                Id = 730,
                HostId = SeedHostId,
                CommandId = 7,
                InvocationId = _computedInvocationId,
                InvocationHash = CustomValueIdentity.Hash(_computedInvocationId),
                ViewerId = _storedValueViewerId,
                Reply = _computedReply,
                ReplyEligible = false,
            }
        );
    }

    private static AutomationSubflowRevision CreateSubflowRevision()
    {
        var contract = new AutomationSubflowInterface([], []);
        var boundaries = new[]
        {
            (
                Guid.Parse("934f29e7-bf36-4e14-850a-159c089c0434"),
                AutomationSubflowDefinitions.Entry
            ),
            (Guid.Parse("9cb9a40d-b0fc-4765-89d0-d201434a1b35"), AutomationSubflowDefinitions.Exit),
        };
        var nodes = boundaries
            .Select(boundary => new AutomationFlowDraftNode(
                new(boundary.Item1),
                AutomationSubflowDefinitions.Boundary(boundary.Item2, contract),
                AutomationExpressionLanguage.CurrentVersion,
                AutomationNodeFailurePolicy.Stop,
                ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
            ))
            .ToImmutableArray();
        var descriptors = AutomationSubflowDefinitions.Definitions.ToDictionary(
            definition => definition.Descriptor.Id.Value,
            definition => definition.Descriptor
        );
        return new(
            new(_subflowRevisionId),
            new(_subflowId),
            1,
            "Cutover reusable graph",
            contract,
            new(
                null,
                new(SeedHostId),
                "Cutover subflow",
                AutomationFlowSchema.CurrentVersion,
                false,
                nodes,
                [
                    new(
                        Guid.Parse("86b6f7a7-7423-4f6a-aa03-0a7209d1d4f9"),
                        AutomationEdgeKind.Flow,
                        nodes[0].Id,
                        new("complete"),
                        nodes[1].Id,
                        new("flow")
                    ),
                ]
            ),
            [
                .. nodes.Select(node =>
                {
                    var descriptor = descriptors[node.Definition.TypeId];
                    return new AutomationSubflowNodeContract(
                        node.Id,
                        descriptor.Kind,
                        descriptor.Display,
                        descriptor.Inputs,
                        descriptor.Outputs,
                        descriptor.Capabilities,
                        descriptor.RetrySafety
                    );
                }),
            ],
            HostFeatureFlags.Automations,
            [],
            new(SeedTime)
        );
    }

    private static MomentCandidate Candidate(
        long id,
        Guid publicId,
        MomentCandidateState state,
        DateTime capturedAt
    ) =>
        new()
        {
            Id = id,
            PublicId = publicId,
            HostId = SeedHostId,
            StreamIdentity = "stream-cutover",
            State = state,
            PublicTitle = $"Moment {id}",
            PublicCategory = "Test",
            CapturedAtUtc = capturedAt,
            LastCapturedAtUtc = capturedAt,
        };

    private async Task CreateLocalStateAsync()
    {
        var files = new Dictionary<string, string>
        {
            [Path.Combine(StateDirectory, "overlays", "scene.json")] = "{\"scene\":\"cutover\"}",
            [Path.Combine(StateDirectory, "plugins", "example.cutover", "plugin.toml")] =
                "id = \"example.cutover\"\n",
            [Path.Combine(StateDirectory, "data-protection-keys", "key.xml")] =
                "<key id=\"cutover\" />",
            [Path.Combine(StateDirectory, "tokens.json")] = "{\"tokens\":[]}",
        };
        foreach (var (path, content) in files)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }

        var pluginDatabase = Path.Combine(
            StateDirectory,
            "plugins",
            "example.cutover",
            "private.db"
        );
        await using var connection = new SqliteConnection($"Data Source={pluginDatabase}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE plugin_state (value TEXT NOT NULL); INSERT INTO plugin_state VALUES ('preserve-me');";
        _ = await command.ExecuteNonQueryAsync();
    }

    private IReadOnlyDictionary<string, string> CaptureLocalStateHashes()
    {
        var protectedPaths = new[]
        {
            AdministratorConnectionFile,
            ApplicationConnectionFile,
            Path.Combine(StateDirectory, "overlays", "scene.json"),
            Path.Combine(StateDirectory, "plugins", "example.cutover", "plugin.toml"),
            Path.Combine(StateDirectory, "plugins", "example.cutover", "private.db"),
            Path.Combine(StateDirectory, "data-protection-keys", "key.xml"),
            Path.Combine(StateDirectory, "tokens.json"),
        };
        return protectedPaths.ToDictionary(
            path => path,
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.Ordinal
        );
    }

    internal void AssertLocalStateUnchanged()
    {
        foreach (var (path, expected) in _localStateHashes)
        {
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ShouldBe(expected);
        }
    }

    internal DatabaseCutoverOptions Options() =>
        new(
            StateDirectory,
            SqliteDatabasePath,
            AdministratorConnectionFile,
            ApplicationConnectionFile,
            OperationId,
            BatchSize: 1
        );
}
