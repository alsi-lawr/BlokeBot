using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using static BlokeBot.Core.Features.Automations.AutomationConfigurationJson;

namespace BlokeBot.Core.Features.Automations;

public static partial class AutomationDefinitionIds
{
    public static AutomationDefinitionId AdTimingSource { get; } = new("ad-timing");
    public static AutomationDefinitionId CountdownSource { get; } = new("countdown-lifecycle");
    public static AutomationDefinitionId StartCountdownAction { get; } = new("start-countdown");
    public static AutomationDefinitionId ResetCountdownAction { get; } = new("reset-countdown");
    public static AutomationDefinitionId CancelCountdownAction { get; } = new("cancel-countdown");
    public static AutomationDefinitionId ScheduledTimeSource { get; } = new("scheduled-time");
    public static AutomationDefinitionId UptimeSource { get; } = new("stream-uptime");
    public static AutomationDefinitionId MetadataSource { get; } = new("channel-metadata-changed");
    public static AutomationDefinitionId ChatMatchSource { get; } = new("chat-match");
    public static AutomationDefinitionId GiveawayLifecycleSource { get; } =
        new("giveaway-lifecycle");
    public static AutomationDefinitionId GuessingLifecycleSource { get; } =
        new("guessing-lifecycle");
    public static AutomationDefinitionId QueueLifecycleSource { get; } = new("queue-lifecycle");
    public static AutomationDefinitionId CueLifecycleSource { get; } = new("cue-lifecycle");
    public static AutomationDefinitionId ManualSource { get; } = new("manual-run");
    public static AutomationDefinitionId OutgoingRaidSource { get; } = new("outgoing-raid");
    public static AutomationDefinitionId GoalSource { get; } = new("twitch-goal");
    public static AutomationDefinitionId RedemptionUpdateSource { get; } =
        new("redemption-updated");
    public static AutomationDefinitionId ChatSettingsSource { get; } = new("chat-settings-changed");
    public static AutomationDefinitionId ModerationSource { get; } = new("moderation-action");
}

internal sealed class ExpandedAutomationCatalogModule : IAutomationCatalogModule
{
    public AutomationModuleId Id { get; } = new("blokebot.expanded-sources");
    public IEnumerable<IAutomationDefinition> Definitions { get; } =
    [
        Source<AdTimingSourceConfiguration>(
            AutomationDefinitionIds.AdTimingSource,
            "Ad timing",
            "Observed ad start or derived scheduled/remaining/expected-end timing. Requires channel:read:ads; derived end is not playback confirmation.",
            [Choice<AdTimingKind>("event"), Seconds("offset-seconds", "Offset", 0)],
            [
                TextPort("timing-kind"),
                TimePort("deadline"),
                NumberPort("remaining-seconds"),
                BoolPort("derived"),
            ],
            j =>
                ChoiceValue<AdTimingKind>(j, "event", out var e)
                && SecondsValue(j, "offset-seconds", 0, out var d)
                    ? Parsed(new AdTimingSourceConfiguration(e, d))
                    : Invalid("event", "Select ad timing and a non-negative offset.")
        ),
        Source<CountdownSourceConfiguration>(
            AutomationDefinitionIds.CountdownSource,
            "Named countdown",
            "Started, reset, cancelled, finished or a future remaining-time crossing. Restart quietly cancels countdowns.",
            [
                NameField(),
                Choice<CountdownLifecycle>("event"),
                Seconds("remaining-seconds", "Remaining threshold", 0),
            ],
            [
                TextPort("timer-name"),
                TextPort("timer-occurrence"),
                TextPort("event-kind"),
                TimePort("deadline"),
                NumberPort("remaining-seconds"),
            ],
            j =>
                NameValue(j, out var n)
                && ChoiceValue<CountdownLifecycle>(j, "event", out var e)
                && SecondsValue(j, "remaining-seconds", 0, out var d)
                    ? Parsed(new CountdownSourceConfiguration(n, e, d))
                    : Invalid(
                        "name",
                        "Enter a timer name, lifecycle event and remaining-time threshold."
                    )
        ),
        CountdownAction(
            AutomationDefinitionIds.StartCountdownAction,
            "Start countdown",
            CountdownOperation.Start
        ),
        CountdownAction(
            AutomationDefinitionIds.ResetCountdownAction,
            "Reset countdown",
            CountdownOperation.Reset
        ),
        CountdownAction(
            AutomationDefinitionIds.CancelCountdownAction,
            "Cancel countdown",
            CountdownOperation.Cancel
        ),
        Source<ScheduledTimeSourceConfiguration>(
            AutomationDefinitionIds.ScheduledTimeSource,
            "Scheduled time",
            "One-off, fixed interval or weekly local time. Missing DST times and missed runs are skipped; repeated local times use the first instant. Live-only requires confirmed live status.",
            [
                Choice<ScheduledTimeKind>("kind"),
                new(
                    new("zone"),
                    "Time zone",
                    "Named time zone, for example Europe/London or UTC; execution resolves this local time to UTC.",
                    new AutomationConfigurationFieldType.Text(128),
                    true
                ),
                new(
                    new("local-time"),
                    "Local date and time",
                    "yyyy-MM-ddTHH:mm:ss in the selected zone; date anchors interval cadence, time is used weekly.",
                    new AutomationConfigurationFieldType.Text(32),
                    true
                ),
                Choice<DayOfWeek>("day"),
                Seconds("interval-seconds", "Interval", 1),
                new(
                    new("live-only"),
                    "Only while streaming",
                    "Offline or unknown occurrences are skipped, never caught up.",
                    new AutomationConfigurationFieldType.Choice(["false", "true"]),
                    true
                ),
            ],
            [TimePort("scheduled-at"), TextPort("schedule-zone")],
            ParseSchedule
        ),
        Source<UptimeSourceConfiguration>(
            AutomationDefinitionIds.UptimeSource,
            "Stream uptime",
            "Actual stream identity and start time, not process age. An overdue threshold fires once on discovery; missed interval ticks are skipped.",
            [Choice<UptimeKind>("kind"), Seconds("duration-seconds", "Uptime", 1)],
            [TextPort("stream-id", true), NumberPort("uptime-seconds")],
            j =>
                ChoiceValue<UptimeKind>(j, "kind", out var k)
                && SecondsValue(j, "duration-seconds", 1, out var d)
                    ? Parsed(new UptimeSourceConfiguration(k, d))
                    : Invalid("duration-seconds", "Select threshold/interval and positive uptime.")
        ),
        Source<MetadataSourceConfiguration>(
            AutomationDefinitionIds.MetadataSource,
            "Title or category changed",
            "Observed changes, after an initial baseline; unrelated updates and older deliveries do not trigger.",
            [Choice<MetadataField>("field"), OptionalText("category-id", "Category ID")],
            [
                TextPort("changed-field"),
                TextPort("title"),
                TextPort("category-id"),
                TextPort("category-name"),
            ],
            j =>
                ChoiceValue<MetadataField>(j, "field", out var f)
                    ? Parsed(new MetadataSourceConfiguration(f, Optional(j, "category-id")))
                    : Invalid("field", "Select the changed field.")
        ),
        Source<ChatMatchSourceConfiguration>(
            AutomationDefinitionIds.ChatMatchSource,
            "Chat match",
            "Keywords: case-insensitive whole words. Phrases: case-insensitive literal text. Emotes: provider ID. First observed: this stream only; remembers viewer IDs across restart. Bot messages are excluded.",
            [
                Choice<ChatMatchKind>("kind"),
                new(
                    new("match"),
                    "Text or emote ID",
                    "Leave empty only for FirstObserved.",
                    new AutomationConfigurationFieldType.Text(500),
                    false
                ),
            ],
            [
                ActorPort(),
                TextPort("message-text", true),
                TextPort("message-id", true),
                TextPort("stream-id", true),
            ],
            j =>
                ChoiceValue<ChatMatchKind>(j, "kind", out var k)
                && (
                    k == ChatMatchKind.FirstObserved
                    || !string.IsNullOrWhiteSpace(Optional(j, "match"))
                )
                    ? Parsed(new ChatMatchSourceConfiguration(k, Optional(j, "match") ?? ""))
                    : Invalid("match", "Enter a word, phrase or Twitch emote ID.")
        ),
        FeatureSource(
            AutomationDefinitionIds.GiveawayLifecycleSource,
            "Giveaway lifecycle",
            [
                FeatureLifecycleKind.GiveawayOpened,
                FeatureLifecycleKind.GiveawayClosed,
                FeatureLifecycleKind.GiveawayWinners,
            ]
        ),
        FeatureSource(
            AutomationDefinitionIds.GuessingLifecycleSource,
            "Guessing lifecycle",
            [FeatureLifecycleKind.GuessingStarted, FeatureLifecycleKind.GuessingFinished]
        ),
        FeatureSource(
            AutomationDefinitionIds.QueueLifecycleSource,
            "Viewer queue lifecycle",
            [FeatureLifecycleKind.QueueJoined, FeatureLifecycleKind.QueueCalled]
        ),
        Source<CueLifecycleSourceConfiguration>(
            AutomationDefinitionIds.CueLifecycleSource,
            "Overlay cue lifecycle",
            "Actual cue authority: Queued is admission only; Started is server-started, Finished is derived or browser-reported, Interrupted is cancellation or unavailable target. Never browser confirmation.",
            [
                Choice<CueLifecycleKind>("event"),
                OptionalText("cue-id", "Cue ID"),
                OptionalText("target-id", "Cue player ID"),
            ],
            [
                TextPort("event-kind"),
                TextPort("cue-id"),
                TextPort("cue-run-id"),
                TextPort("target-id"),
                TextPort("playback-outcome"),
            ],
            j =>
                ChoiceValue<CueLifecycleKind>(j, "event", out var e)
                    ? Parsed(
                        new CueLifecycleSourceConfiguration(
                            e,
                            Optional(j, "cue-id"),
                            Optional(j, "target-id")
                        )
                    )
                    : Invalid("event", "Select a cue lifecycle event.")
        ),
        Source<ManualSourceConfiguration>(
            AutomationDefinitionIds.ManualSource,
            "Manual Run",
            "The authorized Run button executes this saved enabled flow with the explicit data below, not a scenario or a simulated Twitch event.",
            [
                new(
                    new("data"),
                    "Trigger data",
                    "Explicit Text supplied to real manual runs.",
                    new AutomationConfigurationFieldType.Text(500, true),
                    true
                ),
            ],
            [TextPort("manual-data")],
            j =>
                TryReadString(j, "data", out var d)
                    ? Parsed(new ManualSourceConfiguration(d))
                    : Invalid("data", "Supply the manual trigger data.")
        ),
        Source<OutgoingRaidSourceConfiguration>(
            AutomationDefinitionIds.OutgoingRaidSource,
            "Outgoing raid",
            "The channel raided another channel, distinct from incoming raids. Requires Raid & collaboration.",
            [],
            [ActorPort(), NumberPort("viewer-count")],
            _ => Parsed(new OutgoingRaidSourceConfiguration())
        ),
        Source<GoalSourceConfiguration>(
            AutomationDefinitionIds.GoalSource,
            "Twitch goal",
            "Started, progressed, ended or observed milestone crossing. Initial/recovered snapshots establish baselines, not catch-up. Each milestone fires once per goal identity.",
            [
                Choice<GoalLifecycleKind>("event"),
                new(
                    new("milestone"),
                    "Milestone count",
                    "Absolute count crossed upwards, once per goal.",
                    new AutomationConfigurationFieldType.Number(1, long.MaxValue),
                    true
                ),
            ],
            [
                TextPort("goal-id"),
                TextPort("goal-type"),
                TextPort("event-kind"),
                NumberPort("current-amount"),
                NumberPort("target-amount"),
                BoolPort("achieved"),
            ],
            j =>
                ChoiceValue<GoalLifecycleKind>(j, "event", out var e)
                && TryReadInt64(j, "milestone", out var m)
                && m > 0
                    ? Parsed(new GoalSourceConfiguration(e, m))
                    : Invalid("milestone", "Choose a lifecycle event and positive milestone count.")
        ),
        Source<RedemptionUpdateSourceConfiguration>(
            AutomationDefinitionIds.RedemptionUpdateSource,
            "Redemption updated",
            "Fulfilled or cancelled, not redemption received. Uses the Rewards & redemptions owner.",
            [Choice<RedemptionUpdateKind>("event"), OptionalText("reward-id", "Reward ID")],
            [
                ActorPort(),
                TextPort("redemption-id", true),
                TextPort("reward-id", true),
                TextPort("status"),
            ],
            j =>
                ChoiceValue<RedemptionUpdateKind>(j, "event", out var e)
                    ? Parsed(new RedemptionUpdateSourceConfiguration(e, Optional(j, "reward-id")))
                    : Invalid("event", "Choose fulfilled or cancelled.")
        ),
        Source<ChatSettingsSourceConfiguration>(
            AutomationDefinitionIds.ChatSettingsSource,
            "Chat settings changed",
            "Twitch chat settings notification; uses the configured chat account's read grants.",
            [],
            [
                BoolPort("slow-mode"),
                NumberPort("slow-seconds"),
                BoolPort("subscriber-mode"),
                BoolPort("emote-mode"),
                BoolPort("follower-mode"),
                NumberPort("follower-minutes"),
                BoolPort("unique-chat"),
            ],
            _ => Parsed(new ChatSettingsSourceConfiguration())
        ),
        Source<ModerationSourceConfiguration>(
            AutomationDefinitionIds.ModerationSource,
            "Moderation action",
            "Full channel.moderate v2 observation. Requires read blocked terms, chat settings, unban requests, banned users, chat messages, warnings, moderators and VIPs. Private reasons, message bodies and terms are never exposed.",
            [
                new(
                    new("action"),
                    "Action",
                    "Any or the exact Twitch moderation action.",
                    new AutomationConfigurationFieldType.Choice([
                        "any",
                        .. EventSubModerationActions.All,
                    ]),
                    true
                ),
            ],
            [
                ActorPort(),
                TextPort("moderation-action"),
                TextPort("source-channel-id", true),
                TextPort("source-channel-login"),
                TextPort("subject-login"),
                TextPort("subject-name"),
                NumberPort("duration-seconds"),
                NumberPort("affected-count"),
            ],
            j =>
                TryReadString(j, "action", out var a)
                && (a == "any" || EventSubModerationActions.All.Contains(a))
                    ? Parsed(new ModerationSourceConfiguration(a))
                    : Invalid("action", "Select an observed moderation action.")
        ),
    ];

    private static IAutomationDefinition FeatureSource(
        AutomationDefinitionId id,
        string name,
        ImmutableArray<FeatureLifecycleKind> kinds
    ) =>
        Source<FeatureLifecycleSourceConfiguration>(
            id,
            name,
            "Committed feature outcomes; existing configuration/history and public privacy policy remain authoritative.",
            [
                new(
                    new("event"),
                    "Event",
                    "Committed transition.",
                    new AutomationConfigurationFieldType.Choice([
                        .. kinds.Select(k => k.ToString()),
                    ]),
                    true
                ),
            ],
            [TextPort("event-kind"), TextPort("feature-id"), TextPort("public-data"), ActorPort()],
            j =>
                ChoiceValue<FeatureLifecycleKind>(j, "event", out var e) && kinds.Contains(e)
                    ? Parsed(new FeatureLifecycleSourceConfiguration(e))
                    : Invalid("event", "Choose a lifecycle event for this feature.")
        );

    private static IAutomationDefinition Source<T>(
        AutomationDefinitionId id,
        string name,
        string description,
        ImmutableArray<AutomationConfigurationFieldMetadata> fields,
        ImmutableArray<AutomationPortMetadata> outputs,
        Func<JsonElement, AutomationConfigurationParseResult> parse
    )
        where T : AutomationConfiguration =>
        new AutomationDefinition<T>(
            new(
                id,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                new(new(1), new(1)),
                new(name, description, "Timing & activity"),
                [],
                [
                    new(new("flow"), "Flow", "Starts the flow.", AutomationPortValueType.Flow),
                    new(
                        new("channel"),
                        "Channel",
                        "Owning channel.",
                        AutomationPortValueType.Channel
                    ),
                    new(
                        new("event-time"),
                        "Event time",
                        "Observed or scheduled UTC instant.",
                        AutomationPortValueType.Timestamp,
                        AutomationDataSensitivity.Sensitive
                    ),
                    .. outputs,
                ],
                fields,
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            parse,
            ValidateTyped
        );

    private static IAutomationDefinition CountdownAction(
        AutomationDefinitionId id,
        string name,
        CountdownOperation operation
    ) =>
        new AutomationDefinition<CountdownActionConfiguration>(
            new(
                id,
                AutomationNodeKind.Action,
                AutomationDefinitionScope.Host,
                new(new(1), new(1)),
                new(
                    name,
                    operation == CountdownOperation.Start
                            ? "Starts a stopped timer; a running timer is unchanged."
                        : operation == CountdownOperation.Reset
                            ? "Starts a fresh occurrence and emits Reset, not Started."
                        : "Cancels an active countdown and emits Cancelled.",
                    "Timing"
                ),
                [new(new("flow"), "Flow", "Runs the action.", AutomationPortValueType.Flow)],
                [
                    new(
                        new("complete"),
                        "Complete",
                        "Continues the flow.",
                        AutomationPortValueType.Flow
                    ),
                ],
                [
                    NameField(),
                    .. operation == CountdownOperation.Cancel
                        ? Array.Empty<AutomationConfigurationFieldMetadata>()
                        : [Seconds("duration-seconds", "Duration", 1)],
                ],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.Unsafe
            ),
            j =>
                NameValue(j, out var n)
                && (
                    operation == CountdownOperation.Cancel
                    || SecondsValue(j, "duration-seconds", 1, out _)
                )
                    ? Parsed(
                        new CountdownActionConfiguration(
                            n,
                            operation == CountdownOperation.Cancel
                                ? TimeSpan.Zero
                                : TimeSpan.FromSeconds(
                                    j.GetProperty("duration-seconds").GetInt64()
                                ),
                            operation
                        )
                    )
                    : Invalid("name", "Enter a timer name and positive duration."),
            c =>
                c.Operation == operation
                    ? ValidateTyped(c)
                    : AutomationValidationResult.Invalid(
                        new AutomationValidationTarget.Definition(),
                        "Countdown operation does not match the action."
                    )
        );

    private static AutomationValidationResult ValidateTyped(AutomationConfiguration configuration)
    {
        var valid = configuration switch
        {
            AdTimingSourceConfiguration s => Enum.IsDefined(s.Event)
                && BoundedDuration(s.Offset, 0),
            CountdownSourceConfiguration s => ValidName(s.Name)
                && Enum.IsDefined(s.Event)
                && BoundedDuration(s.Remaining, 0),
            CountdownActionConfiguration a => ValidName(a.Name)
                && Enum.IsDefined(a.Operation)
                && (
                    a.Operation == CountdownOperation.Cancel
                        ? a.Duration == TimeSpan.Zero
                        : BoundedDuration(a.Duration, 1)
                ),
            ScheduledTimeSourceConfiguration s => Enum.IsDefined(s.Kind)
                && TimeZoneInfo.TryFindSystemTimeZoneById(s.Zone, out _)
                && Enum.IsDefined(s.Day)
                && s.LocalTime.Kind == DateTimeKind.Unspecified
                && BoundedDuration(s.Interval, 1),
            UptimeSourceConfiguration s => Enum.IsDefined(s.Kind) && BoundedDuration(s.Duration, 1),
            MetadataSourceConfiguration s => Enum.IsDefined(s.Field)
                && (s.CategoryId is null || s.CategoryId.Length <= 128),
            ChatMatchSourceConfiguration s => Enum.IsDefined(s.Kind)
                && s.Match.Length <= 500
                && (s.Kind == ChatMatchKind.FirstObserved || !string.IsNullOrWhiteSpace(s.Match)),
            FeatureLifecycleSourceConfiguration s => Enum.IsDefined(s.Event),
            CueLifecycleSourceConfiguration s => Enum.IsDefined(s.Event)
                && (s.CueId is null || Guid.TryParse(s.CueId, out _))
                && (s.TargetId is null || Guid.TryParse(s.TargetId, out _)),
            ManualSourceConfiguration s => s.Data.Length <= 500,
            GoalSourceConfiguration s => Enum.IsDefined(s.Event) && s.Milestone > 0,
            RedemptionUpdateSourceConfiguration s => Enum.IsDefined(s.Event)
                && (s.RewardId is null || s.RewardId.Length <= 128),
            ModerationSourceConfiguration s => s.Action == "any"
                || EventSubModerationActions.All.Contains(s.Action),
            OutgoingRaidSourceConfiguration or ChatSettingsSourceConfiguration => true,
            _ => false,
        };
        return valid
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Definition(),
                "Invalid timing or activity configuration."
            );
    }

    private static bool ValidName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 96 && value == value.Trim();

    private static bool BoundedDuration(TimeSpan value, int minimum) =>
        value >= TimeSpan.FromSeconds(minimum)
        && value <= TimeSpan.FromDays(365)
        && value.Ticks % TimeSpan.TicksPerSecond == 0;

    private static AutomationConfigurationParseResult ParseSchedule(JsonElement j) =>
        ChoiceValue<ScheduledTimeKind>(j, "kind", out var k)
        && TryReadString(j, "zone", out var z)
        && TimeZoneInfo.TryFindSystemTimeZoneById(z, out _)
        && TryReadString(j, "local-time", out var t)
        && DateTime.TryParseExact(
            t,
            ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var local
        )
        && ChoiceValue<DayOfWeek>(j, "day", out var day)
        && SecondsValue(j, "interval-seconds", 1, out var d)
        && TryReadString(j, "live-only", out var live)
        && bool.TryParse(live, out var only)
            ? Parsed(
                new ScheduledTimeSourceConfiguration(
                    k,
                    z,
                    DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                    day,
                    d,
                    only
                )
            )
            : Invalid(
                "local-time",
                "Choose a named zone, local date/time, day, positive interval and live-only policy. DST gaps are skipped; the first repeated time is used."
            );

    private static AutomationPortMetadata TextPort(string id, bool sensitive = false) =>
        new(
            new(id),
            id.Replace('-', ' '),
            "Trigger data.",
            AutomationPortValueType.Text,
            sensitive ? AutomationDataSensitivity.Sensitive : AutomationDataSensitivity.Safe
        );

    private static AutomationPortMetadata TimePort(string id) =>
        new(
            new(id),
            id.Replace('-', ' '),
            "UTC execution instant.",
            AutomationPortValueType.Timestamp
        );

    private static AutomationPortMetadata NumberPort(string id) =>
        new(new(id), id.Replace('-', ' '), "Trigger amount.", AutomationPortValueType.Number);

    private static AutomationPortMetadata BoolPort(string id) =>
        new(new(id), id.Replace('-', ' '), "Observed state.", AutomationPortValueType.Boolean);

    private static AutomationPortMetadata ActorPort() =>
        new(
            new("actor"),
            "Actor",
            "Public actor identity, when present.",
            AutomationPortValueType.Actor,
            Nullability: AutomationPortNullability.Nullable
        );

    private static AutomationConfigurationFieldMetadata NameField() =>
        new(
            new("name"),
            "Timer name",
            "Host-local named countdown (maximum 96 characters).",
            new AutomationConfigurationFieldType.Text(96),
            true
        );

    private static AutomationConfigurationFieldMetadata Seconds(string id, string label, int min) =>
        new(
            new(id),
            label,
            "Seconds.",
            new AutomationConfigurationFieldType.Number(min, 365 * 86400),
            true
        );

    private static AutomationConfigurationFieldMetadata Choice<T>(string id)
        where T : struct, Enum =>
        new(
            new(id),
            id.Replace('-', ' '),
            "Select the trigger policy.",
            new AutomationConfigurationFieldType.Choice([.. Enum.GetNames<T>()]),
            true
        );

    private static AutomationConfigurationFieldMetadata OptionalText(string id, string label) =>
        new(
            new(id),
            label,
            "Leave empty to match all.",
            new AutomationConfigurationFieldType.Text(128),
            false
        );

    private static string? Optional(JsonElement j, string id) =>
        TryReadString(j, id, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static bool ChoiceValue<T>(JsonElement j, string id, out T value)
        where T : struct, Enum
    {
        value = default;
        return TryReadString(j, id, out var s)
            && Enum.TryParse(s, out value)
            && Enum.IsDefined(value);
    }

    private static bool NameValue(JsonElement j, out string name)
    {
        name = Optional(j, "name") ?? "";
        return name.Length is > 0 and <= 96;
    }

    private static bool SecondsValue(JsonElement j, string id, int minimum, out TimeSpan duration)
    {
        duration = default;
        if (!TryReadInt64(j, id, out var s) || s < minimum || s > 365 * 86400)
        {
            return false;
        }
        duration = TimeSpan.FromSeconds(s);
        return true;
    }
}
