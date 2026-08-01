using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.DataProtection;

namespace BlokeBot.Core.Tests;

internal static class HostBotAccountTokenProtectionTestSupport
{
    private static readonly IDataProtectionProvider _provider =
        new EphemeralDataProtectionProvider();

    public static IHostBotAccountTokenProtector CreateProtector() =>
        new DataProtectionHostBotAccountTokenProtector(_provider);

    public static void SetProtectedPayload(
        HostBotAccountSettings settings,
        HostBotAccountTokenPayload payload
    ) =>
        settings.ProtectedTokenPayload = CreateProtector()
            .Protect(settings.HostId, payload)
            .Match(
                value => value,
                _ => throw new InvalidOperationException("Test token protection failed.")
            );
}
