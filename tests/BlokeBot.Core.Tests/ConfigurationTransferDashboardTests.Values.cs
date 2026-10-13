using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferDashboardTests
{
    [Test]
    public async Task DefinitionImportRefreshesBoundSession_AfterCommitOnly_SkipsOtherHostAndAddMissing()
    {
        var boundary = new AuditBoundary();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(boundary);
        var host = await HostAsync(database, "destination");
        var other = await HostAsync(database, "other");
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(database, changes);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            host,
            "score",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "1"
        );
        var otherDefinition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            other,
            "score",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "2"
        );
        var reader = new DashboardValueReader(
            database,
            new(database, new Granted()),
            values,
            changes,
            NullLogger<DashboardValueReader>.Instance
        );
        var updated = 0;
        var otherUpdated = 0;
        var nativeHints = 0;
        using var display = reader.CreateSession(() =>
        {
            updated++;
            return ValueTask.CompletedTask;
        });
        using var otherDisplay = reader.CreateSession(() =>
        {
            otherUpdated++;
            return ValueTask.CompletedTask;
        });
        await display.BindAsync(
            Session(host),
            new(host),
            new(
                new DashboardBinding.Configured(new DashboardTarget.Value(definition.Id)),
                new DashboardValueSelection.Scalar()
            ),
            default
        );
        await otherDisplay.BindAsync(
            Session(other),
            new(other),
            new(
                new DashboardBinding.Configured(new DashboardTarget.Value(otherDefinition.Id)),
                new DashboardValueSelection.Scalar()
            ),
            default
        );
        await display.WhenIdleAsync();
        await otherDisplay.WhenIdleAsync();
        var initialOther = otherUpdated;
        var initial = updated;
        using var committed = changes.Subscribe(
            new CustomStoredValueChange.Definition(host, definition.Id),
            ObserverIdentity.Named("Dashboard340.import-commit"),
            async (_, _) =>
            {
                await using var read = await database.CreateDbContextAsync();
                (await read.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
                (
                    await read.CustomValueDefinitions.SingleAsync(row => row.Id == definition.Id)
                ).DefaultNumber.ShouldBe(17);
                nativeHints++;
            }
        );
        var commands = new CustomCommandsSectionV1(
            "UTC",
            [],
            [],
            [],
            [new("score", CustomValueScope.Global, CustomValueKind.Number, "17")]
        );
        var document = Document(new([]), commands: commands);
        var apply = await Coordinator(
                database,
                changes,
                new ConfigurationImportObserverDispatcher(
                    [new FailingDashboardImportObserver()],
                    NullLogger<ConfigurationImportObserverDispatcher>.Instance
                )
            )
            .ApplyAsync(
                Session(host),
                document,
                Selection(
                    host,
                    ImportConflictStrategy.Merge,
                    ConfigurationSectionId.CustomCommands
                ),
                new("owner", "streamer"),
                default
            );
        var applied = apply.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        applied.Result.PostCommitFailures.ShouldBe([
            new(ConfigurationSectionId.CustomCommands, "reconciliation-failed"),
        ]);
        await display.WhenIdleAsync();
        display.State.ShouldBeOfType<DashboardValueState.Available>().Value.Value.ShouldBe("17");
        updated.ShouldBe(initial + 1);
        otherUpdated.ShouldBe(initialOther);
        nativeHints.ShouldBe(1);
        _ = (
            await Coordinator(database, changes)
                .ApplyAsync(
                    Session(host),
                    document with
                    {
                        Sections = document.Sections with
                        {
                            CustomCommands = commands with
                            {
                                StoredDefinitions =
                                [
                                    new(
                                        "score",
                                        CustomValueScope.Global,
                                        CustomValueKind.Number,
                                        "99"
                                    ),
                                ],
                            },
                        },
                    },
                    Selection(
                        host,
                        ImportConflictStrategy.AddMissing,
                        ConfigurationSectionId.CustomCommands
                    ),
                    new("owner", "streamer"),
                    default
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await display.WhenIdleAsync();
        updated.ShouldBe(initial + 1);
        nativeHints.ShouldBe(1);
        boundary.Active = true;
        _ = (
            await Coordinator(database, changes)
                .ApplyAsync(
                    Session(host),
                    document,
                    Selection(
                        host,
                        ImportConflictStrategy.Merge,
                        ConfigurationSectionId.CustomCommands
                    ),
                    new("owner", "streamer"),
                    default
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
        await display.WhenIdleAsync();
        updated.ShouldBe(initial + 1);
        nativeHints.ShouldBe(1);
        display.State.ShouldBeOfType<DashboardValueState.Available>().Value.Value.ShouldBe("17");
    }

    private sealed class FailingDashboardImportObserver : IConfigurationImportObserver
    {
        public ConfigurationSectionId Section => ConfigurationSectionId.CustomCommands;

        public ValueTask<ConfigurationImportObservation> ImportedAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromException<ConfigurationImportObservation>(
                new IOException("Owned post-commit observer failure")
            );
    }
}
