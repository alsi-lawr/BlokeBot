using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginPackageCorrectionTests
{
    [Test]
    public void DuplicateTomlKeys_AreRejectedAtCanonicalIngress()
    {
        var duplicateTag = PluginContractFixtures.ManifestReplacing(
            "tag = \"community-link-queue\"",
            "tag = \"community-link-queue\"\ntag = \"replacement-tag\""
        );
        var duplicateIdentity = PluginContractFixtures.ManifestReplacing(
            "id = \"community.link-queue\"",
            "id = \"community.link-queue\"\nid = \"community.other\""
        );

        ManifestErrorCodes(duplicateTag).ShouldContain(PluginManifestErrorCode.MalformedToml);
        ManifestErrorCodes(duplicateIdentity).ShouldContain(PluginManifestErrorCode.MalformedToml);
    }

    [Test]
    public void PackagePaths_RejectWindowsNormalizingAliasPairs()
    {
        var aliases = new[] { "web/app.js.", "web/app.js ", "web/NUL.js", "web/COM1/app.js" };

        foreach (var alias in aliases)
        {
            var package = PluginContractFixtures.PackageWith(
                new PluginPackageEntry.File(alias, ReadOnlyMemory<byte>.Empty)
            );

            PackageEntryCodes(package).ShouldContain(PluginPackageEntryErrorCode.InvalidPath);
        }
    }

    [Test]
    public void MigrationTransitions_IgnoreBuildMetadataForDuplicateRoutes()
    {
        var releaseWithMetadata = PluginContractFixtures.ManifestReplacing(
            "declaredVersion = \"1.2.0\"",
            "declaredVersion = \"1.2.0+package.7\""
        );
        var duplicateTransition = PluginContractFixtures.ManifestReplacing(
            "entryPoint = \"migrate_settings\"\n[[automationDefinitions]]",
            """
            entryPoint = "migrate_settings"
            [[migrations]]
            id = "settings-v1-v2-build"
            fromVersion = "1.0.0+route.2"
            toVersion = "1.2.0+route.2"
            module = "migrations"
            entryPoint = "migrate_settings_build"
            [[automationDefinitions]]
            """
        );

        var acceptedRelease = PluginManifestToml
            .Validate(releaseWithMetadata, PluginContractFixtures.CompatibleHost())
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>();
        acceptedRelease.Manifest.Manifest.Release.DeclaredVersion.Value.ShouldBe("1.2.0+package.7");
        acceptedRelease.Manifest.Manifest.Release.DeclaredVersion.BuildMetadata.ShouldBe(
            "package.7"
        );
        ManifestErrorCodes(duplicateTransition)
            .ShouldContain(PluginManifestErrorCode.InvalidMigration);
    }

    private static IReadOnlyList<PluginManifestErrorCode> ManifestErrorCodes(byte[] manifest) =>
        PluginManifestToml
            .Validate(manifest, PluginContractFixtures.CompatibleHost())
            .ShouldBeOfType<PluginManifestValidationOutcome.Rejected>()
            .Errors.Select(error => error.Code)
            .ToArray();

    private static IReadOnlyList<PluginPackageEntryErrorCode> PackageEntryCodes(
        IReadOnlyList<PluginPackageEntry> package
    ) =>
        PluginPackageValidator
            .Validate(package, PluginContractFixtures.CompatibleHost())
            .ShouldBeOfType<PluginPackageValidationOutcome.Rejected>()
            .Errors.OfType<PluginPackageError.Entry>()
            .Select(error => error.Code)
            .ToArray();
}
