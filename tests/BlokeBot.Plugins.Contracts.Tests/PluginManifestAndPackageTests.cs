using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginManifestAndPackageTests
{
    [Test]
    public void CompleteManifestAndPackage_AreAccepted()
    {
        var target = PluginContractFixtures.CompatibleHost();

        var manifestOutcome = PluginManifestJson.Validate(
            PluginContractFixtures.CompleteManifestJson(),
            target
        );
        var acceptedManifest =
            manifestOutcome.ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
        var serialized = PluginManifestJson.Serialize(acceptedManifest.Manifest);
        var packageOutcome = PluginPackageValidator.Validate(
            PluginContractFixtures.CompletePackage(),
            target
        );

        _ = PluginManifestJson
            .Validate(serialized, target)
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
        var acceptedPackage =
            packageOutcome.ShouldBeOfType<PluginPackageValidationOutcome.Accepted>();
        acceptedPackage.Manifest.Manifest.Id.Value.ShouldBe("community.link-queue");
        acceptedPackage.Manifest.Manifest.Release.Tag.Value.ShouldBe("community-link-queue");
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
            "\"tag\": \"community-link-queue\"",
            "\"tag\": \"community-link-queue\", \"commitSha\": \"0123456789abcdef\""
        );
        var nullDescription = PluginContractFixtures.ManifestReplacing(
            "\"description\": \"Collects community links and publishes them on a schedule.\"",
            "\"description\": null"
        );

        var shaTagErrors = ManifestErrors(PluginManifestJson.Validate(shaTag, target));
        var shaFieldErrors = ManifestErrors(PluginManifestJson.Validate(shaField, target));
        var nullDescriptionErrors = ManifestErrors(
            PluginManifestJson.Validate(nullDescription, target)
        );

        shaTagErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedJson);
        shaFieldErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedJson);
        nullDescriptionErrors
            .Select(error => error.Code)
            .ShouldContain(PluginManifestErrorCode.MalformedJson);
    }

    [Test]
    public void ManifestValidation_RejectsDuplicateAndIncompatibleDeclarations()
    {
        var target = PluginContractFixtures.CompatibleHost();
        var duplicate = PluginContractFixtures.ManifestReplacing(
            "\"id\": \"publishing\"",
            "\"id\": \"collection\""
        );
        var incompatible = PluginContractFixtures.ManifestReplacing(
            "\"minimumApiVersion\": 1,\n    \"maximumApiVersion\": 1",
            "\"minimumApiVersion\": 2,\n    \"maximumApiVersion\": 2"
        );
        var escapingModule = PluginContractFixtures.ManifestReplacing(
            "\"path\": \"lua/main.lua\"",
            "\"path\": \"../main.lua\""
        );
        var duplicateAsset = PluginContractFixtures.ManifestReplacing(
            "\"id\": \"queue-script\"",
            "\"id\": \"queue-document\""
        );
        var malformedId = PluginContractFixtures.ManifestReplacing(
            "community.link-queue",
            "Community.LinkQueue"
        );

        var duplicateErrors = ManifestErrors(PluginManifestJson.Validate(duplicate, target));
        var incompatibleErrors = ManifestErrors(PluginManifestJson.Validate(incompatible, target));
        var escapingErrors = ManifestErrors(PluginManifestJson.Validate(escapingModule, target));
        var duplicateAssetErrors = ManifestErrors(
            PluginManifestJson.Validate(duplicateAsset, target)
        );
        var malformedIdErrors = ManifestErrors(PluginManifestJson.Validate(malformedId, target));

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
            .ShouldContain(PluginManifestErrorCode.MalformedJson);
    }

    [Test]
    public void PackagePolicy_RejectsNativeDotNetAndLuaRocksPayloads()
    {
        var cases = new[]
        {
            (
                new PluginPackageEntry.File("native/module.so", ReadOnlyMemory<byte>.Empty),
                PluginPackageEntryErrorCode.NativePayloadNotPermitted
            ),
            (
                new PluginPackageEntry.File("managed/Plugin.dll", ReadOnlyMemory<byte>.Empty),
                PluginPackageEntryErrorCode.DotNetPayloadNotPermitted
            ),
            (
                new PluginPackageEntry.File("rocks/plugin.rockspec", ReadOnlyMemory<byte>.Empty),
                PluginPackageEntryErrorCode.LuaRocksPayloadNotPermitted
            ),
        };

        foreach (var (entry, expected) in cases)
        {
            PackageEntryCodes(
                    PluginPackageValidator.Validate(
                        PluginContractFixtures.PackageWith(entry),
                        PluginContractFixtures.CompatibleHost()
                    )
                )
                .ShouldContain(expected);
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
                        PluginContractFixtures.CompleteManifestJson()
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
            .Where(entry => entry.Path != "web/app.js")
            .ToArray();
        var oversized = PluginContractFixtures
            .CompletePackage()
            .Select(entry =>
                entry.Path == "web/app.js"
                    ? new PluginPackageEntry.File("web/app.js", new byte[65_537])
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
}
