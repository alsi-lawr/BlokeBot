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

    public sealed record EmptyV1 : OverlayConfiguration
    {
        public override OverlayType Type => OverlayType.Empty;

        public override int SchemaVersion => 1;

        internal override string ToPersistenceJson()
        {
            return """{"schemaVersion":1}""";
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
