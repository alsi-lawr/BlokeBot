namespace BlokeBot.Plugins.Contracts;

public static partial class PluginManifestValidator
{
    private static void ValidateDispatchDeclarations(
        PluginManifest manifest,
        PluginFeatureDescriptor feature,
        List<PluginManifestError> errors
    )
    {
        var dispatch = feature.DispatchDeclarations;
        var modules = manifest.LuaModules.Select(static module => module.Id).ToHashSet();
        var valid =
            dispatch.Commands.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
            && dispatch.Events.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
            && dispatch.Schedules.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
            && dispatch.Webhooks.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
            && dispatch.Actions.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
            && dispatch.Commands.Select(static command => command.Route).Distinct().Count()
                == dispatch.Commands.Length
            && dispatch.Events.Select(static handler => handler.Id).Distinct().Count()
                == dispatch.Events.Length
            && dispatch.Schedules.Select(static handler => handler.Id).Distinct().Count()
                == dispatch.Schedules.Length
            && dispatch.Webhooks.Select(static hook => hook.Id).Distinct().Count()
                == dispatch.Webhooks.Length
            && dispatch.Actions.Select(static action => action.Id).Distinct().Count()
                == dispatch.Actions.Length
            && dispatch.Commands.All(command =>
                ValidCommandRoute(command.Route)
                && modules.Contains(command.Module)
                && command.Operation is not null
                && command.Requirements is not null
            )
            && dispatch.Events.All(handler =>
                handler.Id is not null
                && ValidEventHandler(feature, handler)
                && modules.Contains(handler.Module)
                && handler.Operation is not null
                && handler.Requirements is not null
            )
            && dispatch.Schedules.All(handler =>
                handler.Id is not null
                && modules.Contains(handler.Module)
                && handler.Operation is not null
                && handler.Requirements is not null
            )
            && dispatch.Webhooks.All(hook =>
                hook.Id is not null
                && modules.Contains(hook.Module)
                && hook.Operation is not null
                && hook.Requirements is not null
                && ValidWebhookAuthentication(hook.Authentication, modules)
            )
            && dispatch.Actions.All(action =>
                action.Id is not null
                && modules.Contains(action.Module)
                && action.Operation is not null
                && action.Requirements is not null
                && ValidAction(action)
            )
            && RawEventSubDeclarationsAreComplete(feature, dispatch);
        if (!valid)
        {
            errors.Add(
                new(PluginManifestErrorCode.InvalidDispatchDeclaration, "$.features.dispatch")
            );
        }
    }

    private static bool ValidCommandRoute(string route) =>
        route is { Length: >= 1 and <= 64 }
        && route == route.Trim().ToLowerInvariant()
        && route.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool ValidEventHandler(
        PluginFeatureDescriptor feature,
        PluginEventHandlerDescriptor handler
    ) =>
        handler.Source switch
        {
            PluginEventSource.Twitch twitch => Enum.IsDefined(twitch.Kind)
                && handler.Requirements is { TwitchReady: true }
                && PluginTwitchEventRequirements
                    .EventSubTypes(twitch.Kind)
                    .All(feature.Twitch.EventSubTypes.Contains),
            PluginEventSource.TwitchRaw raw => handler.Requirements is { TwitchReady: true }
                && ValidEventSubType(raw.EventSubType)
                && ValidEventSubVersion(raw.Version)
                && !PluginTwitchEventRequirements.IsTypedEventSubType(raw.EventSubType)
                && feature.Twitch.EventSubTypes.Contains(raw.EventSubType),
            PluginEventSource.BlokeBot blokeBot => Enum.IsDefined(blokeBot.Kind),
            _ => false,
        };

    private static bool RawEventSubDeclarationsAreComplete(
        PluginFeatureDescriptor feature,
        PluginDispatchDeclarations dispatch
    )
    {
        var arbitraryTypes = feature
            .Twitch.EventSubTypes.Where(static eventSubType =>
                !PluginTwitchEventRequirements.IsTypedEventSubType(eventSubType)
            )
            .ToArray();
        var rawHandlers = dispatch
            .Events.Select(static handler => handler.Source)
            .OfType<PluginEventSource.TwitchRaw>()
            .ToArray();
        return rawHandlers.Select(static raw => raw.EventSubType).Distinct().Count()
                == rawHandlers.Length
            && arbitraryTypes.Length == rawHandlers.Length
            && arbitraryTypes.All(eventSubType =>
                rawHandlers.Any(raw => raw.EventSubType == eventSubType)
            );
    }

    private static bool ValidEventSubVersion(string version) =>
        version is { Length: >= 1 and <= 16 } && version.All(char.IsAsciiDigit);

    private static bool ValidWebhookAuthentication(
        PluginWebhookAuthentication? authentication,
        IReadOnlySet<PluginLuaModuleId> modules
    ) =>
        authentication switch
        {
            PluginWebhookAuthentication.Public => true,
            PluginWebhookAuthentication.Callback callback => modules.Contains(callback.Module)
                && callback.Operation is not null,
            _ => false,
        };

    private static bool ValidAction(PluginActionDescriptor action) =>
        action switch
        {
            PluginActionDescriptor.Http => true,
            PluginActionDescriptor.Page page => !page.Inputs.IsDefault
                && page.Inputs.Length <= PluginContractLimits.MaximumPageFields
                && page.Inputs.Select(static input => input.Id).Distinct().Count()
                    == page.Inputs.Length
                && page.Inputs.All(input =>
                    input.Id is not null
                    && IsValidText(input.Name, required: true)
                    && Enum.IsDefined(input.ValueKind)
                    && input.ValueKind != PluginValueKind.Nil
                ),
            _ => false,
        };

    private static void ValidatePageActionHandlerContracts(
        PluginManifest manifest,
        List<PluginManifestError> errors
    )
    {
        var entrypoints = HandlerEntrypoints(manifest).ToArray();
        if (
            manifest
                .Features.SelectMany(static feature => feature.DispatchDeclarations.Actions)
                .OfType<PluginActionDescriptor.Page>()
                .Where(static page => page.Module is not null && page.Operation is not null)
                .Any(page =>
                    entrypoints.Count(entrypoint =>
                        entrypoint.Module == page.Module
                        && entrypoint.Operation == page.Operation.Value
                    ) != 1
                )
        )
        {
            errors.Add(
                new(
                    PluginManifestErrorCode.InvalidDispatchDeclaration,
                    "$.features.dispatch.actions"
                )
            );
        }
    }

    private static IEnumerable<(PluginLuaModuleId Module, string Operation)> HandlerEntrypoints(
        PluginManifest manifest
    )
    {
        foreach (var feature in manifest.Features)
        {
            var dispatch = feature.DispatchDeclarations;
            foreach (var callback in dispatch.Commands)
            {
                if (callback.Module is not null && callback.Operation is not null)
                {
                    yield return (callback.Module, callback.Operation.Value);
                }
            }
            foreach (var callback in dispatch.Events)
            {
                if (callback.Module is not null && callback.Operation is not null)
                {
                    yield return (callback.Module, callback.Operation.Value);
                }
            }
            foreach (var callback in dispatch.Schedules)
            {
                if (callback.Module is not null && callback.Operation is not null)
                {
                    yield return (callback.Module, callback.Operation.Value);
                }
            }
            foreach (var callback in dispatch.Webhooks)
            {
                if (callback.Module is not null && callback.Operation is not null)
                {
                    yield return (callback.Module, callback.Operation.Value);
                }
                if (callback.Authentication is PluginWebhookAuthentication.Callback authentication)
                {
                    if (authentication.Module is not null && authentication.Operation is not null)
                    {
                        yield return (authentication.Module, authentication.Operation.Value);
                    }
                }
            }
            foreach (var callback in dispatch.Actions)
            {
                if (callback.Module is not null && callback.Operation is not null)
                {
                    yield return (callback.Module, callback.Operation.Value);
                }
            }
        }
        foreach (var migration in manifest.Migrations)
        {
            yield return (migration.Module, migration.EntryPoint);
        }
        foreach (var page in manifest.GeneratedPages)
        {
            yield return (page.Module, page.RenderEntryPoint);
        }
        foreach (var automation in manifest.AutomationDefinitions)
        {
            yield return (automation.Module, automation.EntryPoint);
        }
    }
}
