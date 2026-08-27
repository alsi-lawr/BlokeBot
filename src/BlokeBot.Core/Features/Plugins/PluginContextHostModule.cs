using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginContextHostModule : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Context;

    public ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PluginHostCallOutcome>(Unavailable());

    public ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginHostCall call,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginHostCallOutcome>(
            new PluginHostCallOutcome.Returned(Context(identity))
        );

    private static PluginValue.Map Context(PluginWorkerInvocationIdentity identity)
    {
        var properties = ImmutableArray.CreateBuilder<PluginValueProperty>();
        properties.Add(new("kind", Text(Kind(identity.Context.Kind))));
        properties.Add(new("pluginId", Text(identity.Plugin.PluginId.Value)));
        properties.Add(new("pluginVersion", Text(identity.Plugin.Release.DeclaredVersion.Value)));
        properties.Add(new("pluginTag", Text(identity.Plugin.Release.Tag.Value)));
        switch (identity.Context)
        {
            case PluginInvocationContext.Installation:
                break;
            case PluginInvocationContext.Channel channel:
                AddHostFeature(properties, identity);
                Add(properties, "actor", Actor(channel.Actor));
                Add(properties, "stream", Stream(channel.Stream));
                Add(properties, "command", Command(channel.Command));
                Add(properties, "event", Event(channel.Event));
                Add(properties, "schedule", Schedule(channel.Schedule));
                Add(properties, "web", Web(channel.Web));
                break;
            case PluginInvocationContext.Automation automation:
                AddHostFeature(properties, identity);
                properties.Add(
                    new(
                        "automation",
                        Map(
                            ("definitionId", Text(automation.Definition.Value)),
                            ("invocationId", Text(automation.InvocationId.Value.ToString("D")))
                        )
                    )
                );
                break;
            case PluginInvocationContext.Migration migration:
                properties.Add(
                    new(
                        "migration",
                        Map(
                            ("migrationId", Text(migration.MigrationId.Value)),
                            ("fromVersion", Text(migration.FromVersion.Value)),
                            ("toVersion", Text(migration.ToVersion.Value))
                        )
                    )
                );
                break;
            case PluginInvocationContext.Page page:
                AddHostFeature(properties, identity);
                properties.Add(
                    new(
                        "page",
                        Map(
                            ("pageId", Text(page.PageId.Value)),
                            ("sessionId", Text(page.SessionId.Value.ToString("D")))
                        )
                    )
                );
                break;
        }
        return new(properties.ToImmutable());
    }

    private static void AddHostFeature(
        ImmutableArray<PluginValueProperty>.Builder properties,
        PluginWorkerInvocationIdentity identity
    )
    {
        properties.Add(new("hostId", new PluginValue.Number(identity.Host.Value)));
        properties.Add(new("featureId", Text(identity.Feature.Value)));
    }

    private static PluginValue.Map? Actor(PluginActorContext? actor) =>
        actor is null
            ? null
            : Map(
                ("login", Text(actor.Login)),
                ("displayName", Text(actor.DisplayName)),
                ("twitchUserId", OptionalText(actor.TwitchUserId)),
                ("isBroadcaster", new PluginValue.Boolean(actor.IsBroadcaster)),
                ("isModerator", new PluginValue.Boolean(actor.IsModerator)),
                ("isSubscriber", new PluginValue.Boolean(actor.IsSubscriber))
            );

    private static PluginValue.Map? Stream(PluginStreamContext? stream) =>
        stream is null
            ? null
            : Map(
                ("streamId", OptionalText(stream.StreamId)),
                ("isLive", new PluginValue.Boolean(stream.IsLive))
            );

    private static PluginValue.Map? Command(PluginCommandInvocation? command) =>
        command is null
            ? null
            : Map(
                ("route", Text(command.Route)),
                (
                    "arguments",
                    new PluginValue.Array(
                        command
                            .Arguments.Select(static argument => (PluginValue)Text(argument))
                            .ToImmutableArray()
                    )
                )
            );

    private static PluginValue.Map? Event(PluginEventInvocation? @event) =>
        @event is null
            ? null
            : Map(
                ("handlerId", Text(@event.HandlerId.Value)),
                ("source", Text(@event.Source)),
                ("eventId", Text(@event.EventId)),
                ("occurredAt", Text(@event.OccurredAtUtc.ToUniversalTime().ToString("O")))
            );

    private static PluginValue.Map? Schedule(PluginScheduleInvocation? schedule) =>
        schedule is null
            ? null
            : Map(
                ("handlerId", Text(schedule.HandlerId.Value)),
                ("scheduleId", Text(schedule.ScheduleId.ToString("D"))),
                ("dueAt", Text(schedule.DueAtUtc.ToUniversalTime().ToString("O")))
            );

    private static PluginValue.Map? Web(PluginWebInvocation? web) =>
        web is null
            ? null
            : Map(
                ("kind", Text(web.Kind.ToString().ToLowerInvariant())),
                ("routeId", Text(web.RouteId)),
                ("method", Text(web.Method))
            );

    private static void Add(
        ImmutableArray<PluginValueProperty>.Builder properties,
        string name,
        PluginValue? value
    )
    {
        if (value is not null)
        {
            properties.Add(new(name, value));
        }
    }

    private static PluginValue.Map Map(params (string Name, PluginValue Value)[] properties) =>
        new(
            properties
                .Select(static property => new PluginValueProperty(property.Name, property.Value))
                .ToImmutableArray()
        );

    private static PluginValue.String Text(string value) => new(value);

    private static PluginValue OptionalText(string? value) =>
        value is null ? new PluginValue.Nil() : Text(value);

    private static string Kind(PluginInvocationContextKind kind) =>
        kind.ToString().ToLowerInvariant();

    private static PluginHostCallOutcome.Failed Unavailable() =>
        new(new(PluginHostFailureCode.Unavailable, "Plugin invocation context is unavailable."));
}
