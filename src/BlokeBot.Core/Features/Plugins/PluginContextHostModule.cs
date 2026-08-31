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

    private static PluginValue.Map Context(PluginWorkerInvocationIdentity identity) =>
        identity.Context switch
        {
            PluginInvocationContext.Installation =>
                PluginStructuredValueSchemas.InstallationInvocationContext.Create(
                    Common(identity)
                        .Append(
                            PluginStructuredValueSchemas.InstallationKind.Value(
                                Text("installation")
                            )
                        )
                        .ToArray()
                ),
            PluginInvocationContext.Channel channel => Channel(identity, channel),
            PluginInvocationContext.Automation automation => Automation(identity, automation),
            PluginInvocationContext.Migration migration => Migration(identity, migration),
            PluginInvocationContext.Page page => Page(identity, page),
            _ => throw new InvalidOperationException("Unknown plugin invocation context."),
        };

    private static PluginValue.Map Channel(
        PluginWorkerInvocationIdentity identity,
        PluginInvocationContext.Channel channel
    )
    {
        var values = Feature(
            identity,
            PluginStructuredValueSchemas.ChannelKind.Value(Text("channel"))
        );
        Add(values, PluginStructuredValueSchemas.ChannelActor, Actor(channel.Actor));
        Add(values, PluginStructuredValueSchemas.ChannelStream, Stream(channel.Stream));
        Add(values, PluginStructuredValueSchemas.ChannelCommand, Command(channel.Command));
        Add(values, PluginStructuredValueSchemas.ChannelEvent, Event(channel.Event));
        Add(values, PluginStructuredValueSchemas.ChannelSchedule, Schedule(channel.Schedule));
        Add(values, PluginStructuredValueSchemas.ChannelWeb, Web(channel.Web));
        return PluginStructuredValueSchemas.ChannelInvocationContext.Create(values.ToArray());
    }

    private static PluginValue.Map Automation(
        PluginWorkerInvocationIdentity identity,
        PluginInvocationContext.Automation automation
    )
    {
        var values = Feature(
            identity,
            PluginStructuredValueSchemas.AutomationKind.Value(Text("automation"))
        );
        values.Add(
            PluginStructuredValueSchemas.InvocationAutomation.Value(
                PluginStructuredValueSchemas.AutomationContext.Create(
                    PluginStructuredValueSchemas.AutomationDefinitionId.Value(
                        Text(automation.Definition.Value)
                    ),
                    PluginStructuredValueSchemas.AutomationInvocationId.Value(
                        Text(automation.InvocationId.Value.ToString("D"))
                    )
                )
            )
        );
        return PluginStructuredValueSchemas.AutomationInvocationContext.Create(values.ToArray());
    }

    private static PluginValue.Map Migration(
        PluginWorkerInvocationIdentity identity,
        PluginInvocationContext.Migration migration
    ) =>
        PluginStructuredValueSchemas.MigrationInvocationContext.Create(
            Common(identity)
                .Append(PluginStructuredValueSchemas.MigrationKind.Value(Text("migration")))
                .Append(
                    PluginStructuredValueSchemas.InvocationMigration.Value(
                        PluginStructuredValueSchemas.MigrationContext.Create(
                            PluginStructuredValueSchemas.MigrationId.Value(
                                Text(migration.MigrationId.Value)
                            ),
                            PluginStructuredValueSchemas.MigrationFromVersion.Value(
                                Text(migration.FromVersion.Value)
                            ),
                            PluginStructuredValueSchemas.MigrationToVersion.Value(
                                Text(migration.ToVersion.Value)
                            )
                        )
                    )
                )
                .ToArray()
        );

    private static PluginValue.Map Page(
        PluginWorkerInvocationIdentity identity,
        PluginInvocationContext.Page page
    )
    {
        var values = Feature(identity, PluginStructuredValueSchemas.PageKind.Value(Text("page")));
        values.Add(
            PluginStructuredValueSchemas.InvocationPage.Value(
                PluginStructuredValueSchemas.PageContext.Create(
                    PluginStructuredValueSchemas.PageId.Value(Text(page.PageId.Value)),
                    PluginStructuredValueSchemas.PageSessionId.Value(
                        Text(page.SessionId.Value.ToString("D"))
                    )
                )
            )
        );
        return PluginStructuredValueSchemas.PageInvocationContext.Create(values.ToArray());
    }

    private static PluginValue.Map? Actor(PluginActorContext? actor) =>
        actor is null
            ? null
            : PluginStructuredValueSchemas.ActorContext.Create(
                PluginStructuredValueSchemas.ActorLogin.Value(Text(actor.Login)),
                PluginStructuredValueSchemas.ActorDisplayName.Value(Text(actor.DisplayName)),
                PluginStructuredValueSchemas.ActorTwitchUserId.Value(
                    actor.TwitchUserId is null ? new PluginValue.Nil() : Text(actor.TwitchUserId)
                ),
                PluginStructuredValueSchemas.ActorIsBroadcaster.Value(
                    new PluginValue.Boolean(actor.IsBroadcaster)
                ),
                PluginStructuredValueSchemas.ActorIsModerator.Value(
                    new PluginValue.Boolean(actor.IsModerator)
                ),
                PluginStructuredValueSchemas.ActorIsSubscriber.Value(
                    new PluginValue.Boolean(actor.IsSubscriber)
                )
            );

    private static PluginValue.Map? Stream(PluginStreamContext? stream) =>
        stream is null
            ? null
            : PluginStructuredValueSchemas.StreamContext.Create(
                PluginStructuredValueSchemas.StreamId.Value(
                    stream.StreamId is null ? new PluginValue.Nil() : Text(stream.StreamId)
                ),
                PluginStructuredValueSchemas.StreamIsLive.Value(
                    new PluginValue.Boolean(stream.IsLive)
                )
            );

    private static PluginValue.Map? Command(PluginCommandInvocation? command) =>
        command is null
            ? null
            : PluginStructuredValueSchemas.CommandContext.Create(
                PluginStructuredValueSchemas.CommandRoute.Value(Text(command.Route)),
                PluginStructuredValueSchemas.CommandArguments.Value(
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
            : PluginStructuredValueSchemas.EventContext.Create(
                PluginStructuredValueSchemas.EventHandlerId.Value(Text(@event.HandlerId.Value)),
                PluginStructuredValueSchemas.EventSource.Value(Text(@event.Source)),
                PluginStructuredValueSchemas.EventId.Value(Text(@event.EventId)),
                PluginStructuredValueSchemas.EventOccurredAt.Value(
                    Text(@event.OccurredAtUtc.ToUniversalTime().ToString("O"))
                )
            );

    private static PluginValue.Map? Schedule(PluginScheduleInvocation? schedule) =>
        schedule is null
            ? null
            : PluginStructuredValueSchemas.ScheduleContext.Create(
                PluginStructuredValueSchemas.ScheduleHandlerId.Value(
                    Text(schedule.HandlerId.Value)
                ),
                PluginStructuredValueSchemas.ScheduleId.Value(
                    Text(schedule.ScheduleId.ToString("D"))
                ),
                PluginStructuredValueSchemas.ScheduleDueAt.Value(
                    Text(schedule.DueAtUtc.ToUniversalTime().ToString("O"))
                )
            );

    private static PluginValue.Map? Web(PluginWebInvocation? web) =>
        web is null
            ? null
            : PluginStructuredValueSchemas.WebContext.Create(
                PluginStructuredValueSchemas.WebKind.Value(
                    Text(web.Kind.ToString().ToLowerInvariant())
                ),
                PluginStructuredValueSchemas.WebRouteId.Value(Text(web.RouteId)),
                PluginStructuredValueSchemas.WebMethod.Value(Text(web.Method))
            );

    private static PluginLuaFieldValue[] Common(PluginWorkerInvocationIdentity identity) =>
        [
            PluginStructuredValueSchemas.ContextPluginId.Value(
                Text(identity.Plugin.PluginId.Value)
            ),
            PluginStructuredValueSchemas.ContextPluginVersion.Value(
                Text(identity.Plugin.Release.DeclaredVersion.Value)
            ),
            PluginStructuredValueSchemas.ContextPluginTag.Value(
                Text(identity.Plugin.Release.Tag.Value)
            ),
        ];

    private static List<PluginLuaFieldValue> Feature(
        PluginWorkerInvocationIdentity identity,
        PluginLuaFieldValue kind
    ) =>
        [
            .. Common(identity),
            kind,
            PluginStructuredValueSchemas.ContextHostId.Value(
                new PluginValue.Number(identity.Host.Value)
            ),
            PluginStructuredValueSchemas.ContextFeatureId.Value(Text(identity.Feature.Value)),
        ];

    private static void Add(
        ICollection<PluginLuaFieldValue> values,
        PluginLuaFieldDescriptor field,
        PluginValue? value
    )
    {
        if (value is not null)
        {
            values.Add(field.Value(value));
        }
    }

    private static PluginValue.String Text(string value) => new(value);

    private static PluginHostCallOutcome.Failed Unavailable() =>
        new(new(PluginHostFailureCode.Unavailable, "Plugin invocation context is unavailable."));
}
