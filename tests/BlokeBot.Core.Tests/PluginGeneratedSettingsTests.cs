using BlokeBot.Core.Features.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginGeneratedSettingsTests
{
    [Test]
    public void OptionalBoolean_RemainsOmittedUntilTheUserSelectsAValue()
    {
        PluginSettingId.TryCreate("optional-switch", out var settingId).ShouldBeTrue();
        PluginFeatureId.TryCreate("collection", out var featureId).ShouldBeTrue();
        PluginHostId.TryCreate(1, out var hostId).ShouldBeTrue();
        var descriptor = new PluginSettingDescriptor(
            settingId,
            "Optional switch",
            "Uses the plugin default until changed.",
            PluginSettingScope.Channel,
            false,
            new PluginSettingSchema.Boolean()
        );
        var owner = new PluginConfigurationOwner.Feature(
            new(PluginContractFixtures.PluginId("community.link-queue"), featureId, hostId)
        );
        var editor = PluginSettingEditor.Create(
            descriptor,
            new(owner, PluginSettingValues.Empty, [], PluginConfigurationRevision.Initial)
        );

        _ = editor.Build().ShouldBeOfType<PluginSettingEditorOutcome.Omitted>();
        editor.SetOptionalBoolean("false");
        editor
            .Build()
            .ShouldBeOfType<PluginSettingEditorOutcome.Setting>()
            .Entry.Value.ShouldBe(new PluginSettingValue.Boolean(false));
        editor.SetOptionalBoolean(null);
        _ = editor.Build().ShouldBeOfType<PluginSettingEditorOutcome.Omitted>();
    }
}
