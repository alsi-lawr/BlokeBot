using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace BlokeBot.Core.Features.Overlays;

public enum OverlayCueMediaKind
{
    Video,
    Audio,
}

public enum OverlayCueFitMode
{
    Contain,
    Cover,
    Fill,
}

public abstract record OverlayCueLayer
{
    private OverlayCueLayer() { }

    public required int StartOffsetMilliseconds { get; init; }

    public required int DurationMilliseconds { get; init; }

    public required int ZIndex { get; init; }

    public sealed record UploadedMedia : OverlayCueLayer
    {
        public required Guid AssetId { get; init; }

        public required OverlayCueMediaKind MediaKind { get; init; }

        public required decimal Volume { get; init; }

        public required OverlayCueFitMode Fit { get; init; }

        public required OverlayCueRectangle Rectangle { get; init; }
    }

    public sealed record RemoteMedia : OverlayCueLayer
    {
        public required Uri Url { get; init; }

        public required OverlayCueMediaKind MediaKind { get; init; }

        public required decimal Volume { get; init; }

        public required OverlayCueFitMode Fit { get; init; }

        public required OverlayCueRectangle Rectangle { get; init; }
    }

    public sealed record ExternalWeb : OverlayCueLayer
    {
        public required Uri Url { get; init; }

        public required OverlayCueRectangle Rectangle { get; init; }
    }
}

public sealed record OverlayCueRectangle(
    decimal XPercent,
    decimal YPercent,
    decimal WidthPercent,
    decimal HeightPercent
);

public sealed record OverlayCueConfiguration
{
    private const int _maximumJsonBytes = 32768;
    public const int MaximumLayerCount = 16;

    private OverlayCueConfiguration(ImmutableArray<OverlayCueLayer> layers) => Layers = layers;

    public int SchemaVersion => 1;

    public ImmutableArray<OverlayCueLayer> Layers { get; }

    public ImmutableArray<Guid> ReferencedAssetIds =>
        Layers
            .OfType<OverlayCueLayer.UploadedMedia>()
            .Select(layer => layer.AssetId)
            .Distinct()
            .Order()
            .ToImmutableArray();

    public static OverlayCueConfigurationResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > _maximumJsonBytes)
        {
            return Invalid("Cue configuration must be from 1 to 32768 UTF-8 bytes.");
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
            var root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !HasOnly(root, "schemaVersion", "layers")
                || !root.TryGetProperty("schemaVersion", out var schemaVersion)
                || !schemaVersion.TryGetInt32(out var version)
                || version != 1
                || !root.TryGetProperty("layers", out var layers)
                || layers.ValueKind != JsonValueKind.Array
            )
            {
                return Invalid("Cue-V1 must contain only schemaVersion 1 and a layers array.");
            }

            var parsed = ImmutableArray.CreateBuilder<OverlayCueLayer>();
            foreach (var layer in layers.EnumerateArray())
            {
                if (parsed.Count == MaximumLayerCount)
                {
                    return Invalid($"Cue-V1 supports at most {MaximumLayerCount} layers.");
                }

                var result = ParseLayer(layer);
                if (result is LayerParseResult.Invalid invalid)
                {
                    return Invalid(invalid.Message);
                }
                parsed.Add(((LayerParseResult.Valid)result).Layer);
            }

            if (parsed.Count == 0)
            {
                return Invalid("Cue-V1 requires at least one layer.");
            }

            return new OverlayCueConfigurationResult.Valid(new(parsed.ToImmutable()));
        }
        catch (JsonException)
        {
            return Invalid("Cue configuration is not valid JSON.");
        }
        catch (UriFormatException)
        {
            return Invalid("Layer URLs must be absolute HTTPS URLs.");
        }
    }

    public string ToPersistenceJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("layers");
            foreach (var layer in Layers)
            {
                WriteLayer(writer, layer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static LayerParseResult ParseLayer(JsonElement element)
    {
        if (
            element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("type", out var typeValue)
            || typeValue.ValueKind != JsonValueKind.String
        )
        {
            return LayerInvalid("Every Cue-V1 layer requires a supported type.");
        }

        var type = typeValue.GetString();
        return type switch
        {
            "uploadedMedia" => ParseUploaded(element),
            "remoteMedia" => ParseRemote(element),
            "externalWeb" => ParseWeb(element),
            _ => LayerInvalid("Cue-V1 layer type is not supported."),
        };
    }

    private static LayerParseResult ParseUploaded(JsonElement element)
    {
        if (
            !HasOnly(
                element,
                "type",
                "assetId",
                "mediaKind",
                "startOffsetMilliseconds",
                "durationMilliseconds",
                "zIndex",
                "volume",
                "fit",
                "rectangle"
            )
            || !element.TryGetProperty("assetId", out var assetValue)
            || assetValue.ValueKind != JsonValueKind.String
            || !Guid.TryParse(assetValue.GetString(), out var assetId)
            || assetId == Guid.Empty
            || !TryCommonMedia(
                element,
                out var mediaKind,
                out var start,
                out var duration,
                out var zIndex,
                out var volume,
                out var fit,
                out var rectangle
            )
        )
        {
            return LayerInvalid(
                "An uploadedMedia layer requires a valid assetId, mediaKind, timing, volume, fit, zIndex, and rectangle."
            );
        }
        return new LayerParseResult.Valid(
            new OverlayCueLayer.UploadedMedia
            {
                AssetId = assetId,
                MediaKind = mediaKind,
                StartOffsetMilliseconds = start,
                DurationMilliseconds = duration,
                ZIndex = zIndex,
                Volume = volume,
                Fit = fit,
                Rectangle = rectangle,
            }
        );
    }

    private static LayerParseResult ParseRemote(JsonElement element)
    {
        if (
            !HasOnly(
                element,
                "type",
                "url",
                "mediaKind",
                "startOffsetMilliseconds",
                "durationMilliseconds",
                "zIndex",
                "volume",
                "fit",
                "rectangle"
            )
            || !TryHttpsUrl(element, out var url)
            || !TryCommonMedia(
                element,
                out var mediaKind,
                out var start,
                out var duration,
                out var zIndex,
                out var volume,
                out var fit,
                out var rectangle
            )
        )
        {
            return LayerInvalid(
                "A remoteMedia layer requires an absolute HTTPS URL without credentials plus valid media, timing, volume, fit, zIndex, and rectangle fields."
            );
        }
        return new LayerParseResult.Valid(
            new OverlayCueLayer.RemoteMedia
            {
                Url = url,
                MediaKind = mediaKind,
                StartOffsetMilliseconds = start,
                DurationMilliseconds = duration,
                ZIndex = zIndex,
                Volume = volume,
                Fit = fit,
                Rectangle = rectangle,
            }
        );
    }

    private static LayerParseResult ParseWeb(JsonElement element)
    {
        if (
            !HasOnly(
                element,
                "type",
                "url",
                "startOffsetMilliseconds",
                "durationMilliseconds",
                "zIndex",
                "rectangle"
            )
            || !TryHttpsUrl(element, out var url)
            || !TryTiming(element, out var start, out var duration, out var zIndex)
            || !TryRectangle(element, out var rectangle)
        )
        {
            return LayerInvalid(
                "An externalWeb layer requires an absolute HTTPS URL without credentials plus valid timing, zIndex, and rectangle fields."
            );
        }
        return new LayerParseResult.Valid(
            new OverlayCueLayer.ExternalWeb
            {
                Url = url,
                StartOffsetMilliseconds = start,
                DurationMilliseconds = duration,
                ZIndex = zIndex,
                Rectangle = rectangle,
            }
        );
    }

    private static bool TryCommonMedia(
        JsonElement element,
        out OverlayCueMediaKind mediaKind,
        out int start,
        out int duration,
        out int zIndex,
        out decimal volume,
        out OverlayCueFitMode fit,
        out OverlayCueRectangle rectangle
    )
    {
        mediaKind = default;
        start = default;
        duration = default;
        zIndex = default;
        volume = default;
        fit = default;
        rectangle = null!;
        return TryEnumToken(element, "mediaKind", out mediaKind)
            && TryTiming(element, out start, out duration, out zIndex)
            && element.TryGetProperty("volume", out var volumeValue)
            && volumeValue.TryGetDecimal(out volume)
            && volume is >= 0 and <= 1
            && TryEnumToken(element, "fit", out fit)
            && TryRectangle(element, out rectangle);
    }

    private static bool TryTiming(
        JsonElement element,
        out int start,
        out int duration,
        out int zIndex
    )
    {
        start = default;
        duration = default;
        zIndex = default;
        return element.TryGetProperty("startOffsetMilliseconds", out var startValue)
            && startValue.TryGetInt32(out start)
            && start is >= 0 and <= 300000
            && element.TryGetProperty("durationMilliseconds", out var durationValue)
            && durationValue.TryGetInt32(out duration)
            && duration is >= 100 and <= 300000
            && start + duration <= 300000
            && element.TryGetProperty("zIndex", out var zIndexValue)
            && zIndexValue.TryGetInt32(out zIndex)
            && zIndex is >= -100 and <= 100;
    }

    private static bool TryRectangle(JsonElement element, out OverlayCueRectangle rectangle)
    {
        rectangle = null!;
        if (
            !element.TryGetProperty("rectangle", out var value)
            || value.ValueKind != JsonValueKind.Object
            || !HasOnly(value, "xPercent", "yPercent", "widthPercent", "heightPercent")
            || !TryDecimal(value, "xPercent", out var x)
            || !TryDecimal(value, "yPercent", out var y)
            || !TryDecimal(value, "widthPercent", out var width)
            || !TryDecimal(value, "heightPercent", out var height)
            || x is < 0 or > 100
            || y is < 0 or > 100
            || width is <= 0 or > 100
            || height is <= 0 or > 100
            || x + width > 100
            || y + height > 100
        )
        {
            return false;
        }

        rectangle = new(x, y, width, height);
        return true;
    }

    private static bool TryHttpsUrl(JsonElement element, out Uri uri)
    {
        uri = null!;
        if (
            !element.TryGetProperty("url", out var value)
            || value.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Fragment)
        )
        {
            return false;
        }
        uri = candidate;
        return true;
    }

    private static bool TryEnumToken<T>(JsonElement element, string name, out T value)
        where T : struct, Enum
    {
        value = default;
        if (
            !element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }
        var token = property.GetString();
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(ToToken(candidate), token, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        return false;
    }

    private static bool TryDecimal(JsonElement element, string name, out decimal value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.TryGetDecimal(out value);
    }

    private static bool HasOnly(JsonElement element, params string[] expected)
    {
        var properties = element.EnumerateObject().Select(value => value.Name).ToArray();
        return properties.Length == expected.Length
            && properties.All(name => expected.Contains(name, StringComparer.Ordinal));
    }

    private static void WriteLayer(Utf8JsonWriter writer, OverlayCueLayer layer)
    {
        writer.WriteStartObject();
        switch (layer)
        {
            case OverlayCueLayer.UploadedMedia uploaded:
                writer.WriteString("type", "uploadedMedia");
                writer.WriteString("assetId", uploaded.AssetId);
                WriteMedia(writer, uploaded);
                break;
            case OverlayCueLayer.RemoteMedia remote:
                writer.WriteString("type", "remoteMedia");
                writer.WriteString("url", remote.Url.AbsoluteUri);
                WriteMedia(writer, remote);
                break;
            case OverlayCueLayer.ExternalWeb web:
                writer.WriteString("type", "externalWeb");
                writer.WriteString("url", web.Url.AbsoluteUri);
                WriteTiming(writer, web);
                WriteRectangle(writer, web.Rectangle);
                break;
            default:
                throw new InvalidOperationException("Unsupported Cue-V1 layer.");
        }
        writer.WriteEndObject();
    }

    private static void WriteMedia(Utf8JsonWriter writer, OverlayCueLayer layer)
    {
        var (kind, volume, fit, rectangle) = layer switch
        {
            OverlayCueLayer.UploadedMedia value => (
                value.MediaKind,
                value.Volume,
                value.Fit,
                value.Rectangle
            ),
            OverlayCueLayer.RemoteMedia value => (
                value.MediaKind,
                value.Volume,
                value.Fit,
                value.Rectangle
            ),
            _ => throw new InvalidOperationException("A media layer is required."),
        };
        writer.WriteString("mediaKind", ToToken(kind));
        WriteTiming(writer, layer);
        writer.WriteNumber("volume", volume);
        writer.WriteString("fit", ToToken(fit));
        WriteRectangle(writer, rectangle);
    }

    private static void WriteTiming(Utf8JsonWriter writer, OverlayCueLayer layer)
    {
        writer.WriteNumber("startOffsetMilliseconds", layer.StartOffsetMilliseconds);
        writer.WriteNumber("durationMilliseconds", layer.DurationMilliseconds);
        writer.WriteNumber("zIndex", layer.ZIndex);
    }

    private static void WriteRectangle(Utf8JsonWriter writer, OverlayCueRectangle rectangle)
    {
        writer.WriteStartObject("rectangle");
        writer.WriteNumber("xPercent", rectangle.XPercent);
        writer.WriteNumber("yPercent", rectangle.YPercent);
        writer.WriteNumber("widthPercent", rectangle.WidthPercent);
        writer.WriteNumber("heightPercent", rectangle.HeightPercent);
        writer.WriteEndObject();
    }

    private static string ToToken<T>(T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static OverlayCueConfigurationResult.Invalid Invalid(string message) => new(message);

    private static LayerParseResult.Invalid LayerInvalid(string message) => new(message);

    private abstract record LayerParseResult
    {
        private LayerParseResult() { }

        internal sealed record Valid(OverlayCueLayer Layer) : LayerParseResult;

        internal sealed record Invalid(string Message) : LayerParseResult;
    }
}

public abstract record OverlayCueConfigurationResult
{
    private OverlayCueConfigurationResult() { }

    public sealed record Valid(OverlayCueConfiguration Value) : OverlayCueConfigurationResult;

    public sealed record Invalid(string Message) : OverlayCueConfigurationResult;
}
