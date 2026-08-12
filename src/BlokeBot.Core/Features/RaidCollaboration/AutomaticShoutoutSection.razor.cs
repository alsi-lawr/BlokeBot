using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Core.Features.RaidCollaboration;

public partial class AutomaticShoutoutSection
{
    private static readonly PersistedAnnouncementColor[] _announcementColors =
    [
        PersistedAnnouncementColor.Primary,
        PersistedAnnouncementColor.Blue,
        PersistedAnnouncementColor.Green,
        PersistedAnnouncementColor.Orange,
        PersistedAnnouncementColor.Purple,
    ];

    private static readonly AutomaticRaidTemplateValues _previewValues = new(
        "@samplechannel",
        "Sample Channel",
        "https://twitch.tv/samplechannel",
        42,
        null,
        "A late-night challenge"
    );

    private static readonly StudioSegmentedOption<AutomaticRaidChatPresentation>[] _presentationOptions =
    [
        new(
            AutomaticRaidChatPresentation.Regular,
            "Regular",
            "automatic-raid-presentation-regular"
        ),
        new(AutomaticRaidChatPresentation.Pinned, "Pinned", "automatic-raid-presentation-pinned"),
        new(
            AutomaticRaidChatPresentation.Announcement,
            "Announcement",
            "automatic-raid-presentation-announcement"
        ),
    ];

    private int _retainedPinDurationSeconds = 300;
    private bool _previewUsesFallback;
    private int? _authoredCharacters;
    private string? _previewError;
    private string? _preview;

    [Parameter, EditorRequired]
    public AutomaticRaidShoutoutConfiguration Value { get; set; } =
        AutomaticRaidShoutoutConfiguration.Defaults;

    [Parameter]
    public EventCallback<AutomaticRaidShoutoutConfiguration> ValueChanged { get; set; }

    [Parameter]
    public IReadOnlyList<AutomaticRaidShoutoutValidationError> Errors { get; set; } = [];

    [Parameter]
    public EventCallback<AutomaticRaidShoutoutValidationField> ErrorCleared { get; set; }

    private string _pinDurationValue =>
        (Value.PinDurationSeconds ?? _retainedPinDurationSeconds).ToString(
            CultureInfo.InvariantCulture
        );

    private bool _pinUntilStreamEnd => Value.PinDurationSeconds is null;

    internal static string AnnouncementColorLabel(PersistedAnnouncementColor color) =>
        color switch
        {
            PersistedAnnouncementColor.Primary => "Default",
            PersistedAnnouncementColor.Blue => "Blue",
            PersistedAnnouncementColor.Green => "Green",
            PersistedAnnouncementColor.Orange => "Orange",
            PersistedAnnouncementColor.Purple => "Purple",
            _ => throw new UnreachableException("Unknown Twitch announcement color."),
        };

    private string _readinessText =>
        Value.Enabled switch
        {
            false =>
                "Off. Your settings are saved, but incoming raids will not trigger a shoutout.",
            true => Value.Mechanism switch
            {
                AutomaticRaidShoutoutMechanism.Native =>
                    "Keep the bot account connected to Twitch. If Twitch’s shoutout cooldown is still active, this raid is skipped rather than sent as a chat message.",
                _ => Value.ChatPresentation switch
                {
                    AutomaticRaidChatPresentation.Announcement =>
                        "Keep public chat connected and allow the bot to send announcements. If the announcement fails, BlokeBot does not send a regular message instead.",
                    AutomaticRaidChatPresentation.Pinned =>
                        "Keep public chat connected and allow the bot to pin messages. The message may appear even if Twitch cannot pin it afterwards.",
                    _ => "Keep public chat connected so BlokeBot can send the message once.",
                },
            },
        };

    protected override void OnParametersSet()
    {
        if (Value.PinDurationSeconds is { } pinDuration)
        {
            _retainedPinDurationSeconds = pinDuration;
        }
        UpdatePreview();
    }

    private Task Update(AutomaticRaidShoutoutConfiguration value) =>
        ValueChanged.InvokeAsync(value);

    private async Task UpdateAsync(
        AutomaticRaidShoutoutConfiguration value,
        AutomaticRaidShoutoutValidationField clearedField
    )
    {
        await ValueChanged.InvokeAsync(value);
        await ErrorCleared.InvokeAsync(clearedField);
    }

    private Task SetMinimumViewerCount(ChangeEventArgs args) =>
        UpdateAsync(
            Value with
            {
                MinimumViewerCount = ParseInteger(args),
            },
            AutomaticRaidShoutoutValidationField.MinimumViewerCount
        );

    private Task SetPinDuration(ChangeEventArgs args)
    {
        _retainedPinDurationSeconds = ParseInteger(args);
        return UpdateAsync(
            Value with
            {
                PinDurationSeconds = _retainedPinDurationSeconds,
            },
            AutomaticRaidShoutoutValidationField.PinDuration
        );
    }

    private Task TogglePinUntilStreamEnd() =>
        UpdateAsync(
            Value with
            {
                PinDurationSeconds = _pinUntilStreamEnd ? _retainedPinDurationSeconds : null,
            },
            AutomaticRaidShoutoutValidationField.PinDuration
        );

    private Task SetAnnouncementColor(ChangeEventArgs args) =>
        UpdateAsync(
            Enum.TryParse<PersistedAnnouncementColor>(
                args.Value?.ToString(),
                ignoreCase: false,
                out var color
            )
                ? Value with
                {
                    AnnouncementColor = color,
                }
                : Value,
            AutomaticRaidShoutoutValidationField.AnnouncementColor
        );

    private Task SetMessageTemplate(ChangeEventArgs args) =>
        UpdateAsync(
            Value with
            {
                MessageTemplate = args.Value?.ToString() ?? string.Empty,
            },
            AutomaticRaidShoutoutValidationField.MessageTemplate
        );

    private static int ParseInteger(ChangeEventArgs args) =>
        int.TryParse(
            args.Value?.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : 0;

    private void UpdatePreview()
    {
        _preview = null;
        _previewError = null;
        _previewUsesFallback = false;
        _authoredCharacters = null;
        if (Value.Mechanism != AutomaticRaidShoutoutMechanism.Chat)
        {
            return;
        }

        switch (AutomaticRaidShoutoutTemplate.Parse(Value.MessageTemplate))
        {
            case AutomaticRaidTemplateParseOutcome.Invalid invalid:
                _previewError = invalid.Message;
                break;
            case AutomaticRaidTemplateParseOutcome.Valid valid:
                _authoredCharacters = valid.Template.AuthoredCharacters;
                switch (valid.Template.Render(_previewValues))
                {
                    case AutomaticRaidTemplateRenderOutcome.Rendered rendered:
                        _preview = rendered.Message;
                        _previewUsesFallback =
                            Value.MessageTemplate.Contains("{last_game|", StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(rendered.Message);
                        break;
                    case AutomaticRaidTemplateRenderOutcome.TooLong tooLong:
                        _previewError =
                            $"Preview is {tooLong.ActualCharacters} characters; the rendered limit is {tooLong.MaximumCharacters}.";
                        break;
                }
                break;
        }
    }

    private string? ErrorFor(AutomaticRaidShoutoutValidationField field) =>
        Errors.FirstOrDefault(error => error.Field == field)?.Message;

    private string FieldDescription(AutomaticRaidShoutoutValidationField field) =>
        ErrorFor(field) is null
            ? string.Empty
            : field switch
            {
                AutomaticRaidShoutoutValidationField.MinimumViewerCount =>
                    "automatic-raid-minimum-viewers-error",
                AutomaticRaidShoutoutValidationField.PinDuration =>
                    "automatic-raid-pin-duration-error",
                AutomaticRaidShoutoutValidationField.AnnouncementColor =>
                    "automatic-raid-announcement-color-error",
                _ => string.Empty,
            };

    private string HasError(AutomaticRaidShoutoutValidationField field) =>
        ErrorFor(field) is null ? "false" : "true";

    private IReadOnlyList<StudioChatLine> PreviewLines() =>
        [
            new()
            {
                Message =
                    $"{_previewValues.DisplayName} is raiding with a party of {_previewValues.ViewerCount}",
            },
            new()
            {
                Speaker = "pixel_penny",
                SpeakerColour = "#e91e63",
                Message = "RAID HYPE",
            },
            new()
            {
                Speaker = "BlokeBot",
                SpeakerColour = "#00ad6f",
                Badge = Value.ChatPresentation switch
                {
                    AutomaticRaidChatPresentation.Announcement => "ANNOUNCE",
                    AutomaticRaidChatPresentation.Pinned => "PINNED",
                    _ => "BOT",
                },
                Bot = true,
                Message = _preview ?? string.Empty,
            },
        ];
}
