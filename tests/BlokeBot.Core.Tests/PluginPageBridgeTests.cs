using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Hosts;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginPageBridgeTests
{
    [Test]
    public void GeneratedRenderer_UsesExistingComponentsAndSubmitsTypedBoundedValues()
    {
        using var context = new BunitContext();
        _ = PluginActionId.TryCreate("refresh", out var action);
        PluginPageFormSubmission? submitted = null;
        var document = new PluginPageDocument(
            1,
            "Manage the queue.",
            [
                new PluginPageSection.Form(
                    "Queue action",
                    null,
                    action,
                    "Run",
                    [
                        new("query", "Query", PluginPageFieldKind.Text, true, null, []),
                        new("limit", "Limit", PluginPageFieldKind.Number, false, null, []),
                        new("notify", "Notify", PluginPageFieldKind.Boolean, false, null, []),
                    ]
                ),
                new PluginPageSection.Table(
                    "Results",
                    null,
                    [new("name", "Name")],
                    [new(ImmutableDictionary<string, string>.Empty.Add("name", "One"))]
                ),
            ]
        );
        var rendered = context.Render<PluginGeneratedPageRenderer>(parameters =>
            parameters
                .Add(parameter => parameter.Document, document)
                .Add(
                    parameter => parameter.OnSubmit,
                    EventCallback.Factory.Create<PluginPageFormSubmission>(
                        this,
                        value => submitted = value
                    )
                )
        );

        rendered.Find("input[type=text]").Input("cats");
        rendered.Find("input[type=number]").Input("2.5");
        rendered.Find("input[type=checkbox]").Change(true);
        rendered.Find("form").Submit();

        _ = submitted.ShouldNotBeNull();
        submitted.Action.ShouldBe(action);
        submitted
            .Input.Properties.Single(property => property.Name == "query")
            .Value.ShouldBe(new PluginValue.String("cats"));
        submitted
            .Input.Properties.Single(property => property.Name == "limit")
            .Value.ShouldBe(new PluginValue.Number(2.5));
        submitted
            .Input.Properties.Single(property => property.Name == "notify")
            .Value.ShouldBe(new PluginValue.Boolean(true));
        rendered.Find("[role=region]").GetAttribute("aria-label").ShouldBe("Results");
    }

    [Test]
    public void ServerProtocol_RejectsWrongSessionOversizeAndNonHttpsNavigation()
    {
        var session = PluginContractFixtures.PageSessionId();
        var message = Guid.NewGuid();
        var action = $$$"""
            {"protocol":"blokebot.plugin-page","version":1,"sessionId":"{{{session.Value:D}}}","messageId":"{{{message:D}}}","kind":"action","action":"refresh","input":{"query":"cats"}}
            """;

        var parsed = PluginPageBridgeProtocol
            .Parse(action, session)
            .ShouldBeOfType<PluginPageBridgeParseOutcome.Parsed>()
            .Request.ShouldBeOfType<PluginPageBridgeRequest.Action>();
        ((PluginValue.String)parsed.Input.Properties.ShouldHaveSingleItem().Value).Value.ShouldBe(
            "cats"
        );

        var wrongSession = action.Replace(
            session.Value.ToString("D"),
            Guid.NewGuid().ToString("D"),
            StringComparison.Ordinal
        );
        _ = PluginPageBridgeProtocol
            .Parse(wrongSession, session)
            .ShouldBeOfType<PluginPageBridgeParseOutcome.Rejected>();
        var navigation = $$$"""
            {"protocol":"blokebot.plugin-page","version":1,"sessionId":"{{{session.Value:D}}}","messageId":"{{{Guid.NewGuid():D}}}","kind":"navigate","url":"http://example.test"}
            """;
        _ = PluginPageBridgeProtocol
            .Parse(navigation, session)
            .ShouldBeOfType<PluginPageBridgeParseOutcome.Rejected>();
        _ = PluginPageBridgeProtocol
            .Parse(new string('x', PluginContractLimits.MaximumPageMessageBytes + 1), session)
            .ShouldBeOfType<PluginPageBridgeParseOutcome.Rejected>();
    }

    [Test]
    public async Task BrowserBridge_ValidatesSourceOriginSessionSchemaAndSizeBeforeNavigation()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../src/BlokeBot.Core/wwwroot/js/plugin-page-bridge.js"
            )
        );
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
        var script = $$$"""
            import assert from "node:assert/strict";
            const { initializePluginPageBridge } = await import("data:text/javascript;base64,{{{encoded}}}");
            const windowListeners = new Map();
            globalThis.window = {
              addEventListener(type, listener) { windowListeners.set(type, listener); },
              removeEventListener(type, listener) { if (windowListeners.get(type) === listener) windowListeners.delete(type); },
            };
            globalThis.TextEncoder = TextEncoder;
            const sent = [];
            const sourceWindow = { postMessage(message, origin) { sent.push({ message, origin }); } };
            const frameListeners = new Map();
            const iframe = {
              contentWindow: sourceWindow,
              src: "https://blokebot.test/page",
              addEventListener(type, listener) { frameListeners.set(type, listener); },
              removeEventListener(type, listener) { if (frameListeners.get(type) === listener) frameListeners.delete(type); },
            };
            const calls = [];
            const responses = [];
            const dotnet = { async invokeMethodAsync(method, origin, json) {
              calls.push({ method, origin, value: JSON.parse(json) });
              return responses.shift() ?? { accepted: true, navigationUrl: null, message: "done" };
            }};
            const session = "11111111-1111-1111-1111-111111111111";
            const bridge = initializePluginPageBridge(iframe, dotnet, session, ["https://blokebot.test", "https://plugin.example"], 512);
            const emit = async (overrides = {}) => {
              await windowListeners.get("message")({
                source: sourceWindow,
                origin: "https://blokebot.test",
                data: { protocol: "blokebot.plugin-page", version: 1, sessionId: session, messageId: crypto.randomUUID(), kind: "action", action: "refresh", input: {}, ...overrides },
                ...overrides.event,
              });
            };
            await emit({ event: { source: {} } });
            await emit({ event: { origin: "https://spoof.example" } });
            await emit({ sessionId: "22222222-2222-2222-2222-222222222222" });
            await emit({ version: 2 });
            await emit({ input: { oversized: "x".repeat(1000) } });
            assert.equal(calls.length, 0);
            await emit();
            assert.equal(calls.length, 1);
            assert.equal(sent.at(-1).origin, "https://blokebot.test");
            responses.push({ accepted: true, navigationUrl: "https://plugin.example/next", message: "open" });
            await emit({ kind: "navigate", url: "https://plugin.example/next" });
            assert.equal(iframe.src, "https://plugin.example/next");
            responses.push({ accepted: true, navigationUrl: "http://unsafe.example", message: "no" });
            await emit();
            assert.equal(iframe.src, "https://plugin.example/next");
            bridge.dispose();
            assert.equal(windowListeners.has("message"), false);
            """;
        var result = await RunNodeAsync(script);
        result.ExitCode.ShouldBe(0, result.StandardError);
    }

    [Test]
    public async Task PackagedAssets_RequireExactSelectedHostAndDeclarationWithScopedCsp()
    {
        await using var host = await AssetHost.StartAsync();
        var documentRoute = Route(1, "web/index.html");
        using var unauthenticated = await host.Client.GetAsync(documentRoute);
        unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var wrongHostRequest = Request(Route(2, "web/index.html"), 1);
        using var wrongHost = await host.Client.SendAsync(wrongHostRequest);
        wrongHost.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var documentRequest = Request(documentRoute, 1);
        using var document = await host.Client.SendAsync(documentRequest);
        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        document.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        document
            .Headers.GetValues("Content-Security-Policy")
            .ShouldHaveSingleItem()
            .ShouldBe(PluginPageAssetEndpoints.PageCsp);
        document
            .Headers.GetValues("X-Content-Type-Options")
            .ShouldHaveSingleItem()
            .ShouldBe("nosniff");

        using var secondaryDocumentRequest = Request(Route(1, "web/secondary.html"), 1);
        using var secondaryDocument = await host.Client.SendAsync(secondaryDocumentRequest);
        secondaryDocument.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondaryDocument.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        secondaryDocument
            .Headers.GetValues("Content-Security-Policy")
            .ShouldHaveSingleItem()
            .ShouldBe(PluginPageAssetEndpoints.PageCsp);

        using var scriptRequest = Request(Route(1, "web/app.js"), 1);
        using var script = await host.Client.SendAsync(scriptRequest);
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
        script.Content.Headers.ContentType!.MediaType.ShouldBe("application/javascript");
        script
            .Headers.GetValues("Content-Security-Policy")
            .ShouldHaveSingleItem()
            .ShouldBe(PluginPageAssetEndpoints.PageCsp);

        using var undeclaredRequest = Request(Route(1, "secret.txt"), 1);
        using var undeclared = await host.Client.SendAsync(undeclaredRequest);
        undeclared.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await File.WriteAllBytesAsync(
            Path.Combine(host.PackageRoot, "web/app.js"),
            new byte[65_537]
        );
        using var oversizedRequest = Request(Route(1, "web/app.js"), 1);
        using var oversized = await host.Client.SendAsync(oversizedRequest);
        oversized.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Test]
    public async Task LifecycleAssetResolver_RequiresTheExactRequestedPreparedInstallation()
    {
        var manifest = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestToml.Validate(
                    PluginContractFixtures.CompleteManifestToml(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var installation = new PluginInstallationIdentity(
            manifest.Manifest.Id,
            manifest.Manifest.Release
        );
        var prepared = new PreparedPluginWorkerPackage(
            new(
                installation,
                PluginRuntimeIdentifier.LinuxX64,
                manifest.Manifest.EntryModule,
                manifest
                    .Manifest.LuaModules.Select(module => new PluginWorkerLuaModule(
                        module.Id,
                        module.Path
                    ))
                    .ToImmutableArray()
            ),
            "/packages/community-link-queue"
        )
        {
            Manifest = manifest,
        };
        var package = new PluginLifecyclePackage(
            installation,
            PluginPackageOperationId.New(),
            prepared,
            "/state/community-link-queue",
            null!,
            null!
        );
        var resolver = new LifecyclePluginPackageAssetResolver(new FixedLifecycleResolver(package));
        _ = PluginWorkerGeneration.TryCreate(1, out var generation);
        var fence = new PluginLifecycleFence(PluginLifecycleOperationId.New(), generation);

        _ = (
            await resolver.ResolveAsync(
                installation,
                package.PackageOperationId,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginPackageAssetResolution.Available>();
        var other = new PluginInstallationIdentity(
            PluginContractFixtures.PluginId("community.other-plugin"),
            installation.Release
        );
        _ = (
            await resolver.ResolveAsync(other, package.PackageOperationId, CancellationToken.None)
        ).ShouldBeOfType<PluginPackageAssetResolution.Unavailable>();
    }

    private static HttpRequestMessage Request(string route, int selectedHost)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add(
            TestAuthenticationHandler.HeaderName,
            selectedHost.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        return request;
    }

    private static string Route(int host, string path) =>
        $"/plugins/community.link-queue/hosts/{host}/features/collection/pages/queue-preview/assets/{path}";

    private static async Task<NodeResult> RunNodeAsync(string script)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--input-type=module");
        start.ArgumentList.Add("--eval");
        start.ArgumentList.Add(script);
        using var process =
            Process.Start(start) ?? throw new InvalidOperationException("Node did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, await standardOutput, await standardError);
    }

    private sealed record NodeResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class AssetHost(WebApplication app, HttpClient client, string packageRoot)
        : IAsyncDisposable
    {
        internal HttpClient Client { get; } = client;

        internal string PackageRoot { get; } = packageRoot;

        internal static async Task<AssetHost> StartAsync()
        {
            var packageRoot = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-pages-{Guid.NewGuid():N}"
            );
            _ = Directory.CreateDirectory(Path.Combine(packageRoot, "web"));
            _ = Directory.CreateDirectory(Path.Combine(packageRoot, "media"));
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, "web/index.html"),
                "<!doctype html><main>Queue</main>"
            );
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, "web/secondary.html"),
                "<!doctype html><main>Secondary</main>"
            );
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "web/app.js"), "export {};");
            await File.WriteAllBytesAsync(
                Path.Combine(packageRoot, "media/icon.webp"),
                [0x52, 0x49]
            );
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "secret.txt"), "not declared");
            var setup = PageSetup.Create(ManifestWithSecondaryDocument());
            var builder = WebApplication.CreateBuilder();
            _ = builder.Services.AddSingleton(setup.Catalogue);
            _ = builder.Services.AddSingleton<IPluginPackageAssetResolver>(
                new PackageResolver(setup.Manifest, packageRoot)
            );
            _ = builder.Services.AddSingleton<PluginPageAssetService>();
            _ = builder
                .Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    static _ => { }
                );
            _ = builder.Services.AddAuthorization(options =>
                options.AddPolicy(
                    "HostSelected",
                    static policy => policy.RequireAuthenticatedUser()
                )
            );
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();
            app.MapPluginPageAssetEndpoints();
            await app.StartAsync();
            var address =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException("Plugin asset host has no address.");
            return new(app, new HttpClient { BaseAddress = new(address) }, packageRoot);
        }

        private static byte[] ManifestWithSecondaryDocument()
        {
            var accepted = PluginManifestToml
                .Validate(
                    PluginContractFixtures.CompleteManifestToml(),
                    PluginContractFixtures.CompatibleHost()
                )
                .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
                .Manifest;
            PluginAssetId.TryCreate("secondary-document", out var assetId).ShouldBeTrue();
            var embeddedPage = accepted.Manifest.EmbeddedPages.ShouldHaveSingleItem();
            var manifest = accepted.Manifest with
            {
                Assets = accepted.Manifest.Assets.Add(
                    new(
                        assetId,
                        "web/secondary.html",
                        PluginAssetKind.Browser,
                        "text/html",
                        "Provides an additional navigable queue document.",
                        Enum.GetValues<PluginRuntimeIdentifier>().ToImmutableArray(),
                        65_536
                    )
                ),
                EmbeddedPages = accepted.Manifest.EmbeddedPages.Replace(
                    embeddedPage,
                    embeddedPage with
                    {
                        Assets = embeddedPage.Assets.Add(assetId),
                    }
                ),
            };
            var validated = PluginManifestValidator
                .Validate(manifest, PluginContractFixtures.CompatibleHost())
                .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
            return PluginManifestToml.Serialize(validated.Manifest);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            Directory.Delete(PackageRoot, recursive: true);
        }
    }

    private sealed record PageSetup(ValidatedPluginManifest Manifest, PluginPageCatalog Catalogue)
    {
        internal static PageSetup Create(byte[]? manifestToml = null)
        {
            var manifest = (
                (PluginManifestValidationOutcome.Accepted)
                    PluginManifestToml.Validate(
                        manifestToml ?? PluginContractFixtures.CompleteManifestToml(),
                        PluginContractFixtures.CompatibleHost()
                    )
            ).Manifest;
            var feature = manifest.Manifest.Features.Single(candidate =>
                candidate.Id.Value == "collection"
            );
            _ = PluginHostId.TryCreate(1, out var host);
            _ = PluginWorkerGeneration.TryCreate(1, out var workerGeneration);
            _ = PluginFeatureGeneration.TryCreate(1, out var featureGeneration);
            var fence = new PluginLifecycleFence(
                PluginLifecycleOperationId.New(),
                workerGeneration
            );
            var declarations = new PluginFeatureDeclarationRegistry();
            var features = new PluginFeatureSnapshotRegistry();
            var runtime = new PluginRuntimeSnapshotRegistry();
            declarations.Publish(manifest, fence);
            features.Publish(
                new(
                    new(manifest.Manifest.Id, feature.Id, host),
                    fence,
                    featureGeneration,
                    new PluginFeatureReadiness.Ready(),
                    PluginFeatureRevision.Initial
                )
            );
            var installation = new PluginInstallationIdentity(
                manifest.Manifest.Id,
                manifest.Manifest.Release
            );
            var now = DateTimeOffset.UtcNow;
            _ = runtime.Publish(
                new(
                    manifest.Manifest.Id,
                    installation,
                    fence.OperationId,
                    fence.Generation,
                    new(installation, fence),
                    PluginLifecyclePhase.Active,
                    PluginLifecycleOperationKind.Activate,
                    null,
                    false,
                    null,
                    PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Activated, now),
                    1,
                    now
                ),
                new EmptyWorker()
            );
            return new(manifest, new(declarations, features, runtime));
        }
    }

    private sealed class PackageResolver(ValidatedPluginManifest manifest, string root)
        : IPluginPackageAssetResolver
    {
        public ValueTask<PluginPackageAssetResolution> ResolveAsync(
            PluginInstallationIdentity installation,
            PluginPackageOperationId packageOperationId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginPackageAssetResolution>(
                installation
                == new PluginInstallationIdentity(manifest.Manifest.Id, manifest.Manifest.Release)
                    ? new PluginPackageAssetResolution.Available(manifest, root)
                    : new PluginPackageAssetResolution.Unavailable()
            );
    }

    private sealed class FixedLifecycleResolver(PluginLifecyclePackage package)
        : IPluginLifecyclePackageResolver
    {
        public ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
            PluginInstallationIdentity installation,
            PluginPackageOperationId packageOperationId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecyclePackageResolution>(
                new PluginLifecyclePackageResolution.Available(package)
            );
    }

    private sealed class EmptyWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;
        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>().Task;

        public ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginLiveInvocation invocation,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "PluginPageTest";
        internal const string HeaderName = "X-Plugin-Page-Test-Host";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!int.TryParse(Request.Headers[HeaderName], out var hostId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var host = new BotHostChoice(
                hostId,
                $"host{hostId}",
                $"Host {hostId}",
                AuthRole.Streamer
            );
            var identity = new ClaimsIdentity(
                [
                    new(ClaimTypes.NameIdentifier, "streamer-id"),
                    new(ClaimTypes.Name, "Streamer"),
                    new(AuthClaims.Login, "streamer"),
                    new(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(host)),
                    new(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(host)),
                ],
                Scheme.Name
            );
            return Task.FromResult(
                AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name))
            );
        }
    }
}
