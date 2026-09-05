using System.Net;
using BlokeBot.Core.Features.ViewerPortal.Boundary;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PublicViewerAdmissionTests
{
    [Test]
    public void HostSwitchAndNewDocuments_CannotResetStableIdentityOrNetworkBudgets()
    {
        using var admission = new PublicViewerAdmission(TimeProvider.System);
        var client = new PublicViewerClient(IPAddress.Parse("192.0.2.1"), "verified-id");
        for (var index = 0; index < 30; index++)
        {
            admission.TryAttempt(client, PublicViewerAttempt.Action, 1).ShouldBeTrue();
        }
        admission.TryAttempt(client, PublicViewerAttempt.Action, 1).ShouldBeFalse();
        for (var index = 0; index < 29; index++)
        {
            admission.TryAttempt(client, PublicViewerAttempt.Action, 2).ShouldBeTrue();
        }
        admission
            .TryAttempt(
                client with
                {
                    Address = IPAddress.Parse("192.0.2.2"),
                },
                PublicViewerAttempt.Action,
                3
            )
            .ShouldBeFalse();
        var sameNetwork = new PublicViewerClient(IPAddress.Parse("192.0.2.9"), null);
        for (var index = 0; index < 240; index++)
        {
            admission
                .TryAttempt(
                    sameNetwork with
                    {
                        Subject = $"identity-{index}",
                    },
                    PublicViewerAttempt.Http
                )
                .ShouldBeTrue();
        }
        admission
            .TryAttempt(sameNetwork with { Subject = "new-identity" }, PublicViewerAttempt.Http)
            .ShouldBeFalse();
    }

    [Test]
    public void Capacity_DoesNotEvictOwnedConnectionsOrReleaseRetainedCircuitsAtTransportClose()
    {
        var clock = new AdmissionClock();
        using var admission = new PublicViewerAdmission(clock, new() { StateCapacity = 2 });
        var client = new PublicViewerClient(IPAddress.Parse("192.0.2.1"), null);
        using var first = admission.TryAcquire(client, PublicViewerLeaseKind.Transport);
        using var second = admission.TryAcquire(client, PublicViewerLeaseKind.Transport);
        _ = first.ShouldNotBeNull();
        _ = second.ShouldNotBeNull();
        admission.TryAcquire(client, PublicViewerLeaseKind.Transport).ShouldBeNull();
        var retained = admission.TryAcquire(client, PublicViewerLeaseKind.Circuit);
        _ = retained.ShouldNotBeNull();
        first.Dispose();
        second.Dispose();
        clock.Advance(TimeSpan.FromHours(1));
        admission
            .TryAttempt(new(IPAddress.Parse("192.0.2.2"), null), PublicViewerAttempt.Http)
            .ShouldBeFalse();
        retained.Dispose();
        retained.Dispose();
        clock.Advance(TimeSpan.FromMinutes(11));
        admission
            .TryAttempt(new(IPAddress.Parse("192.0.2.2"), null), PublicViewerAttempt.Http)
            .ShouldBeTrue();
    }

    [Test]
    public async Task ConcurrentTransportAdmission_IsAtomicAndRecoversAfterOwnedRelease()
    {
        using var admission = new PublicViewerAdmission(TimeProvider.System);
        var client = new PublicViewerClient(IPAddress.Parse("192.0.2.1"), "verified-id");
        var leases = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(index =>
                    Task.Run(() => admission.TryAcquire(client, PublicViewerLeaseKind.Transport))
                )
        );
        leases.Count(value => value is not null).ShouldBe(4);
        foreach (var lease in leases)
        {
            lease?.Dispose();
        }
        using var restored = admission.TryAcquire(client, PublicViewerLeaseKind.Transport);
        _ = restored.ShouldNotBeNull();
    }

    private sealed class AdmissionClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
