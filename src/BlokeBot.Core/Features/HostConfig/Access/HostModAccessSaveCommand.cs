using BlokeBot.Functional;

namespace BlokeBot.Core.Features.HostConfig.Access;

public abstract record HostModeratorAccessMode
{
    private HostModeratorAccessMode() { }

    internal abstract bool AllowModsByDefault { get; }

    public static HostModeratorAccessMode FromAllowModsByDefault(bool allowModsByDefault) =>
        allowModsByDefault ? new AllModerators() : new AllowlistOnly();

    public sealed record AllModerators : HostModeratorAccessMode
    {
        internal override bool AllowModsByDefault => true;
    }

    public sealed record AllowlistOnly : HostModeratorAccessMode
    {
        internal override bool AllowModsByDefault => false;
    }
}

public sealed record HostModAccessSaveCommand
{
    internal HostModAccessSaveCommand(int hostId, HostModeratorAccessMode mode)
    {
        HostId = hostId;
        Mode = mode;
    }

    public int HostId { get; }

    public HostModeratorAccessMode Mode { get; }
}

public static class HostModAccessSaveValidator
{
    public static Validation<HostModAccessSaveCommand, HostModAccessSaveValidationError> Validate(
        int hostId,
        HostModeratorAccessMode mode
    ) =>
        hostId > 0
            ? Validation<HostModAccessSaveCommand, HostModAccessSaveValidationError>.Valid(
                new HostModAccessSaveCommand(hostId, mode)
            )
            : Validation<HostModAccessSaveCommand, HostModAccessSaveValidationError>.Invalid(
                new HostModAccessSaveValidationError.InvalidHost()
            );
}

public abstract record HostModAccessSaveValidationError
{
    private HostModAccessSaveValidationError() { }

    public abstract string Message { get; }

    public sealed record InvalidHost : HostModAccessSaveValidationError
    {
        public override string Message => "Choose a channel before changing moderator access.";
    }
}

public sealed record HostModAccessSaved(
    int HostId,
    HostModeratorAccessMode Mode,
    int NotifiedObserverCount
);

public abstract record HostModAccessSaveFailure
{
    private HostModAccessSaveFailure() { }

    public abstract string Message { get; }

    public sealed record HostNotFound : HostModAccessSaveFailure
    {
        public override string Message =>
            "That channel setup is no longer available. Reload the page and try again.";
    }

    public sealed record RuntimeNotificationFailed(
        int FailedObserverCount,
        int FailedRollbackObserverCount
    ) : HostModAccessSaveFailure
    {
        public override string Message =>
            "Who can help could not be saved. Your previous setting has been restored.";
    }
}
