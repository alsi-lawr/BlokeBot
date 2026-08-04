using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandCooldownStore(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<CooldownKey, DateTimeOffset> _blockedUntil = [];
    private readonly Dictionary<CooldownKey, TaskCompletionSource> _reservations = [];

    internal int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _blockedUntil.Count;
            }
        }
    }

    public bool TryRecord(
        int commandId,
        CustomCommandCooldownScope scope,
        string userLogin,
        TimeSpan cooldown
    )
    {
        var now = clock.GetUtcNow();

        lock (_gate)
        {
            PruneExpired(now);
            if (cooldown <= TimeSpan.Zero)
            {
                return true;
            }

            var key = new CooldownKey(
                commandId,
                scope == CustomCommandCooldownScope.User ? Login.Normalize(userLogin) : string.Empty
            );
            if (
                _reservations.ContainsKey(key)
                || (_blockedUntil.TryGetValue(key, out var expiry) && expiry > now)
            )
            {
                return false;
            }

            _blockedUntil[key] = now + cooldown;
            return true;
        }
    }

    internal async ValueTask<CustomCommandCooldownReservation?> ReserveAsync(
        int commandId,
        CustomCommandCooldownScope scope,
        string userLogin,
        TimeSpan cooldown,
        CancellationToken cancellationToken
    )
    {
        if (cooldown <= TimeSpan.Zero)
        {
            return CustomCommandCooldownReservation.Unlimited;
        }

        var key = new CooldownKey(
            commandId,
            scope == CustomCommandCooldownScope.User ? Login.Normalize(userLogin) : string.Empty
        );
        while (true)
        {
            Task pending;
            lock (_gate)
            {
                var now = clock.GetUtcNow();
                PruneExpired(now);
                if (_blockedUntil.TryGetValue(key, out var expiry) && expiry > now)
                {
                    return null;
                }

                if (!_reservations.TryGetValue(key, out var reservation))
                {
                    reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _reservations.Add(key, reservation);
                    return new(this, key, reservation, cooldown);
                }

                pending = reservation.Task;
            }

            await pending.WaitAsync(cancellationToken);
        }
    }

    private void CompleteReservation(
        CooldownKey key,
        TaskCompletionSource reservation,
        TimeSpan cooldown,
        bool commit
    )
    {
        lock (_gate)
        {
            if (
                !_reservations.TryGetValue(key, out var current)
                || !ReferenceEquals(current, reservation)
            )
            {
                return;
            }

            _ = _reservations.Remove(key);
            if (commit)
            {
                _blockedUntil[key] = clock.GetUtcNow() + cooldown;
            }
        }

        reservation.SetResult();
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (
            var key in _blockedUntil
                .Where(pair => pair.Value <= now)
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            _ = _blockedUntil.Remove(key);
        }
    }

    internal readonly record struct CooldownKey(int CommandId, string UserLogin);

    internal sealed class CustomCommandCooldownReservation : IDisposable
    {
        private CustomCommandCooldownStore? _store;
        private readonly CooldownKey _key;
        private readonly TaskCompletionSource? _reservation;
        private readonly TimeSpan _cooldown;

        private CustomCommandCooldownReservation() { }

        internal CustomCommandCooldownReservation(
            CustomCommandCooldownStore store,
            CooldownKey key,
            TaskCompletionSource reservation,
            TimeSpan cooldown
        )
        {
            _store = store;
            _key = key;
            _reservation = reservation;
            _cooldown = cooldown;
        }

        internal static CustomCommandCooldownReservation Unlimited { get; } = new();

        internal void Commit() => Complete(commit: true);

        public void Dispose() => Complete(commit: false);

        private void Complete(bool commit) =>
            Interlocked
                .Exchange(ref _store, null)
                ?.CompleteReservation(_key, _reservation!, _cooldown, commit);
    }
}
