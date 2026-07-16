using Microsoft.AspNetCore.Authorization;

namespace BlokeBot.Core.Auth.Sessions;

internal sealed record AuthSessionCapabilityRequirement(AuthSessionCapability Capability)
    : IAuthorizationRequirement;
