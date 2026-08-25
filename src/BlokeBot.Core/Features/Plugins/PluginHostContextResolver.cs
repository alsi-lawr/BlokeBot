using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Plugins.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Plugins;

public sealed record PluginHostContext(PluginHostId Id, string Login);

public interface IPluginHostContextResolver
{
    ValueTask<PluginHostContext?> FindAsync(
        PluginHostId hostId,
        CancellationToken cancellationToken
    );

    ValueTask<PluginHostContext?> FindAsync(
        string channelLogin,
        CancellationToken cancellationToken
    );
}

public sealed class PluginHostContextResolver(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : IPluginHostContextResolver
{
    public async ValueTask<PluginHostContext?> FindAsync(
        PluginHostId hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var login = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId.Value)
            .Select(static host => host.Login)
            .SingleOrDefaultAsync(cancellationToken);
        return login is null ? null : new(hostId, login);
    }

    public async ValueTask<PluginHostContext?> FindAsync(
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = LoginName.Parse(channelLogin).Value;
        var hostId = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Login == normalized)
            .Select(static host => (int?)host.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return hostId is { } value && PluginHostId.TryCreate(value, out var id)
            ? new(id, normalized)
            : null;
    }
}
