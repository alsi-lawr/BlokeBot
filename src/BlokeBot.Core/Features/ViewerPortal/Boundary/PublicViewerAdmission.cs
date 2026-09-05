using System.Diagnostics.Metrics;
using System.Net;
using System.Threading.RateLimiting;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

internal sealed record PublicViewerClient(IPAddress Address, string? Subject);

internal enum PublicViewerAttempt
{
    Http,
    Read,
    Action,
    Connect,
    Inbound,
}

internal enum PublicViewerLeaseKind
{
    Transport,
    Circuit,
}

internal sealed record PublicViewerLimitSettings
{
    internal int StateCapacity { get; init; } = 4096;
    internal TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);
    internal TimeSpan IdleLifetime { get; init; } = TimeSpan.FromMinutes(10);
}

// One bounded public-surface store, shared across hosts, documents and retained circuits.
internal sealed class PublicViewerAdmission : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<BucketKey, Bucket> _buckets = [];
    private readonly TimeProvider _clock;
    private readonly PublicViewerLimitSettings _settings;
    private readonly Meter _meter = new("BlokeBot.PublicViewer", "1.0");
    private readonly Counter<long> _rejections;
    private readonly UpDownCounter<long> _leases;
    private int _transports;
    private int _circuits;
    private bool _disposed;

    public PublicViewerAdmission(TimeProvider clock)
        : this(clock, new()) { }

    internal PublicViewerAdmission(TimeProvider clock, PublicViewerLimitSettings settings)
    {
        _clock = clock;
        _settings = settings;
        _rejections = _meter.CreateCounter<long>("public_viewer.rejections");
        _leases = _meter.CreateUpDownCounter<long>("public_viewer.leases");
    }

    internal bool TryAttempt(
        PublicViewerClient client,
        PublicViewerAttempt attempt,
        int? resolvedHostId = null
    )
    {
        lock (_gate)
        {
            var buckets = Resolve(client, resolvedHostId);
            return buckets is not null && Take(buckets, attempt);
        }
    }

    internal IDisposable? TryAcquire(PublicViewerClient client, PublicViewerLeaseKind kind)
    {
        lock (_gate)
        {
            var buckets = Resolve(client, null);
            if (
                buckets is null
                || (
                    kind == PublicViewerLeaseKind.Transport
                    && !Take(buckets, PublicViewerAttempt.Connect)
                )
            )
            {
                return null;
            }
            var network = buckets[0];
            var viewer = buckets[1];
            var allowed = kind switch
            {
                PublicViewerLeaseKind.Transport => _transports < 256
                    && network.Transports < 8
                    && viewer.Transports < (client.Subject is null ? 2 : 4),
                PublicViewerLeaseKind.Circuit => _circuits < 256
                    && network.Circuits < 64
                    && viewer.Circuits < (client.Subject is null ? 24 : 32),
            };
            if (!allowed)
            {
                Reject(kind.ToString(), "capacity");
                return null;
            }
            switch (kind)
            {
                case PublicViewerLeaseKind.Transport:
                    _transports++;
                    network.Transports++;
                    viewer.Transports++;
                    break;
                case PublicViewerLeaseKind.Circuit:
                    _circuits++;
                    network.Circuits++;
                    viewer.Circuits++;
                    break;
            }
            _leases.Add(1, new KeyValuePair<string, object?>("kind", kind.ToString()));
            return new OwnedLease(this, network, viewer, kind);
        }
    }

    internal bool TryChannelRead(PublicViewerClient client, int resolvedHostId)
    {
        lock (_gate)
        {
            var buckets = Resolve(client, resolvedHostId);
            return buckets is not null && Take([buckets[2]], PublicViewerAttempt.Read);
        }
    }

    private Bucket[]? Resolve(PublicViewerClient client, int? hostId)
    {
        if (_disposed || hostId is <= 0 || client.Subject is { Length: 0 or > 64 })
        {
            return null;
        }
        var address = client.Address.IsIPv4MappedToIPv6
            ? client.Address.MapToIPv4()
            : client.Address;
        var network = new BucketKey(BucketScope.Network, address.ToString(), null);
        var viewer = new BucketKey(
            client.Subject is null ? BucketScope.Anonymous : BucketScope.Authenticated,
            client.Subject ?? address.ToString(),
            null
        );
        BucketKey[] keys = hostId is null
            ? [network, viewer]
            : [network, viewer, viewer with { HostId = hostId }];
        var now = _clock.GetTimestamp();
        var missing = keys.Count(key => !_buckets.ContainsKey(key));
        if (_buckets.Count + missing > _settings.StateCapacity)
        {
            foreach (
                var key in _buckets
                    .Where(pair =>
                        pair.Value.Transports == 0
                        && pair.Value.Circuits == 0
                        && _clock.GetElapsedTime(pair.Value.LastUsed, now) >= _settings.IdleLifetime
                    )
                    .Select(pair => pair.Key)
                    .ToArray()
            )
            {
                _buckets[key].Dispose();
                _ = _buckets.Remove(key);
            }
            missing = keys.Count(key => !_buckets.ContainsKey(key));
            if (_buckets.Count + missing > _settings.StateCapacity)
            {
                Reject("state", "capacity");
                return null;
            }
        }
        var result = new Bucket[keys.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket(key.Scope, key.HostId is not null, _settings.Window);
                _buckets.Add(key, bucket);
            }
            bucket.LastUsed = now;
            result[index] = bucket;
        }
        return result;
    }

    private bool Take(Bucket[] buckets, PublicViewerAttempt attempt)
    {
        // These are attempt budgets: a rejected attempt may consume another applicable bucket.
        foreach (var bucket in buckets)
        {
            if (!bucket.Take(attempt))
            {
                Reject(attempt.ToString(), "rate");
                return false;
            }
        }
        return true;
    }

    private void Reject(string unit, string reason) =>
        _rejections.Add(
            1,
            new KeyValuePair<string, object?>("unit", unit),
            new KeyValuePair<string, object?>("reason", reason)
        );

    private void Release(Bucket network, Bucket viewer, PublicViewerLeaseKind kind)
    {
        lock (_gate)
        {
            switch (kind)
            {
                case PublicViewerLeaseKind.Transport:
                    _transports--;
                    network.Transports--;
                    viewer.Transports--;
                    break;
                case PublicViewerLeaseKind.Circuit:
                    _circuits--;
                    network.Circuits--;
                    viewer.Circuits--;
                    break;
            }
            network.LastUsed = viewer.LastUsed = _clock.GetTimestamp();
            _leases.Add(-1, new KeyValuePair<string, object?>("kind", kind.ToString()));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var bucket in _buckets.Values)
            {
                bucket.Dispose();
            }
            _buckets.Clear();
        }
        _meter.Dispose();
    }

    private enum BucketScope
    {
        Network,
        Anonymous,
        Authenticated,
    }

    private readonly record struct BucketKey(BucketScope Scope, string Value, int? HostId);

    private sealed class Bucket : IDisposable
    {
        private readonly Dictionary<PublicViewerAttempt, TokenBucketRateLimiter> _rates;
        internal long LastUsed { get; set; }
        internal int Transports { get; set; }
        internal int Circuits { get; set; }

        internal Bucket(BucketScope scope, bool channel, TimeSpan window) =>
            _rates = Enum.GetValues<PublicViewerAttempt>()
                .ToDictionary(
                    attempt => attempt,
                    attempt =>
                    {
                        var limit = (scope, attempt) switch
                        {
                            (BucketScope.Network, PublicViewerAttempt.Inbound) => 2400,
                            (BucketScope.Network, _) => 240,
                            (
                                BucketScope.Anonymous,
                                PublicViewerAttempt.Http
                                    or PublicViewerAttempt.Read
                            ) => channel ? 30 : 60,
                            (
                                BucketScope.Authenticated,
                                PublicViewerAttempt.Http
                                    or PublicViewerAttempt.Read
                            ) => channel ? 60 : 120,
                            (
                                BucketScope.Anonymous,
                                PublicViewerAttempt.Action
                                    or PublicViewerAttempt.Connect
                            ) => channel ? 15 : 30,
                            (
                                BucketScope.Authenticated,
                                PublicViewerAttempt.Action
                                    or PublicViewerAttempt.Connect
                            ) => channel ? 30 : 60,
                            (_, PublicViewerAttempt.Inbound) => 600,
                        };
                        return new TokenBucketRateLimiter(
                            new TokenBucketRateLimiterOptions
                            {
                                TokenLimit =
                                    attempt == PublicViewerAttempt.Inbound
                                        ? (scope == BucketScope.Network ? 480 : 120)
                                        : limit,
                                TokensPerPeriod =
                                    attempt == PublicViewerAttempt.Inbound ? limit / 6 : limit,
                                ReplenishmentPeriod =
                                    attempt == PublicViewerAttempt.Inbound ? window / 6 : window,
                                AutoReplenishment = false,
                                QueueLimit = 0,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            }
                        );
                    }
                );

        internal bool Take(PublicViewerAttempt attempt)
        {
            var rate = _rates[attempt];
            _ = rate.TryReplenish();
            using var lease = rate.AttemptAcquire();
            return lease.IsAcquired;
        }

        public void Dispose()
        {
            foreach (var rate in _rates.Values)
            {
                rate.Dispose();
            }
        }
    }

    private sealed class OwnedLease(
        PublicViewerAdmission owner,
        Bucket network,
        Bucket viewer,
        PublicViewerLeaseKind kind
    ) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(network, viewer, kind);
            }
        }
    }
}
