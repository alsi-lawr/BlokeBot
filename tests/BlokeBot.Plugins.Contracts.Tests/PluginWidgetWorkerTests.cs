using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginWidgetWorkerTests
{
    [Test]
    public async Task PublicWidget_TraversesValidatedPackageAndRealWorkerWithServerOnlySettingsAccess()
    {
        var previous = PluginManifestToml
            .Validate(
                PluginContractFixtures.CompleteManifestToml(),
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest.Manifest;
        _ = PluginWidgetId.TryCreate("public-display", out var widgetId);
        var manifest = previous with
        {
            Widgets =
            [
                new(
                    widgetId,
                    previous.Features[0].Id,
                    "Public display",
                    MaterializedPluginTestPackage.ModuleId(),
                    "widget-render",
                    previous.Assets[0].Id,
                    [],
                    [new("text", "Text", PluginValueKind.String, true)],
                    new PluginValue.Map([new("text", new PluginValue.String("hello"))])
                ),
            ],
        };
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            local blokebot = require('blokebot')
            return {
              ['widget-render'] = function(input)
                assert(input.sessionId == nil and input.hostId == nil)
                local settings = blokebot.settings.installation()
                assert(settings.token == 'server-only')
                return { text = input.configuration.text, serverCredentialUsed = true }
              end
            }
            """,
            manifest: manifest,
            temporaryRoot: Path.Combine(
                Directory.GetCurrentDirectory(),
                ".agent-workspace/overlay-composition-20261001/286/worker-packages"
            )
        );
        var dispatcher = new SettingsDispatcher();
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted, dispatcher);
        var identity = MaterializedPluginTestPackage.Identity(package.Package.Descriptor.Plugin);
        identity = identity with
        {
            Context = new PluginInvocationContext.Widget(
                identity.Plugin,
                identity.Host,
                widgetId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                PluginWidgetProjectionMode.PreviewSample
            ),
        };
        var result = await worker.Client.InvokeAsync(
            identity,
            new PluginLiveInvocation.Widget(
                MaterializedPluginTestPackage.ModuleId(),
                MaterializedPluginTestPackage.OperationId("widget-render"),
                PluginInvocationInputSchemas.WidgetInput(
                    new PluginValue.Map([new("text", new PluginValue.String("Public hello"))])
                )
            ),
            CancellationToken.None
        );
        var output = result
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>()
            .Value.ShouldBeOfType<PluginValue.Map>();
        output.Properties.ShouldContain(
            new PluginValueProperty("text", new PluginValue.String("Public hello"))
        );
        output.Properties.ShouldContain(
            new PluginValueProperty("serverCredentialUsed", new PluginValue.Boolean(true))
        );
        output
            .Properties.Any(property =>
                property.Value is PluginValue.String { Value: "server-only" }
            )
            .ShouldBeFalse();
        dispatcher.Calls.ShouldBe(1);
        dispatcher
            .Context.ShouldBeOfType<PluginInvocationContext.Widget>()
            .Mode.ShouldBe(PluginWidgetProjectionMode.PreviewSample);
        dispatcher
            .Context.ShouldBeOfType<PluginInvocationContext.Widget>()
            .OverlayId.ShouldBe(
                identity.Context.ShouldBeOfType<PluginInvocationContext.Widget>().OverlayId
            );
    }

    private sealed class SettingsDispatcher : IPluginHostCallDispatcher
    {
        internal int Calls { get; private set; }
        internal PluginInvocationContext? Context { get; private set; }

        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken ct
        )
        {
            Calls++;
            Context = call.Context;
            return ValueTask.FromResult<PluginHostCallOutcome>(
                new PluginHostCallOutcome.Returned(
                    new PluginValue.Map([new("token", new PluginValue.String("server-only"))])
                )
            );
        }
    }
}
