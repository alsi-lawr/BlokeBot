using System.Diagnostics;
using System.IO.Pipes;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginWorkerHandshakeTests
{
    [Test]
    public async Task Handshake_RejectsProtocolEngineApiTargetAndPackageSkew()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { run = function() return true end }"
        );
        PluginApiVersion.TryCreate(2, out var apiV2).ShouldBeTrue();
        PluginEngineId.TryCreate("other-lua-engine", out var otherEngineId).ShouldBeTrue();
        var otherEngine = PluginWorkerEngineContract.Selected with { Engine = otherEngineId };
        var otherTarget =
            package.Package.Descriptor.RuntimeIdentifier == PluginRuntimeIdentifier.LinuxX64
                ? PluginRuntimeIdentifier.WindowsX64
                : PluginRuntimeIdentifier.LinuxX64;

        var protocol = await RejectAsync(
            package,
            PluginWorkerCompatibilityDescriptor.Current with
            {
                ProtocolVersion = PluginWorkerLimits.ProtocolVersion + 1,
            },
            PluginWorkerEngineContract.Selected,
            package.Package.Descriptor
        );
        var engine = await RejectAsync(
            package,
            PluginWorkerCompatibilityDescriptor.Current,
            otherEngine,
            package.Package.Descriptor
        );
        var api = await RejectAsync(
            package,
            PluginWorkerCompatibilityDescriptor.Current with
            {
                HostApiVersion = apiV2,
            },
            PluginWorkerEngineContract.Selected,
            package.Package.Descriptor
        );
        var target = await RejectAsync(
            package,
            PluginWorkerCompatibilityDescriptor.Current,
            PluginWorkerEngineContract.Selected,
            package.Package.Descriptor with
            {
                RuntimeIdentifier = otherTarget,
            }
        );
        var packageContract = await RejectAsync(
            package,
            PluginWorkerCompatibilityDescriptor.Current with
            {
                ValueContractVersion =
                    PluginWorkerCompatibilityDescriptor.Current.ValueContractVersion + 1,
            },
            PluginWorkerEngineContract.Selected,
            package.Package.Descriptor
        );

        protocol.Code.ShouldBe(PluginWorkerHandshakeFailureCode.ProtocolSkew);
        engine.Code.ShouldBe(PluginWorkerHandshakeFailureCode.EngineMismatch);
        api.Code.ShouldBe(PluginWorkerHandshakeFailureCode.ApiMismatch);
        target.Code.ShouldBe(PluginWorkerHandshakeFailureCode.TargetMismatch);
        packageContract.Code.ShouldBe(PluginWorkerHandshakeFailureCode.PackageMismatch);
    }

    [Test]
    public async Task MismatchedCancellationIdentity_IsRejectedAsProtocolViolation()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { run = function() return blokebot.host.call('chat', 'send-message', 'wait') end }"
        );
        var pipeName = $"blokebot-plugin-protocol-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
        using var process = StartWorker(
            pipeName,
            package.Package.PackageRoot,
            Path.Combine(
                Path.GetDirectoryName(package.Package.PackageRoot)!,
                $"protocol-state-{Guid.NewGuid():N}"
            )
        );
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.WaitForConnectionAsync(timeout.Token);
        var codec = new PluginWorkerProtocolCodec();
        _ = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.WorkerHello>();
        _ = (
            await codec.WriteAsync(
                pipe,
                new PluginWorkerMessage.HostHandshake(
                    PluginWorkerCompatibilityDescriptor.Current,
                    PluginWorkerEngineContract.Selected,
                    PluginWorkerMode.Admitted,
                    package.Package.Descriptor
                ),
                timeout.Token
            )
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        _ = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.HandshakeAccepted>();
        var identity = MaterializedPluginTestPackage.Identity(package.Package.Descriptor.Plugin);
        _ = (
            await codec.WriteAsync(
                pipe,
                new PluginWorkerMessage.Invoke(
                    identity,
                    new PluginLiveInvocation.Command(
                        MaterializedPluginTestPackage.ModuleId(),
                        MaterializedPluginTestPackage.OperationId("run"),
                        new PluginValue.Nil()
                    )
                ),
                timeout.Token
            )
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        _ = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.HostCallRequested>();
        PluginWorkerCancellationId
            .TryCreate(Guid.NewGuid(), out var mismatchedCancellationId)
            .ShouldBeTrue();

        _ = (
            await codec.WriteAsync(
                pipe,
                new PluginWorkerMessage.Cancel(
                    identity with
                    {
                        CancellationId = mismatchedCancellationId,
                    },
                    PluginCancellationReason.CallerRequested
                ),
                timeout.Token
            )
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        var rejected = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.ProtocolRejected>();
        await process.WaitForExitAsync(timeout.Token);
        _ = await standardOutput;
        _ = await standardError;

        rejected.Failure.Code.ShouldBe(PluginWorkerFailureCode.ProtocolViolation);
        process.ExitCode.ShouldBe(0);
    }

    [Test]
    public async Task ExpiredWireInvocation_IsRejectedBeforeLuaExecution()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return { run = function()
              local file = assert(io.open("expired-wire-work-ran", "w"))
              file:write("ran")
              file:close()
              return true
            end }
            """
        );
        var stateRoot = Path.Combine(
            Path.GetDirectoryName(package.Package.PackageRoot)!,
            $"expired-wire-state-{Guid.NewGuid():N}"
        );
        var pipeName = $"blokebot-plugin-expired-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
        using var process = StartWorker(pipeName, package.Package.PackageRoot, stateRoot);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.WaitForConnectionAsync(timeout.Token);
        var codec = new PluginWorkerProtocolCodec();
        _ = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.WorkerHello>();
        _ = (
            await codec.WriteAsync(
                pipe,
                new PluginWorkerMessage.HostHandshake(
                    PluginWorkerCompatibilityDescriptor.Current,
                    PluginWorkerEngineContract.Selected,
                    PluginWorkerMode.Admitted,
                    package.Package.Descriptor
                ),
                timeout.Token
            )
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        _ = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.HandshakeAccepted>();
        var identity = MaterializedPluginTestPackage.Identity(
            package.Package.Descriptor.Plugin,
            TimeSpan.FromSeconds(-1)
        );

        _ = (
            await codec.WriteAsync(
                pipe,
                new PluginWorkerMessage.Invoke(
                    identity,
                    new PluginLiveInvocation.Command(
                        MaterializedPluginTestPackage.ModuleId(),
                        MaterializedPluginTestPackage.OperationId("run"),
                        new PluginValue.Nil()
                    )
                ),
                timeout.Token
            )
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        var rejected = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.InvocationRejected>();
        _ = (
            await codec.WriteAsync(pipe, new PluginWorkerMessage.Stop(), timeout.Token)
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        _ = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.Stopped>();
        await process.WaitForExitAsync(timeout.Token);
        _ = await standardOutput;
        _ = await standardError;

        rejected.InvocationId.ShouldBe(identity.InvocationId);
        rejected.Failure.Code.ShouldBe(PluginWorkerFailureCode.DeadlineExceeded);
        File.Exists(Path.Combine(stateRoot, "expired-wire-work-ran")).ShouldBeFalse();
        process.ExitCode.ShouldBe(0);
    }

    private static async ValueTask<PluginWorkerHandshakeFailure> RejectAsync(
        MaterializedPluginTestPackage package,
        PluginWorkerCompatibilityDescriptor compatibility,
        PluginEngineDescriptor engine,
        PluginWorkerPackageDescriptor descriptor
    )
    {
        var pipeName = $"blokebot-plugin-handshake-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
        using var process = StartWorker(
            pipeName,
            package.Package.PackageRoot,
            Path.Combine(
                Path.GetDirectoryName(package.Package.PackageRoot)!,
                $"handshake-state-{Guid.NewGuid():N}"
            )
        );
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.WaitForConnectionAsync(timeout.Token);
        var codec = new PluginWorkerProtocolCodec();
        _ = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.WorkerHello>();

        _ = (
            await codec.WriteAsync(
                pipe,
                new PluginWorkerMessage.HostHandshake(
                    compatibility,
                    engine,
                    PluginWorkerMode.Admitted,
                    descriptor
                ),
                timeout.Token
            )
        ).ShouldBeOfType<PluginFrameWriteOutcome.Written>();
        var rejected = (await codec.ReadAsync(pipe, timeout.Token))
            .ShouldBeOfType<PluginFrameReadOutcome.Message>()
            .Value.ShouldBeOfType<PluginWorkerMessage.HandshakeRejected>();
        await process.WaitForExitAsync(timeout.Token);
        _ = await standardOutput;
        _ = await standardError;
        process.ExitCode.ShouldBe(4);
        return rejected.Failure;
    }

    private static Process StartWorker(string pipeName, string packageRoot, string stateRoot)
    {
        _ = Directory.CreateDirectory(stateRoot);
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = stateRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(MaterializedPluginTestPackage.WorkerExecutable().Path);
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--package");
        start.ArgumentList.Add(packageRoot);
        start.ArgumentList.Add("--state");
        start.ArgumentList.Add(stateRoot);
        return Process.Start(start)
            ?? throw new InvalidOperationException("Worker process did not start.");
    }
}
