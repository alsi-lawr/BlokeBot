namespace BlokeBot.Hosts;

public interface IBotHostSeeder
{
    Task SeedAsync(int hostId, CancellationToken ct);
}
