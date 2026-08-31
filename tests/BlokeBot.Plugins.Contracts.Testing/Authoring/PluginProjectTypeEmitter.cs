using System.Text;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static partial class PluginProjectTypeEmitter
{
    internal static string Emit(PluginManifest manifest)
    {
        var output = new StringBuilder();
        var prefix = TypeName(manifest.Id.Value);
        _ = output.AppendLine("---@meta");
        _ = output.AppendLine();
        _ = output.AppendLine(
            "-- Generated from plugin.toml. Regenerate with blokebot-plugin generate; do not edit."
        );
        _ = output.AppendLine();
        AppendSettings(manifest, prefix, output);
        AppendAutomations(manifest, prefix, output);
        AppendHandlers(manifest, prefix, output);
        return output.ToString().TrimEnd() + "\n";
    }

    internal static string TypeName(string value) =>
        PluginLuaLanguageServerStubEmitter.PascalCase(value);

    private static void AppendSettings(PluginManifest manifest, string prefix, StringBuilder output)
    {
        var installation = manifest.Settings.Where(setting =>
            setting.Scope == PluginSettingScope.Installation
        );
        AppendSettingsClass($"{prefix}InstallationSettings", installation, output);
        _ = output
            .Append("---@class BlokeBotInstallationSettings: ")
            .Append(prefix)
            .AppendLine("InstallationSettings");
        _ = output.AppendLine();

        var featureSettings = new List<PluginSettingDescriptor>();
        foreach (var feature in manifest.Features)
        {
            var settings = manifest.Settings.Where(setting =>
                setting.Scope == PluginSettingScope.Channel && feature.Settings.Contains(setting.Id)
            );
            var materialized = settings.ToArray();
            featureSettings.AddRange(materialized);
            AppendSettingsClass(
                $"{prefix}{TypeName(feature.Id.Value)}FeatureSettings",
                materialized,
                output
            );
        }
        AppendSettingsClass(
            "BlokeBotFeatureSettings",
            featureSettings
                .DistinctBy(setting => setting.Id)
                .Select(setting => setting with { Required = false }),
            output
        );
    }

    private static void AppendSettingsClass(
        string name,
        IEnumerable<PluginSettingDescriptor> settings,
        StringBuilder output
    )
    {
        _ = output.Append("---@class ").AppendLine(name);
        foreach (
            var setting in settings.OrderBy(setting => setting.Id.Value, StringComparer.Ordinal)
        )
        {
            _ = output
                .Append("---@field [\"")
                .Append(setting.Id.Value)
                .Append("\"]")
                .Append(setting.Required ? string.Empty : "?")
                .Append(' ')
                .Append(SettingType(setting.Schema))
                .Append(" # ")
                .AppendLine(setting.Description);
        }
        _ = output.AppendLine();
    }

    private static string SettingType(PluginSettingSchema schema) =>
        schema.Match(
            static _ => "boolean",
            static _ => "string",
            static _ => "string",
            static _ => "integer",
            static _ => "number",
            static _ => "number",
            choice => string.Join('|', choice.Choices.Select(value => $"\"{value.Id.Value}\"")),
            static _ => "BlokeBotProtectedValue"
        );

    private static void AppendAutomations(
        PluginManifest manifest,
        string prefix,
        StringBuilder output
    )
    {
        foreach (var definition in manifest.AutomationDefinitions)
        {
            var name = $"{prefix}{TypeName(definition.Id.Value)}";
            AppendAutomationClass($"{name}InputValues", definition.Inputs, output);
            _ = output.Append("---@class ").Append(name).AppendLine("Input");
            _ = output.AppendLine("---@field configuration table<string, BlokeBotValue>");
            _ = output.Append("---@field inputs ").Append(name).AppendLine("InputValues");
            _ = output.AppendLine();
            AppendAutomationClass($"{name}Output", definition.Outputs, output);
        }
    }

    private static void AppendAutomationClass(
        string name,
        IEnumerable<PluginAutomationFieldDescriptor> fields,
        StringBuilder output
    )
    {
        _ = output.Append("---@class ").AppendLine(name);
        foreach (var field in fields.OrderBy(field => field.Id.Value, StringComparer.Ordinal))
        {
            _ = output
                .Append("---@field [\"")
                .Append(field.Id.Value)
                .Append("\"]")
                .Append(field.Required ? string.Empty : "?")
                .Append(' ')
                .Append(ValueType(field.ValueKind))
                .Append(" # ")
                .AppendLine(field.Name);
        }
        _ = output.AppendLine();
    }

    private static void AppendHandlers(PluginManifest manifest, string prefix, StringBuilder output)
    {
        var handlers = new List<Handler>();
        foreach (var feature in manifest.Features)
        {
            handlers.AddRange(
                feature.DispatchDeclarations.Commands.Select(command => new Handler(
                    command.Module.Value,
                    command.Operation.Value,
                    "BlokeBotCommandInput",
                    "BlokeBotValue"
                ))
            );
            foreach (var @event in feature.DispatchDeclarations.Events)
            {
                var input = $"{prefix}{TypeName(@event.Id.Value)}EventInput";
                AppendEventInput(input, @event.Source, output);
                handlers.Add(
                    new(@event.Module.Value, @event.Operation.Value, input, "BlokeBotValue")
                );
            }
            handlers.AddRange(
                feature.DispatchDeclarations.Schedules.Select(schedule => new Handler(
                    schedule.Module.Value,
                    schedule.Operation.Value,
                    "BlokeBotScheduleInput",
                    "BlokeBotValue"
                ))
            );
            handlers.AddRange(
                feature.DispatchDeclarations.Webhooks.Select(webhook => new Handler(
                    webhook.Module.Value,
                    webhook.Operation.Value,
                    "BlokeBotWebInput",
                    "BlokeBotValue"
                ))
            );
            handlers.AddRange(
                feature.DispatchDeclarations.Actions.Select(action => new Handler(
                    action.Module.Value,
                    action.Operation.Value,
                    "BlokeBotWebInput",
                    "BlokeBotValue"
                ))
            );
            handlers.AddRange(
                feature.DispatchDeclarations.Webhooks.SelectMany(webhook =>
                    webhook.Authentication is PluginWebhookAuthentication.Callback callback
                        ?
                        [
                            new Handler(
                                callback.Module.Value,
                                callback.Operation.Value,
                                "BlokeBotWebInput",
                                "boolean"
                            ),
                        ]
                        : Array.Empty<Handler>()
                )
            );
        }
        handlers.AddRange(
            manifest.Migrations.Select(migration => new Handler(
                migration.Module.Value,
                migration.EntryPoint,
                "BlokeBotValue",
                "BlokeBotValue"
            ))
        );
        handlers.AddRange(
            manifest.GeneratedPages.Select(page => new Handler(
                page.Module.Value,
                page.RenderEntryPoint,
                "BlokeBotPageInput",
                "BlokeBotValue"
            ))
        );
        handlers.AddRange(
            manifest.AutomationDefinitions.Select(definition => new Handler(
                definition.Module.Value,
                definition.EntryPoint,
                $"{prefix}{TypeName(definition.Id.Value)}Input",
                $"{prefix}{TypeName(definition.Id.Value)}Output"
            ))
        );

        AppendHandlerClass($"{prefix}Handlers", handlers, output);
        foreach (var module in manifest.LuaModules)
        {
            AppendHandlerClass(
                $"{prefix}{TypeName(module.Id.Value)}Handlers",
                handlers.Where(handler => handler.Module == module.Id.Value),
                output
            );
        }
    }

    private static void AppendHandlerClass(
        string name,
        IEnumerable<Handler> handlers,
        StringBuilder output
    )
    {
        _ = output.Append("---@class ").AppendLine(name);
        foreach (
            var group in handlers.GroupBy(handler => handler.Operation, StringComparer.Ordinal)
        )
        {
            var shapes = group
                .Select(handler => (handler.Input, handler.Result))
                .Distinct()
                .ToArray();
            var input = shapes.Length == 1 ? shapes[0].Input : "BlokeBotValue";
            var result = shapes.Length == 1 ? shapes[0].Result : "BlokeBotValue";
            _ = output
                .Append("---@field [\"")
                .Append(group.Key)
                .Append("\"] fun(input: ")
                .Append(input)
                .Append("): ")
                .AppendLine(result);
        }
        _ = output.AppendLine();
    }
}
