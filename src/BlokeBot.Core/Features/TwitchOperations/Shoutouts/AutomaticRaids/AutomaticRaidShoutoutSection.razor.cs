using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

public partial class AutomaticRaidShoutoutSection : IDisposable
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

    private AutomaticRaidShoutoutConfiguration _draft = AutomaticRaidShoutoutConfiguration.Defaults;
    private IReadOnlyList<AutomaticRaidShoutoutValidationError> _validationErrors = [];
    private IReadOnlyList<AutomaticRaidShoutoutOutcomeView> _outcomes = [];
    private bool _settingsOpen;
    private bool _outcomesOpen;
    private bool _loading = true;
    private bool _saving;
    private int _retainedPinDurationSeconds = 300;
    private bool _previewUsesFallback;
    private int? _authoredCharacters;
    private string? _loadError;
    private string? _previewError;
    private string? _preview;
    private string? _saveStatus;
    private IDisposable? _refreshSubscription;
    private int? _loadedHostId;
    private long _hostVersion;

    [Parameter, EditorRequired]
    public int HostId { get; set; }

    [Parameter, EditorRequired]
    public Func<int, Func<Task>, Task> RunHostMutationAsync { get; set; } =
        static (_, _) =>
            throw new InvalidOperationException("A selected-host mutation guard is required.");

    private string _pinDurationValue =>
        (_draft.PinDurationSeconds ?? _retainedPinDurationSeconds).ToString(
            CultureInfo.InvariantCulture
        );

    private bool _pinUntilStreamEnd => _draft.PinDurationSeconds is null;

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

    private string _settingsSummary =>
        this switch
        {
            { _loading: true } => "Loading…",
            { _loadError: not null } => "Unavailable",
            { _draft.Enabled: false } => "Off · every setting stays saved",
            _ => $"On · raids of {_draft.MinimumViewerCount}+ viewers · {MechanismProse(_draft)}",
        };

    private string _outcomesSummary =>
        this switch
        {
            { _loading: true } => "Loading…",
            { _outcomes.Count: 0 } => "No raids recorded yet",
            _ =>
                $"Last raid: {OutcomeTitle(_outcomes[0])} · @{_outcomes[0].SourceLogin}, {_outcomes[0].ViewerCount} viewers",
        };

    private static string MechanismProse(AutomaticRaidShoutoutConfiguration draft) =>
        draft.Mechanism switch
        {
            AutomaticRaidShoutoutMechanism.Native => "native Twitch shoutout",
            _ => draft.ChatPresentation switch
            {
                AutomaticRaidChatPresentation.Announcement => "announcement in chat",
                AutomaticRaidChatPresentation.Pinned => "pinned chat message",
                _ => "chat message",
            },
        };

    private string _readinessText =>
        _draft.Enabled switch
        {
            false =>
                "Off. Your settings are saved, but incoming raids will not trigger a shoutout.",
            true => _draft.Mechanism switch
            {
                AutomaticRaidShoutoutMechanism.Native =>
                    "Keep the bot account connected to Twitch. If Twitch’s shoutout cooldown is still active, this raid is skipped rather than sent as a chat message.",
                _ => _draft.ChatPresentation switch
                {
                    AutomaticRaidChatPresentation.Announcement =>
                        "Keep public chat connected and allow the bot to send announcements. If the announcement fails, BlokeBot does not send a regular message instead.",
                    AutomaticRaidChatPresentation.Pinned =>
                        "Keep public chat connected and allow the bot to pin messages. The message may appear even if Twitch cannot pin it afterwards.",
                    _ => "Keep public chat connected so BlokeBot can send the message once.",
                },
            },
        };

    protected override Task OnInitializedAsync()
    {
        _refreshSubscription = _events.SubscribeForComponentRefresh(
            [
                AppEventKind.AlertsChanged,
                AppEventKind.HostedChannelsChanged,
                AppEventKind.TwitchOperationsChanged,
            ],
            InvokeAsync,
            RefreshOutcomesAsync,
            StateHasChanged
        );
        return Task.CompletedTask;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedHostId == HostId)
        {
            return;
        }

        _loadedHostId = HostId;
        var version = ++_hostVersion;
        ResetForHost();
        await LoadAsync(HostId, version);
    }

    private void ResetForHost()
    {
        _draft = AutomaticRaidShoutoutConfiguration.Defaults;
        _validationErrors = [];
        _outcomes = [];
        _loading = true;
        _saving = false;
        _retainedPinDurationSeconds = 300;
        _previewUsesFallback = false;
        _authoredCharacters = null;
        _loadError = null;
        _previewError = null;
        _preview = null;
        _saveStatus = null;
    }

    private async Task LoadAsync(int hostId, long version)
    {
        try
        {
            var configuration = await _configuration.LoadAsync(hostId, CancellationToken.None);
            if (!IsCurrentHost(hostId, version))
            {
                return;
            }
            if (configuration is null)
            {
                _loadError = "The selected channel is no longer available.";
                return;
            }

            var outcomes = await _configuration.LoadOutcomesAsync(hostId, CancellationToken.None);
            if (!IsCurrentHost(hostId, version))
            {
                return;
            }

            ApplyConfiguration(configuration);
            _outcomes = outcomes.Take(20).ToArray();
            UpdatePreview();
        }
        catch (Exception)
        {
            if (IsCurrentHost(hostId, version))
            {
                _loadError = "BlokeBot could not load the saved settings and outcomes.";
            }
        }
        finally
        {
            if (IsCurrentHost(hostId, version))
            {
                _loading = false;
            }
        }
    }

    private async Task RefreshOutcomesAsync()
    {
        if (_loadedHostId is not { } hostId)
        {
            return;
        }

        var version = _hostVersion;
        try
        {
            var outcomes = await _configuration.LoadOutcomesAsync(hostId, CancellationToken.None);
            if (IsCurrentHost(hostId, version))
            {
                _outcomes = outcomes.Take(20).ToArray();
            }
        }
        catch (Exception)
        {
            if (IsCurrentHost(hostId, version))
            {
                _loadError = "BlokeBot could not load the saved settings and outcomes.";
            }
        }
    }

    private async Task SaveAsync()
    {
        var hostId = HostId;
        var version = _hostVersion;
        var draft = _draft;
        _saving = true;
        _saveStatus = null;
        _validationErrors = [];
        try
        {
            await RunHostMutationAsync(
                hostId,
                async () =>
                {
                    var outcome = await _configuration.SaveAsync(
                        hostId,
                        draft,
                        CancellationToken.None
                    );
                    if (IsCurrentHost(hostId, version))
                    {
                        ApplySaveOutcome(outcome);
                    }
                }
            );
        }
        catch (Exception)
        {
            if (IsCurrentHost(hostId, version))
            {
                _saveStatus = "Automatic shoutout settings could not be saved.";
            }
        }
        finally
        {
            if (IsCurrentHost(hostId, version))
            {
                _saving = false;
            }
        }
    }

    private void ApplySaveOutcome(AutomaticRaidShoutoutSaveOutcome outcome)
    {
        switch (outcome)
        {
            case AutomaticRaidShoutoutSaveOutcome.Saved saved:
                ApplyConfiguration(saved.Configuration);
                _saveStatus = _draft.Enabled
                    ? "Automatic raid shoutouts saved and enabled."
                    : "Automatic raid shoutouts saved and disabled.";
                UpdatePreview();
                break;
            case AutomaticRaidShoutoutSaveOutcome.Invalid invalid:
                _validationErrors = invalid.Errors;
                _saveStatus = "Settings were not saved.";
                break;
            case AutomaticRaidShoutoutSaveOutcome.HostNotFound:
                _saveStatus =
                    "The selected channel is no longer available. Settings were not saved.";
                break;
        }
    }

    private void ApplyConfiguration(AutomaticRaidShoutoutConfiguration configuration)
    {
        _draft = configuration;
        if (configuration.PinDurationSeconds is { } pinDuration)
        {
            _retainedPinDurationSeconds = pinDuration;
        }
    }

    private bool IsCurrentHost(int hostId, long version) =>
        _loadedHostId == hostId && _hostVersion == version;

    private void ToggleEnabled()
    {
        _draft = _draft with { Enabled = !_draft.Enabled };
        _saveStatus = null;
    }

    private void SetMinimumViewerCount(ChangeEventArgs args)
    {
        _draft = _draft with
        {
            MinimumViewerCount = int.TryParse(
                args.Value?.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value
            )
                ? value
                : 0,
        };
        ClearError(AutomaticRaidShoutoutValidationField.MinimumViewerCount);
    }

    private void SetMechanism(AutomaticRaidShoutoutMechanism mechanism)
    {
        _draft = _draft with { Mechanism = mechanism };
        _saveStatus = null;
        UpdatePreview();
    }

    private void SetPresentation(AutomaticRaidChatPresentation presentation)
    {
        _draft = _draft with { ChatPresentation = presentation };
        _saveStatus = null;
    }

    private void SetPinDuration(ChangeEventArgs args)
    {
        if (
            !int.TryParse(
                args.Value?.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
        {
            value = 0;
        }
        _retainedPinDurationSeconds = value;
        _draft = _draft with { PinDurationSeconds = value };
        ClearError(AutomaticRaidShoutoutValidationField.PinDuration);
    }

    private void TogglePinUntilStreamEnd()
    {
        _draft = _draft with
        {
            PinDurationSeconds = _pinUntilStreamEnd ? _retainedPinDurationSeconds : null,
        };
        ClearError(AutomaticRaidShoutoutValidationField.PinDuration);
    }

    private void SetAnnouncementColor(ChangeEventArgs args)
    {
        if (
            Enum.TryParse<PersistedAnnouncementColor>(
                args.Value?.ToString(),
                ignoreCase: false,
                out var color
            )
        )
        {
            _draft = _draft with { AnnouncementColor = color };
        }
        ClearError(AutomaticRaidShoutoutValidationField.AnnouncementColor);
    }

    private void SetMessageTemplate(ChangeEventArgs args)
    {
        _draft = _draft with { MessageTemplate = args.Value?.ToString() ?? string.Empty };
        ClearError(AutomaticRaidShoutoutValidationField.MessageTemplate);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        _preview = null;
        _previewError = null;
        _previewUsesFallback = false;
        _authoredCharacters = null;
        if (_draft.Mechanism != AutomaticRaidShoutoutMechanism.Chat)
        {
            return;
        }

        switch (AutomaticRaidShoutoutTemplate.Parse(_draft.MessageTemplate))
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
                            _draft.MessageTemplate.Contains("{last_game|", StringComparison.Ordinal)
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
        _validationErrors.FirstOrDefault(error => error.Field == field)?.Message;

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

    private void ClearError(AutomaticRaidShoutoutValidationField field)
    {
        _validationErrors = _validationErrors.Where(error => error.Field != field).ToArray();
        _saveStatus = null;
    }

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
                Badge = _draft.ChatPresentation switch
                {
                    AutomaticRaidChatPresentation.Announcement => "ANNOUNCE",
                    AutomaticRaidChatPresentation.Pinned => "PINNED",
                    _ => "BOT",
                },
                Bot = true,
                Message = _preview ?? string.Empty,
            },
        ];

    private static string OutcomePillClass(AutomaticRaidShoutoutOutcomeView outcome) =>
        outcome.ResultCode == AutomaticRaidShoutoutResultCode.Delivered
            ? "status-pill bg-[var(--app-affirmative-surface)] text-[var(--app-affirmative)]"
            : "status-pill bg-[var(--app-surface-muted)] text-[var(--app-text-muted)] ring-1 ring-[var(--app-border)]";

    private static string OutcomeTitle(AutomaticRaidShoutoutOutcomeView outcome) =>
        outcome.ResultCode switch
        {
            AutomaticRaidShoutoutResultCode.Delivered => "Delivered",
            AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong =>
                "Message exceeded the rendered limit",
            AutomaticRaidShoutoutResultCode.NotReady => "Account was not connected",
            AutomaticRaidShoutoutResultCode.AuthorityRequired => "Reconnect the Twitch account",
            AutomaticRaidShoutoutResultCode.Cooldown => "Skipped during Twitch cooldown",
            AutomaticRaidShoutoutResultCode.Invalid => "Raid was not eligible",
            AutomaticRaidShoutoutResultCode.Rejected => "Twitch did not send the shoutout",
            AutomaticRaidShoutoutResultCode.RateLimited => "Chat was too busy to send",
            AutomaticRaidShoutoutResultCode.PartialFailure => "Message sent, pin failed",
            AutomaticRaidShoutoutResultCode.Unexpected => "Shoutout failed",
            AutomaticRaidShoutoutResultCode.Ambiguous => "Check Twitch for the result",
            _ when outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Processing =>
                "Sending shoutout",
            _ => "Shoutout was not sent",
        };

    private static string OutcomeDescription(AutomaticRaidShoutoutOutcomeView outcome) =>
        outcome.ResultCode switch
        {
            AutomaticRaidShoutoutResultCode.Delivered => "BlokeBot sent the shoutout you selected.",
            AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong =>
                "Live Twitch values pushed the chat message over 500 characters. Nothing was sent.",
            AutomaticRaidShoutoutResultCode.NotReady =>
                "Reconnect the account used by this shoutout. BlokeBot did not switch to another mode.",
            AutomaticRaidShoutoutResultCode.AuthorityRequired =>
                "Reconnect the account shown in Channel setup before the next raid.",
            AutomaticRaidShoutoutResultCode.Cooldown =>
                "Twitch’s native shoutout cooldown was still active, so nothing was sent.",
            AutomaticRaidShoutoutResultCode.Invalid =>
                "The raiding channel or saved shoutout choice could not be used.",
            AutomaticRaidShoutoutResultCode.Rejected =>
                "Twitch did not send this shoutout. BlokeBot did not switch to another mode.",
            AutomaticRaidShoutoutResultCode.RateLimited =>
                "Chat stayed busy until this raid’s send window ended, so nothing was sent.",
            AutomaticRaidShoutoutResultCode.PartialFailure =>
                "The chat message was sent once, but the later pin step failed. BlokeBot will not resend or switch modes.",
            AutomaticRaidShoutoutResultCode.Unexpected =>
                "Open Alerts for the failure details before the next raid.",
            AutomaticRaidShoutoutResultCode.Ambiguous =>
                "BlokeBot cannot safely tell whether Twitch completed the request, so it will not retry.",
            _ => "BlokeBot is waiting for Twitch or chat to finish this shoutout.",
        };

    public void Dispose()
    {
        _refreshSubscription?.Dispose();
        _refreshSubscription = null;
        GC.SuppressFinalize(this);
    }
}
