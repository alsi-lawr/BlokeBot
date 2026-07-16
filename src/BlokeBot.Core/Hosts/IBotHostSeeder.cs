namespace BlokeBot.Core.Hosts;

public interface IBotHostSeeder
{
    Task SeedAsync(int hostId, CancellationToken ct);
}
