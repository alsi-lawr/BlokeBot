using System.Security.Cryptography;
using System.Text;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal static class ExpandedAutomationContext
{
    internal static Guid Occurrence(int hostId, string identity) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{hostId}:{identity}")).AsSpan(0, 16));

    internal static AutomationContext Create(
        BotHost host,
        AutomationDefinitionId source,
        string identity,
        DateTimeOffset at,
        DateTimeOffset received,
        IEnumerable<KeyValuePair<AutomationVariableName, AutomationVariable>> variables,
        AutomationActor? actor = null,
        AutomationStream? stream = null
    ) =>
        new(
            new(Occurrence(host.Id, $"{source.Value}:{identity}"), source),
            actor,
            new(new(host.Id), host.TwitchUserId ?? "", host.Login, host.Login),
            stream,
            new(at, received),
            [],
            new(variables)
        );

    internal static KeyValuePair<AutomationVariableName, AutomationVariable> Text(
        string key,
        string value,
        bool sensitive = false
    ) =>
        new(
            new(key),
            new(
                new AutomationValue.Text(value.Length > 500 ? value[..500] : value),
                sensitive ? AutomationDataSensitivity.Sensitive : AutomationDataSensitivity.Safe
            )
        );

    internal static KeyValuePair<AutomationVariableName, AutomationVariable> Number(
        string key,
        decimal value
    ) => new(new(key), new(new AutomationValue.Number(value), AutomationDataSensitivity.Safe));

    internal static KeyValuePair<AutomationVariableName, AutomationVariable> Boolean(
        string key,
        bool value
    ) => new(new(key), new(new AutomationValue.Boolean(value), AutomationDataSensitivity.Safe));

    internal static KeyValuePair<AutomationVariableName, AutomationVariable> Time(
        string key,
        DateTimeOffset value
    ) => new(new(key), new(new AutomationValue.Timestamp(value), AutomationDataSensitivity.Safe));
}
