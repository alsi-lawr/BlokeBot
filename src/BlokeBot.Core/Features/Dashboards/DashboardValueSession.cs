using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardValueSession(
    DashboardValueReader reader,
    CustomStoredValueChanges changes,
    Func<ValueTask> changed
) : IDisposable
{
    private readonly object _gate = new();
    private readonly ObserverIdentity _observer = ObserverIdentity.Named(
        $"DashboardValue.{Guid.NewGuid():N}"
    );
    private IDisposable? _definitionSubscription;
    private IDisposable? _targetSubscription;
    private CustomValueTarget? _target;
    private AuthenticatedSession? _session;
    private DashboardHostId _host;
    private DashboardWidgetConfiguration.Value? _configuration;
    private long _generation;
    private long _read;
    private bool _disposed;

    internal DashboardValueState State
    {
        get
        {
            lock (_gate)
            {
                return field;
            }
        }
        private set;
    } = new DashboardValueState.Unavailable(DashboardValueUnavailableReason.Unconfigured);
    internal CustomCommandViewer? Viewer
    {
        get
        {
            lock (_gate)
            {
                return field;
            }
        }
        private set;
    }

    internal Task BindAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Value configuration,
        CancellationToken ct
    )
    {
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            generation = ++_generation;
            Unsubscribe();
            _session = session;
            _host = host;
            _configuration = configuration;
            Viewer = null;
            State = new DashboardValueState.Unavailable(
                DashboardValueUnavailableReason.Unconfigured
            );
            if (
                configuration.Binding is DashboardBinding.Configured
                {
                    Target: DashboardTarget.Value value
                }
            )
            {
                _definitionSubscription = changes.Subscribe(
                    new CustomStoredValueChange.Definition(host.Value, value.DefinitionId),
                    _observer,
                    OnChangeAsync
                );
            }
        }
        return RefreshAsync(generation, ct);
    }

    internal async Task SelectViewerAsync(string login, CancellationToken ct)
    {
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            generation = ++_generation;
            Viewer = null;
            _targetSubscription?.Dispose();
            _targetSubscription = null;
            _target = null;
            State = new DashboardValueState.Unavailable(
                DashboardValueUnavailableReason.ViewerRequired
            );
        }
        await changed();
        var resolution = await reader.ResolveViewerAsync(login, ct);
        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }
            Viewer = resolution is CustomCommandViewerResolution.Found found ? found.Viewer : null;
            if (Viewer is null)
            {
                State = new DashboardValueState.Unavailable(
                    resolution is CustomCommandViewerResolution.NotFound
                        ? DashboardValueUnavailableReason.ViewerNotFound
                        : DashboardValueUnavailableReason.ViewerUnavailable
                );
            }
        }
        if (resolution is CustomCommandViewerResolution.Found)
        {
            await RefreshAsync(generation, ct);
        }
        else
        {
            await changed();
        }
    }

    internal Task RefreshAsync(CancellationToken ct) => RefreshAsync(null, ct);

    private async Task RefreshAsync(long? expectedGeneration, CancellationToken ct)
    {
        AuthenticatedSession session;
        DashboardHostId host;
        DashboardWidgetConfiguration.Value configuration;
        CustomCommandViewer? viewer;
        CustomValueDefinitionDraft? currentDefinition;
        long generation;
        long read;
        lock (_gate)
        {
            if (
                _disposed
                || _session is null
                || _configuration is null
                || (expectedGeneration is { } expected && expected != _generation)
            )
            {
                return;
            }
            session = _session;
            host = _host;
            configuration = _configuration;
            viewer = Viewer;
            currentDefinition = (State as DashboardValueState.Available)?.Definition;
            generation = _generation;
            read = ++_read;
        }
        var state = await reader.ReadAsync(
            session,
            host,
            configuration,
            viewer,
            currentDefinition,
            target => BindTarget(generation, host, target),
            ct
        );
        lock (_gate)
        {
            if (_disposed || generation != _generation || read != _read)
            {
                return;
            }
            State = state;
        }
        await changed();
    }

    private void BindTarget(long generation, DashboardHostId host, CustomValueTarget target)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation || _target == target)
            {
                return;
            }
            _targetSubscription?.Dispose();
            _target = target;
            _targetSubscription = changes.Subscribe(
                new CustomStoredValueChange.Target(host.Value, target),
                _observer,
                OnChangeAsync
            );
        }
    }

    private async ValueTask OnChangeAsync(
        EventNotification<CustomStoredValueChange> notification,
        CancellationToken ct
    )
    {
        long generation;
        lock (_gate)
        {
            if (
                _disposed
                || notification.Key.HostId != _host.Value
                || _configuration?.Binding
                    is not DashboardBinding.Configured { Target: DashboardTarget.Value value }
                || notification.Key.DefinitionId != value.DefinitionId
                || !notification.Key.Match(_ => true, target => target.Value == _target)
            )
            {
                return;
            }
            generation = _generation;
        }
        await RefreshAsync(generation, ct);
    }

    private void Unsubscribe()
    {
        _definitionSubscription?.Dispose();
        _definitionSubscription = null;
        _targetSubscription?.Dispose();
        _targetSubscription = null;
        _target = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            Unsubscribe();
        }
    }
}
