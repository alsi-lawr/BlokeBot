using Microsoft.AspNetCore.Authorization;

namespace BlokeBot.Auth.Sessions;

internal sealed class AuthSessionCapabilityHandler
    : AuthorizationHandler<AuthSessionCapabilityRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AuthSessionCapabilityRequirement requirement
    )
    {
        var session = AuthenticatedSession.FromPrincipal(context.User);
        if (session.HasCapability(requirement.Capability))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
