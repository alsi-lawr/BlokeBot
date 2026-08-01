using BlokeBot.Core.Features.HostedChannels.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostBotAccountTokenProtectorTests
{
    private static readonly HostBotAccountTokenPayload _payload = new(
        "access-token",
        "refresh-token",
        DateTimeOffset.Parse("2026-07-22T12:00:00Z")
    );

    [Test]
    public void ProtectedPayload_RoundTrip_PreservesCompleteTokenSet()
    {
        var protector = CreateProtector();

        var protectedPayload = Success(protector.Protect(42, _payload));
        var roundTrip = Success(protector.Unprotect(42, protectedPayload));

        roundTrip.ShouldBe(_payload);
    }

    [Test]
    public void TamperedPayload_Unprotecting_FailsClosed()
    {
        var protector = CreateProtector();
        var protectedPayload = Success(protector.Protect(42, _payload));
        protectedPayload[^1] ^= 0x01;

        var failure = Error(protector.Unprotect(42, protectedPayload));

        failure.ShouldBe(new HostBotAccountTokenProtectionFailure());
    }

    [Test]
    public void PayloadCopiedToDifferentHost_Unprotecting_FailsClosed()
    {
        var protector = CreateProtector();
        var protectedPayload = Success(protector.Protect(42, _payload));

        var failure = Error(protector.Unprotect(43, protectedPayload));

        failure.ShouldBe(new HostBotAccountTokenProtectionFailure());
    }

    private static DataProtectionHostBotAccountTokenProtector CreateProtector() =>
        new(new EphemeralDataProtectionProvider());

    private static TValue Success<TValue>(
        BlokeBot.Functional.Result<TValue, HostBotAccountTokenProtectionFailure> result
    ) =>
        result.Match(
            value => value,
            _ => throw new InvalidOperationException("Expected protection to succeed.")
        );

    private static HostBotAccountTokenProtectionFailure Error<TValue>(
        BlokeBot.Functional.Result<TValue, HostBotAccountTokenProtectionFailure> result
    ) =>
        result.Match(
            _ => throw new InvalidOperationException("Expected protection to fail."),
            failure => failure
        );
}
