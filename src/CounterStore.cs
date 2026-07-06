using CommandBot.Store;
using Microsoft.EntityFrameworkCore;

public static class CounterKeys
{
    public const string Deaths = "deaths";
}

public interface ICounterStore
{
    Task EnsureCreatedAsync(CancellationToken ct);
    Task<int> LoadAsync(string key, CancellationToken ct);
    Task SaveAsync(string key, int value, CancellationToken ct);
}

public sealed class EfCounterStore(IDbContextFactory<CounterDbContext> dbFactory) : ICounterStore
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);
    }

    public async Task<int> LoadAsync(string key, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db
            .Counters.AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(ct);
    }

    public async Task SaveAsync(string key, int value, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Counters.SingleOrDefaultAsync(x => x.Key == key, ct);

        if (row is null)
            db.Counters.Add(new CounterRow { Key = key, Value = value });
        else
            row.Value = value;

        await db.SaveChangesAsync(ct);
    }
}
