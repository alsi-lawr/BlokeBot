using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed class ConfigurationDocumentCodec
{
    public const string Format = "blokebot.channel-configuration";
    public const int CurrentVersion = 1;
    public const int MaximumBytes = 2 * 1024 * 1024;
    public const int MaximumRecordsPerCollection = 1_000;

    private static readonly JsonSerializerOptions _headerOptions = CreateOptions(
        JsonUnmappedMemberHandling.Skip
    );
    private static readonly JsonSerializerOptions _documentOptions = CreateOptions(
        JsonUnmappedMemberHandling.Disallow
    );

    public ConfigurationDocumentParseOutcome Parse(string json) =>
        Encoding.UTF8.GetByteCount(json) > MaximumBytes
            ? TooLarge()
            : Parse(Encoding.UTF8.GetBytes(json));

    public ConfigurationDocumentParseOutcome Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaximumBytes)
        {
            return TooLarge();
        }

        try
        {
            var header = JsonSerializer.Deserialize<ConfigurationDocumentHeader>(
                json.Span,
                _headerOptions
            );
            if (header is null || !string.Equals(header.Format, Format, StringComparison.Ordinal))
            {
                return new ConfigurationDocumentParseOutcome.Invalid(
                    new("format", $"Expected format '{Format}'.")
                );
            }

            var document = header.Version switch
            {
                0 => Migrate(
                    JsonSerializer.Deserialize<ConfigurationDocumentV0>(json.Span, _documentOptions)
                ),
                CurrentVersion => JsonSerializer.Deserialize<ConfigurationDocumentV1>(
                    json.Span,
                    _documentOptions
                ),
                > CurrentVersion => null,
                _ => null,
            };
            if (header.Version > CurrentVersion)
            {
                return new ConfigurationDocumentParseOutcome.Invalid(
                    new(
                        "version",
                        $"Format version {header.Version} is newer than supported version {CurrentVersion}."
                    )
                );
            }
            if (document is null)
            {
                return new ConfigurationDocumentParseOutcome.Invalid(
                    new("version", $"Format version {header.Version} is not supported.")
                );
            }

            var validationIssue = ConfigurationDocumentValidator.Validate(document);
            return validationIssue is null
                ? new ConfigurationDocumentParseOutcome.Valid(document)
                : new ConfigurationDocumentParseOutcome.Invalid(validationIssue);
        }
        catch (JsonException exception)
        {
            return new ConfigurationDocumentParseOutcome.Invalid(
                new(
                    exception.Path ?? "$",
                    $"The JSON structure or value is invalid. {exception.Message}"
                )
            );
        }
    }

    public byte[] Serialize(ConfigurationDocumentV1 document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, _documentOptions);

    public static ConfigurationDocumentParseOutcome.Invalid TooLarge() =>
        new(new("$", $"The configuration file exceeds the {MaximumBytes / 1024 / 1024} MB limit."));

    private static ConfigurationDocumentV1? Migrate(ConfigurationDocumentV0? document) =>
        document is null
            ? null
            : new(
                Format,
                CurrentVersion,
                document.ExportedAtUtc,
                new(document.ChannelLogin, null),
                document.Sections
            );

    private static JsonSerializerOptions CreateOptions(JsonUnmappedMemberHandling handling) =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = handling,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) },
        };
}

public abstract record ConfigurationDocumentParseOutcome
{
    private ConfigurationDocumentParseOutcome() { }

    public sealed record Valid(ConfigurationDocumentV1 Document)
        : ConfigurationDocumentParseOutcome;

    public sealed record Invalid(ConfigurationValidationIssue Issue)
        : ConfigurationDocumentParseOutcome;
}
