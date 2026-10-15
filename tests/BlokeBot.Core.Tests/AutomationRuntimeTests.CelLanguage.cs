using System.Collections.Immutable;
using System.Globalization;
using BlokeBot.Core.Features.Automations;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments("5", "5")]
    [Arguments("  +1.25  ", "1.25")]
    [Arguments("-1.25", "-1.25")]
    [Arguments(".5", "0.5")]
    [Arguments("5.", "5")]
    [Arguments("1e2", "100")]
    [Arguments("79228162514264337593543950335", "79228162514264337593543950335")]
    [Arguments("0.123456789012345678901234567890", "0.1234567890123456789012345679")]
    [Arguments("1e-100", "0")]
    public void CelLanguage_NativeDecimalTextAgreesAcrossAllThreeScopes(
        string text,
        string expected
    )
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-GB", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                AssertLanguageValue(
                    "decimal(arguments[0])",
                    [text],
                    new AutomationValue.Number(
                        decimal.Parse(expected, CultureInfo.InvariantCulture)
                    )
                );
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("private-not-a-number")]
    [Arguments("1,000")]
    [Arguments("1,5")]
    [Arguments("NaN")]
    [Arguments("Infinity")]
    [Arguments("1e100")]
    [Arguments("79228162514264337593543950336")]
    public void CelLanguage_NativeInvalidTextRemainsControlledAndAtomic(string text)
    {
        var expression = LanguageExpression("decimal(arguments[0])");
        var context = LanguageContext([text]);
        _ = new AutomationExpressionService()
            .Evaluate(expression, context)
            .ShouldBeOfType<AutomationExpressionResult.Invalid>();
        var safe = new AutomationSafeTriggerExpressionService();
        var port = LanguagePort(AutomationPortValueType.Number);
        safe.Validate(expression, port, out _).ShouldBeTrue();
        safe.Evaluate(expression, port, context).ShouldBeNull();
        var configuration = LanguageConfiguration(
            "decimal(args[0])",
            AutomationPortValueType.Number
        ) with
        {
            Outputs =
            [
                new(
                    new("earlier"),
                    "Earlier",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "'private-earlier-output'"
                ),
                .. LanguageConfiguration(
                    "decimal(args[0])",
                    AutomationPortValueType.Number
                ).Outputs,
            ],
        };
        AutomationTransformCelService
            .ValidateOutput(
                configuration.Outputs[^1],
                configuration.Inputs.ToDictionary(input => input.Identifier.Value)
            )
            .ShouldBeTrue();
        new AutomationTransformCelService()
            .Execute(configuration, LanguageInputs(configuration, [text]))
            .ShouldBeOfType<AutomationPureNodeResult.Failed>()
            .Code.ShouldBe("output-invalid");
    }

    [Test]
    [Arguments("0", "size(arguments)", "' '", " first  a,b LAST!")]
    [Arguments("1", "size(arguments)", "'::'", "a,b::LAST!")]
    [Arguments("size(arguments)", "99", "' '", "")]
    [Arguments("size(arguments)", "size(arguments)", "' '", "")]
    [Arguments("1", "3", "''", "a,bLAST!")]
    [Arguments("-5", "2", "'|~|'", " first |~|a,b")]
    [Arguments("1", "99", "' '", "a,b LAST!")]
    [Arguments("2", "2", "' '", "")]
    [Arguments("3", "1", "' '", "")]
    [Arguments("0", "-1", "' '", "")]
    [Arguments("9223372036854775807", "9223372036854775807", "' '", "")]
    [Arguments("(-9223372036854775807 - 1)", "9223372036854775807", "' '", " first  a,b LAST!")]
    public void CelLanguage_JoinUsesExclusiveClampedLongRangeAndExactText(
        string start,
        string end,
        string separator,
        string expected
    ) =>
        AssertLanguageValue(
            $"join(arguments, {start}, {end}, {separator})",
            [" first ", "a,b", "LAST!"],
            new AutomationValue.Text(expected)
        );

    [Test]
    [Arguments("decimal(null)")]
    [Arguments("decimal(true)")]
    [Arguments("decimal(arguments)")]
    [Arguments("decimal()")]
    [Arguments("decimal('1', '2')")]
    public void CelLanguage_NativeConversionRejectsWrongTypesAndArity(string source)
    {
        _ = new AutomationExpressionService()
            .Evaluate(LanguageExpression(source), LanguageContext(["1"]))
            .ShouldBeOfType<AutomationExpressionResult.Invalid>();
        new AutomationSafeTriggerExpressionService()
            .Validate(
                LanguageExpression(source),
                LanguagePort(AutomationPortValueType.Number),
                out _
            )
            .ShouldBeFalse();
        var config = LanguageConfiguration(
            source.Replace("arguments", "args", StringComparison.Ordinal),
            AutomationPortValueType.Number
        );
        AutomationTransformCelService
            .ValidateOutput(
                config.Outputs.Single(),
                config.Inputs.ToDictionary(input => input.Identifier.Value)
            )
            .ShouldBeFalse();
    }

    [Test]
    public async Task CelLanguage_SavedJoinInputAndFunctionEvaluateTogetherAfterReload()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var transform = Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "args",
                        "join",
                        "Arguments",
                        "args-input",
                        AutomationPortValueType.Arguments,
                        AutomationPortNullability.NonNullable,
                        Array.Empty<string>()
                    ),
                ],
                [
                    new(
                        "name",
                        "Name",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "join(join, 1, size(join), '-')"
                    ),
                ]
            ),
            bindings: Bindings(
                "args-input",
                AutomationInputBindingMode.Expression,
                LanguageExpression("arguments")
            )
        );
        var send = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var draft = Draft(
            fixture.HostId,
            [source, transform, send],
            [
                Edge(source, "flow", send),
                Edge(transform, "name", send, "message", AutomationEdgeKind.Data),
            ]
        );
        var flow = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var loaded = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(item => item.Draft.Id == flow.FlowId)
            .Draft;
        _ = (
            await fixture.Flows.SaveAsync(loaded, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        _ = (
            await fixture
                .NewRuntime()
                .DispatchAsync(
                    new(
                        CelTimerContext(fixture.HostId, ["5", "one", "two"]),
                        new CustomCommandSourceConfiguration(new(7))
                    ),
                    CancellationToken.None
                )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Chat.Messages.ShouldBe(["one-two"]);
    }

    [Test]
    public void CelLanguage_EmptyArgumentsAndSavedJoinIdentifierRemainUsable()
    {
        AssertLanguageValue(
            "join(arguments, 0, size(arguments), '|')",
            [],
            new AutomationValue.Text(string.Empty)
        );
        var config = LanguageConfiguration(
            "join(join, 1, size(join), '-')",
            AutomationPortValueType.Text,
            "join"
        );
        AutomationTransformCelService
            .ValidateOutput(
                config.Outputs.Single(),
                config.Inputs.ToDictionary(input => input.Identifier.Value)
            )
            .ShouldBeTrue();
        var result = new AutomationTransformCelService()
            .Execute(config, LanguageInputs(config, ["5", "one", "two"]))
            .ShouldBeOfType<AutomationPureNodeResult.Succeeded>();
        result.Outputs[new("result")].Value.ShouldBe(new AutomationValue.Text("one-two"));
        var definition = AutomationCelTransform.Definition(
            AutomationDefinitionIds.CelTransform,
            new("Transform", "Transforms declared inputs.", "Transform")
        );
        var json = TransformJson(
            [
                new(
                    "args",
                    "join",
                    "Arguments",
                    "args-binding",
                    AutomationPortValueType.Arguments,
                    AutomationPortNullability.NonNullable,
                    Array.Empty<string>()
                ),
            ],
            [
                new(
                    "result",
                    "Result",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "join(join, 1, size(join), '-')"
                ),
            ]
        );
        _ = definition
            .Parse(System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json))
            .ShouldBeOfType<AutomationConfigurationParseResult.Parsed>();
    }

    [Test]
    [Arguments("join(arguments, 1, ' ')")]
    [Arguments("join(arguments, 0, 1, ' ', 'extra')")]
    [Arguments("join(arguments, 0.0, 1, ' ')")]
    [Arguments("join(arguments, uint(0), 1, ' ')")]
    [Arguments("join(arguments, 0, double(1), ' ')")]
    [Arguments("join(arguments, 0, 1, 2)")]
    [Arguments("join(null, 0, 1, ' ')")]
    [Arguments("join(arguments, null, 1, ' ')")]
    [Arguments("join(arguments, 0, null, ' ')")]
    [Arguments("join(arguments, 0, 1, null)")]
    [Arguments("join(['text', 1], 0, 2, ' ')")]
    [Arguments("join('text', 0, 1, ' ')")]
    public void CelLanguage_JoinRejectsUnsupportedBindingTypesAndArity(string source)
    {
        var expression = LanguageExpression(source);
        _ = new AutomationExpressionService()
            .Evaluate(expression, LanguageContext(["one"]))
            .ShouldBeOfType<AutomationExpressionResult.Invalid>();
        new AutomationSafeTriggerExpressionService()
            .Validate(expression, LanguagePort(AutomationPortValueType.Text), out _)
            .ShouldBeFalse();
        var output = LanguageConfiguration(
            source.Replace("arguments", "args", StringComparison.Ordinal),
            AutomationPortValueType.Text
        );
        AutomationTransformCelService
            .ValidateOutput(
                output.Outputs.Single(),
                output.Inputs.ToDictionary(input => input.Identifier.Value)
            )
            .ShouldBeFalse();
    }

    [Test]
    public void CelLanguage_PreservesCastsScopesNullabilityAndSensitiveOrdinaryValues()
    {
        AssertLanguageValue("decimal(1)", [], new AutomationValue.Number(1m));
        AssertLanguageValue("int(decimal('1.9'))", [], new AutomationValue.Number(1m));
        var context = LanguageContext(["5"]) with
        {
            Variables = new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("private_number")] = new(
                        new AutomationValue.Text("5"),
                        AutomationDataSensitivity.Sensitive
                    ),
                }
            ),
        };
        var value = new AutomationExpressionService()
            .Evaluate(LanguageExpression("decimal(private_number)"), context)
            .ShouldBeOfType<AutomationExpressionResult.Value>();
        value.Result.ShouldBe(5m);
        value.UsesSensitiveValues.ShouldBeTrue();
        _ = new AutomationExpressionService()
            .Evaluate(LanguageExpression("format_number(decimal(arguments[0]))"), context)
            .ShouldBeOfType<AutomationExpressionResult.Invalid>();
        var safe = new AutomationSafeTriggerExpressionService();
        safe.Validate(
                LanguageExpression("decimal(private_number)"),
                LanguagePort(AutomationPortValueType.Number),
                out _
            )
            .ShouldBeFalse();
        foreach (
            var source in new[]
            {
                "decimal(arguments[0])",
                "join(arguments, 0, 1, ' ')",
                "int(args[0])",
            }
        )
        {
            var config = LanguageConfiguration(source, AutomationPortValueType.Number);
            AutomationTransformCelService
                .ValidateOutput(
                    config.Outputs.Single(),
                    config.Inputs.ToDictionary(input => input.Identifier.Value)
                )
                .ShouldBeFalse();
        }
        var optional = LanguageConfiguration(
            "join(args, 0, 1, ' ')",
            AutomationPortValueType.Text
        ) with
        {
            Inputs =
            [
                LanguageConfiguration("''", AutomationPortValueType.Text).Inputs.Single() with
                {
                    Nullability = AutomationPortNullability.Nullable,
                },
            ],
        };
        AutomationTransformCelService
            .ValidateOutput(
                optional.Outputs.Single(),
                optional.Inputs.ToDictionary(input => input.Identifier.Value)
            )
            .ShouldBeFalse();
    }

    private static void AssertLanguageValue(
        string source,
        string[] arguments,
        AutomationValue expected
    )
    {
        var context = LanguageContext(arguments);
        var ordinary = new AutomationExpressionService();
        _ = ordinary
            .Validate(LanguageExpression(source))
            .ShouldBeOfType<AutomationExpressionCheck.Valid>();
        var result = ordinary
            .Evaluate(LanguageExpression(source), context)
            .ShouldBeOfType<AutomationExpressionResult.Value>();
        result.UsesSensitiveValues.ShouldBeFalse();
        result.Result.ShouldBe(AutomationTransformCelService.ToCelValue(expected));
        var type =
            expected is AutomationValue.Number
                ? AutomationPortValueType.Number
                : AutomationPortValueType.Text;
        var safe = new AutomationSafeTriggerExpressionService();
        safe.Validate(LanguageExpression(source), LanguagePort(type), out _).ShouldBeTrue();
        safe.Evaluate(LanguageExpression(source), LanguagePort(type), context)
            .ShouldNotBeNull()
            .Value.ShouldBe(expected);
        var config = LanguageConfiguration(
            source.Replace("arguments", "args", StringComparison.Ordinal),
            type
        );
        AutomationTransformCelService
            .ValidateOutput(
                config.Outputs.Single(),
                config.Inputs.ToDictionary(input => input.Identifier.Value)
            )
            .ShouldBeTrue();
        var output = new AutomationTransformCelService()
            .Execute(config, LanguageInputs(config, arguments))
            .ShouldBeOfType<AutomationPureNodeResult.Succeeded>()
            .Outputs[new("result")];
        output.Value.ShouldBe(expected);
        output.Provenance.ShouldContain(AutomationValueProvenance.PublicChat);
        output.SafeTriggerFields.ShouldBe([new AutomationSafeTriggerFieldId("arguments")]);
        output.ValueFreeDiagnostic.ShouldBeTrue();
    }

    private static AutomationExpressionSource LanguageExpression(string source) =>
        new(AutomationExpressionLanguage.CurrentVersion, source);

    private static AutomationContext LanguageContext(string[] arguments) =>
        Context(1) with
        {
            Arguments =
            [
                .. arguments.Select((text, index) => new AutomationArgument(index, text)).Reverse(),
            ],
        };

    private static AutomationPortMetadata LanguagePort(AutomationPortValueType type) =>
        new(
            new("result"),
            "Result",
            "Calculated value",
            type,
            Nullability: AutomationPortNullability.NonNullable,
            BindingFieldId: new("binding")
        );

    private static AutomationCelTransformConfiguration LanguageConfiguration(
        string source,
        AutomationPortValueType type,
        string identifier = "args"
    ) =>
        new(
            [
                new(
                    new("args"),
                    new(identifier),
                    "Arguments",
                    new("args-binding"),
                    AutomationPortValueType.Arguments,
                    AutomationPortNullability.NonNullable,
                    new AutomationValue.Arguments([])
                ),
            ],
            [new(new("result"), "Result", type, AutomationPortNullability.NonNullable, source)]
        );

    private static ImmutableDictionary<AutomationPortId, AutomationResolvedValue> LanguageInputs(
        AutomationCelTransformConfiguration config,
        string[] arguments
    ) =>
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
            config.Inputs.Single().PortId,
            new(
                new AutomationValue.Arguments([
                    .. arguments
                        .Select(
                            (text, index) =>
                                new AutomationValueArgument(
                                    index,
                                    text,
                                    [AutomationValueProvenance.PublicChat]
                                )
                        )
                        .Reverse(),
                ]),
                [AutomationValueProvenance.PublicChat],
                [new("arguments")]
            )
        );
}
