using System.Globalization;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

public sealed partial class SqliteBaselineRunner
{
    private async Task CreateAndSeedAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using var db = new BlokeBotDbContext(options);
        _ = await db.Database.EnsureCreatedAsync(cancellationToken);
        var flowId = DeterministicGuid(protocol.Seed, 1);
        var nodeId = DeterministicGuid(protocol.Seed, 2);
        var season = new CommunitySeason
        {
            Id = 1,
            PublicId = DeterministicGuid(protocol.Seed, 3),
            HostId = 1,
            CreationOperationId = DeterministicGuid(protocol.Seed, 4),
            Name = "Synthetic season",
            Status = CommunitySeasonStatus.Open,
            Visibility = CommunityVisibility.Public,
            StartsAtUtc = _epoch,
            EndsAtUtc = _epoch.AddYears(1),
            OpenedAtUtc = _epoch,
            Revision = 1,
            CreatedAtUtc = _epoch,
            UpdatedAtUtc = _epoch,
        };
        season.Definitions.Add(
            new CommunityDefinition
            {
                Id = 1,
                PublicId = DeterministicGuid(protocol.Seed, 5),
                HostId = 1,
                Key = "synthetic-chat",
                Name = "Synthetic chat activity",
                Kind = CommunityDefinitionKind.Quest,
                Scope = CommunityProgressScope.Viewer,
                CompletionMode = CommunityCompletionMode.Repeatable,
                EventRule = CommunityEventRuleKind.ChatMessage,
                Increment = CommunityProgressIncrement.Occurrence,
                Target = 100,
                ResetCadence = CommunityResetCadence.None,
                ScheduleRevision = 1,
                CreatedAtUtc = _epoch,
            }
        );
        _ = db.Hosts.Add(
            new BotHost
            {
                Id = 1,
                Login = "synthetic-host",
                DisplayName = "Synthetic Host",
                EnabledFeatures =
                    HostFeatureFlags.Automations
                    | HostFeatureFlags.Points
                    | HostFeatureFlags.CommunityProgression,
                CreatedAtUtc = _epoch,
            }
        );
        _ = db.AutomationFlows.Add(
            new AutomationFlow
            {
                Id = flowId,
                HostId = 1,
                Name = "Synthetic flow",
                SchemaVersion = 1,
                IsEnabled = true,
                CreatedAtUtc = _epoch,
                UpdatedAtUtc = _epoch,
                Nodes =
                [
                    new AutomationFlowNode
                    {
                        Id = nodeId,
                        DefinitionId = "synthetic.action",
                        DefinitionSchemaVersion = 1,
                        ConfigurationJson = "{}",
                        InputBindingsJson = "{}",
                        ExpressionLanguageVersion = 1,
                    },
                ],
            }
        );
        _ = db.CommunitySeasons.Add(season);
        _ = db.PluginFeatureStates.Add(
            new PluginFeatureStateRecord
            {
                PluginId = "synthetic-plugin",
                FeatureId = "synthetic-feature",
                HostId = 1,
                LifecycleOperationId = DeterministicGuid(protocol.Seed, 6),
                WorkerGeneration = 1,
                FeatureGeneration = 1,
                Readiness = PluginFeatureReadinessKind.Ready,
                Revision = 1,
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);

        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        for (var index = 0; index < protocol.Fixture.Viewers; index++)
        {
            var login = Identity(index);
            _ = await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO point_balances (HostId, Login, Amount, UpdatedAtUtc) VALUES (1, $login, $amount, $now);",
                cancellationToken,
                ("$login", login),
                ("$amount", (index % 1000).ToString(CultureInfo.InvariantCulture)),
                ("$now", _epoch)
            );
            _ = await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO community_progress (HostId, SeasonId, DefinitionId, SubjectKey, ViewerTwitchUserId, ViewerLogin, ViewerDisplayName, Amount, CompletionCount, UpdatedAtUtc) VALUES (1, 1, 1, $subject, $viewerId, $login, $display, $amount, 0, $now);",
                cancellationToken,
                ("$subject", $"viewer:{index:D8}"),
                ("$viewerId", $"synthetic-{index:D8}"),
                ("$login", login),
                ("$display", $"Synthetic {index:D8}"),
                ("$amount", index % 100),
                ("$now", _epoch)
            );
        }
        for (var index = 0; index < protocol.Fixture.PublicChatBacklog; index++)
        {
            _ = await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO public_chat_outbox (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc, NextAttemptAtUtc, Status, AttemptCount, SafePreSendFailureCount) VALUES ('synthetic-host', $message, $key, $created, $expires, $created, 'Pending', 0, 0);",
                cancellationToken,
                ("$message", $"synthetic-message-{index:D8}"),
                ("$key", new string('0', 56) + index.ToString("x8", CultureInfo.InvariantCulture)),
                ("$created", _epoch.AddMilliseconds(index)),
                ("$expires", _epoch.AddDays(1))
            );
        }
        transaction.Commit();
    }

    private static Guid DeterministicGuid(int seed, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = BitConverter.TryWriteBytes(bytes, seed);
        _ = BitConverter.TryWriteBytes(bytes[4..], index);
        _ = BitConverter.TryWriteBytes(bytes[8..], ((long)seed << 32) | (uint)index);
        return new(bytes);
    }

    private static string Identity(int index) => $"viewer-{index:D8}";
}
