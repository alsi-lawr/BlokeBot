using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

public abstract record OverlayConfiguration
{
    private const int _maximumJsonBytes = 8192;
    private static readonly JsonSerializerOptions _persistenceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly JsonSerializerOptions _strictJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private OverlayConfiguration() { }

    public abstract OverlayType Type { get; }

    public abstract int SchemaVersion { get; }

    public static OverlayConfigurationParseResult Parse(OverlayType type, string json)
    {
        if (!Enum.IsDefined(type))
        {
            return new OverlayConfigurationParseResult.Invalid(
                "The overlay type is not supported."
            );
        }

        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > _maximumJsonBytes)
        {
            return new OverlayConfigurationParseResult.Invalid(
                "The overlay configuration must be from 1 to 8192 UTF-8 bytes."
            );
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                }
            );
            return document.RootElement.ValueKind != JsonValueKind.Object
                ? new OverlayConfigurationParseResult.Invalid(
                    "The overlay configuration must be a JSON object."
                )
                : type switch
                {
                    OverlayType.Empty => ParseEmpty(document.RootElement),
                    OverlayType.Guessing => ParseGuessing(document.RootElement),
                    OverlayType.CuePlayer => ParseCuePlayer(document.RootElement),
                    OverlayType.Giveaway => ParseGiveaway(document.RootElement),
                    OverlayType.EventFeed => ParseEventFeed(document.RootElement),
                    OverlayType.ViewerQueue => ParseViewerQueue(document.RootElement),
                    _ => new OverlayConfigurationParseResult.Invalid(
                        "The overlay type is not supported."
                    ),
                };
        }
        catch (JsonException)
        {
            return new OverlayConfigurationParseResult.Invalid(
                "The overlay configuration is not valid JSON."
            );
        }
    }

    internal static OverlayConfiguration FromPersistence(OverlayType type, string json) =>
        Parse(type, json)
            .Match(
                static valid => valid.Value,
                static invalid =>
                    throw new InvalidOperationException(
                        $"Persisted overlay configuration is invalid: {invalid.Message}"
                    )
            );

    internal abstract string ToPersistenceJson();

    private static OverlayConfigurationParseResult ParseEmpty(JsonElement root)
    {
        var properties = root.EnumerateObject().ToArray();
        return
            properties.Length != 1
            || !string.Equals(properties[0].Name, "schemaVersion", StringComparison.Ordinal)
            || properties[0].Value.ValueKind != JsonValueKind.Number
            || !properties[0].Value.TryGetInt32(out var schemaVersion)
            || schemaVersion != 1
            ? new OverlayConfigurationParseResult.Invalid(
                "An empty overlay configuration must contain only schemaVersion 1."
            )
            : new OverlayConfigurationParseResult.Valid(new EmptyV1());
    }

    private static OverlayConfigurationParseResult ParseGuessing(JsonElement root)
    {
        var properties = root.EnumerateObject().ToArray();
        return
            properties.Length is not (3 or 4)
            || (properties.Length == 4 && !TryReadProperty(properties, "appearance", out _))
            || !TryReadProperty(properties, "schemaVersion", out var schemaVersion)
            || schemaVersion.Value.ValueKind != JsonValueKind.Number
            || !schemaVersion.Value.TryGetInt32(out var version)
            || version != 1
            || !TryReadProperty(properties, "showGuessCount", out var showGuessCount)
            || showGuessCount.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !TryReadProperty(properties, "resultDurationSeconds", out var resultDuration)
            || resultDuration.Value.ValueKind != JsonValueKind.Number
            || !resultDuration.Value.TryGetInt32(out var resultDurationSeconds)
            || resultDurationSeconds
                is < GuessingV1.MinimumResultDurationSeconds
                    or > GuessingV1.MaximumResultDurationSeconds
            || !TryReadAppearance(properties, GuessingV1.Default.Appearance, out var appearance)
            ? new OverlayConfigurationParseResult.Invalid(
                "A guessing overlay configuration must contain schemaVersion 1, a showGuessCount boolean, and a resultDurationSeconds value from 1 to 30."
            )
            : new OverlayConfigurationParseResult.Valid(
                new GuessingV1(showGuessCount.Value.GetBoolean(), resultDurationSeconds, appearance)
            );
    }

    private static OverlayConfigurationParseResult ParseCuePlayer(JsonElement root)
    {
        var properties = root.EnumerateObject().ToArray();
        return
            properties.Length == 1
            && string.Equals(properties[0].Name, "schemaVersion", StringComparison.Ordinal)
            && properties[0].Value.ValueKind == JsonValueKind.Number
            && properties[0].Value.TryGetInt32(out var schemaVersion)
            && schemaVersion == 1
            ? new OverlayConfigurationParseResult.Valid(new CuePlayerV1())
            : new OverlayConfigurationParseResult.Invalid(
                "A cue player configuration must contain only schemaVersion 1."
            );
    }

    private static OverlayConfigurationParseResult ParseGiveaway(JsonElement root)
    {
        var properties = root.EnumerateObject().ToArray();
        return
            properties.Length is not (5 or 6)
            || (properties.Length == 6 && !TryReadProperty(properties, "appearance", out _))
            || !TryReadProperty(properties, "schemaVersion", out var schemaVersion)
            || schemaVersion.Value.ValueKind != JsonValueKind.Number
            || !schemaVersion.Value.TryGetInt32(out var version)
            || version != 1
            || !TryReadProperty(properties, "title", out var title)
            || title.Value.ValueKind != JsonValueKind.String
            || title.Value.GetString() is not { } titleValue
            || titleValue.Trim().Length is < 1 or > GiveawayV1.MaximumTitleLength
            || !TryReadBoolean(properties, "showEntrantCount", out var showEntrantCount)
            || !TryReadBoolean(properties, "showCountdown", out var showCountdown)
            || !TryReadBoolean(properties, "showJoinCommand", out var showJoinCommand)
            || !TryReadAppearance(properties, GiveawayV1.Default.Appearance, out var appearance)
            ? new OverlayConfigurationParseResult.Invalid(
                "A giveaway overlay configuration must contain schemaVersion 1, a title from 1 to 80 characters, and showEntrantCount, showCountdown, and showJoinCommand booleans."
            )
            : new OverlayConfigurationParseResult.Valid(
                new GiveawayV1(
                    titleValue,
                    showEntrantCount,
                    showCountdown,
                    showJoinCommand,
                    appearance
                )
            );
    }

    private static OverlayConfigurationParseResult ParseEventFeed(JsonElement root)
    {
        try
        {
            var dto = root.Deserialize<EventFeedConfigurationDto>(_strictJsonOptions);
            if (
                dto is null
                || dto.SchemaVersion != 1
                || dto.OverflowPolicy is null
                || dto.Kinds is null
                || dto.Kinds.Count is not (3 or 4)
                || dto.Kinds.Any(static pair =>
                    pair.Value is null || pair.Value.Template is null || pair.Value.Priority is null
                )
            )
            {
                throw new ArgumentException();
            }
            var expected = new[] { "pointAward", "guessingWinner", "giveawayWinner" };
            if (dto.Kinds.Count == 4)
            {
                expected = [.. expected, "bingoEvent"];
            }
            if (
                !dto
                    .Kinds.Keys.Order(StringComparer.Ordinal)
                    .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            )
            {
                throw new ArgumentException();
            }
            var kinds = dto.Kinds.ToDictionary(
                static pair => PersistedEnumTokens<OverlayEventFeedKind>.Parse(pair.Key),
                static pair => new EventFeedKindConfiguration(
                    pair.Value!.Enabled,
                    pair.Value.Template!,
                    PersistedEnumTokens<OverlayEventFeedPriority>.Parse(pair.Value.Priority!),
                    pair.Value.DurationSeconds
                )
            );
            _ = kinds.TryAdd(
                OverlayEventFeedKind.BingoEvent,
                OverlayConfiguration.EventFeedV1.Default.Kinds[OverlayEventFeedKind.BingoEvent]
            );
            return new OverlayConfigurationParseResult.Valid(
                new EventFeedV1(
                    dto.Capacity,
                    PersistedEnumTokens<EventFeedOverflowPolicy>.Parse(dto.OverflowPolicy),
                    kinds,
                    dto.Appearance is null
                        ? OverlayAppearance.EventFeedDefault
                        : ParseAppearance(dto.Appearance)
                )
            );
        }
        catch (Exception exception)
            when (exception is JsonException or ArgumentException or FormatException)
        {
            return new OverlayConfigurationParseResult.Invalid(
                "An event feed configuration must use EventFeedV1 with supported capacity, overflow, and event-kind settings."
            );
        }
    }

    private static OverlayConfigurationParseResult ParseViewerQueue(JsonElement root)
    {
        try
        {
            var dto = root.Deserialize<ViewerQueueConfigurationDto>(_strictJsonOptions);
            return dto is null || dto.SchemaVersion != 1 || dto.Appearance is null
                ? new OverlayConfigurationParseResult.Invalid(
                    "A Viewer Queue configuration must contain schemaVersion 1, a saved queue, current and next row counts from 0 to 12, and appearance."
                )
                : new OverlayConfigurationParseResult.Valid(
                    new ViewerQueueV1(
                        dto.QueueId,
                        dto.CurrentRows,
                        dto.NextRows,
                        ParseAppearance(dto.Appearance)
                    )
                );
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new OverlayConfigurationParseResult.Invalid(
                "A Viewer Queue configuration must contain schemaVersion 1, a saved queue, current and next row counts from 0 to 12, and appearance."
            );
        }
    }

    private static bool TryReadProperty(
        IEnumerable<JsonProperty> properties,
        string name,
        out JsonProperty property
    )
    {
        foreach (var candidate in properties)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                property = candidate;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static bool TryReadBoolean(
        IEnumerable<JsonProperty> properties,
        string name,
        out bool value
    )
    {
        if (
            TryReadProperty(properties, name, out var property)
            && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            value = property.Value.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryReadAppearance(
        IEnumerable<JsonProperty> properties,
        OverlayAppearance defaultAppearance,
        out OverlayAppearance appearance
    )
    {
        if (!TryReadProperty(properties, "appearance", out var property))
        {
            appearance = defaultAppearance;
            return true;
        }
        try
        {
            var dto = property.Value.Deserialize<OverlayAppearanceDto>(_strictJsonOptions);
            appearance = dto is null ? throw new ArgumentException() : ParseAppearance(dto);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            appearance = defaultAppearance;
            return false;
        }
    }

    private static OverlayAppearance ParseAppearance(OverlayAppearanceDto dto) =>
        new OverlayAppearance(dto.X, dto.Y, dto.Width, dto.Height, dto.Css ?? string.Empty);

    public sealed record EmptyV1 : OverlayConfiguration
    {
        public override OverlayType Type => OverlayType.Empty;

        public override int SchemaVersion => 1;

        internal override string ToPersistenceJson() => """{"schemaVersion":1}""";
    }

    public sealed record GuessingV1 : OverlayConfiguration
    {
        public const int MinimumResultDurationSeconds = 1;
        public const int MaximumResultDurationSeconds = 30;
        public const int DefaultResultDurationSeconds = 8;

        public GuessingV1(
            bool showGuessCount,
            int resultDurationSeconds,
            OverlayAppearance? appearance = null
        )
        {
            if (
                resultDurationSeconds
                is < MinimumResultDurationSeconds
                    or > MaximumResultDurationSeconds
            )
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resultDurationSeconds),
                    "The result duration must be from 1 to 30 seconds."
                );
            }

            ShowGuessCount = showGuessCount;
            ResultDurationSeconds = resultDurationSeconds;
            Appearance = appearance ?? OverlayAppearance.GuessingDefault;
        }

        public override OverlayType Type => OverlayType.Guessing;

        public override int SchemaVersion => 1;

        public bool ShowGuessCount { get; }

        public int ResultDurationSeconds { get; }

        public OverlayAppearance Appearance { get; }

        public static GuessingV1 Default =>
            new(true, DefaultResultDurationSeconds, OverlayAppearance.GuessingDefault);

        internal override string ToPersistenceJson() =>
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = SchemaVersion,
                    showGuessCount = ShowGuessCount,
                    resultDurationSeconds = ResultDurationSeconds,
                    appearance = Appearance,
                },
                _persistenceJsonOptions
            );
    }

    public sealed record CuePlayerV1 : OverlayConfiguration
    {
        public override OverlayType Type => OverlayType.CuePlayer;

        public override int SchemaVersion => 1;

        internal override string ToPersistenceJson() => """{"schemaVersion":1}""";
    }

    public sealed record GiveawayV1 : OverlayConfiguration
    {
        public const int MaximumTitleLength = 80;
        public const string DefaultTitle = "Points giveaway";

        public GiveawayV1(
            string title,
            bool showEntrantCount,
            bool showCountdown,
            bool showJoinCommand,
            OverlayAppearance? appearance = null
        )
        {
            var normalizedTitle = title.Trim();
            if (normalizedTitle.Length is < 1 or > MaximumTitleLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(title),
                    "The title must contain from 1 to 80 characters."
                );
            }

            Title = normalizedTitle;
            ShowEntrantCount = showEntrantCount;
            ShowCountdown = showCountdown;
            ShowJoinCommand = showJoinCommand;
            Appearance = appearance ?? OverlayAppearance.GiveawayDefault;
        }

        public override OverlayType Type => OverlayType.Giveaway;

        public override int SchemaVersion => 1;

        public string Title { get; }

        public bool ShowEntrantCount { get; }

        public bool ShowCountdown { get; }

        public bool ShowJoinCommand { get; }

        public OverlayAppearance Appearance { get; }

        public static GiveawayV1 Default =>
            new(DefaultTitle, true, true, true, OverlayAppearance.GiveawayDefault);

        internal override string ToPersistenceJson() =>
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = SchemaVersion,
                    title = Title,
                    showEntrantCount = ShowEntrantCount,
                    showCountdown = ShowCountdown,
                    showJoinCommand = ShowJoinCommand,
                    appearance = Appearance,
                },
                _persistenceJsonOptions
            );
    }

    public sealed record EventFeedV1 : OverlayConfiguration
    {
        public const int MinimumCapacity = 1;
        public const int MaximumCapacity = 25;
        public const int DefaultCapacity = 10;

        public EventFeedV1(
            int capacity,
            EventFeedOverflowPolicy overflowPolicy,
            IReadOnlyDictionary<OverlayEventFeedKind, EventFeedKindConfiguration> kinds,
            OverlayAppearance? appearance = null
        )
        {
            if (
                capacity is < MinimumCapacity or > MaximumCapacity
                || !Enum.IsDefined(overflowPolicy)
            )
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            if (
                kinds.Count != Enum.GetValues<OverlayEventFeedKind>().Length
                || Enum.GetValues<OverlayEventFeedKind>().Any(kind => !kinds.ContainsKey(kind))
            )
            {
                throw new ArgumentException(
                    "Every event kind requires configuration.",
                    nameof(kinds)
                );
            }
            Capacity = capacity;
            OverflowPolicy = overflowPolicy;
            Kinds = kinds.ToDictionary(pair => pair.Key, pair => pair.Value);
            Appearance = appearance ?? OverlayAppearance.EventFeedDefault;
            foreach (var pair in Kinds)
            {
                ValidateTemplate(pair.Key, pair.Value.Template);
            }
        }

        public override OverlayType Type => OverlayType.EventFeed;
        public override int SchemaVersion => 1;
        public int Capacity { get; }
        public EventFeedOverflowPolicy OverflowPolicy { get; }
        public IReadOnlyDictionary<OverlayEventFeedKind, EventFeedKindConfiguration> Kinds { get; }
        public OverlayAppearance Appearance { get; }

        internal override string ToPersistenceJson() =>
            JsonSerializer.Serialize(
                new EventFeedConfigurationDto(
                    SchemaVersion,
                    Capacity,
                    PersistedEnumTokens<EventFeedOverflowPolicy>.Format(OverflowPolicy),
                    Kinds.ToDictionary(
                        static pair => PersistedEnumTokens<OverlayEventFeedKind>.Format(pair.Key),
                        static pair =>
                            (EventFeedKindConfigurationDto?)
                                new EventFeedKindConfigurationDto(
                                    pair.Value.Enabled,
                                    pair.Value.Template,
                                    PersistedEnumTokens<OverlayEventFeedPriority>.Format(
                                        pair.Value.Priority
                                    ),
                                    pair.Value.DurationSeconds
                                )
                    ),
                    new OverlayAppearanceDto(
                        Appearance.X,
                        Appearance.Y,
                        Appearance.Width,
                        Appearance.Height,
                        Appearance.Css
                    )
                ),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );

        public static EventFeedV1 Default =>
            new(
                DefaultCapacity,
                EventFeedOverflowPolicy.DropNewest,
                new Dictionary<OverlayEventFeedKind, EventFeedKindConfiguration>
                {
                    [OverlayEventFeedKind.PointAward] = new(
                        true,
                        "{recipient} received {amount} {pointLabel}",
                        OverlayEventFeedPriority.Normal,
                        6
                    ),
                    [OverlayEventFeedKind.GuessingWinner] = new(
                        true,
                        "{winners} won {roundName}: {winningAnswer}",
                        OverlayEventFeedPriority.High,
                        8
                    ),
                    [OverlayEventFeedKind.GiveawayWinner] = new(
                        true,
                        "{winners} won {prizes}",
                        OverlayEventFeedPriority.High,
                        8
                    ),
                    [OverlayEventFeedKind.BingoEvent] = new(
                        true,
                        "{summary}",
                        OverlayEventFeedPriority.High,
                        8
                    ),
                },
                OverlayAppearance.EventFeedDefault
            );

        private static void ValidateTemplate(OverlayEventFeedKind kind, string template)
        {
            string[] allowed = kind switch
            {
                OverlayEventFeedKind.PointAward => ["recipient", "amount", "pointLabel"],
                OverlayEventFeedKind.GuessingWinner =>
                [
                    "roundName",
                    "winningAnswer",
                    "winners",
                    "winnerCount",
                    "amount",
                    "pointLabel",
                ],
                OverlayEventFeedKind.GiveawayWinner =>
                [
                    "winners",
                    "winnerCount",
                    "prizes",
                    "pointLabel",
                ],
                OverlayEventFeedKind.BingoEvent => ["summary"],
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            var remaining = template;
            foreach (var placeholder in allowed)
            {
                remaining = remaining.Replace(
                    $"{{{placeholder}}}",
                    string.Empty,
                    StringComparison.Ordinal
                );
            }
            if (
                remaining.Contains('{', StringComparison.Ordinal)
                || remaining.Contains('}', StringComparison.Ordinal)
            )
            {
                throw new ArgumentException(
                    "Templates may contain only placeholders supported by their event kind.",
                    nameof(template)
                );
            }
        }
    }

    public sealed record ViewerQueueV1 : OverlayConfiguration
    {
        public const int MinimumRows = 0;
        public const int MaximumRows = 12;
        public const int DefaultCurrentRows = 4;
        public const int DefaultNextRows = 6;

        public ViewerQueueV1(
            int queueId,
            int currentRows,
            int nextRows,
            OverlayAppearance? appearance = null
        )
        {
            if (
                queueId <= 0
                || currentRows is < MinimumRows or > MaximumRows
                || nextRows is < MinimumRows or > MaximumRows
            )
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queueId),
                    "Choose a saved queue and show from 0 to 12 current and next rows."
                );
            }

            QueueId = queueId;
            CurrentRows = currentRows;
            NextRows = nextRows;
            Appearance = appearance ?? OverlayAppearance.ViewerQueueDefault;
        }

        public override OverlayType Type => OverlayType.ViewerQueue;

        public override int SchemaVersion => 1;

        public int QueueId { get; }

        public int CurrentRows { get; }

        public int NextRows { get; }

        public OverlayAppearance Appearance { get; }

        internal override string ToPersistenceJson() =>
            JsonSerializer.Serialize(
                new ViewerQueueConfigurationDto(
                    SchemaVersion,
                    QueueId,
                    CurrentRows,
                    NextRows,
                    new OverlayAppearanceDto(
                        Appearance.X,
                        Appearance.Y,
                        Appearance.Width,
                        Appearance.Height,
                        Appearance.Css
                    )
                ),
                _persistenceJsonOptions
            );
    }
}

public enum EventFeedOverflowPolicy
{
    [PersistedToken("dropNewest")]
    DropNewest,

    [PersistedToken("replaceNewestSameKind")]
    ReplaceNewestSameKind,
}

public sealed record EventFeedKindConfiguration
{
    public EventFeedKindConfiguration(
        bool enabled,
        string template,
        OverlayEventFeedPriority priority,
        int durationSeconds
    )
    {
        var normalized = template.Trim();
        if (
            normalized.Length is < 1 or > 500
            || durationSeconds is < 1 or > 30
            || !Enum.IsDefined(priority)
        )
        {
            throw new ArgumentOutOfRangeException(nameof(template));
        }
        Enabled = enabled;
        Template = normalized;
        Priority = priority;
        DurationSeconds = durationSeconds;
    }

    public bool Enabled { get; }
    public string Template { get; }
    public OverlayEventFeedPriority Priority { get; }
    public int DurationSeconds { get; }
}

internal sealed record EventFeedConfigurationDto(
    int SchemaVersion,
    int Capacity,
    string? OverflowPolicy,
    Dictionary<string, EventFeedKindConfigurationDto?>? Kinds,
    OverlayAppearanceDto? Appearance
);

internal sealed record OverlayAppearanceDto(int X, int Y, int Width, int Height, string? Css);

internal sealed record EventFeedKindConfigurationDto(
    bool Enabled,
    string? Template,
    string? Priority,
    int DurationSeconds
);

internal sealed record ViewerQueueConfigurationDto(
    int SchemaVersion,
    int QueueId,
    int CurrentRows,
    int NextRows,
    OverlayAppearanceDto? Appearance
);

public abstract record OverlayConfigurationParseResult
{
    private OverlayConfigurationParseResult() { }

    public abstract TResult Match<TResult>(
        Func<Valid, TResult> valid,
        Func<Invalid, TResult> invalid
    );

    public sealed record Valid(OverlayConfiguration Value) : OverlayConfigurationParseResult
    {
        public override TResult Match<TResult>(
            Func<Valid, TResult> valid,
            Func<Invalid, TResult> invalid
        ) => valid(this);
    }

    public sealed record Invalid(string Message) : OverlayConfigurationParseResult
    {
        public override TResult Match<TResult>(
            Func<Valid, TResult> valid,
            Func<Invalid, TResult> invalid
        ) => invalid(this);
    }
}
