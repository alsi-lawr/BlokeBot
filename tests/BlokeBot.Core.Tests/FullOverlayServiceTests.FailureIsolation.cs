using System.Text.Json;
using BlokeBot.Core.Features.Overlays.Full;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task PluginDependencyFailure_LogsOnlySafeClassificationAndKeepsSiblingContentWhileCancellationPropagates()
    {
        const string SensitiveMessage = "private-worker-response-with-access-token";
        const string SensitiveData = "private-configuration-and-viewer-content";
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        var logger = new RecordingLogger<FullOverlayWidgetRegistry>();
        await using var environment = new WidgetEnvironment(fixture, plugin, logger: logger);
        var failure = new InvalidOperationException(SensitiveMessage);
        failure.Data["provider-response"] = SensitiveData;
        plugin.Worker.Failure = failure;
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var html = environment.Registry.Create(new("html"), Guid.NewGuid())! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new FullOverlayHtmlConfiguration("Public sibling content", "")
            ),
        };
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            Guid.NewGuid(),
            null,
            null,
            Document() with
            {
                Widgets = [widget, html],
            },
            FullOverlayDataMode.Live,
            []
        );
        var outputs = await environment.Registry.ProjectAsync(context, _ct);
        var unavailable = outputs[0].Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        unavailable.Diagnostic.Code.ShouldBe("source-unavailable");
        unavailable.Diagnostic.Message.ShouldNotContain(SensitiveMessage);
        unavailable.Diagnostic.Message.ShouldNotContain(SensitiveData);
        outputs[1]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Html>()
            .Content.ShouldBe("Public sibling content");
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Properties["WidgetId"].ShouldBe(widget.Id);
        entry.Properties["FailureClassification"].ShouldBe("source-unavailable");
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        entry.Message.ShouldNotContain(SensitiveMessage);
        entry.Message.ShouldNotContain(SensitiveData);
        var loggedProperties = JsonSerializer.Serialize(entry.Properties);
        loggedProperties.ShouldNotContain(SensitiveMessage);
        loggedProperties.ShouldNotContain(SensitiveData);

        plugin.Worker.Failure = null;
        plugin.Worker.Pause = true;
        using var cancellation = new CancellationTokenSource();
        var pending = environment.Registry.ProjectAsync(context, cancellation.Token);
        await plugin.Worker.Started.Task;
        cancellation.Cancel();
        plugin.Worker.Failure = new OperationCanceledException(cancellation.Token);
        _ = plugin.Worker.Release.TrySetResult();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => pending);
        logger.Entries.Count.ShouldBe(1);
    }
}
