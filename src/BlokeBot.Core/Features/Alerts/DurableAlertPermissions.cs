using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Features.Alerts;

public static class DurableAlertPermissions
{
    public static bool CanAcknowledge(AuthenticatedSession session)
    {
        return session.HasCapability(AuthSessionCapability.Operator);
    }
}
