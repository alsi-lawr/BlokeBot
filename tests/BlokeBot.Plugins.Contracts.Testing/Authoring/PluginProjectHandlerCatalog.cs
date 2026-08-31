using System.Collections.Immutable;
using System.Diagnostics;

namespace BlokeBot.Plugins.Contracts.Testing;

internal sealed record PluginProjectHandlerDescriptor(
    string Module,
    string Operation,
    string InputType,
    string ResultType,
    string SkeletonStatement
);

internal sealed record PluginProjectDerivedInputDescriptor(
    string TypeName,
    PluginLuaSchemaDescriptor Schema,
    bool ExtendsSchema
);

internal sealed record PluginProjectHandlerCatalog(
    ImmutableArray<PluginProjectHandlerDescriptor> Handlers,
    ImmutableArray<PluginProjectDerivedInputDescriptor> DerivedInputs
)
{
    internal static PluginProjectHandlerCatalog Create(PluginManifest manifest)
    {
        var prefix = PluginProjectTypeEmitter.TypeName(manifest.Id.Value);
        var handlers = ImmutableArray.CreateBuilder<PluginProjectHandlerDescriptor>();
        var inputs = ImmutableArray.CreateBuilder<PluginProjectDerivedInputDescriptor>();
        foreach (var feature in manifest.Features)
        {
            handlers.AddRange(
                feature.DispatchDeclarations.Commands.Select(command =>
                    Handler(
                        command.Module,
                        command.Operation,
                        PluginInvocationInputSchemas.Command.LuaTypeName
                    )
                )
            );
            foreach (var @event in feature.DispatchDeclarations.Events)
            {
                var inputType =
                    $"{prefix}{PluginProjectTypeEmitter.TypeName(@event.Id.Value)}EventInput";
                inputs.Add(new(inputType, EventSchema(@event.Source), true));
                handlers.Add(Handler(@event.Module, @event.Operation, inputType));
            }
            handlers.AddRange(
                feature.DispatchDeclarations.Schedules.Select(schedule =>
                    Handler(schedule.Module, schedule.Operation, "BlokeBotScheduleInput")
                )
            );
            handlers.AddRange(
                feature.DispatchDeclarations.Webhooks.Select(webhook =>
                    Handler(
                        webhook.Module,
                        webhook.Operation,
                        PluginInvocationInputSchemas.Web.LuaTypeName,
                        "BlokeBotValue",
                        "return { status = 200, body = \"\" }"
                    )
                )
            );
            foreach (var action in feature.DispatchDeclarations.Actions)
            {
                switch (action)
                {
                    case PluginActionDescriptor.Http http:
                        handlers.Add(
                            Handler(
                                http.Module,
                                http.Operation,
                                PluginInvocationInputSchemas.Web.LuaTypeName
                            )
                        );
                        break;
                    case PluginActionDescriptor.Page page:
                        var inputType =
                            $"{prefix}{PluginProjectTypeEmitter.TypeName(feature.Id.Value)}{PluginProjectTypeEmitter.TypeName(page.Id.Value)}PageActionInput";
                        inputs.Add(new(inputType, PageActionSchema(inputType, page), false));
                        handlers.Add(Handler(page.Module, page.Operation, inputType));
                        break;
                }
            }
            handlers.AddRange(
                feature.DispatchDeclarations.Webhooks.SelectMany(webhook =>
                    webhook.Authentication is PluginWebhookAuthentication.Callback callback
                        ?
                        [
                            Handler(
                                callback.Module,
                                callback.Operation,
                                PluginInvocationInputSchemas.Web.LuaTypeName,
                                "boolean",
                                "return true"
                            ),
                        ]
                        : Array.Empty<PluginProjectHandlerDescriptor>()
                )
            );
        }
        handlers.AddRange(
            manifest.Migrations.Select(migration =>
                Handler(
                    migration.Module,
                    HostOperation(migration.EntryPoint),
                    PluginInvocationInputSchemas.Migration.LuaTypeName
                )
            )
        );
        handlers.AddRange(
            manifest.GeneratedPages.Select(page =>
                Handler(
                    page.Module,
                    HostOperation(page.RenderEntryPoint),
                    PluginInvocationInputSchemas.Page.LuaTypeName,
                    "BlokeBotValue",
                    "return {}"
                )
            )
        );
        handlers.AddRange(
            manifest.AutomationDefinitions.Select(definition =>
                Handler(
                    definition.Module,
                    HostOperation(definition.EntryPoint),
                    $"{prefix}{PluginProjectTypeEmitter.TypeName(definition.Id.Value)}Input",
                    $"{prefix}{PluginProjectTypeEmitter.TypeName(definition.Id.Value)}Output",
                    AutomationResult(definition)
                )
            )
        );
        return new(handlers.ToImmutable(), inputs.Distinct().ToImmutableArray());
    }

    private static PluginProjectHandlerDescriptor Handler(
        PluginLuaModuleId module,
        PluginHostOperationId operation,
        string inputType,
        string resultType = "BlokeBotValue",
        string skeletonStatement = "return input"
    ) => new(module.Value, operation.Value, inputType, resultType, skeletonStatement);

    private static PluginLuaSchemaDescriptor EventSchema(PluginEventSource source) =>
        source is PluginEventSource.Twitch ? PluginInvocationInputSchemas.TwitchEvent
        : source is PluginEventSource.TwitchRaw ? PluginInvocationInputSchemas.TwitchRawEvent
        : source is PluginEventSource.BlokeBot ? PluginInvocationInputSchemas.BlokeBotEvent
        : throw new UnreachableException("Unknown plugin event source.");

    private static PluginLuaSchemaDescriptor PageActionSchema(
        string typeName,
        PluginActionDescriptor.Page action
    ) =>
        new(
            typeName,
            $"Input delivered to the declared page action '{action.Id.Value}'.",
            [
                .. action
                    .Inputs.OrderBy(static field => field.Id.Value, StringComparer.Ordinal)
                    .Select(field => new PluginLuaFieldDescriptor(
                        field.Id.Value,
                        PluginLuaFieldShape.For(field.ValueKind),
                        field.Name,
                        field.Required
                    )),
            ]
        );

    private static PluginHostOperationId HostOperation(string value) =>
        PluginHostOperationId.TryCreate(value, out var operation)
            ? operation
            : throw new InvalidOperationException($"Invalid handler operation '{value}'.");

    private static string AutomationResult(PluginAutomationDefinitionDescriptor definition)
    {
        var fields = definition
            .Outputs.OrderBy(static field => field.Id.Value, StringComparer.Ordinal)
            .Select(field => $"[\"{field.Id.Value}\"] = {DefaultValue(field.ValueKind)}")
            .ToArray();
        return fields.Length == 0 ? "return {}" : $"return {{ {string.Join(", ", fields)} }}";
    }

    private static string DefaultValue(PluginValueKind kind) =>
        kind switch
        {
            PluginValueKind.Nil => "nil",
            PluginValueKind.Boolean => "false",
            PluginValueKind.Number => "0",
            PluginValueKind.String => "\"\"",
            PluginValueKind.Array or PluginValueKind.Map => "{}",
        };
}
