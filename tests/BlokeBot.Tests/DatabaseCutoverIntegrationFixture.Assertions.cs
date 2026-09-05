using System.Globalization;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.DatabaseCutover;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Polly;
using Shouldly;

namespace BlokeBot.Tests;

internal sealed partial class DatabaseCutoverIntegrationFixture
{
    private int? _writtenHostId;
    private int _pendingDeliveryCount;

    internal async Task<CutoverReceipt?> ReadReceiptAsync() =>
        await new CutoverReceiptStore(StateDirectory).ReadAsync(CancellationToken.None);

    internal async Task AssertReceiptRedactedAsync() =>
        (await File.ReadAllTextAsync(ReceiptPath)).ShouldNotContain(_password);

    internal async Task<IReadOnlyList<string>> SqliteMigrationsAsync()
    {
        await using var source = new SqliteConnection(
            $"Data Source={SqliteDatabasePath};Pooling=False"
        );
        await source.OpenAsync();
        await using var command = source.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
        return await ReadStringsAsync(command);
    }

    internal async Task<IReadOnlyList<string>> TargetMigrationsAsync(DisposablePostgreSql target)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";";
        return await ReadStringsAsync(command);
    }

    internal async Task<IReadOnlyList<string>> TargetTablesAsync(DisposablePostgreSql target)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY tablename;";
        return await ReadStringsAsync(command);
    }

    internal async Task<TargetDatabaseState?> TargetDatabaseStateAsync(
        DisposablePostgreSql target,
        string database = _database
    )
    {
        await using var administrator = new NpgsqlConnection(target.AdminConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        command.CommandText =
            "SELECT role.rolname, shobj_description(database.oid, 'pg_database') FROM pg_database AS database JOIN pg_roles AS role ON role.oid = database.datdba WHERE database.datname = @database;";
        _ = command.Parameters.AddWithValue("database", database);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new TargetDatabaseState(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)
            )
            : null;
    }

    internal string ExpectedMarker => $"blokebot-cutover:{OperationId:D}";

    internal Task CreateDatabaseByHandAsync(DisposablePostgreSql target) =>
        ExecuteAsAdministratorAsync(target, $"CREATE DATABASE {_database} OWNER {_role};");

    internal Task DropDatabaseByHandAsync(DisposablePostgreSql target) =>
        ExecuteAsAdministratorAsync(target, $"DROP DATABASE {_database};");

    internal Task ChangeOwnerByHandAsync(DisposablePostgreSql target, bool restore) =>
        ExecuteAsAdministratorAsync(
            target,
            $"ALTER DATABASE {_database} OWNER TO {(restore ? _role : _otherRole)};"
        );

    internal Task CreateStrayTableAsync(DisposablePostgreSql target) =>
        ExecuteInTargetAsync(target, "CREATE TABLE hosts (id integer);");

    internal Task DropStrayTableAsync(DisposablePostgreSql target) =>
        ExecuteInTargetAsync(target, "DROP TABLE hosts;");

    private static async Task ExecuteAsAdministratorAsync(DisposablePostgreSql target, string sql)
    {
        await using var administrator = new NpgsqlConnection(target.AdminConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteInTargetAsync(DisposablePostgreSql target, string sql)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync();
    }

    internal BlokeBotDatabaseConfiguration SourceConfiguration =>
        BlokeBotDatabaseConfiguration.Sqlite(SqliteDatabasePath);

    internal BlokeBotDatabaseConfiguration TargetConfiguration =>
        BlokeBotDatabaseConfiguration.PostgreSqlFromFile(ApplicationConnectionFile);

    internal static async Task<int> InsertHostAsync(BlokeBotDatabaseConfiguration configuration)
    {
        await using var db = CutoverDbContextFactory.CreateDbContext(configuration);
        var host = new BotHost
        {
            Login = "extra_host",
            DisplayName = "Extra host",
            BotRuntimeState = BotChannelRuntimeState.Stopped,
            TimeZoneId = "UTC",
            CreatedAtUtc = SeedTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    internal static async Task DeleteHostAsync(BlokeBotDatabaseConfiguration configuration, int id)
    {
        await using var db = CutoverDbContextFactory.CreateDbContext(configuration);
        _ = await db.Hosts.Where(host => host.Id == id).ExecuteDeleteAsync();
    }

    internal async Task<long> DomainRowCountAsync(DisposablePostgreSql target, string table)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    internal async Task AssertDomainTablesEmptyAsync(DisposablePostgreSql target)
    {
        foreach (var table in await TargetTablesAsync(target))
        {
            (await DomainRowCountAsync(target, table)).ShouldBe(0, table);
        }
    }

    internal async Task AssertTargetsHaveDistinctClusterIdentityAsync()
    {
        var primary = await IdentityAsync(Primary);
        var other = await IdentityAsync(Other);
        primary.SystemIdentifier.ShouldNotBe(other.SystemIdentifier);
        primary.IsSuperuser.ShouldBeFalse();
        other.IsSuperuser.ShouldBeFalse();
    }

    internal async Task AssertSelfReferencesRestoredAsync()
    {
        await using var db = TargetConfiguration.CreateDbContext();
        var submission = await db
            .RequestSubmissions.AsNoTracking()
            .SingleAsync(submission => submission.Id == MergedSubmissionId);
        submission.MergedIntoSubmissionId.ShouldBe(TargetSubmissionId);
        var candidate = await db
            .MomentCandidates.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == MergedCandidateId);
        candidate.MergedIntoCandidateId.ShouldBe(TargetCandidateId);
    }

    internal async Task AssertProviderMetadataWasNotCopiedAsync()
    {
        var sourceMigrations = await SqliteMigrationsAsync();
        var targetMigrations = await TargetMigrationsAsync(Primary);
        targetMigrations.ShouldBe(CurrentPostgreSqlMigrations);
        targetMigrations.Intersect(sourceMigrations, StringComparer.Ordinal).ShouldBeEmpty();

        await using var target = new NpgsqlConnection(Primary.ConnectionString);
        await target.OpenAsync();
        await using var metadata = target.CreateCommand();
        metadata.CommandText = "SELECT to_regclass('public.sqlite_sequence') IS NULL;";
        ((bool)(await metadata.ExecuteScalarAsync() ?? false)).ShouldBeTrue();
    }

    internal async Task AssertPendingWorkPreservedAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
            ApplicationConnectionFile
        );
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
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
            ApplicationConnectionFile
        );
        await using var db = configuration.CreateDbContext();
        return await db
            .RequestSubmissions.Where(submission => submission.Id == MergedSubmissionId)
            .Select(submission => submission.MergedIntoSubmissionId)
            .SingleAsync();
    }

    internal async Task StartPostgreSqlAndWriteAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
            ApplicationConnectionFile
        );
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
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
            ApplicationConnectionFile
        );
        await using var target = configuration.CreateDbContext();
        (await target.Hosts.CountAsync()).ShouldBe(2);
        await using var source = BlokeBotDatabaseConfiguration
            .Sqlite(SqliteDatabasePath)
            .CreateDbContext();
        (await source.Hosts.CountAsync()).ShouldBe(1);
    }

    internal async Task DeliverTransferredPendingWorkOnceAsync()
    {
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
            ApplicationConnectionFile
        );
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
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
            ApplicationConnectionFile
        );
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

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        System.Data.Common.DbCommand command
    )
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<PostgreSqlIdentity> IdentityAsync(DisposablePostgreSql target)
    {
        await using var administrator = new NpgsqlConnection(target.AdminConnectionString);
        await administrator.OpenAsync();
        await using var cluster = administrator.CreateCommand();
        cluster.CommandText = "SELECT system_identifier::text FROM pg_control_system();";
        var systemIdentifier = (string)(await cluster.ExecuteScalarAsync())!;

        await using var command = administrator.CreateCommand();
        command.CommandText = "SELECT rolsuper FROM pg_roles WHERE rolname = @role;";
        _ = command.Parameters.AddWithValue("role", _role);
        return new PostgreSqlIdentity(
            systemIdentifier,
            (bool)(await command.ExecuteScalarAsync() ?? true)
        );
    }

    internal sealed record TargetDatabaseState(string Owner, string? Comment);

    private sealed record PostgreSqlIdentity(string SystemIdentifier, bool IsSuperuser);
}
