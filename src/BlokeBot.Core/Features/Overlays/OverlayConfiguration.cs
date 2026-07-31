using System.Text;
using System.Text.Json;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

public abstract record OverlayConfiguration
{
    private const int _maximumJsonBytes = 4096;

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
                "The overlay configuration must be from 1 to 4096 UTF-8 bytes."
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new OverlayConfigurationParseResult.Invalid(
                    "The overlay configuration must be a JSON object."
                );
            }

            return type switch
            {
                OverlayType.Empty => ParseEmpty(document.RootElement),
                OverlayType.Guessing => ParseGuessing(document.RootElement),
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

    internal static OverlayConfiguration FromPersistence(OverlayType type, string json)
    {
        return Parse(type, json)
            .Match(
                valid => valid.Value,
                invalid =>
                    throw new InvalidOperationException(
                        $"Persisted overlay configuration is invalid: {invalid.Message}"
                    )
            );
    }

    internal abstract string ToPersistenceJson();

    private static OverlayConfigurationParseResult ParseEmpty(JsonElement root)
    {
        var properties = root.EnumerateObject().ToArray();
        if (
            properties.Length != 1
            || !string.Equals(properties[0].Name, "schemaVersion", StringComparison.Ordinal)
            || properties[0].Value.ValueKind != JsonValueKind.Number
            || !properties[0].Value.TryGetInt32(out var schemaVersion)
            || schemaVersion != 1
        )
        {
            return new OverlayConfigurationParseResult.Invalid(
                "An empty overlay configuration must contain only schemaVersion 1."
            );
        }

        return new OverlayConfigurationParseResult.Valid(new EmptyV1());
    }

    private static OverlayConfigurationParseResult ParseGuessing(JsonElement root)
    {
        var properties = root.EnumerateObject().ToArray();
        if (
            properties.Length != 3
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
        )
        {
            return new OverlayConfigurationParseResult.Invalid(
                "A guessing overlay configuration must contain schemaVersion 1, a showGuessCount boolean, and a resultDurationSeconds value from 1 to 30."
            );
        }

        return new OverlayConfigurationParseResult.Valid(
            new GuessingV1(showGuessCount.Value.GetBoolean(), resultDurationSeconds)
        );
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

    public sealed record EmptyV1 : OverlayConfiguration
    {
        public override OverlayType Type => OverlayType.Empty;

        public override int SchemaVersion => 1;

        internal override string ToPersistenceJson()
        {
            return """{"schemaVersion":1}""";
        }
    }

    public sealed record GuessingV1 : OverlayConfiguration
    {
        public const int MinimumResultDurationSeconds = 1;
        public const int MaximumResultDurationSeconds = 30;
        public const int DefaultResultDurationSeconds = 8;

        public GuessingV1(bool showGuessCount, int resultDurationSeconds)
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
        }

        public override OverlayType Type => OverlayType.Guessing;

        public override int SchemaVersion => 1;

        public bool ShowGuessCount { get; }

        public int ResultDurationSeconds { get; }

        internal override string ToPersistenceJson()
        {
            return $$"""{"schemaVersion":1,"showGuessCount":{{ShowGuessCount.ToString().ToLowerInvariant()}},"resultDurationSeconds":{{ResultDurationSeconds}}}""";
        }
    }
}

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
        )
        {
            return valid(this);
        }
    }

    public sealed record Invalid(string Message) : OverlayConfigurationParseResult
    {
        public override TResult Match<TResult>(
            Func<Valid, TResult> valid,
            Func<Invalid, TResult> invalid
        )
        {
            return invalid(this);
        }
    }
}
