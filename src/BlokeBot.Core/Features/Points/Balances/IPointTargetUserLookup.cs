namespace BlokeBot.Core.Features.Points.Balances;

public interface IPointTargetUserLookup
{
    Task<bool> ExistsAsync(string login, CancellationToken ct);
}
