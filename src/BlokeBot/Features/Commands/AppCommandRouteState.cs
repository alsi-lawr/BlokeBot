namespace BlokeBot.Features.Commands;

public abstract record AppCommandRouteState
{
    private AppCommandRouteState() { }

    public abstract TResult Match<TResult>(
        Func<Host, TResult> host,
        Func<GuessingProfile, TResult> guessingProfile
    );

    public sealed record Host : AppCommandRouteState
    {
        public Host(int hostId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostId);
            HostId = hostId;
        }

        public int HostId { get; }

        public override TResult Match<TResult>(
            Func<Host, TResult> host,
            Func<GuessingProfile, TResult> guessingProfile
        )
        {
            return host(this);
        }
    }

    public sealed record GuessingProfile : AppCommandRouteState
    {
        public GuessingProfile(int hostId, int profileId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profileId);
            HostId = hostId;
            ProfileId = profileId;
        }

        public int HostId { get; }

        public int ProfileId { get; }

        public override TResult Match<TResult>(
            Func<Host, TResult> host,
            Func<GuessingProfile, TResult> guessingProfile
        )
        {
            return guessingProfile(this);
        }
    }
}
