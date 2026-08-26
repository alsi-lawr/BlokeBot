using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(SemanticVersionJsonConverter))]
[TomlConverter(typeof(SemanticVersionTomlConverter))]
public sealed record SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(
        int major,
        int minor,
        int patch,
        string preRelease,
        string buildMetadata,
        string value
    )
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        BuildMetadata = buildMetadata;
        Value = value;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string PreRelease { get; }

    public string BuildMetadata { get; }

    public string Value { get; }

    public static bool TryCreate(string? candidate, out SemanticVersion version)
    {
        version = null!;
        if (candidate is null or { Length: < 5 or > 128 })
        {
            return false;
        }

        var buildSeparator = candidate.IndexOf('+', StringComparison.Ordinal);
        var coreAndPreRelease = buildSeparator < 0 ? candidate : candidate[..buildSeparator];
        var buildMetadata = buildSeparator < 0 ? string.Empty : candidate[(buildSeparator + 1)..];
        if (
            buildSeparator >= 0
            && (
                buildMetadata.Length == 0
                || candidate.IndexOf('+', buildSeparator + 1) >= 0
                || !ValidIdentifiers(buildMetadata, allowLeadingZero: true)
            )
        )
        {
            return false;
        }

        var preReleaseSeparator = coreAndPreRelease.IndexOf('-', StringComparison.Ordinal);
        var core =
            preReleaseSeparator < 0 ? coreAndPreRelease : coreAndPreRelease[..preReleaseSeparator];
        var preRelease =
            preReleaseSeparator < 0 ? string.Empty : coreAndPreRelease[(preReleaseSeparator + 1)..];
        if (
            preReleaseSeparator >= 0
            && (preRelease.Length == 0 || !ValidIdentifiers(preRelease, allowLeadingZero: false))
        )
        {
            return false;
        }

        var components = core.Split('.');
        if (
            components.Length != 3
            || !TryParseComponent(components[0], out var major)
            || !TryParseComponent(components[1], out var minor)
            || !TryParseComponent(components[2], out var patch)
        )
        {
            return false;
        }

        version = new(major, minor, patch, preRelease, buildMetadata, candidate);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var coreComparison = Major.CompareTo(other.Major);
        coreComparison = coreComparison != 0 ? coreComparison : Minor.CompareTo(other.Minor);
        coreComparison = coreComparison != 0 ? coreComparison : Patch.CompareTo(other.Patch);
        return coreComparison != 0
            ? coreComparison
            : ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => Value;

    public bool HasSamePrecedenceAs(SemanticVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CompareTo(other) == 0;
    }

    internal int GetPrecedenceHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    private static bool TryParseComponent(string component, out int value)
    {
        value = 0;
        return component.Length > 0
            && (component.Length == 1 || component[0] != '0')
            && int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool ValidIdentifiers(string value, bool allowLeadingZero)
    {
        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0)
            {
                return false;
            }

            var numeric = true;
            foreach (var character in identifier)
            {
                if (!IsIdentifierCharacter(character))
                {
                    return false;
                }

                numeric &= character is >= '0' and <= '9';
            }

            if (!allowLeadingZero && numeric && identifier.Length > 1 && identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierCharacter(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-';

    private static int ComparePreRelease(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return left.Length == right.Length ? 0
                : left.Length == 0 ? 1
                : -1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var comparison = CompareIdentifier(leftParts[index], rightParts[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(character => character is >= '0' and <= '9');
        var rightNumeric = right.All(character => character is >= '0' and <= '9');

        return leftNumeric
            ? rightNumeric
                ? left.Length != right.Length
                    ? left.Length.CompareTo(right.Length)
                    : string.CompareOrdinal(left, right)
                : -1
            : rightNumeric
                ? 1
                : string.CompareOrdinal(left, right);
    }
}

internal sealed class SemanticVersionJsonConverter : JsonConverter<SemanticVersion>
{
    public override SemanticVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var candidate = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return SemanticVersion.TryCreate(candidate, out var version)
            ? version
            : throw new JsonException("Invalid semantic version.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        SemanticVersion value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
