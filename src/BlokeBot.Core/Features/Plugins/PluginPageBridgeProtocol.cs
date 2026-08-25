using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

internal abstract record PluginPageBridgeRequest(PluginPageMessageId MessageId)
{
    internal sealed record Action(
        PluginPageMessageId MessageId,
        PluginActionId ActionId,
        PluginValue.Map Input
    ) : PluginPageBridgeRequest(MessageId);

    internal sealed record Navigate(PluginPageMessageId MessageId, Uri Url)
        : PluginPageBridgeRequest(MessageId);
}

internal abstract record PluginPageBridgeParseOutcome
{
    private PluginPageBridgeParseOutcome() { }

    internal sealed record Parsed(PluginPageBridgeRequest Request) : PluginPageBridgeParseOutcome;

    internal sealed record Rejected : PluginPageBridgeParseOutcome;
}

internal static class PluginPageBridgeProtocol
{
    internal const string Name = "blokebot.plugin-page";
    internal const int Version = 1;

    internal static PluginPageBridgeParseOutcome Parse(
        string json,
        PluginPageSessionId expectedSession
    )
    {
        if (Encoding.UTF8.GetByteCount(json) > PluginContractLimits.MaximumPageMessageBytes)
        {
            return new PluginPageBridgeParseOutcome.Rejected();
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = PluginContractLimits.MaximumPluginValueDepth,
                }
            );
            var root = document.RootElement;
            return (
                root.ValueKind is not JsonValueKind.Object
                || !String(root, "protocol", out var protocol)
                || protocol != Name
                || !Integer(root, "version", out var version)
                || version != Version
                || !GuidValue(root, "sessionId", out var sessionValue)
                || !PluginPageSessionId.TryCreate(sessionValue, out var session)
                || session != expectedSession
                || !GuidValue(root, "messageId", out var messageValue)
                || !PluginPageMessageId.TryCreate(messageValue, out var messageId)
                || !String(root, "kind", out var kind)
            )
                ? new PluginPageBridgeParseOutcome.Rejected()
                : kind switch
                {
                    "action" => ParseAction(root, messageId),
                    "navigate" => ParseNavigation(root, messageId),
                    _ => new PluginPageBridgeParseOutcome.Rejected(),
                };
        }
        catch (JsonException)
        {
            return new PluginPageBridgeParseOutcome.Rejected();
        }
    }

    private static PluginPageBridgeParseOutcome ParseAction(
        JsonElement root,
        PluginPageMessageId messageId
    ) =>
        (
            !String(root, "action", out var actionValue)
            || !PluginActionId.TryCreate(actionValue, out var action)
            || !root.TryGetProperty("input", out var inputElement)
            || inputElement.ValueKind is not JsonValueKind.Object
            || !TryValue(inputElement, out var input)
            || input is not PluginValue.Map map
            || PluginValueValidator.Validate(map) is PluginValueValidationOutcome.Invalid
        )
            ? new PluginPageBridgeParseOutcome.Rejected()
            : new PluginPageBridgeParseOutcome.Parsed(
                new PluginPageBridgeRequest.Action(messageId, action, map)
            );

    private static PluginPageBridgeParseOutcome ParseNavigation(
        JsonElement root,
        PluginPageMessageId messageId
    ) =>
        (
            !String(root, "url", out var urlValue)
            || !Uri.TryCreate(urlValue, UriKind.Absolute, out var url)
            || !url.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(url.UserInfo)
        )
            ? new PluginPageBridgeParseOutcome.Rejected()
            : new PluginPageBridgeParseOutcome.Parsed(
                new PluginPageBridgeRequest.Navigate(messageId, url)
            );

    private static bool TryValue(JsonElement element, out PluginValue value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                value = new PluginValue.Nil();
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = new PluginValue.Boolean(element.GetBoolean());
                return true;
            case JsonValueKind.Number when element.TryGetDouble(out var number):
                value = new PluginValue.Number(number);
                return true;
            case JsonValueKind.String:
                value = new PluginValue.String(element.GetString()!);
                return true;
            case JsonValueKind.Array:
                var items = ImmutableArray.CreateBuilder<PluginValue>();
                foreach (var item in element.EnumerateArray())
                {
                    if (!TryValue(item, out var parsed))
                    {
                        value = null!;
                        return false;
                    }
                    items.Add(parsed);
                }
                value = new PluginValue.Array(items.ToImmutable());
                return true;
            case JsonValueKind.Object:
                var properties = ImmutableArray.CreateBuilder<PluginValueProperty>();
                foreach (var property in element.EnumerateObject())
                {
                    if (!TryValue(property.Value, out var parsed))
                    {
                        value = null!;
                        return false;
                    }
                    properties.Add(new(property.Name, parsed));
                }
                value = new PluginValue.Map(properties.ToImmutable());
                return true;
            default:
                value = null!;
                return false;
        }
    }

    private static bool String(JsonElement root, string name, out string value)
    {
        var valid =
            root.TryGetProperty(name, out var property)
            && property.ValueKind is JsonValueKind.String;
        value = valid ? property.GetString()! : string.Empty;
        return valid;
    }

    private static bool Integer(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }

    private static bool GuidValue(JsonElement root, string name, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(name, out var property) && property.TryGetGuid(out value);
    }
}

public sealed record PluginPageBridgeResponse(bool Accepted, string? NavigationUrl, string Message);
