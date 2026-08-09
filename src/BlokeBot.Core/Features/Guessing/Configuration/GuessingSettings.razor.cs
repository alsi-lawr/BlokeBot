using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public partial class GuessingSettings
{
    private const int _removalAnimationDelayMs = 150;
    private const string _defaultPinDurationSeconds = "300";

    private static readonly ReplySettingsEditor _replyDefaults = GuessingDefaults.Replies();

    private static readonly IReadOnlyList<GuessingAliasField> _aliasFields =
    [
        new(
            GuessCommandKind.Start,
            "Start round",
            "guessing-start-aliases",
            static x => x.StartAliases,
            static (x, value) => x.StartAliases = value
        ),
        new(
            GuessCommandKind.Stop,
            "Stop guessing",
            "guessing-stop-aliases",
            static x => x.StopAliases,
            static (x, value) => x.StopAliases = value
        ),
        new(
            GuessCommandKind.Win,
            "Declare winner",
            "guessing-winner-aliases",
            static x => x.WinAliases,
            static (x, value) => x.WinAliases = value
        ),
        new(
            GuessCommandKind.Guess,
            "Player guess",
            "guessing-guess-aliases",
            static x => x.GuessAliases,
            static (x, value) => x.GuessAliases = value
        ),
        new(
            GuessCommandKind.Guesses,
            "Available guesses",
            "guessing-guesses-aliases",
            static x => x.GuessesAliases,
            static (x, value) => x.GuessesAliases = value
        ),
    ];

    private static readonly IReadOnlyList<GuessingReplyField> _replyFields =
    [
        new(
            "round-started",
            "Round started",
            static x => x.RoundStartedReply,
            static (x, value) => x.RoundStartedReply = value,
            null,
            "Always sent to chat, so everyone sees the round open. {round} and {options} are filled in for you."
        ),
        new(
            "round-already-running",
            "Round already running",
            static x => x.RoundAlreadyOpenReply,
            static (x, value) => x.RoundAlreadyOpenReply = value,
            GuessingReplyKeys.RoundAlreadyOpen,
            null
        ),
        new(
            "no-round-running",
            "No round running",
            static x => x.NoOpenRoundReply,
            static (x, value) => x.NoOpenRoundReply = value,
            GuessingReplyKeys.NoOpenRound,
            null
        ),
        new(
            "guessing-stopped",
            "Guessing stopped",
            static x => x.GuessingStoppedReply,
            static (x, value) => x.GuessingStoppedReply = value,
            null,
            "Always sent to chat."
        ),
        new(
            "guessing-already-stopped",
            "Guessing already stopped",
            static x => x.GuessingAlreadyStoppedReply,
            static (x, value) => x.GuessingAlreadyStoppedReply = value,
            GuessingReplyKeys.GuessingAlreadyStopped,
            null
        ),
        new(
            "guessing-closed",
            "Guessing closed",
            static x => x.GuessingClosedReply,
            static (x, value) => x.GuessingClosedReply = value,
            GuessingReplyKeys.GuessingClosed,
            null
        ),
        new(
            "invalid-guess",
            "Invalid guess",
            static x => x.InvalidGuessReply,
            static (x, value) => x.InvalidGuessReply = value,
            GuessingReplyKeys.InvalidGuess,
            null
        ),
        new(
            "how-to-guess",
            "How to guess",
            static x => x.GuessUsageReply,
            static (x, value) => x.GuessUsageReply = value,
            GuessingReplyKeys.GuessUsage,
            null
        ),
        new(
            "available-guesses",
            "Available guesses",
            static x => x.AvailableGuessesReply,
            static (x, value) => x.AvailableGuessesReply = value,
            GuessingReplyKeys.AvailableGuesses,
            null
        ),
        new(
            "how-to-choose-a-winner",
            "How to choose a winner",
            static x => x.WinUsageReply,
            static (x, value) => x.WinUsageReply = value,
            GuessingReplyKeys.WinUsage,
            null
        ),
        new(
            "only-moderators",
            "Only moderators can use this",
            static x => x.ModeratorOnlyReply,
            static (x, value) => x.ModeratorOnlyReply = value,
            GuessingReplyKeys.ModeratorOnly,
            null
        ),
        new(
            "winner-announced",
            "Winner announced",
            static x => x.WinnerReply,
            static (x, value) => x.WinnerReply = value,
            null,
            "Always sent to chat. {reward_text} appends the point payout when a reward is set."
        ),
        new(
            "no-winners",
            "No winners",
            static x => x.NoWinnersReply,
            static (x, value) => x.NoWinnersReply = value,
            null,
            "Always sent to chat."
        ),
    ];

    private static readonly IReadOnlyList<StudioSegmentedOption<bool>> _deliveryOptions =
    [
        new(false, "Chat"),
        new(true, "Whisper"),
    ];

    private static readonly IReadOnlyList<StudioSegmentedOption<bool>> _pinDurationOptions =
    [
        new(false, "Until the stream ends"),
        new(true, "For a set time"),
    ];

    private readonly Dictionary<GuessCommandKind, string> _aliasDrafts = [];
    private readonly StudioOpenSet<GuessingStage> _openStages = new(GuessingStage.RoundType);
    private readonly StudioOpenSet<string> _openReplies = new();
    private readonly HashSet<GuessOptionEditor> _pendingRemovals = [];

    private GuessingConfiguration? _config;
    private bool _featureEnabled;
    private GuessingConfigurationDraftSnapshot? _loadedDraft;
    private string _newProfileName = string.Empty;
    private int? _pendingProfileId;
    private string _pinDurationDraft = _defaultPinDurationSeconds;
    private bool _pinUsesDuration;

    private enum GuessingStage
    {
        RoundType,
        Answers,
        Commands,
        Replies,
        Pin,
    }

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private Task LoadAsync() => ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);

    private async Task LoadCoreAsync()
    {
        _ = await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Guessing,
                CancellationToken.None
            );
        if (!_featureEnabled)
        {
            _config = null;
            _loadedDraft = null;
            _pendingProfileId = null;
            return;
        }

        _pendingProfileId = null;
        await LoadConfigurationAsync(new GuessingProfileSelection.Default());
    }

    private static string RoundTypeChipId(int profileId) =>
        $"guessing-round-type-{profileId.ToString(CultureInfo.InvariantCulture)}";

    private static string RoundTypeLabel(GuessRoundProfileSummary profile) =>
        profile.IsDefault ? $"{profile.Name} (default)" : profile.Name;

    private string RoundTypeSummary() =>
        _config is null
            ? string.Empty
            : string.Join(
                " · ",
                new[]
                {
                    _config.Profile.Name,
                    _config.Profile.IsDefault ? "default" : null,
                    RewardSummary(_config.Profile.WinningGuessPointReward),
                }.Where(static part => part is { Length: > 0 })
            );

    private static string RewardSummary(string reward) =>
        PointAmount
            .ParseNonNegativeAbsolute(reward)
            .Match(
                static amount =>
                    amount.IsZero ? "no reward" : $"{amount.ToDisplayString()} points to winners",
                static _ => "reward needs checking"
            );

    private string AnswersSummary()
    {
        if (_config is null)
        {
            return string.Empty;
        }

        var count = _config.Profile.Options.Count;
        var answers =
            count == 1 ? "1 answer" : $"{count.ToString(CultureInfo.CurrentCulture)} answers";
        return $"{answers} · replies {(_config.Profile.WhisperAnswerReplies ? "whispered" : "in chat")}";
    }

    private string CommandsSummary()
    {
        if (_config is null)
        {
            return string.Empty;
        }

        var words = _aliasFields.Sum(field => AliasWords(field).Count);
        var counted = words == 1 ? "1 word" : $"{words.ToString(CultureInfo.CurrentCulture)} words";
        return $"{counted} across {_aliasFields.Count.ToString(CultureInfo.CurrentCulture)} commands";
    }

    private string RepliesSummary()
    {
        if (_config is null)
        {
            return string.Empty;
        }

        var whispered = _replyFields.Count(IsWhispered);
        var customised = _replyFields.Count(field => !IsWhispered(field) && IsCustomised(field));
        var stock = _replyFields.Count - whispered - customised;
        return $"{customised.ToString(CultureInfo.CurrentCulture)} customised · {whispered.ToString(CultureInfo.CurrentCulture)} whispered · {stock.ToString(CultureInfo.CurrentCulture)} on defaults";
    }

    private string PinSummary()
    {
        if (_config is null || !_config.Pin.Enabled)
        {
            return "Off";
        }

        var duration = _config.Pin.DurationSeconds is { } seconds
            ? $"Pinned for {DurationProse.Format(seconds)}"
            : "Pinned until the stream ends";
        return _config.Pin.UnpinWhenRoundStops
            ? $"{duration} · removed when the round stops"
            : $"{duration} · left in place";
    }

    private string PinDurationHint() =>
        ParsePinDuration(_pinDurationDraft) is { } seconds
            ? $"= {DurationProse.Format(seconds)} · allowed range 30 s – 30 min"
            : "Allowed range 30 s – 30 min";

    private bool IsCustomised(GuessingReplyField field) =>
        _config is not null
        && !string.Equals(
            field.Read(_config.Profile.Replies),
            field.Read(_replyDefaults),
            StringComparison.Ordinal
        );

    private bool IsWhispered(GuessingReplyField field) =>
        _config is not null && field.DeliveryKey is { } key && _config.ReplyDelivery.IsWhisper(key);

    private void SetReplyWhispered(GuessingReplyField field, bool whisper)
    {
        if (_config is null || !_config.WhisperResponsesEnabled || field.DeliveryKey is not { } key)
        {
            return;
        }

        if (whisper)
        {
            _config.ReplyDelivery.DeliverAsWhisper(key);
        }
        else
        {
            _config.ReplyDelivery.DeliverInChat(key);
        }
    }

    private IReadOnlyList<string> AliasWords(GuessingAliasField field) =>
        _config is null
            ? []
            : CommandAliasNormalizer.SplitPreservingOrder(field.Read(_config.Aliases));

    private string AliasDraft(GuessCommandKind kind) =>
        _aliasDrafts.TryGetValue(kind, out var draft) ? draft : string.Empty;

    private void SetAliasDraft(GuessCommandKind kind, string value) => _aliasDrafts[kind] = value;

    private void AddAliasWord(GuessingAliasField field)
    {
        var word = CommandAliasNormalizer.Normalize(AliasDraft(field.Kind));
        _aliasDrafts[field.Kind] = string.Empty;
        if (_config is null || word.Length == 0)
        {
            return;
        }

        var words = AliasWords(field);
        if (words.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        field.Write(_config.Aliases, string.Join(", ", words.Append(word)));
    }

    private void AddAliasWordOnEnter(GuessingAliasField field, KeyboardEventArgs args)
    {
        if (args.Key is "Enter")
        {
            AddAliasWord(field);
        }
    }

    private void RemoveAliasWord(GuessingAliasField field, string word)
    {
        if (_config is null)
        {
            return;
        }

        field.Write(
            _config.Aliases,
            string.Join(
                ", ",
                AliasWords(field)
                    .Where(candidate => !string.Equals(candidate, word, StringComparison.Ordinal))
            )
        );
    }

    private void SetAnswerWhispers(bool whisper)
    {
        if (_config is null || !_config.WhisperResponsesEnabled)
        {
            return;
        }

        _config.Profile.WhisperAnswerReplies = whisper;
        var target = whisper ? ReplyDeliveryTarget.Whisper : ReplyDeliveryTarget.Chat;
        foreach (var option in _config.Profile.Options)
        {
            option.ReplyTarget = target;
        }
    }

    private void AddOption()
    {
        if (_config is null)
        {
            return;
        }

        _config.Profile.Options.Add(
            new GuessOptionEditor
            {
                ReplyTarget = _config.Profile.WhisperAnswerReplies
                    ? ReplyDeliveryTarget.Whisper
                    : ReplyDeliveryTarget.Chat,
            }
        );
    }

    private string OptionRowClass(GuessOptionEditor option) =>
        _pendingRemovals.Contains(option)
            ? "motion-list__item studio-grid motion-list__item--removing"
            : "motion-list__item studio-grid";

    private async Task RemoveOptionAsync(GuessOptionEditor option)
    {
        if (!_pendingRemovals.Add(option))
        {
            return;
        }

        StateHasChanged();
        try
        {
            await Task.Delay(_removalAnimationDelayMs);
            _ = _config?.Profile.Options.Remove(option);
        }
        finally
        {
            _ = _pendingRemovals.Remove(option);
        }
    }

    private void SetPinUsesDuration(bool usesDuration)
    {
        _pinUsesDuration = usesDuration;
        if (_config is null)
        {
            return;
        }

        _config.Pin.DurationSeconds = usesDuration ? ParsePinDuration(_pinDurationDraft) : null;
    }

    private void SetPinDuration(string value)
    {
        _pinDurationDraft = value;
        if (_config is not null && _pinUsesDuration)
        {
            _config.Pin.DurationSeconds = ParsePinDuration(value);
        }
    }

    private static int? ParsePinDuration(string value) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string FirstAlias(string aliases, string fallback) =>
        CommandAliasNormalizer.SplitPreservingOrder(aliases).FirstOrDefault()
            is { Length: > 0 } first
            ? first
            : fallback;

    private string PreviewOptions() =>
        _config is null
            ? "none"
            : GuessAnswerNames.FormatOptionList(
                _config.Profile.Options.Select(static option =>
                    GuessAnswerNames.Parse(option.Name).Canonical.Value
                )
            );

    private IReadOnlyList<StudioChatLine> AnswerPreviewLines()
    {
        if (_config is null || _config.Profile.Options.Count == 0)
        {
            return [];
        }

        var first = _config.Profile.Options[0];
        var names = GuessAnswerNames.Parse(first.Name);
        var reply = string.IsNullOrWhiteSpace(first.ReplyText)
            ? names.CanonicalDisplayName
            : first.ReplyText;
        return
        [
            new()
            {
                Speaker = "pixel_penny",
                SpeakerColour = "#e91e63",
                Message = $"!{FirstAlias(_config.Aliases.GuessesAliases, "guesses")}",
                Monospace = true,
            },
            BotLine(
                MessageTemplateFormatter.Format(
                    _config.Profile.Replies.AvailableGuessesReply,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["round"] = _config.Profile.Name,
                        ["options"] = PreviewOptions(),
                    }
                )
            ),
            new()
            {
                Speaker = "grumblesworth",
                SpeakerColour = "#1e90ff",
                Message =
                    $"!{FirstAlias(_config.Aliases.GuessAliases, "guess")} {names.Canonical.Value}",
                Monospace = true,
            },
            BotLine(FormatViewerReply(reply, "Grumblesworth", "grumblesworth")),
        ];
    }

    private IReadOnlyList<StudioChatLine> RoundStartedPreviewLines() =>
        _config is null
            ? []
            :
            [
                BotLine(
                    MessageTemplateFormatter.Format(
                        _config.Profile.Replies.RoundStartedReply,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["round"] = _config.Profile.Name,
                            ["options"] = PreviewOptions(),
                        }
                    )
                ),
            ];

    private IReadOnlyList<StudioChatLine> WinnerPreviewLines()
    {
        if (_config is null)
        {
            return [];
        }

        var winning =
            _config
                .Profile.Options.Select(static option =>
                    GuessAnswerNames.Parse(option.Name).CanonicalDisplayName
                )
                .FirstOrDefault(static name => name.Length > 0)
            ?? "the answer";
        var reward = PointAmount
            .ParseNonNegativeAbsolute(_config.Profile.WinningGuessPointReward)
            .Match(static amount => amount, static _ => PointAmount.Zero);
        return
        [
            BotLine(
                MessageTemplateFormatter.Format(
                    _config.Profile.Replies.WinnerReply,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = winning,
                        ["winners"] = "pixel_penny, grumblesworth",
                        ["count"] = "2",
                        ["reward"] = reward.ToDisplayString(),
                        ["label"] = "points",
                        ["reward_text"] = reward.IsZero
                            ? string.Empty
                            : $" Each winner gets {reward.ToDisplayString()} points.",
                    }
                )
            ),
        ];
    }

    private static string FormatViewerReply(string template, string name, string login) =>
        MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["login"] = login,
            }
        );

    private static StudioChatLine BotLine(string message) =>
        new()
        {
            Speaker = "BlokeBot",
            SpeakerColour = "#00ad6f",
            Badge = "BOT",
            Bot = true,
            Message = message,
        };

    private Task CreateProfileAsync() =>
        ObserveUiOperationAsync(nameof(CreateProfileAsync), CreateProfileCoreAsync);

    private async Task CreateProfileCoreAsync() =>
        await GuessingConfigurationValidator
            .ValidateNewProfile(_newProfileName)
            .Match(
                CreateProfileAsync,
                errors =>
                {
                    _ = _toasts.Publish(
                        new ToastRequest<WarningToastStrategy>(ValidationMessage(errors))
                    );
                    return Task.CompletedTask;
                }
            );

    private async Task CreateProfileAsync(GuessingProfileCreateCommand command) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var selectedId = _config?.Profile.Id;
                var result = await _configuration
                    .CreateProfile(HostId, command)
                    .ExecuteAsync(CancellationToken.None);
                await result.Match(
                    async created =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>(created.Message)
                        );
                        _newProfileName = string.Empty;
                        await LoadConfigurationAsync(
                            selectedId is { } id
                                ? new GuessingProfileSelection.Selected(id)
                                : new GuessingProfileSelection.Default()
                        );
                    },
                    failure =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(failure.Message)
                        );
                        return Task.CompletedTask;
                    }
                );
            }
        );

    private Task DeleteProfileAsync() =>
        ObserveUiOperationAsync(nameof(DeleteProfileAsync), DeleteProfileCoreAsync);

    private Task DeleteProfileCoreAsync() =>
        _config is null
            ? Task.CompletedTask
            : GuessingConfigurationValidator
                .ValidateDelete(_config)
                .Match(
                    DeleteProfileAsync,
                    errors =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(ValidationMessage(errors))
                        );
                        return Task.CompletedTask;
                    }
                );

    private async Task DeleteProfileAsync(GuessingProfileDeleteCommand command) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _configuration
                    .DeleteProfile(HostId, command)
                    .ExecuteAsync(CancellationToken.None);
                await result.Match(
                    async deleted =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>(deleted.Message)
                        );
                        await LoadConfigurationAsync(new GuessingProfileSelection.Default());
                    },
                    async failure =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(failure.Message)
                        );
                        if (
                            failure
                            is GuessingProfileDeleteFailure.ProfileNotFound
                                or GuessingProfileDeleteFailure.ConcurrentEdit
                        )
                        {
                            await LoadConfigurationAsync(new GuessingProfileSelection.Default());
                        }
                    }
                );
            }
        );

    private Task SaveAsync() => ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);

    private async Task SaveCoreAsync() =>
        _ = await TrySaveAsync(reloadAfterConcurrentFailure: true);

    private async Task<bool> TrySaveAsync(bool reloadAfterConcurrentFailure) =>
        _config switch
        {
            null => false,
            { } config => await GuessingConfigurationValidator
                .Validate(config)
                .Match(
                    command => SaveConfigurationAsync(command, reloadAfterConcurrentFailure),
                    errors =>
                    {
                        _ = _toasts.Publish(
                            new ToastRequest<ErrorToastStrategy>(ValidationMessage(errors))
                        );
                        return Task.FromResult(false);
                    }
                ),
        };

    private async Task<bool> SaveConfigurationAsync(
        GuessingConfigurationSaveCommand command,
        bool reloadAfterConcurrentFailure
    )
    {
        var saved = false;
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _configuration
                    .SaveConfiguration(HostId, command)
                    .ExecuteAsync(CancellationToken.None);
                saved = await result.Match(
                    async completed =>
                    {
                        await LoadConfigurationAsync(
                            new GuessingProfileSelection.Selected(command.ProfileId)
                        );
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>("Guessing settings saved.")
                        );
                        return true;
                    },
                    async failure =>
                    {
                        _ = _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
                        if (
                            reloadAfterConcurrentFailure
                            && failure
                                is GuessingConfigurationSaveFailure.ProfileNotFound
                                    or GuessingConfigurationSaveFailure.ConcurrentEdit
                        )
                        {
                            await LoadConfigurationAsync(
                                new GuessingProfileSelection.Selected(command.ProfileId)
                            );
                        }

                        return false;
                    }
                );
            }
        );
        return saved;
    }

    private Task SelectProfileAsync(int profileId) =>
        ObserveUiOperationAsync(
            nameof(SelectProfileAsync),
            () => SelectProfileCoreAsync(profileId)
        );

    private async Task SelectProfileCoreAsync(int profileId)
    {
        if (_config?.Profile.Id == profileId)
        {
            return;
        }

        if (HasUnsavedChanges())
        {
            _pendingProfileId = profileId;
            return;
        }

        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private Task SaveAndSwitchAsync() =>
        ObserveUiOperationAsync(nameof(SaveAndSwitchAsync), SaveAndSwitchCoreAsync);

    private async Task SaveAndSwitchCoreAsync()
    {
        if (_pendingProfileId is not { } profileId)
        {
            return;
        }

        if (!await TrySaveAsync(reloadAfterConcurrentFailure: false))
        {
            return;
        }

        _pendingProfileId = null;
        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private Task DiscardAndSwitchAsync() =>
        ObserveUiOperationAsync(nameof(DiscardAndSwitchAsync), DiscardAndSwitchCoreAsync);

    private async Task DiscardAndSwitchCoreAsync()
    {
        if (_pendingProfileId is not { } profileId)
        {
            return;
        }

        _pendingProfileId = null;
        await LoadConfigurationAsync(new GuessingProfileSelection.Selected(profileId));
    }

    private void KeepEditing() => _pendingProfileId = null;

    private bool HasUnsavedChanges() =>
        _config is not null && _loadedDraft is not null && !_loadedDraft.Matches(_config);

    private async Task LoadConfigurationAsync(GuessingProfileSelection selection)
    {
        var result = await _configuration
            .LoadConfiguration(HostId, selection)
            .ExecuteAsync(CancellationToken.None);
        await result.Match(
            draft =>
            {
                Adopt(draft);
                return Task.CompletedTask;
            },
            async failure =>
            {
                _ = _toasts.Publish(new ToastRequest<WarningToastStrategy>(failure.Message));
                var fallback = await _configuration
                    .LoadConfiguration(HostId, new GuessingProfileSelection.Default())
                    .ExecuteAsync(CancellationToken.None);
                _ = fallback.Match(
                    draft =>
                    {
                        Adopt(draft);
                        return true;
                    },
                    fallbackFailure =>
                    {
                        _config = null;
                        _loadedDraft = null;
                        _ = _toasts.Publish(
                            new ToastRequest<ErrorToastStrategy>(fallbackFailure.Message)
                        );
                        return false;
                    }
                );
            }
        );
    }

    private void Adopt(GuessingConfiguration draft)
    {
        _config = draft;
        _loadedDraft = GuessingConfigurationDraftSnapshot.Capture(draft);
        _aliasDrafts.Clear();
        _pinUsesDuration = draft.Pin.DurationSeconds is not null;
        _pinDurationDraft =
            draft.Pin.DurationSeconds?.ToString(CultureInfo.InvariantCulture)
            ?? _defaultPinDurationSeconds;
    }

    private static string ValidationMessage(
        IReadOnlyList<GuessingConfigurationValidationError> errors
    ) => string.Join(" ", errors.Select(static error => error.Message));

    private sealed record GuessingAliasField(
        GuessCommandKind Kind,
        string Label,
        string FieldId,
        Func<CommandAliasEditor, string> Read,
        Action<CommandAliasEditor, string> Write
    );

    private sealed record GuessingReplyField(
        string Key,
        string Label,
        Func<ReplySettingsEditor, string> Read,
        Action<ReplySettingsEditor, string> Write,
        string? DeliveryKey,
        string? ChatOnlyNote
    );
}
