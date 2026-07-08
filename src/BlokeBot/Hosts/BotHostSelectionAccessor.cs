using System.Security.Claims;

namespace BlokeBot.Hosts;

internal sealed class BotHostSelectionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public BotHostSelection? Current => FromPrincipal(httpContextAccessor.HttpContext?.User);

    public static BotHostSelection? FromPrincipal(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        var available = user.FindAll(BotHostClaims.AvailableHost)
            .Select(claim => BotHostClaimCodec.Decode(claim.Value))
            .OfType<BotHostChoice>()
            .OrderBy(host => host.DisplayName)
            .ToArray();

        if (available.Length == 0)
            return null;

        var selectedId = int.TryParse(
            user.FindFirstValue(BotHostClaims.SelectedHostId),
            out var parsed
        )
            ? parsed
            : available[0].Id;
        var current = available.FirstOrDefault(host => host.Id == selectedId) ?? available[0];
        return new BotHostSelection(current, available);
    }
}
