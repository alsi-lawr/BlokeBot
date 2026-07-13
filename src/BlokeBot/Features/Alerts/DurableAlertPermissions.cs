using BlokeBot.Auth.Sessions;

namespace BlokeBot.Features.Alerts;

public static class DurableAlertPermissions
{
    public static bool CanAcknowledge(AuthenticatedSession session)
    {
        return session.HasCapability(AuthSessionCapability.Operator);
    }
}
