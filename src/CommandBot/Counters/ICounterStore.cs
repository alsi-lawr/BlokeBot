public interface ICounterStore
{
    Task EnsureCreatedAsync(CancellationToken ct);
    Task<int> LoadAsync(string key, CancellationToken ct);
    Task SaveAsync(string key, int value, CancellationToken ct);
}
