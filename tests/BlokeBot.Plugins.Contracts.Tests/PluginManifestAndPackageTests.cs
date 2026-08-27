using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginManifestAndPackageTests
{
    [Test]
    [Arguments("array")]
    [Arguments("map")]
    public void AutomationPorts_AcceptBoundedStructuredKinds(string valueKind)
    {
        var manifest = PluginContractFixtures.ManifestReplacing(
            "valueKind = \"string\"",
            $"valueKind = \"{valueKind}\""
        );

        _ = PluginManifestToml
            .Validate(manifest, PluginContractFixtures.CompatibleHost())
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
    }

    [Test]
    public void AutomationPorts_RejectNilAsAConnectableSchemaKind()
    {
        var manifest = PluginContractFixtures.ManifestReplacing(
            "valueKind = \"string\"",
            "valueKind = \"nil\""
        );

        ManifestErrors(
                PluginManifestToml.Validate(manifest, PluginContractFixtures.CompatibleHost())
            )
            .Select(static error => error.Code)
            .ShouldContain(PluginManifestErrorCode.InvalidAutomationDefinition);
    }

    [Test]
    public void AutomationTemplate_RejectsDefinitionsOwnedByAnotherFeature()
    {
        var manifest = PluginContractFixtures.ManifestReplacing(
            "featureId = \"publishing\"\nkind = \"source\"",
            "featureId = \"collection\"\nkind = \"source\""
        );

        ManifestErrors(
                PluginManifestToml.Validate(manifest, PluginContractFixtures.CompatibleHost())
            )
            .Select(static error => error.Code)
            .ShouldContain(PluginManifestErrorCode.InvalidAutomationTemplate);
    }

    [Test]
    public void DeclaredMixedPayloadPackage_IsAcceptedForMatchingTarget()
    {
        var target = PluginContractFixtures.CompatibleHost();

        var manifestOutcome = PluginManifestToml.Validate(
            PluginContractFixtures.CompleteManifestToml(),
            target
        );
        var acceptedManifest =
            manifestOutcome.ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
        var serialized = PluginManifestToml.Serialize(acceptedManifest.Manifest);
        var packageOutcome = PluginPackageValidator.Validate(
            PluginContractFixtures.CompletePackage(),
            target
        );

        _ = PluginManifestToml
            .Validate(serialized, target)
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
        PluginManifestToml.Serialize(acceptedManifest.Manifest).ShouldBe(serialized);
        var acceptedPackage =
            packageOutcome.ShouldBeOfType<PluginPackageValidationOutcome.Accepted>();
        acceptedPackage.Manifest.Manifest.Id.Value.ShouldBe("community.link-queue");
        acceptedPackage.Manifest.Manifest.Release.Tag.Value.ShouldBe("community-link-queue");
    }

    [Test]
    public void TomlRoundTrip_PreservesEveryPluginValueShape()
    {
        var target = PluginContractFixtures.CompatibleHost();
        var accepted = PluginManifestToml
            .Validate(PluginContractFixtures.CompleteManifestToml(), target)
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        var template = accepted.Manifest.AutomationTemplates.ShouldHaveSingleItem();
        var node = template.Nodes[0];
        var configuration = new PluginValue.Map([
            new("nil", new PluginValue.Nil()),
            new("boolean", new PluginValue.Boolean(true)),
            new("number", new PluginValue.Number(3.5)),
            new("string", new PluginValue.String("value")),
            new(
                "array",
                new PluginValue.Array([
                    new PluginValue.Boolean(false),
                    new PluginValue.Map([new("nested", new PluginValue.String("map"))]),
                ])
            ),
        ]);
        var manifest = accepted.Manifest with
        {
            AutomationTemplates = accepted.Manifest.AutomationTemplates.Replace(
                template,
                template with
                {
                    Nodes = template.Nodes.Replace(
                        node,
                        node with
                        {
                            Configuration = configuration,
                        }
                    ),
                }
            ),
        };
        var validated = PluginManifestValidator
            .Validate(manifest, target)
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();

        var roundTripped = PluginManifestToml
            .Validate(PluginManifestToml.Serialize(validated.Manifest), target)
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest.Manifest.AutomationTemplates.ShouldHaveSingleItem()
            .Nodes[0]
            .Configuration;

        PluginValueComparer.SemanticallyEquals(configuration, roundTripped).ShouldBeTrue();
    }

    [Test]
    public void ManifestIdentity_RejectsCommitShaAndUnknownCommitField()
    {
        var target = PluginContractFixtures.CompatibleHost();
        var shaTag = PluginContractFixtures.ManifestReplacing(
            "community-link-queue",
            "0123456789abcdef0123456789abcdef01234567"
        );
        var shaField = PluginContractFixtures.ManifestReplacing(
            "tag = \"community-link-queue\"",
            "tag = \"community-link-queue\"\ncommitSha = \"0123456789abcdef\""
        );
        var nullDescription = PluginContractFixtures.ManifestReplacing(
            "description = \"Collects community links and publishes them on a schedule.\"",
            "description = 42"
        );
        var unknownVariantField = PluginContractFixtures.ManifestReplacing(
            "maximumLength = 256\nkind = \"secret\"",
            "maximumLength = 256\nkind = \"secret\"\nminimum = 1"
        );

        var shaTagErrors = ManifestErrors(PluginManifestToml.Validate(shaTag, target));
        var shaFieldErrors = ManifestErrors(PluginManifestToml.Validate(shaField, target));
        var nullDescriptionErrors = ManifestErrors(
            PluginManifestToml.Validate(nullDescription, target)
        );
        var unknownVariantErrors = ManifestErrors(
            PluginManifestToml.Validate(unknownVariantField, target)
        );

        shaTagErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedToml);
        shaFieldErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedToml);
        nullDescriptionErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedToml);
        unknownVariantErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedToml);
    }

    [Test]
    public void ManifestValidation_RejectsDuplicateAndIncompatibleDeclarations()
    {
        var target = PluginContractFixtures.CompatibleHost();
        var duplicate = PluginContractFixtures.ManifestReplacing(
            "id = \"publishing\"",
            "id = \"collection\""
        );
        var incompatible = PluginContractFixtures.ManifestReplacing(
            "minimumApiVersion = 1\nmaximumApiVersion = 1",
            "minimumApiVersion = 2\nmaximumApiVersion = 2"
        );
        var escapingModule = PluginContractFixtures.ManifestReplacing(
            "path = \"lua/main.lua\"",
            "path = \"../main.lua\""
        );
        var duplicateAsset = PluginContractFixtures.ManifestReplacing(
            "id = \"queue-script\"",
            "id = \"queue-document\""
        );
        var malformedId = PluginContractFixtures.ManifestReplacing(
            "community.link-queue",
            "Community.LinkQueue"
        );

        var duplicateErrors = ManifestErrors(PluginManifestToml.Validate(duplicate, target));
        var incompatibleErrors = ManifestErrors(PluginManifestToml.Validate(incompatible, target));
        var escapingErrors = ManifestErrors(PluginManifestToml.Validate(escapingModule, target));
        var duplicateAssetErrors = ManifestErrors(
            PluginManifestToml.Validate(duplicateAsset, target)
        );
        var malformedIdErrors = ManifestErrors(PluginManifestToml.Validate(malformedId, target));

        duplicateErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.DuplicateIdentifier);
        incompatibleErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.IncompatibleDeclaration);
        escapingErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.InvalidLuaModule);
        duplicateAssetErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.DuplicateIdentifier);
        malformedIdErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedToml);
    }

    [Test]
    public void PayloadDeclarations_RequirePurposeAndSupportedTargets()
    {
        var missingPurpose = PluginContractFixtures.ManifestReplacing(
            "Provides the plugin-managed native queue helper.",
            ""
        );
        var missingTargets = PluginContractFixtures.ManifestReplacing("[\"linux-x64\"]", "[]");
        var unsupportedTarget = PluginContractFixtures.ManifestReplacing(
            "[\"linux-x64\"]",
            "[\"freebsd-x64\"]"
        );

        ManifestErrors(
                PluginManifestToml.Validate(missingPurpose, PluginContractFixtures.CompatibleHost())
            )
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.InvalidPayload);
        ManifestErrors(
                PluginManifestToml.Validate(
                    unsupportedTarget,
                    PluginContractFixtures.CompatibleHost()
                )
            )
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedToml);
        ManifestErrors(
                PluginManifestToml.Validate(missingTargets, PluginContractFixtures.CompatibleHost())
            )
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.InvalidPayload);
    }

    [Test]
    public void PackagePolicy_RejectsUnsupportedReleaseTarget()
    {
        var target = PluginContractFixtures.CompatibleHost() with
        {
            RuntimeIdentifier = PluginRuntimeIdentifier.WindowsX64,
        };
        var linuxOnlyManifest = PluginContractFixtures.ManifestReplacing(
            "[\"linux-x64\", \"linux-arm64\", \"osx-arm64\", \"win-x64\", \"win-arm64\"]",
            "[\"linux-x64\"]"
        );
        var linuxOnlyPackage = PluginContractFixtures
            .CompletePackage()
            .Select(entry =>
                entry.Path == PluginPackage.ManifestPath
                    ? new PluginPackageEntry.File(PluginPackage.ManifestPath, linuxOnlyManifest)
                    : entry
            )
            .ToArray();

        PackageManifestErrorCodes(PluginPackageValidator.Validate(linuxOnlyPackage, target))
            .ShouldContain(PluginManifestErrorCode.IncompatibleDeclaration);
    }

    [Test]
    public void ManifestTargets_AcceptSeparateLinuxAndWindowsAssetAndPayloadDeclarations()
    {
        var manifest = PluginManifestToml
            .Validate(
                PluginContractFixtures.CompleteManifestToml(),
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest.Manifest;
        manifest = manifest with
        {
            Compatibility = manifest.Compatibility with
            {
                SupportedTargets =
                [
                    PluginRuntimeIdentifier.LinuxX64,
                    PluginRuntimeIdentifier.WindowsX64,
                ],
            },
            Assets =
            [
                manifest.Assets[0] with
                {
                    RuntimeIdentifiers = [PluginRuntimeIdentifier.LinuxX64],
                },
                manifest.Assets[1] with
                {
                    RuntimeIdentifiers = [PluginRuntimeIdentifier.WindowsX64],
                },
                manifest.Assets[2] with
                {
                    RuntimeIdentifiers = [PluginRuntimeIdentifier.WindowsX64],
                },
            ],
            Payloads =
            [
                manifest.Payloads[0] with
                {
                    RuntimeIdentifiers = [PluginRuntimeIdentifier.LinuxX64],
                },
                manifest.Payloads[1] with
                {
                    RuntimeIdentifiers = [PluginRuntimeIdentifier.WindowsX64],
                },
                manifest.Payloads[2] with
                {
                    RuntimeIdentifiers = [PluginRuntimeIdentifier.WindowsX64],
                },
            ],
        };

        foreach (
            var runtimeIdentifier in new[]
            {
                PluginRuntimeIdentifier.LinuxX64,
                PluginRuntimeIdentifier.WindowsX64,
            }
        )
        {
            _ = PluginManifestValidator
                .Validate(
                    manifest,
                    PluginContractFixtures.CompatibleHost() with
                    {
                        RuntimeIdentifier = runtimeIdentifier,
                    }
                )
                .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
        }
    }

    [Test]
    public void PackagePolicy_RejectsEscapesLinksDuplicatesAndCaseCollisions()
    {
        var cases = new[]
        {
            (
                PluginContractFixtures.PackageWith(
                    new PluginPackageEntry.File("../escape.lua", ReadOnlyMemory<byte>.Empty)
                ),
                PluginPackageEntryErrorCode.InvalidPath
            ),
            (
                PluginContractFixtures.PackageWith(
                    new PluginPackageEntry.SymbolicLink("lua/alias.lua", "lua/main.lua")
                ),
                PluginPackageEntryErrorCode.LinkNotPermitted
            ),
            (
                PluginContractFixtures.PackageWith(
                    new PluginPackageEntry.File(
                        PluginPackage.ManifestPath,
                        PluginContractFixtures.CompleteManifestToml()
                    )
                ),
                PluginPackageEntryErrorCode.DuplicatePath
            ),
            (
                PluginContractFixtures.PackageWith(
                    new PluginPackageEntry.File("Lua/main.lua", ReadOnlyMemory<byte>.Empty)
                ),
                PluginPackageEntryErrorCode.CaseCollidingPath
            ),
        };

        foreach (var (package, expected) in cases)
        {
            PackageEntryCodes(
                    PluginPackageValidator.Validate(
                        package,
                        PluginContractFixtures.CompatibleHost()
                    )
                )
                .ShouldContain(expected);
        }
    }

    [Test]
    public void PackagePolicy_RejectsUndeclaredMissingAndOversizedContent()
    {
        var undeclared = PluginContractFixtures.PackageWith(
            new PluginPackageEntry.File("notes.txt", ReadOnlyMemory<byte>.Empty)
        );
        var missing = PluginContractFixtures
            .CompletePackage()
            .Where(entry => entry.Path != "payloads/linux-x64/libqueue.so")
            .ToArray();
        var oversized = PluginContractFixtures
            .CompletePackage()
            .Select(entry =>
                entry.Path == "payloads/linux-x64/libqueue.so"
                    ? new PluginPackageEntry.File(
                        "payloads/linux-x64/libqueue.so",
                        new byte[65_537]
                    )
                    : entry
            )
            .ToArray();

        PackageEntryCodes(
                PluginPackageValidator.Validate(undeclared, PluginContractFixtures.CompatibleHost())
            )
            .ShouldContain(PluginPackageEntryErrorCode.UndeclaredContent);
        PackageEntryCodes(
                PluginPackageValidator.Validate(missing, PluginContractFixtures.CompatibleHost())
            )
            .ShouldContain(PluginPackageEntryErrorCode.MissingDeclaredContent);
        PackageEntryCodes(
                PluginPackageValidator.Validate(oversized, PluginContractFixtures.CompatibleHost())
            )
            .ShouldContain(PluginPackageEntryErrorCode.EntryTooLarge);
    }

    [Test]
    public void PackagePolicy_UsesCanonicalManifestSizeBound()
    {
        var package = PluginContractFixtures
            .CompletePackage()
            .Select(entry =>
                entry.Path == PluginPackage.ManifestPath
                    ? new PluginPackageEntry.File(
                        PluginPackage.ManifestPath,
                        new byte[PluginContractLimits.MaximumManifestBytes + 1]
                    )
                    : entry
            )
            .ToArray();

        PackageManifestErrorCodes(
                PluginPackageValidator.Validate(package, PluginContractFixtures.CompatibleHost())
            )
            .ShouldContain(PluginManifestErrorCode.ManifestTooLarge);
    }

    private static IReadOnlyList<PluginManifestError> ManifestErrors(
        PluginManifestValidationOutcome outcome
    ) => outcome.ShouldBeOfType<PluginManifestValidationOutcome.Rejected>().Errors;

    private static IReadOnlyList<PluginPackageEntryErrorCode> PackageEntryCodes(
        PluginPackageValidationOutcome outcome
    ) =>
        outcome
            .ShouldBeOfType<PluginPackageValidationOutcome.Rejected>()
            .Errors.OfType<PluginPackageError.Entry>()
            .Select(error => error.Code)
            .ToArray();

    private static IReadOnlyList<PluginManifestErrorCode> PackageManifestErrorCodes(
        PluginPackageValidationOutcome outcome
    ) =>
        outcome
            .ShouldBeOfType<PluginPackageValidationOutcome.Rejected>()
            .Errors.OfType<PluginPackageError.Manifest>()
            .SelectMany(error => error.Errors)
            .Select(error => error.Code)
            .ToArray();
}
