using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Eventing;
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

    private AutomaticRaidShoutoutConfiguration _draft = AutomaticRaidShoutoutConfiguration.Defaults;
    private IReadOnlyList<AutomaticRaidShoutoutValidationError> _validationErrors = [];
    private IReadOnlyList<AutomaticRaidShoutoutOutcomeView> _outcomes = [];
    private bool _loading = true;
    private bool _saving;
    private int _retainedPinDurationSeconds = 300;
    private bool _previewUsesFallback;
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

    private string _readinessText =>
        !_draft.Enabled
            ? "Disabled. Saved settings are retained, but incoming raids will not trigger a shoutout."
        : _draft.Mechanism == AutomaticRaidShoutoutMechanism.Native
            ? "Native delivery is ready when Twitch is connected, the bot has shoutout authority, and Twitch’s shoutout cooldown permits delivery. BlokeBot never falls back to chat."
        : _draft.ChatPresentation == AutomaticRaidChatPresentation.Announcement
            ? "Announcement delivery is ready when public chat and Twitch announcement authority are available. A failed announcement is not retried as another mode."
        : _draft.ChatPresentation == AutomaticRaidChatPresentation.Pinned
            ? "Pinned delivery is ready when public chat and pin authority are available. The chat message may be sent even if the later pin step fails."
        : "Regular chat delivery is ready when the public chat connection can accept one message.";

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

    private bool IsCurrentHost(int hostId, long version)
    {
        return _loadedHostId == hostId && _hostVersion == version;
    }

    private void SetEnabled(ChangeEventArgs args)
    {
        _draft = _draft with { Enabled = args.Value is true };
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

    private void SetPinUntilStreamEnd(ChangeEventArgs args)
    {
        _draft = _draft with
        {
            PinDurationSeconds = args.Value is true ? null : _retainedPinDurationSeconds,
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

    private string? ErrorFor(AutomaticRaidShoutoutValidationField field)
    {
        return _validationErrors.FirstOrDefault(error => error.Field == field)?.Message;
    }

    private string FieldDescription(AutomaticRaidShoutoutValidationField field)
    {
        return ErrorFor(field) is null
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
    }

    private string HasError(AutomaticRaidShoutoutValidationField field)
    {
        return ErrorFor(field) is null ? "false" : "true";
    }

    private void ClearError(AutomaticRaidShoutoutValidationField field)
    {
        _validationErrors = _validationErrors.Where(error => error.Field != field).ToArray();
        _saveStatus = null;
    }

    private static string PresentationLabel(AutomaticRaidChatPresentation presentation)
    {
        return presentation switch
        {
            AutomaticRaidChatPresentation.Regular => "Regular message",
            AutomaticRaidChatPresentation.Pinned => "Pinned message",
            AutomaticRaidChatPresentation.Announcement => "Announcement",
            _ => throw new ArgumentOutOfRangeException(nameof(presentation)),
        };
    }

    private static string OutcomeTitle(AutomaticRaidShoutoutOutcomeView outcome)
    {
        return outcome.ResultCode switch
        {
            AutomaticRaidShoutoutResultCode.Delivered => "Delivered",
            AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong =>
                "Message exceeded the rendered limit",
            AutomaticRaidShoutoutResultCode.NotReady => "Delivery was not ready",
            AutomaticRaidShoutoutResultCode.AuthorityRequired => "Twitch authority required",
            AutomaticRaidShoutoutResultCode.Cooldown => "Native shoutout cooldown active",
            AutomaticRaidShoutoutResultCode.Invalid => "Raid was not eligible",
            AutomaticRaidShoutoutResultCode.Rejected => "Twitch rejected delivery",
            AutomaticRaidShoutoutResultCode.RateLimited => "Chat delivery was rate limited",
            AutomaticRaidShoutoutResultCode.PartialFailure => "Message sent, pin failed",
            AutomaticRaidShoutoutResultCode.Unexpected => "Delivery failed unexpectedly",
            AutomaticRaidShoutoutResultCode.Ambiguous => "Delivery outcome is uncertain",
            _ when outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Processing =>
                "Delivery in progress",
            _ => "Delivery did not complete",
        };
    }

    private static string OutcomeDescription(AutomaticRaidShoutoutOutcomeView outcome)
    {
        return outcome.ResultCode switch
        {
            AutomaticRaidShoutoutResultCode.Delivered =>
                "BlokeBot completed the selected shoutout mechanism.",
            AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong =>
                "Live Twitch values pushed the chat message over 500 characters. Nothing was sent.",
            AutomaticRaidShoutoutResultCode.NotReady =>
                "The selected delivery connection was unavailable. No fallback mode was attempted.",
            AutomaticRaidShoutoutResultCode.AuthorityRequired =>
                "Reconnect the relevant Twitch account with the required authority before a future raid.",
            AutomaticRaidShoutoutResultCode.Cooldown =>
                "Twitch’s native shoutout cooldown prevented this delivery.",
            AutomaticRaidShoutoutResultCode.Invalid =>
                "The raid target or configured delivery choice could not be used.",
            AutomaticRaidShoutoutResultCode.Rejected =>
                "The selected delivery mechanism rejected this shoutout. No fallback mode was attempted.",
            AutomaticRaidShoutoutResultCode.RateLimited =>
                "The selected chat message was not admitted before its delivery deadline.",
            AutomaticRaidShoutoutResultCode.PartialFailure =>
                "The chat message was sent once, but the later pin step failed. BlokeBot will not resend or switch modes.",
            AutomaticRaidShoutoutResultCode.Unexpected =>
                "The selected delivery mechanism failed. Review Alerts for operational details.",
            AutomaticRaidShoutoutResultCode.Ambiguous =>
                "BlokeBot cannot safely tell whether Twitch completed the request, so it will not retry.",
            _ => "BlokeBot is waiting for the selected delivery mechanism to finish.",
        };
    }

    public void Dispose()
    {
        _refreshSubscription?.Dispose();
        _refreshSubscription = null;
        GC.SuppressFinalize(this);
    }
}
