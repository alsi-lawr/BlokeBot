using System.Security.Claims;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginFeatureNavigationTests
{
    [Test]
    public async Task FeatureSave_WhenSelectedHostChanges_DoesNotWriteTheStaleHost()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (firstHostId, secondHostId) = await SeedHostsAsync(database);
        var declarations = new PluginFeatureDeclarationRegistry();
        var snapshots = new PluginFeatureSnapshotRegistry();
        var manifest = ValidatedManifest();
        declarations.Publish(manifest, Fence());
        var codec = new PluginSettingValuesCodec();
        var store = new EfPluginFeatureStore(database, codec);
        var manager = new PluginFeatureManager(
            store,
            declarations,
            new HealthyLifecycle(),
            new EmptyPluginCoreDependencyChecker(),
            new EmptyPluginFeatureReconciler(),
            new UnusedSecretProtector(),
            new PluginSettingsValidator(),
            codec,
            snapshots,
            new PluginLifecycleSerialization()
        );
        PluginHostId.TryCreate(firstHostId, out var firstPluginHost).ShouldBeTrue();
        var key = new PluginFeatureKey(
            manifest.Manifest.Id,
            Feature("collection"),
            firstPluginHost
        );
        var owner = new PluginConfigurationOwner.Feature(key);
        var initial = await manager.LoadConfigurationAsync(owner, CancellationToken.None);
        var loaded = initial.ShouldBeOfType<PluginConfigurationLoadOutcome.Loaded>();
        var seeded = await manager.SaveConfigurationAsync(
            new(owner, loaded.Configuration.Revision, CollectionValues(), []),
            CancellationToken.None
        );
        var saved = seeded.ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = context.Services.AddSingleton(manager);
        _ = context.Services.AddSingleton<IPluginFeatureSnapshotProvider>(snapshots);
        _ = context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        _ = context.Services.AddSingleton(
            new UiFaultTelemetry(NullLogger<UiFaultTelemetry>.Instance)
        );
        _ = context.Services.AddSingleton(new ToastService());
        _ = context.Services.AddSingleton(
            new ModeratorAuthorityService(null!, null!, null!, null!, TimeProvider.System)
        );
        var firstHost = Host(firstHostId, "first");
        var secondHost = Host(secondHostId, "second");
        var principal = TestPrincipals.BlokeBotUser(
            "streamer",
            role: AuthRole.Streamer,
            availableHosts: [firstHost],
            selectedHost: firstHost
        );
        var authentication = Task.FromResult(new AuthenticationState(principal));
        RenderFragment pageContent = builder =>
        {
            builder.OpenComponent<PluginFeatureSettingsPage>(0);
            builder.AddAttribute(
                1,
                nameof(PluginFeatureSettingsPage.PluginIdValue),
                manifest.Manifest.Id.Value
            );
            builder.AddAttribute(2, nameof(PluginFeatureSettingsPage.FeatureIdValue), "collection");
            builder.CloseComponent();
        };
        var host = context.Render<CascadingValue<Task<AuthenticationState>>>(parameters =>
            parameters
                .Add(value => value.Value, authentication)
                .Add(value => value.IsFixed, true)
                .Add(value => value.ChildContent, pageContent)
        );
        host.Find("#plugin-setting-chat-command").Input("!changed");
        ReplaceSelectedHost(principal, secondHost);

        host.Find("button.save-changes-button").Click();

        var current = await store.LoadConfigurationAsync(owner, CancellationToken.None);
        current.Revision.ShouldBe(saved.Configuration.Revision);
        current
            .Values.Entries.Single(entry => entry.SettingId == Setting("chat-command"))
            .Value.ShouldBeOfType<PluginSettingValue.Text>()
            .Value.ShouldBe("!link");
    }

    [Test]
    public void DeclaredFeatureRoute_PushesCanonicalDestinationToHistory()
    {
        using var context = new BunitContext();
        var declarations = new PluginFeatureDeclarationRegistry();
        var snapshots = new PluginFeatureSnapshotRegistry();
        _ = context.Services.AddSingleton<IPluginFeatureDeclarationProvider>(declarations);
        _ = context.Services.AddSingleton<IPluginFeatureSnapshotProvider>(snapshots);
        var authorization = context.AddAuthorization();
        _ = authorization.SetAuthorized("streamer");
        _ = authorization.SetPolicies("BotAdmin");
        var manifest = ValidatedManifest();
        declarations.Publish(manifest, Fence());
        PluginHostId.TryCreate(41, out var firstHost).ShouldBeTrue();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        var routes = Routes(navigation, static () => Task.CompletedTask);
        var component = context.Render<NavMenuPluginFeatures>(parameters =>
            parameters.Add(value => value.HostId, firstHost).Add(value => value.Routes, routes)
        );
        var historyDepth = navigation.History.Count;
        const string Route = "plugins/community.link-queue/features/collection";

        var destination = component.Find($"a[href='{Route}']").GetAttribute("href");
        navigation.NavigateTo(destination!);

        navigation.ToBaseRelativePath(navigation.Uri).ShouldBe(Route);
        navigation.History.Count.ShouldBe(historyDepth + 1);
        navigation.History.First().Uri.ShouldEndWith(Route);
    }

    private static ValidatedPluginManifest ValidatedManifest() =>
        PluginManifestToml.Validate(
            PluginContractFixtures.CompleteManifestToml(),
            PluginContractFixtures.CompatibleHost()
        )
            is PluginManifestValidationOutcome.Accepted accepted
            ? accepted.Manifest
            : throw new InvalidOperationException("The plugin fixture is invalid.");

    private static PluginLifecycleFence Fence()
    {
        PluginWorkerGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        return new(PluginLifecycleOperationId.New(), generation);
    }

    private static async Task<(int First, int Second)> SeedHostsAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var first = new BotHost
        {
            TwitchUserId = "first-id",
            Login = "first",
            DisplayName = "First",
            CreatedAtUtc = DateTime.UtcNow,
        };
        var second = new BotHost
        {
            TwitchUserId = "second-id",
            Login = "second",
            DisplayName = "Second",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.AddRange(first, second);
        _ = await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    private static PluginSettingValues CollectionValues() =>
        Values(
            Entry("collect-messages", new PluginSettingValue.Boolean(true)),
            Entry("chat-command", new PluginSettingValue.Text("!link")),
            Entry("queue-note", new PluginSettingValue.Text("Review first.")),
            Entry("maximum-links", new PluginSettingValue.Integer(40)),
            Entry("minimum-score", new PluginSettingValue.Number(4.5m)),
            Entry("wait-between-links", new PluginSettingValue.Duration(30))
        );

    private static PluginSettingValues Values(params PluginSettingValueEntry[] entries) =>
        PluginSettingValues.Create(entries) is PluginSettingValuesOutcome.Created created
            ? created.Values
            : throw new InvalidOperationException("The test plugin values are invalid.");

    private static PluginSettingValueEntry Entry(string id, PluginSettingValue value) =>
        new(Setting(id), value);

    private static PluginSettingId Setting(string value) =>
        PluginSettingId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("The test setting ID is invalid.");

    private static PluginFeatureId Feature(string value) =>
        PluginFeatureId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("The test feature ID is invalid.");

    private static BotHostChoice Host(int id, string login) =>
        new(id, login, login, AuthRole.Streamer);

    private static void ReplaceSelectedHost(ClaimsPrincipal principal, BotHostChoice selected)
    {
        var identity = principal.Identity.ShouldBeOfType<ClaimsIdentity>();
        foreach (
            var claim in identity
                .Claims.Where(claim =>
                    claim.Type is BotHostClaims.AvailableHost or BotHostClaims.SelectedHost
                )
                .ToArray()
        )
        {
            identity.RemoveClaim(claim);
        }
        identity.AddClaim(
            new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(selected))
        );
        identity.AddClaim(
            new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(selected))
        );
    }

    private sealed class HealthyLifecycle : IPluginFeatureLifecycleHealth
    {
        public bool IsCurrent(PluginFeatureDeclaration declaration) => true;

        public bool IsHealthy(PluginFeatureDeclaration declaration) => true;
    }

    private sealed class UnusedSecretProtector : IPluginSecretProtector
    {
        public PluginProtectedSecret Protect(
            PluginSecretKey key,
            PluginSecretPlaintext plaintext
        ) => new(new byte[] { 1 });
    }

    private static NavMenuRouteBindings Routes(
        NavigationManager navigation,
        Func<Task> navigated
    ) =>
        new(
            (route, exact) => IsCurrent(navigation, route, exact) ? "page" : null,
            route => IsCurrent(navigation, route, exact: false),
            route => IsCurrent(navigation, route, exact: false) ? "page" : null,
            _ => null,
            static (_, _) => static _ => { },
            navigated
        );

    private static bool IsCurrent(NavigationManager navigation, string route, bool exact)
    {
        var path = navigation.ToBaseRelativePath(navigation.Uri).Trim('/');
        route = route.Trim('/');
        return string.Equals(path, route, StringComparison.OrdinalIgnoreCase)
            || (
                !exact
                && route.Length > 0
                && path.StartsWith($"{route}/", StringComparison.OrdinalIgnoreCase)
            );
    }
}
