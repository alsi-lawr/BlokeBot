using System.Diagnostics;

namespace BlokeBot.Twitch;

public abstract record ModeratedChannelStatus
{
    private ModeratedChannelStatus() { }

    public TResult Match<TResult>(
        Func<Unknown, TResult> unknown,
        Func<NeedsAuthorization, TResult> needsAuthorization,
        Func<MissingPermission, TResult> missingPermission,
        Func<IsModerator, TResult> isModerator,
        Func<NotModerator, TResult> notModerator
    ) =>
        this switch
        {
            Unknown value => unknown(value),
            NeedsAuthorization value => needsAuthorization(value),
            MissingPermission value => missingPermission(value),
            IsModerator value => isModerator(value),
            NotModerator value => notModerator(value),
            _ => throw new UnreachableException("Unknown moderated channel status."),
        };

    public sealed record Unknown : ModeratedChannelStatus;

    public sealed record NeedsAuthorization : ModeratedChannelStatus;

    public sealed record MissingPermission : ModeratedChannelStatus;

    public sealed record IsModerator : ModeratedChannelStatus;

    public sealed record NotModerator : ModeratedChannelStatus;
}
