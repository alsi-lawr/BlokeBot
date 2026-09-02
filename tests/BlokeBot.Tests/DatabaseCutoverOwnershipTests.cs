using BlokeBot.DatabaseCutover;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class DatabaseCutoverOwnershipTests
{
    [Test]
    public async Task SqliteExclusiveLease_BlocksAnotherWriterAndReleasesOwnership()
    {
        var root = TemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "blokebot.db");
            await using var owner = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False"
            );
            await owner.OpenAsync();
            await using (var create = owner.CreateCommand())
            {
                create.CommandText = "CREATE TABLE state (id INTEGER PRIMARY KEY);";
                _ = await create.ExecuteNonQueryAsync();
            }

            await using (await SqliteExclusiveLease.AcquireAsync(owner, CancellationToken.None))
            {
                await using var contender = new SqliteConnection(
                    $"Data Source={databasePath};Pooling=False;Default Timeout=1"
                );
                await contender.OpenAsync();
                await using var blockedWrite = contender.CreateCommand();
                blockedWrite.CommandText = "BEGIN IMMEDIATE;";
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                _ = await Should.ThrowAsync<SqliteException>(() =>
                    blockedWrite.ExecuteNonQueryAsync(timeout.Token)
                );
            }

            await using var released = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False"
            );
            await released.OpenAsync();
            await using var permittedWrite = released.CreateCommand();
            permittedWrite.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
            _ = await permittedWrite.ExecuteNonQueryAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReceiptStore_ReplacesCheckpointDurablyWithOwnerOnlyPermissions()
    {
        var root = TemporaryDirectory();
        try
        {
            var store = new CutoverReceiptStore(root);
            var initial = Receipt(CutoverPhase.Prepared);
            await store.WriteAsync(initial, CancellationToken.None);
            await store.WriteAsync(
                initial.WithPhase(CutoverPhase.Verifying),
                CancellationToken.None
            );

            var restored = await store.ReadAsync(CancellationToken.None);

            restored!.OperationId.ShouldBe(initial.OperationId);
            restored.Phase.ShouldBe(CutoverPhase.Verifying);
            Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").ShouldBeEmpty();
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(store.Path)
                    .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task LocalStateFingerprint_IgnoresCutoverArtifactsButDetectsPluginPrivateChanges()
    {
        var root = TemporaryDirectory();
        try
        {
            var source = Path.Combine(root, "blokebot.db");
            var plugin = Path.Combine(root, "plugins", "example", "private.db");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
            await File.WriteAllTextAsync(source, "source-v1");
            await File.WriteAllTextAsync(plugin, "private-v1");
            var store = new CutoverReceiptStore(root);
            var before = await LocalStateFingerprint.CalculateAsync(
                root,
                source,
                store,
                CancellationToken.None
            );

            await File.WriteAllTextAsync(source, "source-v2");
            await store.WriteAsync(Receipt(CutoverPhase.Copying), CancellationToken.None);
            var afterCutoverArtifacts = await LocalStateFingerprint.CalculateAsync(
                root,
                source,
                store,
                CancellationToken.None
            );
            await File.WriteAllTextAsync(plugin, "private-v2");
            var afterPluginChange = await LocalStateFingerprint.CalculateAsync(
                root,
                source,
                store,
                CancellationToken.None
            );

            afterCutoverArtifacts.ShouldBe(before);
            afterPluginChange.ShouldNotBe(before);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CutoverReceipt Receipt(CutoverPhase phase) =>
        new(
            CutoverReceipt.CurrentFormatVersion,
            Guid.NewGuid(),
            phase,
            "source-fingerprint",
            "target-fingerprint",
            "local-state-fingerprint",
            "cluster-identity",
            "blokebot",
            "blokebot",
            [],
            null,
            null,
            DateTimeOffset.UtcNow,
            null
        );

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blokebot-cutover-tests-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(path);
        return path;
    }
}
