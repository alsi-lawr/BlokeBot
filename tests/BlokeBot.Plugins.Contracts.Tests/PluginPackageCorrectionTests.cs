using System.Buffers.Binary;
using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginPackageCorrectionTests
{
    [Test]
    public void DuplicateJsonProperties_AreRejectedAtCanonicalIngress()
    {
        var duplicateTag = PluginContractFixtures.ManifestReplacing(
            "\"tag\": \"community-link-queue\"",
            "\"tag\": \"community-link-queue\", \"tag\": \"replacement-tag\""
        );
        var duplicateIdentity = PluginContractFixtures.ManifestReplacing(
            "\"id\": \"community.link-queue\"",
            "\"id\": \"community.link-queue\", \"id\": \"community.other\""
        );

        ManifestErrorCodes(duplicateTag).ShouldContain(PluginManifestErrorCode.MalformedJson);
        ManifestErrorCodes(duplicateIdentity).ShouldContain(PluginManifestErrorCode.MalformedJson);
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
    public void PackagePayloadSignatures_RejectDisguisedExecutables()
    {
        var cases = new[]
        {
            (
                Path: "media/icon.webp",
                Content: PortableExecutable(managed: false, pe32Plus: false),
                Error: PluginPackageEntryErrorCode.NativePayloadNotPermitted
            ),
            (
                Path: "media/icon.webp",
                Content: PortableExecutable(managed: true, pe32Plus: false),
                Error: PluginPackageEntryErrorCode.DotNetPayloadNotPermitted
            ),
            (
                Path: "media/icon.webp",
                Content: PortableExecutable(managed: true, pe32Plus: true),
                Error: PluginPackageEntryErrorCode.DotNetPayloadNotPermitted
            ),
            (
                Path: "media/icon.webp",
                Content: [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01],
                Error: PluginPackageEntryErrorCode.NativePayloadNotPermitted
            ),
            (
                Path: "media/icon.webp",
                Content: [0xCF, 0xFA, 0xED, 0xFE, 0x07, 0x00],
                Error: PluginPackageEntryErrorCode.NativePayloadNotPermitted
            ),
            (
                Path: "web/app.js",
                Content: [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00],
                Error: PluginPackageEntryErrorCode.BrowserExecutablePayloadNotPermitted
            ),
        };

        foreach (var @case in cases)
        {
            var package = PackageReplacingContent(@case.Path, @case.Content);

            PackageEntryCodes(package).ShouldContain(@case.Error);
        }
    }

    [Test]
    public void PackagePayloadSignatures_DoNotRejectMereMagicPrefixes()
    {
        var cases = new[]
        {
            "MZ; valid browser text"u8.ToArray(),
            [0x4D, 0x5A],
            TruncatedPortableExecutable(),
            [0x2F, 0x2F, 0x20, 0x00, 0x61, 0x73, 0x6D],
        };

        foreach (var content in cases)
        {
            var package = PackageReplacingContent("web/app.js", content);

            _ = PluginPackageValidator
                .Validate(package, PluginContractFixtures.CompatibleHost())
                .ShouldBeOfType<PluginPackageValidationOutcome.Accepted>();
        }
    }

    [Test]
    public void MigrationTransitions_IgnoreBuildMetadataForDuplicateRoutes()
    {
        var releaseWithMetadata = PluginContractFixtures.ManifestReplacing(
            "\"declaredVersion\": \"1.2.0\"",
            "\"declaredVersion\": \"1.2.0+package.7\""
        );
        var duplicateTransition = PluginContractFixtures.ManifestReplacing(
            "\"entryPoint\": \"migrate_settings\"\n    }\n  ],\n  \"automationDefinitions\"",
            """
            "entryPoint": "migrate_settings"
                },
                {
                  "id": "settings-v1-v2-build",
                  "fromVersion": "1.0.0+route.2",
                  "toVersion": "1.2.0+route.2",
                  "module": "migrations",
                  "entryPoint": "migrate_settings_build"
                }
              ],
              "automationDefinitions"
            """
        );

        var acceptedRelease = PluginManifestJson
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
        PluginManifestJson
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

    private static IReadOnlyList<PluginPackageEntry> PackageReplacingContent(
        string path,
        byte[] content
    ) =>
        PluginContractFixtures
            .CompletePackage()
            .Select(entry =>
                entry is PluginPackageEntry.File file && file.Path == path
                    ? new PluginPackageEntry.File(path, content)
                    : entry
            )
            .ToArray();

    private static byte[] PortableExecutable(bool managed, bool pe32Plus)
    {
        const int PeOffset = 0x80;
        const int OptionalHeaderOffset = PeOffset + 24;
        var optionalHeaderSize = pe32Plus ? 240 : 224;
        var numberOfDirectoriesOffset = pe32Plus ? 108 : 92;
        var clrDirectoryOffset = pe32Plus ? 224 : 208;
        var content = new byte[OptionalHeaderOffset + optionalHeaderSize];
        content[0] = 0x4D;
        content[1] = 0x5A;
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(0x3C), PeOffset);
        content[PeOffset] = 0x50;
        content[PeOffset + 1] = 0x45;
        BinaryPrimitives.WriteUInt16LittleEndian(
            content.AsSpan(PeOffset + 20),
            (ushort)optionalHeaderSize
        );
        BinaryPrimitives.WriteUInt16LittleEndian(
            content.AsSpan(OptionalHeaderOffset),
            pe32Plus ? (ushort)0x020B : (ushort)0x010B
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            content.AsSpan(OptionalHeaderOffset + numberOfDirectoriesOffset),
            16
        );
        if (managed)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                content.AsSpan(OptionalHeaderOffset + clrDirectoryOffset),
                0x2000
            );
            BinaryPrimitives.WriteUInt32LittleEndian(
                content.AsSpan(OptionalHeaderOffset + clrDirectoryOffset + sizeof(uint)),
                0x48
            );
        }

        return content;
    }

    private static byte[] TruncatedPortableExecutable()
    {
        var content = new byte[0x80];
        content[0] = 0x4D;
        content[1] = 0x5A;
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(0x3C), 0x70);
        content[0x70] = 0x50;
        content[0x71] = 0x45;
        return content;
    }
}
