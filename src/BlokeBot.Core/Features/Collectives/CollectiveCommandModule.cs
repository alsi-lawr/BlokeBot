using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Collectives;

internal sealed class CollectiveCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CollectiveService collectives
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands) =>
        _ = commands.Map(FixedChatCommandRoutes.Collective, ViewAsync);

    private async ValueTask ViewAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var login = context.Message.Channel.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var collectiveId = await db
            .CollectiveMemberships.AsNoTracking()
            .Where(value =>
                value.Host.Login == login
                && (value.Host.EnabledFeatures & HostFeatureFlags.Collectives)
                    == HostFeatureFlags.Collectives
                && value.Status == CollectiveMembershipStatus.Active
            )
            .OrderBy(value => value.Collective.Name)
            .Select(value => (Guid?)value.Collective.PublicId)
            .FirstOrDefaultAsync(ct);
        if (collectiveId is null)
        {
            return;
        }
        var projection = await collectives.LoadPublicAsync(login, new(collectiveId.Value), ct);
        if (projection is null)
        {
            return;
        }
        var progress = projection.Goal is { } goal
            ? $" · {goal.Name}: {goal.Current}/{goal.Target} {goal.UnitName}"
            : string.Empty;
        await context.ReplyAsync(
            $"{projection.Name}: {projection.ParticipatingHosts.Count} participating hosts{progress}. /collectives/{login}/{projection.Id.Value:D}",
            ct
        );
    }
}
