using System.Text;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Simulation;

internal static class SimulationPluginFeatureManifest
{
    private const string _manifestToml = """
        manifestVersion = 1
        id = "community.link-queue"
        name = "Community link queue"
        description = "Collects community links and publishes them on a schedule."
        entryModule = "main"
        assets = []
        payloads = []
        hostModules = []
        migrations = []
        automationDefinitions = []
        automationTemplates = []
        generatedPages = []
        embeddedPages = []
        [release]
        declaredVersion = "1.2.0"
        tag = "community-link-queue"
        [compatibility]
        minimumApiVersion = 1
        maximumApiVersion = 1
        minimumBlokeBotVersion = "0.13.0"
        maximumBlokeBotVersionExclusive = "0.14.0"
        luaVersion = "lua54"
        [[luaModules]]
        id = "main"
        path = "lua/main.lua"
        [[settings]]
        id = "moderation-mode"
        name = "Moderation mode"
        description = "Controls how submitted links are admitted."
        scope = "installation"
        required = true
        [settings.schema]
        kind = "choice"
        [[settings.schema.choices]]
        id = "manual"
        name = "Manual review"

        [[settings.schema.choices]]
        id = "automatic"
        name = "Automatic review"

        [[settings]]
        id = "service-token"
        name = "Service token"
        description = "Connects the installation to the queue service."
        scope = "installation"
        required = false
        [settings.schema]
        maximumLength = 256
        kind = "secret"

        [[settings]]
        id = "collect-messages"
        name = "Collect messages"
        description = "Allows links from chat."
        scope = "channel"
        required = true
        [settings.schema]
        kind = "boolean"

        [[settings]]
        id = "chat-command"
        name = "Chat command"
        description = "Sets the link command."
        scope = "channel"
        required = true
        [settings.schema]
        maximumLength = 24
        kind = "text"

        [[settings]]
        id = "queue-note"
        name = "Queue note"
        description = "Shows a note with the channel queue."
        scope = "channel"
        required = false
        [settings.schema]
        maximumLength = 500
        kind = "multilineText"

        [[settings]]
        id = "maximum-links"
        name = "Maximum links"
        description = "Limits the number of queued links."
        scope = "channel"
        required = true
        [settings.schema]
        minimum = 1
        maximum = 100
        kind = "integer"

        [[settings]]
        id = "minimum-score"
        name = "Minimum score"
        description = "Sets the score required for publication."
        scope = "channel"
        required = true
        [settings.schema]
        minimum = 0.0
        maximum = 10.0
        decimalPlaces = 1
        kind = "number"

        [[settings]]
        id = "wait-between-links"
        name = "Wait between links"
        description = "Sets the delay before another link is accepted."
        scope = "channel"
        required = true
        [settings.schema]
        minimumSeconds = 0
        maximumSeconds = 3600
        kind = "duration"

        [[settings]]
        id = "publish-time"
        name = "Publish time"
        description = "Sets the channel publication time."
        scope = "channel"
        required = true
        [settings.schema]
        maximumLength = 32
        kind = "text"
        [[features]]
        id = "collection"
        name = "Link collection"
        description = "Collects links from chat."
        settings = ["moderation-mode", "service-token", "collect-messages", "chat-command", "queue-note", "maximum-links", "minimum-score", "wait-between-links"]
        automationTemplates = []
        [features.twitch]
        scopes = ["moderator:read:chatters"]
        eventSubTypes = ["channel.chat.message"]

        [[features]]
        id = "publishing"
        name = "Scheduled publishing"
        description = "Publishes approved links on a schedule."
        settings = ["publish-time"]
        automationTemplates = []
        [features.twitch]
        scopes = []
        eventSubTypes = []
        """;

    public static ValidatedPluginManifest Load()
    {
        _ = SemanticVersion.TryCreate("0.13.0", out var minimum);
        var target = new PluginHostCompatibilityTarget(
            minimum,
            PluginApiVersion.V1,
            PluginRuntimeIdentifier.LinuxX64,
            []
        );
        return
            PluginManifestToml.Validate(Encoding.UTF8.GetBytes(_manifestToml), target)
                is PluginManifestValidationOutcome.Accepted accepted
            ? accepted.Manifest
            : throw new InvalidOperationException("The simulation plugin manifest is invalid.");
    }
}
