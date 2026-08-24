using System.Text;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Simulation;

internal static class SimulationPluginFeatureManifest
{
    private const string _manifestJson = """
        {
          "manifestVersion": 1,
          "id": "community.link-queue",
          "name": "Community link queue",
          "description": "Collects community links and publishes them on a schedule.",
          "release": { "declaredVersion": "1.2.0", "tag": "community-link-queue" },
          "compatibility": {
            "minimumApiVersion": 1,
            "maximumApiVersion": 1,
            "minimumBlokeBotVersion": "0.13.0",
            "maximumBlokeBotVersionExclusive": "0.14.0",
            "luaVersion": "lua54"
          },
          "entryModule": "main",
          "luaModules": [{ "id": "main", "path": "lua/main.lua" }],
          "assets": [],
          "payloads": [],
          "settings": [
            {
              "id": "moderation-mode", "name": "Moderation mode",
              "description": "Controls how submitted links are admitted.",
              "scope": "installation", "required": true,
              "schema": { "kind": "choice", "choices": [
                { "id": "manual", "name": "Manual review" },
                { "id": "automatic", "name": "Automatic review" }
              ] }
            },
            {
              "id": "service-token", "name": "Service token",
              "description": "Connects the installation to the queue service.",
              "scope": "installation", "required": false,
              "schema": { "kind": "secret", "maximumLength": 256 }
            },
            {
              "id": "collect-messages", "name": "Collect messages",
              "description": "Allows links from chat.", "scope": "channel", "required": true,
              "schema": { "kind": "boolean" }
            },
            {
              "id": "chat-command", "name": "Chat command",
              "description": "Sets the link command.", "scope": "channel", "required": true,
              "schema": { "kind": "text", "maximumLength": 24 }
            },
            {
              "id": "queue-note", "name": "Queue note",
              "description": "Shows a note with the channel queue.", "scope": "channel", "required": false,
              "schema": { "kind": "multilineText", "maximumLength": 500 }
            },
            {
              "id": "maximum-links", "name": "Maximum links",
              "description": "Limits the number of queued links.", "scope": "channel", "required": true,
              "schema": { "kind": "integer", "minimum": 1, "maximum": 100 }
            },
            {
              "id": "minimum-score", "name": "Minimum score",
              "description": "Sets the score required for publication.", "scope": "channel", "required": true,
              "schema": { "kind": "number", "minimum": 0, "maximum": 10, "decimalPlaces": 1 }
            },
            {
              "id": "wait-between-links", "name": "Wait between links",
              "description": "Sets the delay before another link is accepted.", "scope": "channel", "required": true,
              "schema": { "kind": "duration", "minimumSeconds": 0, "maximumSeconds": 3600 }
            },
            {
              "id": "publish-time", "name": "Publish time",
              "description": "Sets the channel publication time.", "scope": "channel", "required": true,
              "schema": { "kind": "text", "maximumLength": 32 }
            }
          ],
          "features": [
            {
              "id": "collection", "name": "Link collection",
              "description": "Collects links from chat.",
              "settings": ["moderation-mode", "service-token", "collect-messages", "chat-command", "queue-note", "maximum-links", "minimum-score", "wait-between-links"],
              "twitch": { "scopes": ["moderator:read:chatters"], "eventSubTypes": ["channel.chat.message"] },
              "automationTemplates": []
            },
            {
              "id": "publishing", "name": "Scheduled publishing",
              "description": "Publishes approved links on a schedule.",
              "settings": ["publish-time"],
              "twitch": { "scopes": [], "eventSubTypes": [] },
              "automationTemplates": []
            }
          ],
          "hostModules": [], "migrations": [], "automationDefinitions": [],
          "automationTemplates": [], "generatedPages": [], "embeddedPages": []
        }
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
            PluginManifestJson.Validate(Encoding.UTF8.GetBytes(_manifestJson), target)
                is PluginManifestValidationOutcome.Accepted accepted
            ? accepted.Manifest
            : throw new InvalidOperationException("The simulation plugin manifest is invalid.");
    }
}
