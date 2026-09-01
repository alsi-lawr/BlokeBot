using System.Security.Cryptography;
using BlokeBot.DatabaseCutover;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Tests;

internal sealed partial class DatabaseCutoverIntegrationFixture
{
    internal const int SeedHostId = 900;
    internal const long PendingOutboxId = 700;
    internal const string PendingDeduplicationKey =
        "ad42bceab72645c7a931673b69c39971ad42bceab72645c7a931673b69c39971";
    internal const long MergedSubmissionId = 100;
    internal const long TargetSubmissionId = 200;
    internal const long MergedCandidateId = 300;
    internal const long TargetCandidateId = 400;
    internal static readonly Guid FlowId = Guid.Parse("c0edc830-c63f-4ec9-92ad-27632794c855");
    internal static readonly DateTime SeedTime = new(2026, 8, 31, 19, 23, 41, DateTimeKind.Utc);

    private async Task InitializeAsync()
    {
        await InitializeDatabaseAsync(BlokeBotDatabaseConfiguration.Sqlite(SqliteDatabasePath));
        await SeedSqliteAsync();
        await CreateLocalStateAsync();
        await SelectTargetAsync(Primary);
        await InitializeDatabaseAsync(
            BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile)
        );
        await SelectTargetAsync(Other);
        await InitializeDatabaseAsync(
            BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile)
        );
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

    private async Task SeedSqliteAsync()
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
        var targetSubmission = new RequestSubmission
        {
            Id = TargetSubmissionId,
            HostId = SeedHostId,
            BoardId = board.Id,
            Board = board,
            OperationId = Guid.Parse("25f76bd0-e548-483d-9585-f0e4f995d322"),
            SubmitterLogin = "target",
            Title = "Target request",
            NormalizedTitle = "target request",
            Status = RequestSubmissionStatus.Approved,
            CreatedAtUtc = SeedTime,
            UpdatedAtUtc = SeedTime,
        };
        var mergedSubmission = new RequestSubmission
        {
            Id = MergedSubmissionId,
            HostId = SeedHostId,
            BoardId = board.Id,
            Board = board,
            OperationId = Guid.Parse("407ec677-8e17-478a-9a4a-cec0b5cc2b70"),
            SubmitterLogin = "merged",
            Title = "Merged request",
            NormalizedTitle = "merged request",
            Status = RequestSubmissionStatus.Merged,
            MergedIntoSubmissionId = TargetSubmissionId,
            MergedIntoSubmission = targetSubmission,
            CreatedAtUtc = SeedTime.AddMinutes(2),
            UpdatedAtUtc = SeedTime.AddMinutes(3),
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
        db.AddRange(targetSubmission, mergedSubmission, targetCandidate, mergedCandidate);
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
        _ = db.Add(
            new PublicChatOutboxMessage
            {
                Id = PendingOutboxId,
                Channel = "cutover-channel",
                Message = "pending once",
                DeduplicationKey = PendingDeduplicationKey,
                CreatedAtUtc = SeedTime,
                ExpiresAtUtc = SeedTime.AddYears(10),
                NextAttemptAtUtc = SeedTime.AddYears(10),
                Status = PublicChatOutboxStatus.Pending,
            }
        );
        _ = await db.SaveChangesAsync();
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
            SqliteDatabasePath,
            ConnectionFile,
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
        new(StateDirectory, SqliteDatabasePath, ConnectionFile, OperationId, BatchSize: 1);
}
