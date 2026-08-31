namespace BlokeBot.Plugins.Contracts;

public static class PluginInvocationInputs
{
    public static PluginValue.Map Command(string route, IEnumerable<string> arguments) =>
        PluginInvocationInputSchemas.Command.Create(
            PluginInvocationInputSchemas.CommandRoute.Value(new PluginValue.String(route)),
            PluginInvocationInputSchemas.CommandArguments.Value(
                new PluginValue.Array([
                    .. arguments.Select(static argument =>
                        (PluginValue)new PluginValue.String(argument)
                    ),
                ])
            )
        );

    public static PluginValue.Map Web(
        string method,
        IEnumerable<KeyValuePair<string, string>> headers,
        ReadOnlySpan<byte> body
    ) =>
        PluginInvocationInputSchemas.Web.Create(
            PluginInvocationInputSchemas.WebMethod.Value(
                new PluginValue.String(method.ToUpperInvariant())
            ),
            PluginInvocationInputSchemas.WebHeaders.Value(
                new PluginValue.Map([
                    .. headers
                        .OrderBy(static header => header.Key, StringComparer.Ordinal)
                        .Select(static header => new PluginValueProperty(
                            header.Key.ToLowerInvariant(),
                            new PluginValue.String(header.Value)
                        )),
                ])
            ),
            PluginInvocationInputSchemas.WebBodyBase64.Value(
                new PluginValue.String(Convert.ToBase64String(body))
            )
        );

    public static PluginValue.Map Page(PluginHostId hostId, PluginPageSessionId sessionId) =>
        PluginInvocationInputSchemas.Page.Create(
            PluginInvocationInputSchemas.PageVersion.Value(new PluginValue.Number(1)),
            PluginInvocationInputSchemas.PageHostId.Value(new PluginValue.Number(hostId.Value)),
            PluginInvocationInputSchemas.PageSessionId.Value(
                new PluginValue.String(sessionId.Value.ToString("D"))
            )
        );

    public static PluginValue.Map BlokeBotEvent(string eventId, string source) =>
        PluginInvocationInputSchemas.BlokeBotEvent.Create(
            PluginInvocationInputSchemas.EventId.Value(new PluginValue.String(eventId)),
            PluginInvocationInputSchemas.EventSource.Value(new PluginValue.String(source))
        );

    public static PluginValue.Map TwitchEvent(
        string eventId,
        string source,
        DateTimeOffset occurredAtUtc
    ) =>
        PluginInvocationInputSchemas.TwitchEvent.Create(
            PluginInvocationInputSchemas.EventId.Value(new PluginValue.String(eventId)),
            PluginInvocationInputSchemas.EventSource.Value(new PluginValue.String(source)),
            PluginInvocationInputSchemas.EventOccurredAt.Value(
                new PluginValue.String(occurredAtUtc.ToUniversalTime().ToString("O"))
            )
        );

    public static PluginValue.Map TwitchRawEvent(
        string subscriptionType,
        string subscriptionVersion,
        PluginValue.Map eventPayload
    )
    {
        var subscription = PluginInvocationInputSchemas.TwitchRawSubscription.Create(
            PluginInvocationInputSchemas.RawSubscriptionType.Value(
                new PluginValue.String(subscriptionType)
            ),
            PluginInvocationInputSchemas.RawSubscriptionVersion.Value(
                new PluginValue.String(subscriptionVersion)
            )
        );
        return PluginInvocationInputSchemas.TwitchRawEvent.Create(
            PluginInvocationInputSchemas.RawSubscription.Value(subscription),
            PluginInvocationInputSchemas.RawEvent.Value(eventPayload)
        );
    }

    public static PluginValue.Map Migration(PluginMigrationDescriptor migration) =>
        PluginInvocationInputSchemas.Migration.Create(
            PluginInvocationInputSchemas.MigrationId.Value(
                new PluginValue.String(migration.Id.Value)
            ),
            PluginInvocationInputSchemas.MigrationFromVersion.Value(
                new PluginValue.String(migration.FromVersion.Value)
            ),
            PluginInvocationInputSchemas.MigrationToVersion.Value(
                new PluginValue.String(migration.ToVersion.Value)
            )
        );
}
