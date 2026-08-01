namespace BlokeBot.Core.Features.Commands;

public abstract record CommandAliasScope
{
    private CommandAliasScope() { }

    public abstract TResult Match<TResult>(
        Func<Global, TResult> global,
        Func<Profile, TResult> profile
    );

    public sealed record Global : CommandAliasScope
    {
        public override TResult Match<TResult>(
            Func<Global, TResult> global,
            Func<Profile, TResult> profile
        ) => global(this);
    }

    public sealed record Profile : CommandAliasScope
    {
        public Profile(int profileId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profileId);
            ProfileId = profileId;
        }

        public int ProfileId { get; }

        public override TResult Match<TResult>(
            Func<Global, TResult> global,
            Func<Profile, TResult> profile
        ) => profile(this);
    }
}
