using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomCommandExecutionTests
{
    [Test]
    public async Task DashboardValues_CommandCommitCoalescesTargetsBeforeReplyAndReplayDoesNotRepublish()
    {
        await using var database = await PointProviderFixture.SqliteFileAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        await using var services = BuildServices(database);
        var values = services.GetRequiredService<CustomStoredValueService>();
        var changes = services.GetRequiredService<CustomStoredValueChanges>();
        var total = await DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        var dictionary = await DeclareAsync(
            values,
            hostId,
            "items",
            CustomValueScope.User,
            CustomValueKind.Dictionary,
            "default"
        );
        _ = await SeedCommandAsync(
            database,
            hostId,
            "save",
            [
                "{var_inc|global|total|1}:{var_inc|global|total|2}:{dict_set|user|items|one|ready}:{dict_set|user|items|two|second}",
            ]
        );
        int totalChanges = 0,
            oneChanges = 0,
            twoChanges = 0,
            wrongViewer = 0,
            wrongHost = 0;
        var observer = ObserverIdentity.Named("DashboardValues.Command");
        using var global = changes.Subscribe(
            new CustomStoredValueChange.Target(hostId, new(total.Id, "", "")),
            observer,
            async (_, _) =>
            {
                (await values.ValueAsync(hostId, new(total.Id, "", ""), default))!.Value.ShouldBe(
                    "3"
                );
                totalChanges++;
            }
        );
        using var one = changes.Subscribe(
            new CustomStoredValueChange.Target(hostId, new(dictionary.Id, "viewer-id", "one")),
            observer,
            (_, _) =>
            {
                oneChanges++;
                return ValueTask.CompletedTask;
            }
        );
        using var two = changes.Subscribe(
            new CustomStoredValueChange.Target(hostId, new(dictionary.Id, "viewer-id", "two")),
            observer,
            (_, _) =>
            {
                twoChanges++;
                return ValueTask.CompletedTask;
            }
        );
        using var unrelatedViewer = changes.Subscribe(
            new CustomStoredValueChange.Target(hostId, new(dictionary.Id, "different-id", "one")),
            observer,
            (_, _) =>
            {
                wrongViewer++;
                return ValueTask.CompletedTask;
            }
        );
        using var unrelatedHost = changes.Subscribe(
            new CustomStoredValueChange.Target(hostId + 1, new(total.Id, "", "")),
            observer,
            (_, _) =>
            {
                wrongHost++;
                return ValueTask.CompletedTask;
            }
        );
        var host = new DashboardHostId(hostId);
        var choice = new BotHostChoice(hostId, "streamer", "Streamer", AuthRole.Streamer);
        var owner = new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = "owner-id",
            Login = "streamer",
            State = new AuthSessionState.Selected(new BotHostSelection(choice, [choice])),
        };
        var reader = new DashboardValueReader(
            database,
            new(database, new NoModerator()),
            values,
            new UnavailableCustomCommandViewerResolver(),
            changes,
            NullLogger<DashboardValueReader>.Instance
        );
        var projected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        DashboardValueSession? currentDisplay = null;
        using var display = reader.CreateSession(() =>
        {
            if (currentDisplay?.State is DashboardValueState.Available { Value.Value: "3" })
            {
                _ = projected.TrySetResult();
            }
            return ValueTask.CompletedTask;
        });
        currentDisplay = display;
        await display.BindAsync(
            owner,
            host,
            new(
                new DashboardBinding.Configured(new DashboardTarget.Value(total.Id)),
                new DashboardValueSelection.Scalar()
            ),
            default
        );
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        var message = Identified(
            "viewer",
            "streamer",
            "!save",
            "commit-before-delivery",
            "viewer-id"
        );
        _ = await Should.ThrowAsync<IOException>(async () =>
            await dispatcher.DispatchResponsesAsync(
                message,
                (_, _) =>
                {
                    totalChanges.ShouldBe(1);
                    oneChanges.ShouldBe(1);
                    twoChanges.ShouldBe(1);
                    throw new IOException("Finite reply failure after commit");
                },
                default
            )
        );
        await projected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        display.State.ShouldBeOfType<DashboardValueState.Available>().Value.Value.ShouldBe("3");
        List<string> replies = [];
        await dispatcher.DispatchResponsesAsync(message, RecordMessages(replies), default);
        totalChanges.ShouldBe(1);
        oneChanges.ShouldBe(1);
        twoChanges.ShouldBe(1);
        wrongViewer.ShouldBe(0);
        wrongHost.ShouldBe(0);
        replies.ShouldBe(["1:3:ready:second"]);
    }

    private sealed class NoModerator : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int hostId,
            CancellationToken ct
        ) =>
            Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Unavailable());
    }

    [Test]
    public async Task DashboardValues_CommandRenderAndStorageRollbackAndSandboxEmitNoChanges()
    {
        var failure = new StoredSaveFailure { Enabled = false };
        await using var database = await PointProviderFixture.SqliteFileAsync(failure);
        var hostId = await SeedHostAsync(database, "streamer");
        await using var services = BuildServices(database);
        var values = services.GetRequiredService<CustomStoredValueService>();
        var changes = services.GetRequiredService<CustomStoredValueChanges>();
        var total = await DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        _ = await DeclareAsync(
            values,
            hostId,
            "bounded",
            CustomValueScope.Global,
            CustomValueKind.Number,
            long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        _ = await SeedCommandAsync(
            database,
            hostId,
            "invalid",
            ["{var_inc|global|total|1}:{var_inc|global|bounded|1}"]
        );
        _ = await SeedCommandAsync(database, hostId, "save", ["{var_inc|global|total|1}"]);
        _ = await SeedCommandAsync(database, hostId, "read", ["{var_get|global|total}"]);
        int notifications = 0;
        using var subscription = changes.Subscribe(
            new CustomStoredValueChange.Target(hostId, new(total.Id, "", "")),
            ObserverIdentity.Named("DashboardValues.Rollback"),
            (_, _) =>
            {
                notifications++;
                return ValueTask.CompletedTask;
            }
        );
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!invalid", "render-failure", "viewer-id"),
            RecordMessages(replies),
            default
        );
        failure.Enabled = true;
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!save", "storage-failure", "viewer-id"),
            RecordMessages(replies),
            default
        );
        failure.Enabled = false;
        var sandbox = await values.SandboxAsync(hostId, "viewer-id", default);
        _ = sandbox.Apply("var_inc", ["global", "total", "5"]);
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!read", "read-only", "viewer-id"),
            RecordMessages(replies),
            default
        );
        notifications.ShouldBe(0);
        (await values.ValueAsync(hostId, new(total.Id, "", ""), default))!.Value.ShouldBe("0");
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!save", "storage-failure", "viewer-id"),
            RecordMessages(replies),
            default
        );
        notifications.ShouldBe(1);
        replies.Last().ShouldBe("1");
    }
}
