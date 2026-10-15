using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationDiagnosticJsonConverter : JsonConverter<AutomationDiagnostic>
{
    public override AutomationDiagnostic? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || root.GetRawText().Length > 8192
            || !Integer(root, "version", out var version)
            || version != 1
            || !Integer(root, "category", out var category)
            || !Enum.IsDefined((AutomationDiagnosticCategory)category)
            || Member(root, "member") is not { } member
        )
        {
            return null;
        }
        AutomationExpressionSpan? span = null;
        if (root.TryGetProperty("span", out var range) && range.ValueKind != JsonValueKind.Null)
        {
            if (
                !Integer(range, "start", out var start)
                || !Integer(range, "length", out var length)
                || start < 0
                || length < 0
                || start > int.MaxValue - length
            )
            {
                return null;
            }
            span = new(start, length);
        }
        return Site(root, "origin", out var origin) && Site(root, "affected", out var affected)
            ? new((AutomationDiagnosticCategory)category, member, span, origin, affected)
            : null;
    }

    public override void Write(
        Utf8JsonWriter writer,
        AutomationDiagnostic value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteNumber("category", (int)value.Category);
        WriteMember(writer, value.Member);
        if (value.Span is { } span)
        {
            writer.WriteStartObject("span");
            writer.WriteNumber("start", span.Start);
            writer.WriteNumber("length", span.Length);
            writer.WriteEndObject();
        }
        WriteSite(writer, "origin", value.Origin);
        WriteSite(writer, "affected", value.Affected);
        writer.WriteEndObject();
    }

    private static bool Integer(JsonElement parent, string name, out int value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static string? Identifier(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && property.GetString() is { Length: > 0 and <= 128 } text
            ? text
            : null;

    private static AutomationDiagnosticMember? Member(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var member)
        && member.ValueKind == JsonValueKind.Object
        && Integer(member, "kind", out var kind)
            ? kind switch
            {
                0
                    when Identifier(member, "field") is { } field
                        && Identifier(member, "port") is { } port =>
                    new AutomationDiagnosticMember.Input(new(field), new(port)),
                1 when Identifier(member, "port") is { } port =>
                    new AutomationDiagnosticMember.Output(new(port)),
                2 => new AutomationDiagnosticMember.Node(),
                _ => null,
            }
            : null;

    private static bool Site(JsonElement parent, string name, out AutomationDiagnosticSite? site)
    {
        site = null;
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (
            value.ValueKind != JsonValueKind.Object
            || !GuidValue(value, "node", out var node)
            || node is null
            || Identifier(value, "definition") is not { } definition
            || Member(value, "member") is not { } member
        )
        {
            return false;
        }
        AutomationTraceInvocation? invocation = null;
        if (value.TryGetProperty("invocation", out var nested))
        {
            if (
                nested.ValueKind != JsonValueKind.Object
                || !GuidValue(nested, "id", out var id)
                || id is null
                || !GuidValue(nested, "parent", out var caller)
                || !GuidValue(nested, "subflow", out var subflow)
                || !GuidValue(nested, "revision", out var revision)
            )
            {
                return false;
            }
            invocation = new(id.Value, caller, subflow, revision);
        }
        site = new(new(node.Value), new(definition), member, invocation);
        return true;
    }

    private static bool GuidValue(JsonElement parent, string name, out Guid? value)
    {
        value = null;
        if (
            !parent.TryGetProperty(name, out var property)
            || property.ValueKind == JsonValueKind.Null
        )
        {
            return true;
        }
        if (property.ValueKind != JsonValueKind.String || !property.TryGetGuid(out var parsed))
        {
            return false;
        }
        value = parsed;
        return true;
    }

    private static void WriteMember(Utf8JsonWriter writer, AutomationDiagnosticMember member)
    {
        writer.WriteStartObject("member");
        _ = member.Match(
            input =>
            {
                writer.WriteNumber("kind", 0);
                writer.WriteString("field", input.FieldId.Value);
                writer.WriteString("port", input.PortId.Value);
                return true;
            },
            output =>
            {
                writer.WriteNumber("kind", 1);
                writer.WriteString("port", output.PortId.Value);
                return true;
            },
            _ =>
            {
                writer.WriteNumber("kind", 2);
                return true;
            }
        );
        writer.WriteEndObject();
    }

    private static void WriteSite(
        Utf8JsonWriter writer,
        string name,
        AutomationDiagnosticSite? site
    )
    {
        if (site is null)
        {
            return;
        }
        writer.WriteStartObject(name);
        writer.WriteString("node", site.NodeId.Value);
        writer.WriteString("definition", site.DefinitionId.Value);
        WriteMember(writer, site.Member);
        if (site.Invocation is { } invocation)
        {
            writer.WriteStartObject("invocation");
            writer.WriteString("id", invocation.Id);
            if (invocation.ParentId is { } parent)
            {
                writer.WriteString("parent", parent);
            }
            if (invocation.SubflowId is { } subflow)
            {
                writer.WriteString("subflow", subflow);
            }
            if (invocation.RevisionId is { } revision)
            {
                writer.WriteString("revision", revision);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}
