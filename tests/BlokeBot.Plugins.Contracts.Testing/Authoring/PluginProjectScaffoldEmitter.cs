namespace BlokeBot.Plugins.Contracts.Testing;

internal static class PluginProjectScaffoldEmitter
{
    internal static string Manifest(PluginId pluginId) =>
        $$"""
            manifestVersion = 1
            id = "{{pluginId.Value}}"
            name = "{{pluginId.Value}}"
            description = "A BlokeBot plugin."
            entryModule = "main"
            assets = []
            payloads = []
            migrations = []
            automationDefinitions = []
            automationTemplates = []
            generatedPages = []
            embeddedPages = []

            [marketplace]
            author = "Plugin author"
            tags = ["starter"]
            mediaUrls = []

            [release]
            declaredVersion = "0.1.0"
            tag = "{{pluginId.Value.Replace('.', '-')}}-v0.1.0"

            [compatibility]
            minimumApiVersion = 1
            maximumApiVersion = 1
            minimumBlokeBotVersion = "0.13.0"
            maximumBlokeBotVersionExclusive = "0.14.0"
            luaVersion = "lua54"
            supportedTargets = ["linux-x64", "linux-arm64", "osx-arm64", "win-x64", "win-arm64"]

            [[luaModules]]
            id = "main"
            path = "lua/main.lua"

            [[settings]]
            id = "response-message"
            name = "Response message"
            description = "Sets the starter command response."
            scope = "channel"
            required = false
            [settings.schema]
            kind = "text"
            maximumLength = 500

            [[features]]
            id = "starter"
            name = "Starter command"
            description = "Replies to the plugin starter command."
            settings = ["response-message"]
            automationTemplates = []
            [features.twitch]
            scopes = []
            eventSubTypes = []
            [features.dispatch]
            events = []
            schedules = []
            webhooks = []
            actions = []
            [[features.dispatch.commands]]
            route = "plugin-starter"
            module = "main"
            operation = "handle_command"
            [features.dispatch.commands.requirements]
            twitchReady = false

            [[hostModules]]
            id = "settings"
            minimumVersion = 1
            maximumVersion = 1

            [[hostModules]]
            id = "responses"
            minimumVersion = 1
            maximumVersion = 1
            """ + "\n";

    internal static string Lua(PluginManifest manifest)
    {
        var handlers =
            $"{PluginProjectTypeEmitter.TypeName(manifest.Id.Value)}{PluginProjectTypeEmitter.TypeName(manifest.EntryModule.Value)}Handlers";
        return $$"""
                local blokebot = require("blokebot")

                ---@type {{handlers}}
                local handlers = {
                  handle_command = function(input)
                    local settings = blokebot.settings.feature()
                    blokebot.responses.chat(settings["response-message"] or "Hello from {{manifest.Id.Value}}.")
                    return input
                  end,
                }

                return handlers
                """ + "\n";
    }

    internal static string Tests(PluginId pluginId) =>
        $$"""
            name = "{{pluginId.Value}}"

            [[scenarios]]
            name = "starter-command"
            workerMode = "admitted"
            invocationKind = "command"
            module = "main"
            operation = "handle_command"
            expectation = "returned"
            input = { route = "plugin-starter", arguments = [] }
            expectedHostCalls = ["settings.feature", "responses.chat"]
            """ + "\n";

    internal static string LuaLanguageServerConfiguration() =>
        """
            {
              "runtime": {
                "version": "Lua 5.4"
              },
              "workspace": {
                "library": [
                  "./.blokebot/lua/5.4/v1"
                ]
              }
            }
            """ + "\n";
}
