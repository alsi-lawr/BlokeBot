namespace BlokeBot.Core.Features.Points.Giveaways;

public sealed partial class PointsGiveawayService
{
    internal async Task<PointsGiveawayCurrent?> ReadCurrentAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PointsGiveawayQueries.LoadCurrentAsync(db, hostId, ct);
    }
}
