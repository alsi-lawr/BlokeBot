using System.Text.Json;
using System.Text.Json.Nodes;
using BlokeBot.Core.Features.Automations;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public void Diagnostic_OrdinaryCompilationIsDistinctFromOpaqueNativeEvaluation()
    {
        var ordinary = new AutomationExpressionService();
        var invalid = LanguageExpression("1 +");
        ordinary
            .Validate(invalid)
            .ShouldBeOfType<AutomationExpressionCheck.Invalid>()
            .Diagnostic.ShouldNotBeNull()
            .Category.ShouldBe(AutomationDiagnosticCategory.Compilation);
        ordinary
            .Evaluate(invalid, LanguageContext([]))
            .ShouldBeOfType<AutomationExpressionResult.Invalid>()
            .Diagnostic.ShouldNotBeNull()
            .Category.ShouldBe(AutomationDiagnosticCategory.Compilation);
        ordinary
            .Evaluate(
                LanguageExpression("decimal(arguments[0])"),
                LanguageContext(["private-invalid"])
            )
            .ShouldBeOfType<AutomationExpressionResult.Invalid>()
            .Diagnostic.ShouldNotBeNull()
            .Category.ShouldBe(AutomationDiagnosticCategory.Evaluation);
        const string Source = "🌻 café\n${size(args)} : ${int(args[0])}";
        var config = LanguageConfiguration(Source, AutomationPortValueType.Text);
        AutomationTransformCelService
            .ValidateOutput(
                config.Outputs.Single(),
                config.Inputs.ToDictionary(input => input.Identifier.Value),
                out var diagnostic
            )
            .ShouldBeFalse();
        diagnostic
            .ShouldNotBeNull()
            .Category.ShouldBe(AutomationDiagnosticCategory.UnsupportedOperation);
        diagnostic.Span.ShouldBe(
            new AutomationExpressionSpan(
                Source.IndexOf("int(", StringComparison.Ordinal),
                "int(args[0])".Length
            )
        );
    }

    [Test]
    [Arguments("private_identifier", AutomationDiagnosticCategory.UnavailableName)]
    [Arguments("args.secret", AutomationDiagnosticCategory.UnavailableName)]
    [Arguments("unknown_function(args)", AutomationDiagnosticCategory.UnsupportedOperation)]
    [Arguments("int(args[0])", AutomationDiagnosticCategory.UnsupportedOperation)]
    [Arguments("size(args) + 'text'", AutomationDiagnosticCategory.IncompatibleType)]
    [Arguments("null", AutomationDiagnosticCategory.IncompatibleType)]
    public void Diagnostic_RestrictedStaticEvidenceKeepsSafeCategoryAndAuthoredSpan(
        string source,
        AutomationDiagnosticCategory category
    )
    {
        var config = LanguageConfiguration(source, AutomationPortValueType.Number);
        AutomationTransformCelService
            .ValidateOutput(
                config.Outputs.Single(),
                config.Inputs.ToDictionary(input => input.Identifier.Value),
                out var diagnostic
            )
            .ShouldBeFalse();
        diagnostic.ShouldNotBeNull().Category.ShouldBe(category);
        diagnostic
            .Member.ShouldBeOfType<AutomationDiagnosticMember.Output>()
            .PortId.ShouldBe(new("result"));
        var span = diagnostic.Span.ShouldNotBeNull();
        span.Start.ShouldBeGreaterThanOrEqualTo(0);
        (span.Start + span.Length).ShouldBeLessThanOrEqualTo(source.Length);
        var safe = new AutomationSafeTriggerExpressionService();
        safe.Validate(
                LanguageExpression(source.Replace("args", "arguments", StringComparison.Ordinal)),
                LanguagePort(AutomationPortValueType.Number),
                out _,
                out _,
                out var inputDiagnostic
            )
            .ShouldBeFalse();
        inputDiagnostic.ShouldNotBeNull().Category.ShouldBe(category);
        var json = JsonSerializer.Serialize(diagnostic);
        json.ShouldNotContain("private_identifier");
        json.ShouldNotContain("unknown_function");
        // Ordinary scope still has compile-only validation, not the restricted allowlist.
        _ = new AutomationExpressionService()
            .Validate(LanguageExpression("[1, 2, 3]"))
            .ShouldBeOfType<AutomationExpressionCheck.Valid>();
    }

    [Test]
    [Arguments("decimal(args[0])")]
    [Arguments("args[99]")]
    public void Diagnostic_OpaqueNativeErrorsKeepOnlyKnownInterpolationSegment(string expression)
    {
        var source = "🌻 café\nvalue: ${" + expression + "} tail";
        var config = LanguageConfiguration(source, AutomationPortValueType.Text);
        AutomationTransformCelService
            .ValidateOutput(
                config.Outputs.Single(),
                config.Inputs.ToDictionary(input => input.Identifier.Value)
            )
            .ShouldBeTrue();
        var failed = new AutomationTransformCelService()
            .Execute(config, LanguageInputs(config, ["private-invalid"]))
            .ShouldBeOfType<AutomationPureNodeResult.Failed>();
        var diagnostic = failed.Diagnostic.ShouldNotBeNull();
        diagnostic.Category.ShouldBe(AutomationDiagnosticCategory.Evaluation);
        diagnostic.Span.ShouldBe(
            new AutomationExpressionSpan(
                source.IndexOf(expression, StringComparison.Ordinal),
                expression.Length
            )
        );
        var ordinary = new AutomationExpressionService()
            .Interpolate(
                source.Replace("args", "arguments", StringComparison.Ordinal),
                LanguageContext(["private-invalid"])
            )
            .ShouldBeOfType<AutomationExpressionResult.Invalid>();
        ordinary.Diagnostic.ShouldNotBeNull().Category.ShouldBe(diagnostic.Category);
        ordinary.Diagnostic.Span.ShouldBe(
            new AutomationExpressionSpan(
                source.IndexOf(expression, StringComparison.Ordinal),
                expression.Replace("args", "arguments", StringComparison.Ordinal).Length
            )
        );
        JsonSerializer.Serialize(diagnostic).ShouldNotContain("private-invalid");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Diagnostic_FreshRecursiveFailureSurvivesContinueAndCachedRestartWithoutRerun(
        bool expireTrace
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var leaf = DiagnosticLeaf();
        var wrapper = DiagnosticWrapper();
        var first = Node(
            "send-chat",
            """{"message":"unused"}""",
            AutomationNodeFailurePolicy.Continue,
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var second = first with { Id = new(Guid.NewGuid()) };
        var after = Node("send-chat", """{"message":"after"}""");
        var draft = Draft(
            fixture.HostId,
            [source, leaf, wrapper, first, delay, second, after],
            [
                Edge(source, "flow", first),
                Edge(first, "complete", delay),
                Edge(delay, "complete", second),
                Edge(second, "complete", after),
                Edge(leaf, "name", wrapper, "text", AutomationEdgeKind.Data),
                Edge(wrapper, "value", first, "message", AutomationEdgeKind.Data),
                Edge(wrapper, "value", second, "message", AutomationEdgeKind.Data),
            ]
        );
        var saved = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(
                    CelTimerContext(fixture.HostId, ["private-invalid-number"]),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Chat.Calls.ShouldBe(0);
        fixture.ProductTransformHandler.Calls.ShouldBe(1);
        var trace = await ReadTraceAsync(fixture, new(run.Value));
        var producer = trace
            .Events.Single(entry =>
                entry.Event.Node?.Id == leaf.Id
                && entry.Event.Kind == AutomationTraceEventKind.Outputs
            )
            .Event;
        producer.Outcome.ShouldBe(AutomationTraceOutcome.Failed);
        producer.Values.ShouldBeEmpty();
        producer.Diagnostic.ShouldNotBeNull().Origin.ShouldNotBeNull().NodeId.ShouldBe(leaf.Id);
        producer
            .Diagnostic.Member.ShouldBeOfType<AutomationDiagnosticMember.Output>()
            .PortId.ShouldBe(new("seconds"));
        foreach (var node in new[] { wrapper, first })
        {
            var failure = trace
                .Events.Single(entry =>
                    entry.Event.Node?.Id == node.Id
                    && entry.Event.Outcome
                        is AutomationTraceOutcome.Failed
                            or AutomationTraceOutcome.ContinuedAfterFailure
                )
                .Event;
            failure.Diagnostic.ShouldNotBeNull().Origin.ShouldBe(producer.Diagnostic.Origin);
            failure.Diagnostic.Affected.ShouldNotBeNull().NodeId.ShouldBe(node.Id);
            failure.Diagnostic.Span.ShouldBe(producer.Diagnostic.Span);
        }
        // Repairing the mutable graph cannot change the frozen failed checkpoint.
        _ = (
            await fixture.Flows.SaveAsync(
                draft with
                {
                    Id = saved.FlowId,
                    Nodes = draft.Nodes.Replace(
                        leaf,
                        CelTimerParse("1", "'repaired'") with
                        {
                            Id = leaf.Id,
                        }
                    ),
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == leaf.Id.Value
            );
            checkpoint.OutputJson.ShouldBeNull();
            if (expireTrace)
            {
                _ = await db
                    .AutomationTraces.Where(item => item.Id == run.Value)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(
                            item => item.ExpiresAtUtc,
                            fixture.Clock.GetUtcNow().UtcDateTime
                        )
                    );
            }
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["after"]);
        fixture.ProductTransformHandler.Calls.ShouldBe(1);
        await using var final = await fixture.Database.CreateDbContextAsync();
        var persisted = string.Join(
            "",
            await final.AutomationTraceEvents.Select(item => item.EventJson).ToArrayAsync()
        );
        persisted.ShouldNotContain("private-");
        (await final.AutomationNodeRuns.CountAsync(node => node.NodeId == leaf.Id.Value)).ShouldBe(
            1
        );
        if (!expireTrace)
        {
            var cached = (await ReadTraceAsync(fixture, new(run.Value)))
                .Events.Single(entry =>
                    entry.Event.Node?.Id == second.Id
                    && entry.Event.Kind == AutomationTraceEventKind.Result
                )
                .Event.Diagnostic.ShouldNotBeNull();
            cached.Category.ShouldBe(AutomationDiagnosticCategory.HistoricalCauseUnavailable);
            cached.Origin.ShouldNotBeNull().NodeId.ShouldBe(wrapper.Id);
            cached
                .Member.ShouldBeOfType<AutomationDiagnosticMember.Output>()
                .PortId.ShouldBe(new("value"));
            cached.Span.ShouldBeNull();
            cached.Affected.ShouldNotBeNull().NodeId.ShouldBe(second.Id);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Diagnostic_SavedRecursiveLiveAndScenarioFailureKeepLeafAndConsumerPolicy(
        bool continues
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [writes],
            migrateSchema: true
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var leaf = DiagnosticLeaf();
        var wrapper = DiagnosticWrapper();
        var consumer = Node(
            "send-chat",
            """{"message":"unused"}""",
            continues ? AutomationNodeFailurePolicy.Continue : AutomationNodeFailurePolicy.Stop,
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var after = Node("send-chat", """{"message":"after"}""");
        var draft = Draft(
            fixture.HostId,
            [source, leaf, wrapper, consumer, after],
            [
                Edge(source, "flow", consumer),
                Edge(consumer, "complete", after),
                Edge(leaf, "name", wrapper, "text", AutomationEdgeKind.Data),
                Edge(wrapper, "value", consumer, "message", AutomationEdgeKind.Data),
            ]
        );
        var saved = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        draft = draft with { Id = saved.FlowId };
        var context = CelTimerContext(fixture.HostId, ["private-invalid"]);
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Chat.Messages.ShouldBe(continues ? ["after"] : []);
        var live = await ReadTraceAsync(fixture, new(run.Value));
        writes.Armed = true;
        var scenario = await fixture.Scenarios.RunAsync(
            draft,
            fixture.Scenarios.CreateDefaultFixture(draft, source.Id) with
            {
                Context = context,
            },
            CancellationToken.None
        );
        var testId = continues
            ? scenario.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>().TraceId
            : scenario.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>().TraceId;
        writes.Writes.ShouldBe(0);
        writes.Armed = false;
        var simulated = await ReadTraceAsync(fixture, testId);
        foreach (var trace in new[] { live, simulated })
        {
            var failure = trace
                .Events.Single(entry =>
                    entry.Event.Node?.Id == consumer.Id
                    && entry.Event.Kind == AutomationTraceEventKind.Result
                )
                .Event;
            failure.Outcome.ShouldBe(
                continues
                    ? AutomationTraceOutcome.ContinuedAfterFailure
                    : AutomationTraceOutcome.Failed
            );
            failure.Diagnostic.ShouldNotBeNull().Origin.ShouldNotBeNull().NodeId.ShouldBe(leaf.Id);
            failure.Diagnostic.Affected.ShouldNotBeNull().NodeId.ShouldBe(consumer.Id);
            failure
                .Diagnostic.Member.ShouldBeOfType<AutomationDiagnosticMember.Output>()
                .PortId.ShouldBe(new("seconds"));
            trace
                .Events.Where(entry =>
                    entry.Event.Node?.Id == leaf.Id || entry.Event.Node?.Id == wrapper.Id
                )
                .ShouldNotContain(entry => entry.Event.Outcome == AutomationTraceOutcome.Succeeded);
            JsonSerializer.Serialize(trace).ShouldNotContain("private-");
        }
        fixture.Chat.Messages.ShouldBe(continues ? ["after"] : []);
    }

    [Test]
    [Arguments("absent")]
    [Arguments("unknown")]
    [Arguments("malformed")]
    public async Task Diagnostic_StoredOptionalMetadataFallbackKeepsHistoricalEventReadable(
        string shape
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var leaf = CelTimerParse("decimal(args[0])", "'private-secret'");
        var consumer = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, leaf, consumer],
            [
                Edge(source, "flow", consumer),
                Edge(leaf, "name", consumer, "message", AutomationEdgeKind.Data),
            ]
        );
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(
                    CelTimerContext(fixture.HostId, ["private-invalid"]),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        var original = await ReadTraceAsync(fixture, new(run.Value));
        var entry = original.Events.Single(item =>
            item.Event.Kind == AutomationTraceEventKind.Outputs && item.Event.Node?.Id == leaf.Id
        );
        _ = entry.Event.Diagnostic.ShouldNotBeNull();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var row = await db.AutomationTraceEvents.SingleAsync(item =>
                item.TraceId == run.Value && item.Sequence == entry.Sequence
            );
            var json = JsonNode.Parse(row.EventJson)!.AsObject();
            if (shape == "absent")
            {
                _ = json.Remove("diagnostic");
            }
            else if (shape == "unknown")
            {
                json["diagnostic"]!["version"] = 999;
            }
            else
            {
                json["diagnostic"] = JsonNode.Parse(
                    """{"version":1,"category":1,"member":{"kind":1,"port":"value"},"span":{"start":"private-payload","length":10}}"""
                );
            }
            row.EventJson = json.ToJsonString();
            _ = await db.SaveChangesAsync();
        }
        var restored = await ReadTraceAsync(fixture, new(run.Value));
        var selected = restored.Events.Single(item => item.Sequence == entry.Sequence);
        selected.Event.ShouldBe(entry.Event with { Diagnostic = null });
        JsonSerializer.Serialize(restored).ShouldNotContain("private-");
    }

    private static AutomationFlowDraftNode DiagnosticLeaf() =>
        Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "args",
                        "args",
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
                        "'private-earlier-output'"
                    ),
                    new(
                        "seconds",
                        "Seconds",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        "decimal(args[0])"
                    ),
                ]
            ),
            bindings: Bindings(
                "args-input",
                AutomationInputBindingMode.Expression,
                LanguageExpression("arguments")
            )
        );

    private static AutomationFlowDraftNode DiagnosticWrapper() =>
        Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "text",
                        "text",
                        "Text",
                        "text-input",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        ""
                    ),
                ],
                [
                    new(
                        "value",
                        "Value",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "text"
                    ),
                ]
            ),
            bindings: Bindings("text-input", AutomationInputBindingMode.Connected)
        );
}
