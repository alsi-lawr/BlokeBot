using System.Globalization;
using System.Numerics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.Points.Configuration;

public partial class PointsConfigurationPage
{
    private static readonly PointsReplySettingsEditor _replyDefaults = PointsDefaults.Replies(
        PointsDefaults.Settings()
    );

    private static readonly IReadOnlyList<PointsAliasField> _aliasFields =
    [
        new(
            PointsCommandKind.Points,
            "Balance",
            "points-aliases",
            static x => x.PointsAliases,
            static (x, value) => x.PointsAliases = value
        ),
        new(
            PointsCommandKind.GivePoints,
            "Give",
            "points-give-aliases",
            static x => x.GivePointsAliases,
            static (x, value) => x.GivePointsAliases = value
        ),
        new(
            PointsCommandKind.AddPoints,
            "Add",
            "points-add-aliases",
            static x => x.AddPointsAliases,
            static (x, value) => x.AddPointsAliases = value
        ),
        new(
            PointsCommandKind.RemovePoints,
            "Remove",
            "points-remove-aliases",
            static x => x.RemovePointsAliases,
            static (x, value) => x.RemovePointsAliases = value
        ),
        new(
            PointsCommandKind.Gamble,
            "Gamble",
            "points-gamble-aliases",
            static x => x.GambleAliases,
            static (x, value) => x.GambleAliases = value
        ),
        new(
            PointsCommandKind.Giveaway,
            "Start giveaway",
            "points-giveaway-aliases",
            static x => x.GiveawayAliases,
            static (x, value) => x.GiveawayAliases = value
        ),
        new(
            PointsCommandKind.Join,
            "Join giveaway",
            "points-join-giveaway-aliases",
            static x => x.JoinAliases,
            static (x, value) => x.JoinAliases = value
        ),
        new(
            PointsCommandKind.EndGiveaway,
            "End giveaway",
            "points-end-giveaway-aliases",
            static x => x.EndGiveawayAliases,
            static (x, value) => x.EndGiveawayAliases = value
        ),
        new(
            PointsCommandKind.CancelGiveaway,
            "Cancel giveaway",
            "points-cancel-giveaway-aliases",
            static x => x.CancelGiveawayAliases,
            static (x, value) => x.CancelGiveawayAliases = value
        ),
    ];

    private static readonly IReadOnlyList<PointsReplyGroup> _replyGroups =
    [
        new(
            "Balances & moving points",
            [
                new(
                    "balance",
                    "Balance",
                    static x => x.BalanceReply,
                    static (x, value) => x.BalanceReply = value,
                    PointsReplyKeys.Balance,
                    null
                ),
                new(
                    "other-balance",
                    "Another viewer's balance",
                    static x => x.OtherBalanceReply,
                    static (x, value) => x.OtherBalanceReply = value,
                    PointsReplyKeys.OtherBalance,
                    null
                ),
                new(
                    "transfer",
                    "Points given",
                    static x => x.TransferReply,
                    static (x, value) => x.TransferReply = value,
                    PointsReplyKeys.Transfer,
                    null
                ),
                new(
                    "add",
                    "Moderator adds points",
                    static x => x.AddReply,
                    static (x, value) => x.AddReply = value,
                    PointsReplyKeys.Add,
                    null
                ),
                new(
                    "remove",
                    "Moderator removes points",
                    static x => x.RemoveReply,
                    static (x, value) => x.RemoveReply = value,
                    PointsReplyKeys.Remove,
                    null
                ),
                new(
                    "invalid-amount",
                    "Amount not understood",
                    static x => x.InvalidAmountReply,
                    static (x, value) => x.InvalidAmountReply = value,
                    PointsReplyKeys.InvalidAmount,
                    null
                ),
                new(
                    "insufficient-balance",
                    "Not enough points",
                    static x => x.InsufficientBalanceReply,
                    static (x, value) => x.InsufficientBalanceReply = value,
                    PointsReplyKeys.InsufficientBalance,
                    null
                ),
                new(
                    "moderator-only",
                    "Only moderators can use this",
                    static x => x.ModeratorOnlyReply,
                    static (x, value) => x.ModeratorOnlyReply = value,
                    PointsReplyKeys.ModeratorOnly,
                    null
                ),
            ]
        ),
        new(
            "Gambling",
            [
                new(
                    "gamble-win",
                    "Gamble win",
                    static x => x.GamblingWinReply,
                    static (x, value) => x.GamblingWinReply = value,
                    null,
                    "Always sent to chat, so wins stay public."
                ),
                new(
                    "gamble-loss",
                    "Gamble loss",
                    static x => x.GamblingLoseReply,
                    static (x, value) => x.GamblingLoseReply = value,
                    null,
                    "Always sent to chat."
                ),
            ]
        ),
        new(
            "Giveaways",
            [
                new(
                    "giveaway-started",
                    "Giveaway started",
                    static x => x.GiveawayStartedReply,
                    static (x, value) => x.GiveawayStartedReply = value,
                    null,
                    "Always sent to chat, so everyone sees the giveaway open."
                ),
                new(
                    "giveaway-status",
                    "Giveaway status",
                    static x => x.GiveawayUpdateReply,
                    static (x, value) => x.GiveawayUpdateReply = value,
                    null,
                    "Always sent to chat."
                ),
                new(
                    "giveaway-joined",
                    "Giveaway joined",
                    static x => x.GiveawayJoinedReply,
                    static (x, value) => x.GiveawayJoinedReply = value,
                    PointsReplyKeys.GiveawayJoined,
                    null
                ),
                new(
                    "giveaway-already-joined",
                    "Already joined",
                    static x => x.GiveawayAlreadyJoinedReply,
                    static (x, value) => x.GiveawayAlreadyJoinedReply = value,
                    PointsReplyKeys.GiveawayAlreadyJoined,
                    null
                ),
                new(
                    "giveaway-ended",
                    "Giveaway ended",
                    static x => x.GiveawayEndedReply,
                    static (x, value) => x.GiveawayEndedReply = value,
                    null,
                    "Always sent to chat."
                ),
                new(
                    "giveaway-no-entrants",
                    "No one joined",
                    static x => x.GiveawayNoEntrantsReply,
                    static (x, value) => x.GiveawayNoEntrantsReply = value,
                    null,
                    "Always sent to chat."
                ),
                new(
                    "giveaway-cancelled",
                    "Cancelled",
                    static x => x.GiveawayCancelledReply,
                    static (x, value) => x.GiveawayCancelledReply = value,
                    null,
                    "Always sent to chat."
                ),
                new(
                    "giveaway-already-active",
                    "Giveaway already running",
                    static x => x.GiveawayAlreadyActiveReply,
                    static (x, value) => x.GiveawayAlreadyActiveReply = value,
                    PointsReplyKeys.GiveawayAlreadyActive,
                    null
                ),
                new(
                    "giveaway-not-active",
                    "No giveaway running",
                    static x => x.GiveawayNotActiveReply,
                    static (x, value) => x.GiveawayNotActiveReply = value,
                    PointsReplyKeys.GiveawayNotActive,
                    null
                ),
                new(
                    "giveaway-cooldown",
                    "Giveaway used too recently",
                    static x => x.GiveawayCooldownReply,
                    static (x, value) => x.GiveawayCooldownReply = value,
                    PointsReplyKeys.GiveawayCooldown,
                    null
                ),
                new(
                    "stream-offline",
                    "Stream is offline",
                    static x => x.StreamOfflineReply,
                    static (x, value) => x.StreamOfflineReply = value,
                    PointsReplyKeys.StreamOffline,
                    null
                ),
                new(
                    "not-eligible",
                    "Viewer cannot enter",
                    static x => x.NotEligibleReply,
                    static (x, value) => x.NotEligibleReply = value,
                    PointsReplyKeys.NotEligible,
                    null
                ),
                new(
                    "follower-unavailable",
                    "Follower check unavailable",
                    static x => x.FollowerEligibilityUnavailableReply,
                    static (x, value) => x.FollowerEligibilityUnavailableReply = value,
                    PointsReplyKeys.FollowerEligibilityUnavailable,
                    null
                ),
            ]
        ),
    ];

    private static readonly IReadOnlyList<PointsReplyField> _replyFields =
    [
        .. _replyGroups.SelectMany(static group => group.Fields),
    ];

    private static readonly IReadOnlyList<StudioSegmentedOption<bool>> _deliveryOptions =
    [
        new(false, "Chat"),
        new(true, "Whisper"),
    ];

    private readonly Dictionary<PointsCommandKind, string> _aliasDrafts = [];
    private readonly StudioOpenSet<PointsStage> _openStages = new(PointsStage.Label);
    private readonly StudioOpenSet<string> _openReplies = new();

    private PointsConfiguration? _config;
    private bool _featureEnabled;
    private IReadOnlyList<PointsConfigurationValidationError> _validationErrors = [];
    private long _gamblingFocusRequest;
    private long _giveawaysFocusRequest;
    private string _validationFocusId = "gamblingCooldown";

    private enum PointsStage
    {
        Label,
        Gambling,
        Giveaways,
        Commands,
        Replies,
    }

    private PointsConfigurationValidationError.NegativeGamblingCooldown? _gamblingCooldownError =>
        _validationErrors
            .OfType<PointsConfigurationValidationError.NegativeGamblingCooldown>()
            .FirstOrDefault();

    private PointsConfigurationValidationError.GiveawayDurationBelowMinimum? _giveawayDurationError =>
        _validationErrors
            .OfType<PointsConfigurationValidationError.GiveawayDurationBelowMinimum>()
            .FirstOrDefault();

    private PointsConfigurationValidationError.GiveawayWinnerCountBelowMinimum? _giveawayWinnerCountError =>
        _validationErrors
            .OfType<PointsConfigurationValidationError.GiveawayWinnerCountBelowMinimum>()
            .FirstOrDefault();

    private PointsConfigurationValidationError.GiveawayCooldownBelowMinimum? _giveawayCooldownError =>
        _validationErrors
            .OfType<PointsConfigurationValidationError.GiveawayCooldownBelowMinimum>()
            .FirstOrDefault();

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
                HostFeatureFlags.Points,
                CancellationToken.None
            );
        _config = _featureEnabled
            ? await _configuration.LoadConfigurationAsync(HostId, CancellationToken.None)
            : null;
        _validationErrors = [];
        _aliasDrafts.Clear();
    }

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static BigInteger? Bound(string value) =>
        BigInteger.TryParse(
            value.Trim(),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;

    /// <summary>
    /// Keeps an unreadable entry out of the persisted draft while leaving it on screen, which is
    /// what the number inputs these steppers replace did on a failed parse.
    /// </summary>
    private static void SetWhole(string value, Action<int> write)
    {
        if (
            int.TryParse(
                value.Trim(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            write(parsed);
        }
    }

    private void SetWinRate(string value) =>
        SetWhole(value, parsed => _config!.GamblingWinRatePercent = parsed);

    private void SetGamblingCooldown(string value) =>
        SetWhole(value, parsed => _config!.GamblingCooldownSeconds = parsed);

    private void SetGiveawayDuration(string value) =>
        SetWhole(value, parsed => _config!.GiveawayDurationSeconds = parsed);

    private void SetGiveawayWinnerCount(string value) =>
        SetWhole(value, parsed => _config!.GiveawayWinnerCount = parsed);

    private void SetGiveawayCooldown(string value) =>
        SetWhole(value, parsed => _config!.GiveawayCooldownSeconds = parsed);

    private string PointLabel() =>
        _config is null
            ? "points"
            : PointsConfigurationValidator.NormalizePointLabel(_config.PointLabel);

    private string LabelSummary() => $"Viewers earn \"{PointLabel()}\"";

    private string GamblingSummary()
    {
        if (_config is null)
        {
            return string.Empty;
        }

        var chance =
            $"{_config.GamblingWinRatePercent.ToString(CultureInfo.CurrentCulture)}% win chance";
        return $"{chance} · {GamblingCooldownSummary(_config.GamblingCooldownSeconds)}";
    }

    private static string GamblingCooldownSummary(int seconds) =>
        seconds <= 0
            ? "no wait between gambles"
            : $"{DurationProse.Format(seconds)} between gambles";

    private string GamblingCooldownHint() =>
        _config is null || _config.GamblingCooldownSeconds <= 0
            ? "0 = no wait"
            : $"= {DurationProse.Format(_config.GamblingCooldownSeconds)}";

    private string GiveawaysSummary()
    {
        if (_config is null)
        {
            return string.Empty;
        }

        var winners =
            _config.GiveawayWinnerCount == 1
                ? "1 winner"
                : $"{_config.GiveawayWinnerCount.ToString(CultureInfo.CurrentCulture)} winners";
        return string.Join(
            " · ",
            $"{DurationProse.Format(_config.GiveawayDurationSeconds)} to enter",
            $"{_config.GiveawayMinimumPayout}–{_config.GiveawayMaximumPayout} prize",
            winners,
            EligibilitySummary(_config.GiveawayEligibility)
        );
    }

    private static string EligibilitySummary(PointsEligibilityMode mode) =>
        mode switch
        {
            PointsEligibilityMode.Subscribers => "subscribers only",
            PointsEligibilityMode.Followers => "followers only",
            _ => "everyone can enter",
        };

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

    private bool IsCustomised(PointsReplyField field) =>
        _config is not null
        && !string.Equals(
            field.Read(_config.Replies),
            field.Read(_replyDefaults),
            StringComparison.Ordinal
        );

    private bool IsWhispered(PointsReplyField field) =>
        _config is not null && field.DeliveryKey is { } key && _config.ReplyDelivery.IsWhisper(key);

    private void SetReplyWhispered(PointsReplyField field, bool whisper)
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

    private IReadOnlyList<string> AliasWords(PointsAliasField field) =>
        _config is null
            ? []
            : CommandAliasNormalizer.SplitPreservingOrder(field.Read(_config.Aliases));

    private string AliasDraft(PointsCommandKind kind) =>
        _aliasDrafts.TryGetValue(kind, out var draft) ? draft : string.Empty;

    private void SetAliasDraft(PointsCommandKind kind, string value) => _aliasDrafts[kind] = value;

    private void AddAliasWord(PointsAliasField field)
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

    private void AddAliasWordOnEnter(PointsAliasField field, KeyboardEventArgs args)
    {
        if (args.Key is "Enter")
        {
            AddAliasWord(field);
        }
    }

    private void RemoveAliasWord(PointsAliasField field, string word)
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

    private static string FirstAlias(string aliases, string fallback) =>
        CommandAliasNormalizer.SplitPreservingOrder(aliases).FirstOrDefault()
            is { Length: > 0 } first
            ? first
            : fallback;

    private string FormatReply(string template, params (string Token, string Value)[] values)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["label"] = PointLabel(),
        };
        foreach (var (token, value) in values)
        {
            tokens[token] = value;
        }

        return MessageTemplateFormatter.Format(template, tokens);
    }

    private IReadOnlyList<StudioChatLine> BalancePreviewLines() =>
        _config is null
            ? []
            :
            [
                ViewerLine(
                    "pixel_penny",
                    "#e91e63",
                    $"!{FirstAlias(_config.Aliases.PointsAliases, "points")}"
                ),
                BotLine(
                    FormatReply(
                        _config.Replies.BalanceReply,
                        ("user", "pixel_penny"),
                        ("balance", "12,480")
                    )
                ),
            ];

    private IReadOnlyList<StudioChatLine> GamblePreviewLines() =>
        _config is null
            ? []
            :
            [
                ViewerLine(
                    "grumblesworth",
                    "#1e90ff",
                    $"!{FirstAlias(_config.Aliases.GambleAliases, "gamble")} 100"
                ),
                BotLine(
                    FormatReply(
                        _config.Replies.GamblingWinReply,
                        ("user", "grumblesworth"),
                        ("amount", "100"),
                        ("balance", "9,250")
                    )
                ),
            ];

    private IReadOnlyList<StudioChatLine> GiveawayStartedPreviewLines() =>
        _config is null
            ? []
            :
            [
                BotLine(FormatReply(_config.Replies.GiveawayStartedReply)),
                ViewerLine(
                    "grumblesworth",
                    "#1e90ff",
                    $"!{FirstAlias(_config.Aliases.JoinAliases, "join")}"
                ),
                BotLine(
                    FormatReply(_config.Replies.GiveawayJoinedReply, ("user", "grumblesworth"))
                ),
            ];

    private static StudioChatLine ViewerLine(string speaker, string colour, string message) =>
        new()
        {
            Speaker = speaker,
            SpeakerColour = colour,
            Message = message,
            Monospace = true,
        };

    private static StudioChatLine BotLine(string message) =>
        new()
        {
            Speaker = "BlokeBot",
            SpeakerColour = "#00ad6f",
            Badge = "BOT",
            Bot = true,
            Message = message,
        };

    private Task SaveAsync() => ObserveUiOperationAsync(nameof(SaveAsync), SaveCoreAsync);

    private async Task SaveCoreAsync()
    {
        if (_config is null || HostId == 0)
        {
            return;
        }

        await PointsConfigurationValidator
            .Validate(_config)
            .Match(
                command =>
                {
                    _validationErrors = [];
                    return SaveCommandAsync(command);
                },
                errors =>
                {
                    _validationErrors = errors.ToArray();
                    RevealFirstError(_validationErrors[0]);
                    _ = _toasts.Publish(
                        new ToastRequest<ErrorToastStrategy>(
                            string.Join(" ", errors.Select(error => error.Message))
                        )
                    );
                    return Task.CompletedTask;
                }
            );
    }

    private void RevealFirstError(PointsConfigurationValidationError error)
    {
        switch (error)
        {
            case PointsConfigurationValidationError.NegativeGamblingCooldown:
                Reveal(PointsStage.Gambling, "gamblingCooldown");
                break;
            case PointsConfigurationValidationError.GiveawayDurationBelowMinimum:
                Reveal(PointsStage.Giveaways, "duration");
                break;
            case PointsConfigurationValidationError.GiveawayWinnerCountBelowMinimum:
                Reveal(PointsStage.Giveaways, "winnerCount");
                break;
            case PointsConfigurationValidationError.GiveawayCooldownBelowMinimum:
                Reveal(PointsStage.Giveaways, "cooldown");
                break;
        }
    }

    private void Reveal(PointsStage stage, string focusId)
    {
        _validationFocusId = focusId;
        _openStages.Open(stage);
        if (stage is PointsStage.Gambling)
        {
            _gamblingFocusRequest++;
        }
        else
        {
            _giveawaysFocusRequest++;
        }
    }

    private async Task SaveCommandAsync(PointsConfigurationSaveCommand command) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _configuration
                    .SaveConfiguration(HostId, command)
                    .ExecuteAsync(CancellationToken.None);
                await result.Match(
                    async completed =>
                    {
                        _config = await _configuration.LoadConfigurationAsync(
                            HostId,
                            CancellationToken.None
                        );
                        _validationErrors = [];
                        _ = _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>("Points settings saved.")
                        );
                    },
                    failure =>
                    {
                        _ = _toasts.Publish(new ToastRequest<ErrorToastStrategy>(failure.Message));
                        return Task.CompletedTask;
                    }
                );
            }
        );

    private sealed record PointsAliasField(
        PointsCommandKind Kind,
        string Label,
        string FieldId,
        Func<PointsCommandAliasEditor, string> Read,
        Action<PointsCommandAliasEditor, string> Write
    );

    private sealed record PointsReplyField(
        string Key,
        string Label,
        Func<PointsReplySettingsEditor, string> Read,
        Action<PointsReplySettingsEditor, string> Write,
        string? DeliveryKey,
        string? ChatOnlyNote
    );

    private sealed record PointsReplyGroup(string Label, IReadOnlyList<PointsReplyField> Fields);
}
