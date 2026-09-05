using System.Diagnostics;
using System.Diagnostics.Metrics;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

internal enum PortalReadOutcome
{
    Available,
    Empty,
    Disabled,
    Unauthorized,
    Unavailable,
    Degraded,
    BudgetExceeded,
    Cancelled,
}

internal sealed class PortalReadTelemetry(
    TimeProvider clock,
    DurableAlertService alerts,
    ILogger<PortalReadTelemetry> logger
) : IDisposable
{
    private const int _maximumStates = 1024;
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _cooldown = TimeSpan.FromMinutes(30);
    private readonly object _gate = new();
    private readonly Dictionary<AlertKey, FaultWindow> _states = [];
    private readonly SemaphoreSlim _delivery = new(1, 1);
    private readonly Meter _meter = new("BlokeBot.ViewerPortal", "1.0");
    private readonly ActivitySource _traces = new("BlokeBot.ViewerPortal", "1.0");
    private Histogram<double>? _duration;
    private Counter<long>? _outcomes;

    internal Activity? Start(PortalIcon owner, PortalAudience audience)
    {
        var activity = _traces.StartActivity("portal.owner.read", ActivityKind.Internal);
        _ = activity?.SetTag("portal.owner", owner.ToString());
        _ = activity?.SetTag("portal.audience", audience.ToString());
        return activity;
    }

    internal Task ObserveAsync(
        int hostId,
        PortalIcon owner,
        PortalAudience audience,
        PortalReadOutcome outcome,
        TimeSpan elapsed
    )
    {
        var tags = new TagList
        {
            { "owner", owner.ToString() },
            { "audience", audience.ToString() },
            { "outcome", outcome.ToString() },
        };
        lock (_gate)
        {
            _duration ??= _meter.CreateHistogram<double>("portal.owner.duration", "ms");
            _outcomes ??= _meter.CreateCounter<long>("portal.owner.reads");
            _duration.Record(elapsed.TotalMilliseconds, tags);
            _outcomes.Add(1, tags);
        }
        if (
            outcome
            is PortalReadOutcome.Cancelled
                or PortalReadOutcome.Disabled
                or PortalReadOutcome.Unauthorized
        )
        {
            return Task.CompletedTask;
        }
        var failed =
            outcome
                is PortalReadOutcome.Unavailable
                    or PortalReadOutcome.Degraded
                    or PortalReadOutcome.BudgetExceeded
            || elapsed >= TimeSpan.FromSeconds(1);
        var key = new AlertKey(hostId, owner, audience);
        long revision;
        bool raise;
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (!_states.TryGetValue(key, out var state))
            {
                if (!failed)
                {
                    return Task.CompletedTask;
                }
                foreach (
                    var expired in _states
                        .Where(pair => !pair.Value.Active && now - pair.Value.LastSeen >= _cooldown)
                        .Select(pair => pair.Key)
                        .ToArray()
                )
                {
                    _ = _states.Remove(expired);
                }
                if (_states.Count >= _maximumStates)
                {
                    return Task.CompletedTask;
                }
                state = new FaultWindow { FirstFailure = now };
                _states.Add(key, state);
            }
            state.LastSeen = now;
            if (failed)
            {
                state.Successes = 0;
                if (now - state.FirstFailure > _window)
                {
                    state.FirstFailure = now;
                    state.Failures = 0;
                }
                state.Failures++;
                if (
                    state.Active
                    || state.Failures < 10
                    || now - state.FirstFailure < TimeSpan.FromSeconds(30)
                    || now < state.NextAlert
                )
                {
                    return Task.CompletedTask;
                }
                state.Active = true;
                state.NextAlert = now + _cooldown;
                raise = true;
            }
            else
            {
                state.Failures = 0;
                state.FirstFailure = now;
                if (++state.Successes < 3 || !state.Active)
                {
                    return Task.CompletedTask;
                }
                state.Active = false;
                raise = false;
            }
            revision = ++state.Revision;
        }
        return DeliverAsync(key, revision, raise);
    }

    private async Task DeliverAsync(AlertKey key, long revision, bool raise)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var entered = false;
        try
        {
            await _delivery.WaitAsync(timeout.Token);
            entered = true;
            lock (_gate)
            {
                if (!_states.TryGetValue(key, out var state) || state.Revision != revision)
                {
                    return;
                }
            }
            var sourceKey = $"{key.Owner}:{key.Audience}";
            if (raise)
            {
                _ = await alerts
                    .Create(
                        key.HostId,
                        DurableAlertSeverity.Warning,
                        "viewer-portal",
                        sourceKey,
                        "Public channel reads need attention",
                        "Channel summaries have repeatedly failed or exceeded their read budget. Check database health and the viewer portal operator guide.",
                        "/alerts"
                    )
                    .RunAsync(timeout.Token);
                logger.LogWarning(
                    "A sustained portal read fault was reported for {Owner} {Audience}.",
                    key.Owner,
                    key.Audience
                );
            }
            else
            {
                _ = await alerts
                    .Resolve(key.HostId, "viewer-portal", sourceKey, "system")
                    .RunAsync(timeout.Token);
                logger.LogInformation(
                    "Portal reads recovered for {Owner} {Audience}.",
                    key.Owner,
                    key.Audience
                );
            }
        }
        catch (Exception)
        {
            // Do not attach exception/SQL/request data. Reporting failure cannot break a reader.
            logger.LogWarning("The aggregate portal alert transition could not be stored.");
        }
        finally
        {
            if (entered)
            {
                _ = _delivery.Release();
            }
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
        _traces.Dispose();
        _delivery.Dispose();
    }

    private readonly record struct AlertKey(int HostId, PortalIcon Owner, PortalAudience Audience);

    private sealed class FaultWindow
    {
        internal DateTimeOffset FirstFailure { get; set; }
        internal DateTimeOffset LastSeen { get; set; }
        internal DateTimeOffset NextAlert { get; set; }
        internal int Failures { get; set; }
        internal int Successes { get; set; }
        internal bool Active { get; set; }
        internal long Revision { get; set; }
    }
}
