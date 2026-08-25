using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginActivationFenceProtocolTests
{
    [Test]
    public async Task ActivationFence_RoundTripsWithTheTypedInvocationFrame()
    {
        var installation = Installation();
        PluginActivationOperationId.TryCreate(Guid.NewGuid(), out var operationId).ShouldBeTrue();
        PluginFeatureActivationGeneration.TryCreate(7, out var featureGeneration).ShouldBeTrue();
        var identity = MaterializedPluginTestPackage.Identity(installation) with
        {
            Activation = new(operationId, WorkerGeneration(), featureGeneration),
        };
        var message = new PluginWorkerMessage.Invoke(
            identity,
            new PluginLiveInvocation.Command(
                Module(),
                PluginContractFixtures.HostOperationId("handle"),
                new PluginValue.String("input")
            )
        );
        var codec = new PluginWorkerProtocolCodec();
        await using var frame = new MemoryStream();

        _ = (
            await codec.WriteAsync(frame, message, CancellationToken.None)
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        frame.Position = 0;
        var decoded = (await codec.ReadAsync(frame, CancellationToken.None))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.Invoke>();

        decoded.Identity.Activation.ShouldBe(identity.Activation);
        decoded.Identity.Generation.ShouldBe(identity.Generation);
        decoded.Identity.Context.ShouldBe(identity.Context);
    }

    private static PluginInstallationIdentity Installation()
    {
        PluginGitTag.TryCreate("community-link-queue", out var tag).ShouldBeTrue();
        return new(
            PluginContractFixtures.PluginId("community.link-queue"),
            new(PluginContractFixtures.SemanticVersion("1.2.0"), tag)
        );
    }

    private static PluginWorkerGeneration WorkerGeneration()
    {
        PluginWorkerGeneration.TryCreate(3, out var generation).ShouldBeTrue();
        return generation;
    }

    private static PluginLuaModuleId Module()
    {
        PluginLuaModuleId.TryCreate("main", out var module).ShouldBeTrue();
        return module;
    }
}
