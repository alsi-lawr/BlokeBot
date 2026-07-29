using BlokeBot.Persistence.Models;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

public sealed record AutomaticRaidShoutoutConfiguration(
    bool Enabled,
    int MinimumViewerCount,
    AutomaticRaidShoutoutMechanism Mechanism,
    AutomaticRaidChatPresentation ChatPresentation,
    string MessageTemplate,
    int? PinDurationSeconds,
    PersistedAnnouncementColor AnnouncementColor
)
{
    public static AutomaticRaidShoutoutConfiguration Defaults { get; } =
        new(
            false,
            1,
            AutomaticRaidShoutoutMechanism.Native,
            AutomaticRaidChatPresentation.Regular,
            AutomaticRaidShoutoutDefaults.MessageTemplate,
            null,
            PersistedAnnouncementColor.Primary
        );
}

public abstract record AutomaticRaidShoutoutSaveOutcome
{
    private AutomaticRaidShoutoutSaveOutcome() { }

    public sealed record Saved(AutomaticRaidShoutoutConfiguration Configuration)
        : AutomaticRaidShoutoutSaveOutcome;

    public sealed record Invalid(IReadOnlyList<AutomaticRaidShoutoutValidationError> Errors)
        : AutomaticRaidShoutoutSaveOutcome;

    public sealed record HostNotFound : AutomaticRaidShoutoutSaveOutcome;
}

public sealed record AutomaticRaidShoutoutValidationError(
    AutomaticRaidShoutoutValidationField Field,
    string Message
);

public enum AutomaticRaidShoutoutValidationField
{
    MinimumViewerCount,
    Mechanism,
    ChatPresentation,
    MessageTemplate,
    PinDuration,
    AnnouncementColor,
}

public abstract record AutomaticRaidShoutoutPreviewOutcome
{
    private AutomaticRaidShoutoutPreviewOutcome() { }

    public sealed record Rendered(string Message) : AutomaticRaidShoutoutPreviewOutcome;

    public sealed record InvalidTemplate(string Message) : AutomaticRaidShoutoutPreviewOutcome;

    public sealed record TooLong(int ActualCharacters, int MaximumCharacters)
        : AutomaticRaidShoutoutPreviewOutcome;

    public sealed record HostNotFound : AutomaticRaidShoutoutPreviewOutcome;
}

public sealed record AutomaticRaidShoutoutOutcomeView(
    long Id,
    string ProviderMessageId,
    string SourceLogin,
    string SourceDisplayName,
    int ViewerCount,
    AutomaticRaidShoutoutOutcomeStatus Status,
    AutomaticRaidShoutoutResultCode? ResultCode,
    DateTimeOffset MessageTimestamp,
    DateTimeOffset? CompletedAt
);

public sealed record AutomaticRaidShoutoutDeliveryRequest(
    int HostId,
    string HostLogin,
    AutomaticRaidShoutoutConfiguration Configuration,
    string ProviderMessageId,
    DateTimeOffset MessageTimestamp,
    string RaiderTwitchUserId,
    string RaiderLogin,
    string RaiderDisplayName,
    int ViewerCount
);

public interface IAutomaticRaidShoutoutDelivery
{
    Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
        AutomaticRaidShoutoutDeliveryRequest request,
        CancellationToken cancellationToken
    );
}

public abstract record AutomaticRaidShoutoutDeliveryResult
{
    private AutomaticRaidShoutoutDeliveryResult() { }

    public sealed record Delivered : AutomaticRaidShoutoutDeliveryResult;

    public sealed record NotDelivered(AutomaticRaidShoutoutResultCode Reason)
        : AutomaticRaidShoutoutDeliveryResult;

    public sealed record Ambiguous : AutomaticRaidShoutoutDeliveryResult;
}
