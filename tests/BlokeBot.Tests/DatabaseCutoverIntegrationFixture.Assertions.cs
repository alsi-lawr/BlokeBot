using System.Globalization;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Polly;
using Shouldly;

namespace BlokeBot.Tests;

internal sealed partial class DatabaseCutoverIntegrationFixture
{
    private int? _writtenHostId;
    private int _pendingDeliveryCount;

    internal async Task<long> DomainRowCountAsync(DisposablePostgreSql target, string table)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    internal async Task AssertTargetsHaveDistinctClusterIdentityAsync()
    {
        var primary = await IdentityAsync(Primary);
        var other = await IdentityAsync(Other);
        primary.Database.ShouldBe(other.Database);
        primary.DatabaseOid.ShouldBe(other.DatabaseOid);
        primary.SystemIdentifier.ShouldNotBe(other.SystemIdentifier);
        primary.IsSuperuser.ShouldBeFalse();
        primary.CanReadControlIdentity.ShouldBeTrue();
        other.IsSuperuser.ShouldBeFalse();
        other.CanReadControlIdentity.ShouldBeTrue();
    }

    internal async Task AssertTransferredStateAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile);
        await using var db = configuration.CreateDbContext();
        var host = await db.Hosts.AsNoTracking().SingleAsync(host => host.Id == SeedHostId);
        host.Login.ShouldBe("cutover_seed");
        host.StartupMessageEnabled.ShouldBe(true);
        host.AutomationGeneration.ShouldBe(17);
        host.CreatedAtUtc.ShouldBe(SeedTime);

        var flow = await db.AutomationFlows.AsNoTracking().SingleAsync(flow => flow.Id == FlowId);
        flow.IsEnabled.ShouldBeTrue();
        flow.UseVerticalLayout.ShouldBeTrue();
        flow.UseSmoothEdges.ShouldBeFalse();
        flow.CreatedAtUtc.ShouldBe(SeedTime);

        var field = await db.RequestBoardFields.AsNoTracking().SingleAsync(field => field.Id == 11);
        field.MinimumNumber.ShouldBe(12.5m);
        field.MaximumNumber.ShouldBe(98.75m);
        var submission = await db
            .RequestSubmissions.AsNoTracking()
            .SingleAsync(submission => submission.Id == MergedSubmissionId);
        submission.MergedIntoSubmissionId.ShouldBe(TargetSubmissionId);
        var candidate = await db
            .MomentCandidates.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == MergedCandidateId);
        candidate.MergedIntoCandidateId.ShouldBe(TargetCandidateId);

        var configurationRecord = await db.Set<PluginInstallationConfigurationRecord>()
            .AsNoTracking()
            .SingleAsync(record => record.PluginId == "example.cutover");
        configurationRecord.Revision.ShouldBe(42);
        var secret = await db.Set<PluginInstallationSecretRecord>()
            .AsNoTracking()
            .SingleAsync(record => record.PluginId == "example.cutover");
        secret.ProtectedValue.ShouldBe([0x00, 0x7F, 0x80, 0xFF]);
    }

    internal async Task AssertProviderMetadataWasNotCopiedAsync()
    {
        var sourceMigrations = new List<string>();
        await using (
            var source = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={SqliteDatabasePath};Pooling=False"
            )
        )
        {
            await source.OpenAsync();
            await using var command = source.CreateCommand();
            command.CommandText =
                "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sourceMigrations.Add(reader.GetString(0));
            }
        }

        await using var target = new NpgsqlConnection(Primary.ConnectionString);
        await target.OpenAsync();
        await using (var migrations = target.CreateCommand())
        {
            migrations.CommandText =
                "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";";
            var targetMigrations = new List<string>();
            await using var reader = await migrations.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                targetMigrations.Add(reader.GetString(0));
            }
            targetMigrations.ShouldBe(["20260901145930_20260901_v0_14_0_Baseline"]);
            targetMigrations.Intersect(sourceMigrations, StringComparer.Ordinal).ShouldBeEmpty();
        }
        await using var metadata = target.CreateCommand();
        metadata.CommandText = "SELECT to_regclass('public.sqlite_sequence') IS NULL;";
        ((bool)(await metadata.ExecuteScalarAsync() ?? false)).ShouldBeTrue();
    }

    internal async Task AssertPendingWorkPreservedAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile);
        await using var db = configuration.CreateDbContext();
        var messages = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(message => message.DeduplicationKey == PendingDeduplicationKey)
            .ToArrayAsync();
        _ = messages.ShouldHaveSingleItem();
        messages[0].Id.ShouldBe(PendingOutboxId);
        messages[0].Status.ShouldBe(PublicChatOutboxStatus.Pending);
        messages[0].AttemptCount.ShouldBe(0);
    }

    internal async Task<long?> MergedSubmissionTargetAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile);
        await using var db = configuration.CreateDbContext();
        return await db
            .RequestSubmissions.Where(submission => submission.Id == MergedSubmissionId)
            .Select(submission => submission.MergedIntoSubmissionId)
            .SingleAsync();
    }

    internal async Task StartPostgreSqlAndWriteAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile);
        await InitializeDatabaseAsync(configuration);
        await using var db = configuration.CreateDbContext();
        var host = new BotHost
        {
            Login = "postgresql_start_write",
            DisplayName = "PostgreSql start write",
            BotRuntimeState = BotChannelRuntimeState.Stopped,
            TimeZoneId = "UTC",
            CreatedAtUtc = SeedTime.AddDays(1),
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _writtenHostId = host.Id;
    }

    internal async Task AssertPostgreSqlWriteAndSequenceAsync()
    {
        _ = _writtenHostId.ShouldNotBeNull();
        _writtenHostId.Value.ShouldBeGreaterThan(SeedHostId);
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile);
        await using var target = configuration.CreateDbContext();
        (await target.Hosts.CountAsync()).ShouldBe(2);
        await using var source = BlokeBotDatabaseConfiguration
            .Sqlite(SqliteDatabasePath)
            .CreateDbContext();
        (await source.Hosts.CountAsync()).ShouldBe(1);
    }

    internal async Task DeliverTransferredPendingWorkOnceAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile);
        var now = new DateTimeOffset(SeedTime.AddDays(1), TimeSpan.Zero);
        var outbox = Outbox(configuration);
        var claimed = (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PublicChatClaimOutcome.Claimed>()
            .Message;
        claimed.Id.ShouldBe(PendingOutboxId);

        _ = (
            await outbox.BeginSendAsync(claimed, now, now.AddMinutes(5), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        _ = Interlocked.Increment(ref _pendingDeliveryCount);
        _ = (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                new PublicChatDeliveryOutcome.Sent("cutover-delivery"),
                now,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();

        await AssertTransferredPendingWorkDoesNotReplayAsync();
    }

    internal async Task AssertTransferredPendingWorkDoesNotReplayAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ConnectionFile);
        var now = new DateTimeOffset(SeedTime.AddDays(1).AddMinutes(1), TimeSpan.Zero);
        var afterRestart = await Outbox(configuration)
            .TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.FromDays(7),
                CancellationToken.None
            );

        afterRestart.ShouldNotBeOfType<PublicChatClaimOutcome.Claimed>();
        Volatile.Read(ref _pendingDeliveryCount).ShouldBe(1);
        await using var db = configuration.CreateDbContext();
        (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
        (
            await db.PublicChatSendReceipts.CountAsync(receipt =>
                receipt.OutboxMessageId == PendingOutboxId
            )
        ).ShouldBe(1);
    }

    private static EfPublicChatOutbox Outbox(BlokeBotDatabaseConfiguration configuration) =>
        new(
            new CutoverDbContextFactory(configuration),
            new PublicChatRetryPolicy
            {
                AttemptLimit = 3,
                Delay = TimeSpan.FromSeconds(1),
                MaximumDelay = TimeSpan.FromSeconds(5),
                DelayBackoffType = DelayBackoffType.Exponential,
            },
            new PublicChatDeliveryLifetimePolicy { MaximumAge = TimeSpan.FromSeconds(30) },
            new PublicChatTerminalRetentionPolicy { Duration = TimeSpan.FromDays(7) }
        );

    private sealed class CutoverDbContextFactory(BlokeBotDatabaseConfiguration configuration)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() => configuration.CreateDbContext();

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private static async Task<PostgreSqlIdentity> IdentityAsync(DisposablePostgreSql target)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT control.system_identifier::text, current_database(), database.oid::text, role.rolsuper, has_function_privilege(current_user, 'pg_control_system()', 'EXECUTE') FROM pg_control_system() AS control CROSS JOIN pg_database AS database CROSS JOIN pg_roles AS role WHERE database.datname = current_database() AND role.rolname = current_user;";
        await using var reader = await command.ExecuteReaderAsync();
        _ = await reader.ReadAsync();
        return new PostgreSqlIdentity(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4)
        );
    }

    private sealed record PostgreSqlIdentity(
        string SystemIdentifier,
        string Database,
        string DatabaseOid,
        bool IsSuperuser,
        bool CanReadControlIdentity
    );
}
