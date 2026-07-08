using Microsoft.AspNetCore.Authorization;

namespace BlokeBot.Auth.Sessions;

internal sealed record AuthSessionCapabilityRequirement(AuthSessionCapability Capability)
    : IAuthorizationRequirement;
