using System.Text.Json;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Plugins.Contracts;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Transfer_OptionalWithheldPluginFieldsRemainSetupRequiredAcrossSaveReopenUntilExplicitConfirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        PublishPortableWidget(plugin);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        var transfer = Transfer(fixture, environment, plugin.Declarations);
        var source = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new
                {
                    text = "Portable title",
                    destination = "source-private-binding",
                    secret = "source-private-setting",
                    legacy = "undeclared-private-value",
                }
            ),
        };
        var package = await Export(transfer, fixture.Owner, Document() with { Widgets = [source] });
        var imported = Value(
            await transfer.ImportAsync(fixture.Owner, new MemoryStream(package), _ct)
        ).Created.Overlay;
        var widget = imported.Draft.Widgets.Single();
        var descriptor = plugin.Declarations.Current.Declarations.Values.Single().Manifest.Widgets[
            0
        ];
        PluginWidgetConfiguration
            .IsValid(descriptor, FullOverlayPluginValues.FromJson(widget.Configuration)!)
            .ShouldBeTrue();
        widget.RequiresSetup.ShouldBeTrue();
        widget.Configuration.TryGetProperty("destination", out _).ShouldBeFalse();
        widget.Configuration.TryGetProperty("secret", out _).ShouldBeFalse();
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            imported.Id,
            null,
            null,
            imported.Draft,
            FullOverlayDataMode.Sample,
            []
        );
        (await environment.Registry.ProjectAsync(context, _ct))[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>()
            .Diagnostic.Code.ShouldBe("destination-setup-required");
        plugin.Worker.Identity.ShouldBeNull();
        var changed = Value(
            await fixture.Service.SaveAsync(
                fixture.Owner,
                new(
                    imported.Id,
                    imported.Revision,
                    imported.Name,
                    imported.Draft with
                    {
                        Widgets = [widget with { Audio = new(true, .42) }],
                    }
                ),
                _ct
            )
        );
        var reopened = Value(await fixture.Service.GetAsync(fixture.Owner, imported.Id, _ct));
        reopened.Draft.Widgets.Single().RequiresSetup.ShouldBeTrue();
        reopened.Draft.Widgets.Single().Audio.ShouldBe(new(true, .42));
        _ = (
            await environment.Registry.ProjectAsync(context with { Document = reopened.Draft }, _ct)
        )[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        var resolved = reopened.Draft.Widgets.Single() with
        {
            RequiresSetup = false,
            Configuration = JsonSerializer.SerializeToElement(
                new { text = "Portable title", destination = "destination-binding" }
            ),
        };
        var confirmed = Value(
            await fixture.Service.SaveAsync(
                fixture.Owner,
                new(
                    changed.Id,
                    changed.Revision,
                    changed.Name,
                    changed.Draft with
                    {
                        Widgets = [resolved],
                    }
                ),
                _ct
            )
        );
        var publicOutput = (
            await environment.Registry.ProjectAsync(
                context with
                {
                    Document = confirmed.Draft,
                },
                _ct
            )
        )[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Plugin>();
        publicOutput.State.Properties.ShouldContain(
            new PluginValueProperty("text", new PluginValue.String("Portable title"))
        );
        plugin.Worker.Identity!.Host.Value.ShouldBe(fixture.HostId);
        var next = await Export(transfer, fixture.Owner, confirmed.Draft);
        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(next),
            System.IO.Compression.ZipArchiveMode.Read
        );
        await using var staging = await environment.Media.BeginTransferAsync(_ct);
        var again = Value(await FullOverlayPackageCodec.ReadAsync(archive, staging, _ct));
        again.Document.Widgets[0].RequiresSetup.ShouldBeTrue();
        again
            .Document.Widgets[0]
            .Configuration.GetRawText()
            .ShouldNotContain("destination-binding");
        again.Document.Widgets[0].Audio.ShouldBe(new(true, .42));
    }
}
