using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed record AutomationFixtureInputValidity(string Key, bool Valid);

public sealed class AutomationScenarioEditor(AutomationScenarioFixture fixture)
{
    public AutomationScenarioId? Id { get; set; }
    public string Name { get; set; } = "New scenario";
    public AutomationScenarioFixture Fixture { get; private set; } = fixture;

    public void Replace(AutomationScenarioFixture replacement) => Fixture = replacement;

    public void SetClock(DateTimeOffset value) =>
        Fixture = Fixture with { ClockUtc = value, SyntheticRecipe = null };

    public void SetSeed(ulong value) =>
        Fixture = Fixture with { Seed = value, SyntheticRecipe = null };

    public AutomationValue SourceValue(AutomationPortMetadata field) =>
        field.Id.Value switch
        {
            "actor" => Fixture.Context.Actor is { } actor
                ? new AutomationValue.Actor(new(actor.Login, actor.DisplayName))
                : new AutomationValue.Null(field.ValueType),
            "channel" => new AutomationValue.Channel(
                new(Fixture.Context.Channel.Login, Fixture.Context.Channel.DisplayName)
            ),
            "stream" => Fixture.Context.Stream is { } stream
                ? new AutomationValue.Stream(
                    new(stream.Title, stream.GameName, stream.StartedAtUtc)
                )
                : new AutomationValue.Null(field.ValueType),
            "arguments" => new AutomationValue.Arguments([
                .. Fixture.Context.Arguments.Select(value => new AutomationValueArgument(
                    value.Position,
                    value.Value,
                    [AutomationValueProvenance.Generated]
                )),
            ]),
            "event-time" => new AutomationValue.Timestamp(Fixture.Context.Timestamps.OccurredAtUtc),
            _ => Fixture
                .Context.Variables.ForExecution()
                .TryGetValue(new(field.Id.Value), out var variable)
                ? variable.Value
                : new AutomationValue.Null(field.ValueType),
        };

    public void SetSourceValue(AutomationPortMetadata field, AutomationValue value)
    {
        var context = Fixture.Context;
        context = (field.Id.Value, value) switch
        {
            ("actor", AutomationValue.Actor actor) => context with
            {
                Actor = new(
                    context.Actor?.TwitchUserId ?? "sample-viewer",
                    actor.Value.Login,
                    actor.Value.DisplayName
                ),
            },
            ("actor", AutomationValue.Null) => context with { Actor = null },
            ("channel", AutomationValue.Channel channel) => context with
            {
                Channel = context.Channel with
                {
                    Login = channel.Value.Login,
                    DisplayName = channel.Value.DisplayName,
                },
            },
            ("stream", AutomationValue.Stream stream) => context with
            {
                Stream = new(
                    context.Stream?.TwitchStreamId ?? "sample-stream",
                    stream.Value.Title,
                    stream.Value.GameName,
                    stream.Value.StartedAtUtc
                ),
            },
            ("stream", AutomationValue.Null) => context with { Stream = null },
            ("arguments", AutomationValue.Arguments arguments) => context with
            {
                Arguments =
                [
                    .. arguments.Values.Select(argument => new AutomationArgument(
                        argument.Position,
                        argument.Value
                    )),
                ],
            },
            ("event-time", AutomationValue.Timestamp timestamp) => context with
            {
                Timestamps = context.Timestamps with { OccurredAtUtc = timestamp.Value },
            },
            _ => context with
            {
                Variables = new(
                    context
                        .Variables.ForExecution()
                        .ToImmutableDictionary()
                        .SetItem(new(field.Id.Value), new(value, field.Sensitivity))
                ),
            },
        };
        Fixture = Fixture with { Context = context, SyntheticRecipe = null };
    }

    public void SetEffect(AutomationNodeId nodeId, AutomationScenarioEffectResult result) =>
        Fixture = Fixture with
        {
            Effects =
            [
                .. Fixture.Effects.Where(effect => effect.NodeId != nodeId),
                new(nodeId, result),
            ],
            SyntheticRecipe = null,
        };

    public void SetConnected(
        AutomationNodeId node,
        AutomationPortMetadata port,
        AutomationValue value
    ) =>
        Fixture = Fixture with
        {
            ConnectedInputs =
            [
                .. Fixture.ConnectedInputs.Where(input =>
                    input.NodeId != node || input.PortId != port.Id
                ),
                new(node, port.Id, new(value, port.Sensitivity)),
            ],
            SyntheticRecipe = null,
        };

    public void RemoveConnected(AutomationNodeId node, AutomationPortId port) =>
        Fixture = Fixture with
        {
            ConnectedInputs =
            [
                .. Fixture.ConnectedInputs.Where(input =>
                    input.NodeId != node || input.PortId != port
                ),
            ],
            SyntheticRecipe = null,
        };
}
