using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class CustomCommandAllowedUserPersistenceTests
{
    [Test]
    public async Task StableUserGrant_DuplicatingOrCrossingHosts_IsRejectedAndCommandDeleteCascades()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int commandId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var host = Host("streamer");
            var otherHost = Host("other");
            db.Hosts.AddRange(host, otherHost);
            _ = await db.SaveChangesAsync();
            var command = Command(host.Id);
            _ = db.CustomCommands.Add(command);
            _ = await db.SaveChangesAsync();
            commandId = command.Id;
            _ = db.CustomCommandAllowedUsers.Add(User(host.Id, command.Id, "viewer-id"));
            _ = await db.SaveChangesAsync();
        }

        await using (var duplicate = await factory.CreateDbContextAsync())
        {
            var hostId = await duplicate
                .CustomCommands.Where(command => command.Id == commandId)
                .Select(command => command.HostId)
                .SingleAsync();
            _ = duplicate.CustomCommandAllowedUsers.Add(User(hostId, commandId, "viewer-id"));
            _ = await Should.ThrowAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using (var crossHost = await factory.CreateDbContextAsync())
        {
            var otherHostId = await crossHost
                .Hosts.Where(host => host.Login == "other")
                .Select(host => host.Id)
                .SingleAsync();
            _ = crossHost.CustomCommandAllowedUsers.Add(
                User(otherHostId, commandId, "other-viewer-id")
            );
            _ = await Should.ThrowAsync<DbUpdateException>(() => crossHost.SaveChangesAsync());
        }

        await using (var delete = await factory.CreateDbContextAsync())
        {
            var command = await delete.CustomCommands.SingleAsync(value => value.Id == commandId);
            _ = delete.CustomCommands.Remove(command);
            _ = await delete.SaveChangesAsync();
            (await delete.CustomCommandAllowedUsers.CountAsync()).ShouldBe(0);
        }
    }

    private static BotHost Host(string login) =>
        new()
        {
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static CustomCommand Command(int hostId) =>
        new()
        {
            HostId = hostId,
            Name = "Command",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static CustomCommandAllowedUser User(int hostId, int commandId, string twitchUserId) =>
        new()
        {
            HostId = hostId,
            CustomCommandId = commandId,
            TwitchUserId = twitchUserId,
            Login = "viewer",
            DisplayName = "Viewer",
        };
}
