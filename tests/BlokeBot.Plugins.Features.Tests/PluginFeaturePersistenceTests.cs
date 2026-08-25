using System.Text;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginFeaturePersistenceTests
{
    [Test]
    public async Task ConfigurationStore_SharesInstallationIsolatesHostsAndRejectsStaleWrites()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        var pluginId = PluginFeatureTestContext.Key("collection").PluginId;
        var installationOwner = new PluginConfigurationOwner.Installation(pluginId);
        var installation = await context.Store.LoadConfigurationAsync(
            installationOwner,
            CancellationToken.None
        );
        var secretId = PluginFeatureTestContext.SecretReplacement("ignored").SettingId;
        var installationWrite = await context.Store.WriteConfigurationAsync(
            new(
                installation,
                PluginFeatureTestContext.InstallationValues(),
                new([new(secretId, new(Encoding.UTF8.GetBytes("protected-value")))], [])
            ),
            CancellationToken.None
        );
        var savedInstallation = installationWrite
            .ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>()
            .State;

        var firstOwner = new PluginConfigurationOwner.Feature(
            PluginFeatureTestContext.Key("collection", 1)
        );
        var secondOwner = new PluginConfigurationOwner.Feature(
            PluginFeatureTestContext.Key("collection", 2)
        );
        var first = await context.Store.LoadConfigurationAsync(firstOwner, CancellationToken.None);
        var second = await context.Store.LoadConfigurationAsync(
            secondOwner,
            CancellationToken.None
        );
        _ = (
            await context.Store.WriteConfigurationAsync(
                new(
                    first,
                    PluginFeatureTestContext.CollectionValues(command: "first"),
                    PluginSecretChanges.Empty
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();
        _ = (
            await context.Store.WriteConfigurationAsync(
                new(
                    second,
                    PluginFeatureTestContext.CollectionValues(command: "second"),
                    PluginSecretChanges.Empty
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();

        var stale = await context.Store.LoadConfigurationAsync(firstOwner, CancellationToken.None);
        var winner = await context.Store.WriteConfigurationAsync(
            new(
                stale,
                PluginFeatureTestContext.CollectionValues(command: "winner"),
                PluginSecretChanges.Empty
            ),
            CancellationToken.None
        );
        var conflict = await context.Store.WriteConfigurationAsync(
            new(
                stale,
                PluginFeatureTestContext.CollectionValues(command: "loser"),
                PluginSecretChanges.Empty
            ),
            CancellationToken.None
        );

        savedInstallation.Secrets.Single().HasValue.ShouldBeTrue();
        (
            await context.Store.LoadConfigurationAsync(installationOwner, CancellationToken.None)
        ).Values.ShouldBe(savedInstallation.Values);
        Value(
                await context.Store.LoadConfigurationAsync(firstOwner, CancellationToken.None),
                "chat-command"
            )
            .ShouldBe("winner");
        Value(
                await context.Store.LoadConfigurationAsync(secondOwner, CancellationToken.None),
                "chat-command"
            )
            .ShouldBe("second");
        _ = winner.ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();
        conflict
            .ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Conflict>()
            .Current.Revision.ShouldBe(
                ((PluginConfigurationStoreWriteOutcome.Written)winner).State.Revision
            );
        (
            await context.Store.LoadFeatureStateAsync(
                PluginFeatureTestContext.Key("collection"),
                CancellationToken.None
            )
        ).ShouldBeNull();
    }

    [Test]
    public async Task SecretStore_ReplacesAndClearsWithoutReturningProtectedOrPlaintextValues()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        var owner = new PluginConfigurationOwner.Installation(
            PluginFeatureTestContext.Key("collection").PluginId
        );
        var current = await context.Store.LoadConfigurationAsync(owner, CancellationToken.None);
        var secretId = PluginFeatureTestContext.SecretReplacement("ignored").SettingId;
        var firstBytes = Encoding.UTF8.GetBytes("ciphertext-one");
        var first = (
            await context.Store.WriteConfigurationAsync(
                new(
                    current,
                    PluginFeatureTestContext.InstallationValues(),
                    new([new(secretId, new(firstBytes))], [])
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();
        var secondBytes = Encoding.UTF8.GetBytes("ciphertext-two");
        var second = (
            await context.Store.WriteConfigurationAsync(
                new(first.State, first.State.Values, new([new(secretId, new(secondBytes))], [])),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();

        await using (var db = context.Database.CreateDbContext())
        {
            var stored = await db.PluginInstallationSecrets.SingleAsync();
            stored.ProtectedValue.ShouldBe(secondBytes);
            Encoding.UTF8.GetString(stored.ProtectedValue).ShouldNotBe("raw-secret");
        }
        second.State.Secrets.ShouldBe([new PluginSecretPresence(secretId, true)]);

        var cleared = (
            await context.Store.WriteConfigurationAsync(
                new(second.State, second.State.Values, new([], [secretId])),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();

        cleared.State.Secrets.ShouldBeEmpty();
        await using var verify = context.Database.CreateDbContext();
        (await verify.PluginInstallationSecrets.CountAsync()).ShouldBe(0);
    }

    private static string Value(PluginConfigurationState configuration, string settingId) =>
        configuration
            .Values.Entries.Single(entry => entry.SettingId.Value == settingId)
            .Value.ShouldBeOfType<PluginSettingValue.Text>()
            .Value;
}
