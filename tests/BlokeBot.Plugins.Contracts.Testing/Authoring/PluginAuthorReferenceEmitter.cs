using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BlokeBot.Plugins.Runtime;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static class PluginAuthorReferenceEmitter
{
    private static readonly NullabilityInfoContext _nullability = new();

    internal static string Emit(PluginAuthoringContract contract)
    {
        var output = new StringBuilder();
        _ = output
            .Append("# BlokeBot plugin author reference v")
            .Append(contract.Runtime.HostApiVersion.Value)
            .AppendLine();
        _ = output.AppendLine();
        _ = output.AppendLine(
            "This reference is generated from the canonical plugin contracts. Regenerate the author artifacts instead of editing its contract tables."
        );
        _ = output.AppendLine();
        AppendRuntime(contract, output);
        AppendIdentityAndSubmission(contract, output);
        AppendPackage(contract, output);
        AppendManifestShapes(output);
        AppendHostModules(contract, output);
        AppendCanonicalPublicApi(contract, output);
        AppendInvocationGuidance(contract, output);
        AppendExamples(output);
        AppendRemoval(output);
        return output.ToString();
    }

    private static void AppendRuntime(PluginAuthoringContract contract, StringBuilder output)
    {
        _ = output.AppendLine("## Runtime contract");
        _ = output.AppendLine();
        _ = output.AppendLine("| Contract | Version |");
        _ = output.AppendLine("| --- | ---: |");
        _ = output
            .Append("| Manifest | ")
            .Append(contract.Runtime.ManifestVersion)
            .AppendLine(" |");
        _ = output
            .Append("| Package policy | ")
            .Append(contract.Runtime.PackagePolicyVersion)
            .AppendLine(" |");
        _ = output
            .Append("| Typed value | ")
            .Append(contract.Runtime.ValueContractVersion)
            .AppendLine(" |");
        _ = output
            .Append("| Host API | ")
            .Append(contract.Runtime.HostApiVersion.Value)
            .AppendLine(" |");
        _ = output.AppendLine("| Lua | 5.4 |");
        _ = output.AppendLine();
        _ = output.AppendLine(
            CultureInfo.InvariantCulture,
            $"Plugins are `{contract.Runtime.Trust.TrustLevel}` and run with `{contract.Runtime.Trust.OperatingSystemAccess}` operating-system access. The `{contract.Runtime.Trust.ProcessIsolation}` process boundary limits availability failures; it is not a security boundary. The Lua standard library is `{contract.Runtime.Trust.StandardLibrary}`. Do not describe a plugin as sandboxed."
        );
        _ = output.AppendLine();
    }

    private static void AppendIdentityAndSubmission(
        PluginAuthoringContract contract,
        StringBuilder output
    )
    {
        var identifiers = contract.IdentifierSyntax;
        var tags = contract.GitTagSyntax;
        _ = output.AppendLine("## Identity and submission");
        _ = output.AppendLine();
        _ = output.AppendLine(
            CultureInfo.InvariantCulture,
            $"Use one stable lowercase plugin ID. `PluginIdentifierSyntaxContract.Current` requires {identifiers.MinimumLength}-{identifiers.MaximumLength} characters, a lowercase ASCII letter prefix, a lowercase ASCII letter or digit suffix, and separators from `{identifiers.Separators}`. Adjacent separators are {(identifiers.PermitsAdjacentSeparators ? "permitted" : "rejected")}."
        );
        _ = output.AppendLine();
        _ = output.AppendLine(
            CultureInfo.InvariantCulture,
            $"A marketplace submission names the manifest's declared semantic version and one mutable Git tag. `PluginGitTagSyntaxContract.Current` accepts {tags.MinimumLength}-{tags.MaximumLength} characters and rejects all-hex values {tags.MinimumCommitShaLength}-{tags.MaximumCommitShaLength} characters long, so a commit SHA is never plugin identity."
        );
        _ = output.AppendLine();
        _ = output.AppendLine(
            $"The curated repository path is `plugins/<plugin-id>/{PluginPackage.ManifestPath}`. The directory name exactly matches the manifest ID. The manifest owns author, search tags, optional presentation URLs, supported release targets, release identity, compatibility, and runtime declarations; there is no global catalogue or generated index."
        );
        _ = output.AppendLine();
    }

    private static void AppendPackage(PluginAuthoringContract contract, StringBuilder output)
    {
        _ = output.AppendLine("## Package and targets");
        _ = output.AppendLine();
        _ = output.AppendLine(
            CultureInfo.InvariantCulture,
            $"The package root contains `{PluginPackage.ManifestPath}`. Declare every Lua module, browser or media asset, and other payload. Paths are relative canonical `/` paths. The canonical package boundary reports concrete `PluginPackageEntryErrorCode` values for absolute, dot-segment, link, undeclared, missing, oversized, duplicate, and case-folded-collision failures. Limits are {PluginContractLimits.MaximumPackageEntries} entries and {PluginContractLimits.MaximumPackageBytes} bytes."
        );
        _ = output.AppendLine();
        _ = output.AppendLine(
            "Every asset and payload declares its path, purpose, maximum size, and supported runtime identifiers. Every declaration target must be one of the manifest's supported release targets; separate declarations may select different target subsets. Payloads may be native files, .NET assemblies, WebAssembly, or another plugin-managed type. Admission enforces declarations, targets, paths, links, collisions, and byte limits; it does not infer or police a payload's byte type from its extension."
        );
        _ = output.AppendLine();
        _ = output.AppendLine("Supported runtime identifiers:");
        _ = output.AppendLine();
        foreach (var runtimeIdentifier in contract.RuntimeIdentifiers)
        {
            _ = output.Append("- `").Append(TomlEnum(runtimeIdentifier)).AppendLine("`");
        }

        _ = output.AppendLine();
        _ = output.AppendLine(
            "Lua modules are the only BlokeBot-managed entrypoints. Other payloads and their dependencies remain the trusted plugin's responsibility."
        );
        _ = output.AppendLine();
    }

    private static void AppendManifestShapes(StringBuilder output)
    {
        _ = output.AppendLine("## Manifest shapes");
        _ = output.AppendLine();
        _ = output.AppendLine(
            "TOML names and shapes below come from the canonical `PluginManifest` graph. TOML keys are camel-case, reject unknown or duplicate declarations, and use the listed discriminator values for variant records."
        );
        _ = output.AppendLine();
        foreach (var type in ManifestTypes())
        {
            _ = output.Append("### `").Append(DisplayName(type)).AppendLine("`");
            _ = output.AppendLine();
            if (type.IsEnum)
            {
                _ = output
                    .Append("Values: ")
                    .AppendJoin(
                        ", ",
                        Enum.GetValues(type).Cast<object>().Select(value => $"`{TomlEnum(value)}`")
                    )
                    .AppendLine();
                _ = output.AppendLine();
                continue;
            }

            var alternatives = type.GetCustomAttributes<TomlDerivedTypeAttribute>().ToArray();
            if (alternatives.Length > 0)
            {
                _ = output
                    .Append("Variants: ")
                    .AppendJoin(
                        ", ",
                        alternatives.Select(alternative =>
                            $"`{alternative.Discriminator}` -> `{DisplayName(alternative.DerivedType)}`"
                        )
                    )
                    .AppendLine();
                _ = output.AppendLine();
                continue;
            }

            _ = output.AppendLine("| TOML key | Type |");
            _ = output.AppendLine("| --- | --- |");
            foreach (
                var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            )
            {
                if (!IsManifestTomlProperty(property))
                {
                    continue;
                }

                _ = output
                    .Append("| `")
                    .Append(JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                    .Append("` | ")
                    .Append(MarkdownType(property))
                    .AppendLine(" |");
            }

            _ = output.AppendLine();
        }
    }

    private static void AppendHostModules(PluginAuthoringContract contract, StringBuilder output)
    {
        _ = output.AppendLine("## Host calls");
        _ = output.AppendLine();
        _ = output.AppendLine(
            "Call `blokebot.host.call(module, operation, ...)`. Values are limited to nil, boolean, finite number, string, array, and string-keyed map. The generated Lua language-server stub supplies one overload per operation."
        );
        _ = output.AppendLine();
        _ = output.AppendLine(
            "`context.current` takes no arguments. The host derives its result from the admitted invocation; plugins cannot supply or override identities. Every result includes `kind`, `pluginId`, `pluginVersion`, and `pluginTag`. Channel results also include `hostId`, `featureId`, and any applicable `actor`, `stream`, `command`, `event`, `schedule`, or `web` map. Automation and page results include their exact host, feature, and invocation map. Migration results include only the migration identity in addition to the common fields."
        );
        _ = output.AppendLine();
        _ = output.AppendLine(
            "`settings.installation` and `settings.feature` also take no arguments. They return only configured values declared for the invoking plugin and exact installation or host-feature owner. Protected values are available only inside that admitted plugin invocation. Do not copy protected values into logs, diagnostics, audit fields, failure messages, or generated documents. Missing optional values are absent from the returned map; a configuration read or decryption fault returns only the generic typed host failure. Settings reads are unavailable during migration."
        );
        _ = output.AppendLine();
        _ = output.AppendLine("| Module | Operation | Contexts | Arguments | Result |");
        _ = output.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var module in contract.HostModules)
        {
            foreach (var operation in module.Operations)
            {
                _ = output
                    .Append("| `")
                    .Append(module.Id.Value)
                    .Append("` | `")
                    .Append(operation.Id.Value)
                    .Append("` | ")
                    .AppendJoin(", ", operation.PermittedContexts.Select(TomlEnum))
                    .Append(" | ")
                    .AppendJoin(", ", operation.ArgumentKinds.Select(TomlEnum))
                    .Append(" | ")
                    .Append(TomlEnum(operation.ResultKind))
                    .AppendLine(" |");
            }
        }

        _ = output.AppendLine();
        _ = output.AppendLine(
            "Declare every host module your plugin uses with a minimum and maximum API version. Host failures and cancellations are typed outcomes. Treat safe failure messages as operator-readable detail, not provider internals."
        );
        _ = output.AppendLine();
    }

    private static void AppendCanonicalPublicApi(
        PluginAuthoringContract contract,
        StringBuilder output
    )
    {
        _ = output.AppendLine("## Canonical public plugin API");
        _ = output.AppendLine();
        _ = output.AppendLine(
            "Every row is discovered from exported public types and their declared public members in the canonical plugin contract assemblies and namespaces. Regenerate this reference when that surface changes."
        );
        _ = output.AppendLine();
        foreach (var surface in contract.PublicContractSurfaces)
        {
            _ = output
                .Append("### `")
                .Append(surface.Assembly.GetName().Name)
                .Append("` / `")
                .Append(surface.Namespace)
                .AppendLine("`");
            _ = output.AppendLine();
            _ = output.AppendLine("| Kind | Canonical API | Shape |");
            _ = output.AppendLine("| --- | --- | --- |");
            foreach (
                var member in surface.ExportedTypes.SelectMany(type =>
                    PluginAuthoringSemanticCoverage.Members(type)
                )
            )
            {
                _ = output.AppendLine(PluginAuthoringSemanticCoverage.MarkdownRow(member));
            }

            _ = output.AppendLine();
        }
    }

    private static void AppendInvocationGuidance(
        PluginAuthoringContract contract,
        StringBuilder output
    )
    {
        _ = output.AppendLine("## Invocations and effects");
        _ = output.AppendLine();
        _ = output.AppendLine(
            "Features may declare settings, Twitch requirements, commands, event handlers, schedules, webhooks, actions, automation definitions and templates, generated pages, and embedded pages. A declaration does not register a live feature or run Lua."
        );
        _ = output.AppendLine();
        _ = output.AppendLine(
            CultureInfo.InvariantCulture,
            $"Host-call waits return the canonical `{nameof(PluginWorkerInvocationOutcome.Cancelled)}` outcome with a `{nameof(PluginCancellationReason)}` and whether the worker terminated. Cancellation stops the coroutine from waiting and a later result is not resumed. It does not promise to undo a chat message, schedule, HTTP request, SQLite write, or other effect that completed before cancellation won."
        );
        _ = output.AppendLine();
        _ = output.AppendLine(
            CultureInfo.InvariantCulture,
            $"Use the `storage` module for plugin-private SQLite operations and `http.send` for approved web integrations. Generated and embedded UI pages must use declared modules or browser assets. A failed update migration becomes `{PluginLifecycleFailureCode.MigrationFailed}` and leaves the selected update `{PluginLifecyclePhase.Faulted}` without resuming the old generation. A worker crash reports `{PluginWorkerFailureCode.WorkerExited}` and is isolated by the `{contract.Runtime.Trust.ProcessIsolation}` boundary."
        );
        _ = output.AppendLine();
    }

    private static void AppendExamples(StringBuilder output)
    {
        _ = output.AppendLine("## Executable examples");
        _ = output.AppendLine();
        _ = output.AppendLine(
            "Published sources live under `examples/plugins`. Optional package-local `tests.toml` files define author-harness scenarios; they are not part of normal plugin package validation. The Contracts test harness packages each example from local files, validates it for every supported RID, and executes every declared scenario through the supported worker protocol on the current RID. The harness has deterministic host adapters and does not install into the production local-source directory, join production inventory, contact Twitch, or make third-party network requests."
        );
        _ = output.AppendLine();
    }

    private static void AppendRemoval(StringBuilder output)
    {
        _ = output.AppendLine("## Remove");
        _ = output.AppendLine();
        _ = output.AppendLine(
            CultureInfo.InvariantCulture,
            $"`{PluginLifecycleOperationKind.Remove}` is destructive for plugin-owned state. Canonical removal owners delete the installed package, installation and channel settings, feature state, configuration, secrets, schedules, private data, automation definitions, ledgers, dependent flows and nodes, run history, marketplace receipts, and invocation context. A retained owner resource faults removal as `{PluginLifecycleFailureCode.RemovalFailed}`; there is no purge or retention mode. The derived marketplace snapshot is not plugin-owned installation state and remains discoverable for a later install."
        );
    }

    private static IReadOnlyList<Type> ManifestTypes()
    {
        var assembly = typeof(PluginManifest).Assembly;
        var pending = new Queue<Type>();
        var result = new HashSet<Type>();
        pending.Enqueue(typeof(PluginManifest));
        while (pending.TryDequeue(out var type))
        {
            type = Unwrap(type);
            if (
                type.Assembly != assembly
                || typeof(PluginContractIdentifier).IsAssignableFrom(type)
                || type == typeof(SemanticVersion)
                || type == typeof(PluginApiVersion)
                || type == typeof(PluginGitTag)
                || !result.Add(type)
            )
            {
                continue;
            }

            if (type.IsEnum)
            {
                continue;
            }

            foreach (var derived in type.GetCustomAttributes<TomlDerivedTypeAttribute>())
            {
                pending.Enqueue(derived.DerivedType);
            }

            foreach (
                var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            )
            {
                if (IsManifestTomlProperty(property))
                {
                    pending.Enqueue(property.PropertyType);
                }
            }
        }

        return result
            .OrderBy(type => type == typeof(PluginManifest) ? 0 : 1)
            .ThenBy(DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    private static Type Unwrap(Type type)
    {
        if (type == typeof(string))
        {
            return type;
        }

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return Unwrap(nullable);
        }

        if (type.IsArray)
        {
            return Unwrap(type.GetElementType()!);
        }

        var enumerable = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            );
        return enumerable is null ? type : Unwrap(enumerable.GetGenericArguments()[0]);
    }

    private static string MarkdownType(PropertyInfo property)
    {
        var rendered = MarkdownType(property.PropertyType);
        NullabilityState state;
        lock (_nullability)
        {
            state = _nullability.Create(property).ReadState;
        }

        return
            Nullable.GetUnderlyingType(property.PropertyType) is null
            && state == NullabilityState.Nullable
            ? $"{rendered} (optional)"
            : rendered;
    }

    private static string MarkdownType(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return $"{MarkdownType(nullable)} (optional)";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(Uri))
        {
            return "absolute URI";
        }

        if (type == typeof(SemanticVersion))
        {
            return "semantic version string";
        }

        if (type == typeof(PluginApiVersion))
        {
            return "API version number";
        }

        if (type == typeof(PluginGitTag))
        {
            return "mutable Git tag string";
        }

        if (typeof(PluginContractIdentifier).IsAssignableFrom(type))
        {
            return "contract identifier string";
        }

        if (type.IsPrimitive || type == typeof(decimal))
        {
            return "number";
        }

        var unwrapped = Unwrap(type);
        return unwrapped != type ? $"{MarkdownType(unwrapped)} array" : $"`{DisplayName(type)}`";
    }

    private static string DisplayName(Type type) =>
        type.DeclaringType is null ? type.Name : $"{type.DeclaringType.Name}.{type.Name}";

    private static bool IsManifestTomlProperty(PropertyInfo property) =>
        property.SetMethod is not null
        && property.GetCustomAttribute<TomlIgnoreAttribute>() is null;

    private static string TomlEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum => TomlEnum((object)value);

    private static string TomlEnum(object value) =>
        value is PluginRuntimeIdentifier runtimeIdentifier
            ? runtimeIdentifier switch
            {
                PluginRuntimeIdentifier.LinuxX64 => "linux-x64",
                PluginRuntimeIdentifier.LinuxArm64 => "linux-arm64",
                PluginRuntimeIdentifier.MacOsArm64 => "osx-arm64",
                PluginRuntimeIdentifier.WindowsX64 => "win-x64",
                PluginRuntimeIdentifier.WindowsArm64 => "win-arm64",
            }
            : JsonNamingPolicy.CamelCase.ConvertName(value.ToString()!);
}
