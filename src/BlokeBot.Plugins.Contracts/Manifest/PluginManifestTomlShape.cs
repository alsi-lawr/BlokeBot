using Tomlyn.Model;

namespace BlokeBot.Plugins.Contracts;

internal static class PluginManifestTomlShape
{
    internal static bool HasOnlyKnownFields(TomlTable manifest) =>
        Table(
            manifest,
            [
                "manifestVersion",
                "id",
                "name",
                "description",
                "release",
                "compatibility",
                "entryModule",
                "luaModules",
                "assets",
                "payloads",
                "settings",
                "features",
                "hostModules",
                "migrations",
                "automationDefinitions",
                "automationTemplates",
                "generatedPages",
                "embeddedPages",
            ]
        )
        && Child(manifest, "release", Release)
        && Child(manifest, "compatibility", Compatibility)
        && Children(manifest, "luaModules", LuaModule)
        && Children(manifest, "assets", Asset)
        && Children(manifest, "payloads", Payload)
        && Children(manifest, "settings", Setting)
        && Children(manifest, "features", Feature)
        && Children(manifest, "hostModules", HostModule)
        && Children(manifest, "migrations", Migration)
        && Children(manifest, "automationDefinitions", AutomationDefinition)
        && Children(manifest, "automationTemplates", AutomationTemplate)
        && Children(manifest, "generatedPages", GeneratedPage)
        && Children(manifest, "embeddedPages", EmbeddedPage);

    private static bool Release(TomlTable table) => Table(table, ["declaredVersion", "tag"]);

    private static bool Compatibility(TomlTable table) =>
        Table(
            table,
            [
                "minimumApiVersion",
                "maximumApiVersion",
                "minimumBlokeBotVersion",
                "maximumBlokeBotVersionExclusive",
                "luaVersion",
            ]
        );

    private static bool LuaModule(TomlTable table) => Table(table, ["id", "path"]);

    private static bool Asset(TomlTable table) =>
        Table(
            table,
            ["id", "path", "kind", "mediaType", "purpose", "runtimeIdentifiers", "maximumBytes"]
        );

    private static bool Payload(TomlTable table) =>
        Table(table, ["id", "path", "purpose", "runtimeIdentifiers", "maximumBytes"]);

    private static bool Setting(TomlTable table) =>
        Table(table, ["id", "name", "description", "scope", "required", "schema"])
        && Child(table, "schema", SettingSchema);

    private static bool SettingSchema(TomlTable table) =>
        table.TryGetValue("kind", out var kind)
        && kind is string name
        && name switch
        {
            "boolean" => Table(table, ["kind"]),
            "text" or "multilineText" or "secret" => Table(table, ["kind", "maximumLength"]),
            "integer" => Table(table, ["kind", "minimum", "maximum"]),
            "number" => Table(table, ["kind", "minimum", "maximum", "decimalPlaces"]),
            "duration" => Table(table, ["kind", "minimumSeconds", "maximumSeconds"]),
            "choice" => Table(table, ["kind", "choices"])
                && Children(table, "choices", SettingChoice),
            _ => false,
        };

    private static bool SettingChoice(TomlTable table) => Table(table, ["id", "name"]);

    private static bool Feature(TomlTable table) =>
        Table(
            table,
            ["id", "name", "description", "settings", "twitch", "automationTemplates", "dispatch"]
        )
        && Child(table, "twitch", Twitch)
        && OptionalChild(table, "dispatch", Dispatch);

    private static bool Twitch(TomlTable table) => Table(table, ["scopes", "eventSubTypes"]);

    private static bool Dispatch(TomlTable table) =>
        Table(table, ["commands", "events", "schedules", "webhooks", "actions"])
        && Children(table, "commands", Command)
        && Children(table, "events", Event)
        && Children(table, "schedules", Schedule)
        && OptionalChildren(table, "webhooks", Webhook)
        && OptionalChildren(table, "actions", Action);

    private static bool Command(TomlTable table) =>
        Callback(table, ["route", "module", "operation", "requirements"]);

    private static bool Event(TomlTable table) =>
        Callback(table, ["id", "source", "module", "operation", "requirements"])
        && Child(table, "source", EventSource);

    private static bool Schedule(TomlTable table) =>
        Callback(table, ["id", "module", "operation", "requirements"]);

    private static bool Webhook(TomlTable table) =>
        Callback(table, ["id", "module", "operation", "requirements", "authentication"])
        && Child(table, "authentication", WebhookAuthentication);

    private static bool Action(TomlTable table) =>
        Callback(table, ["id", "module", "operation", "requirements"]);

    private static bool Callback(TomlTable table, IReadOnlyList<string> fields) =>
        Table(table, fields) && Child(table, "requirements", Requirements);

    private static bool Requirements(TomlTable table) => Table(table, ["twitchReady"]);

    private static bool EventSource(TomlTable table) =>
        table.TryGetValue("type", out var type)
        && type is string name
        && name switch
        {
            "twitch" or "blokeBot" => Table(table, ["type", "kind"]),
            "twitchRaw" => Table(table, ["type", "eventSubType", "version"]),
            _ => false,
        };

    private static bool WebhookAuthentication(TomlTable table) =>
        table.TryGetValue("kind", out var kind)
        && kind is string name
        && name switch
        {
            "public" => Table(table, ["kind"]),
            "callback" => Table(table, ["kind", "module", "operation"]),
            _ => false,
        };

    private static bool HostModule(TomlTable table) =>
        Table(table, ["id", "minimumVersion", "maximumVersion"]);

    private static bool Migration(TomlTable table) =>
        Table(table, ["id", "fromVersion", "toVersion", "module", "entryPoint"]);

    private static bool AutomationDefinition(TomlTable table) =>
        Table(
            table,
            [
                "id",
                "featureId",
                "kind",
                "name",
                "description",
                "module",
                "entryPoint",
                "inputs",
                "outputs",
            ]
        )
        && Children(table, "inputs", AutomationField)
        && Children(table, "outputs", AutomationField);

    private static bool AutomationField(TomlTable table) =>
        Table(table, ["id", "name", "valueKind", "required"]);

    private static bool AutomationTemplate(TomlTable table) =>
        Table(table, ["id", "featureId", "name", "nodes", "edges"])
        && Children(table, "nodes", AutomationNode)
        && Children(table, "edges", AutomationEdge);

    private static bool AutomationNode(TomlTable table) =>
        Table(table, ["id", "definitionId", "configuration"])
        && Child(table, "configuration", PluginValue);

    private static bool AutomationEdge(TomlTable table) =>
        Table(table, ["fromNode", "fromOutput", "toNode", "toInput"]);

    private static bool PluginValue(TomlTable table) =>
        table.TryGetValue("kind", out var kind)
        && kind is string name
        && name switch
        {
            "nil" => Table(table, ["kind"]),
            "boolean" or "number" or "string" => Table(table, ["kind", "value"]),
            "array" => Table(table, ["kind", "items"]) && Children(table, "items", PluginValue),
            "map" => Table(table, ["kind", "properties"])
                && Children(table, "properties", PluginValueProperty),
            _ => false,
        };

    private static bool PluginValueProperty(TomlTable table) =>
        Table(table, ["name", "value"]) && Child(table, "value", PluginValue);

    private static bool GeneratedPage(TomlTable table) =>
        Table(table, ["id", "featureId", "route", "title", "module", "renderEntryPoint"]);

    private static bool EmbeddedPage(TomlTable table) =>
        Table(
            table,
            ["id", "featureId", "route", "title", "documentAsset", "assets", "messageOrigins"]
        );

    private static bool Table(TomlTable table, IReadOnlyList<string> fields) =>
        table.Keys.All(fields.Contains);

    private static bool Child(TomlTable table, string name, Func<TomlTable, bool> validate) =>
        table.TryGetValue(name, out var value) && value is TomlTable child && validate(child);

    private static bool OptionalChild(
        TomlTable table,
        string name,
        Func<TomlTable, bool> validate
    ) => !table.TryGetValue(name, out var value) || (value is TomlTable child && validate(child));

    private static bool Children(TomlTable table, string name, Func<TomlTable, bool> validate) =>
        table.TryGetValue(name, out var value) && Tables(value, validate);

    private static bool OptionalChildren(
        TomlTable table,
        string name,
        Func<TomlTable, bool> validate
    ) => !table.TryGetValue(name, out var value) || Tables(value, validate);

    private static bool Tables(object? value, Func<TomlTable, bool> validate) =>
        value is IEnumerable<object> values
        && values.All(item => item is TomlTable table && validate(table));
}
