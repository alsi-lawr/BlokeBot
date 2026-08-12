using BlokeBot.Persistence.Models;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

public sealed record AutomaticRaidShoutoutConfiguration(
    bool Enabled,
    bool OnlyApprovedChannels,
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
            false,
            1,
            AutomaticRaidShoutoutMechanism.Native,
            AutomaticRaidChatPresentation.Regular,
            AutomaticRaidShoutoutDefaults.MessageTemplate,
            null,
            PersistedAnnouncementColor.Primary
        );

    public static AutomaticRaidShoutoutConfiguration From(AutomaticRaidShoutoutSettings settings) =>
        new(
            settings.Enabled,
            settings.OnlyApprovedChannels,
            settings.MinimumViewerCount,
            settings.Mechanism,
            settings.ChatPresentation,
            settings.MessageTemplate,
            settings.PinDurationSeconds,
            settings.AnnouncementColor
        );

    public IReadOnlyList<AutomaticRaidShoutoutValidationError> Validate()
    {
        var errors = new List<AutomaticRaidShoutoutValidationError>();
        if (MinimumViewerCount < 1)
        {
            Add(
                AutomaticRaidShoutoutValidationField.MinimumViewerCount,
                "Minimum viewers must be at least 1."
            );
        }
        if (!Enum.IsDefined(Mechanism))
        {
            Add(AutomaticRaidShoutoutValidationField.Mechanism, "Choose Native or chat delivery.");
        }
        if (!Enum.IsDefined(ChatPresentation))
        {
            Add(
                AutomaticRaidShoutoutValidationField.ChatPresentation,
                "Choose a supported chat presentation."
            );
        }
        if (!Enum.IsDefined(AnnouncementColor))
        {
            Add(
                AutomaticRaidShoutoutValidationField.AnnouncementColor,
                "Choose a supported Twitch announcement color."
            );
        }
        if (PinDurationSeconds is { } duration && duration is < 30 or > 1800)
        {
            Add(
                AutomaticRaidShoutoutValidationField.PinDuration,
                "Pin duration must be 30 through 1800 seconds, or until stream end."
            );
        }
        if (
            AutomaticRaidShoutoutTemplate.Parse(MessageTemplate)
            is AutomaticRaidTemplateParseOutcome.Invalid invalid
        )
        {
            Add(AutomaticRaidShoutoutValidationField.MessageTemplate, invalid.Message);
        }
        return errors;

        void Add(AutomaticRaidShoutoutValidationField field, string message) =>
            errors.Add(new AutomaticRaidShoutoutValidationError(field, message));
    }
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
