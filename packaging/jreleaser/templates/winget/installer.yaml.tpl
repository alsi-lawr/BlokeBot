# {{jreleaserCreationStamp}}
# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.9.0.schema.json

PackageIdentifier: {{wingetPackageIdentifier}}
PackageVersion: {{wingetPackageVersion}}
ReleaseDate: {{wingetReleaseDate}}
Installers:
  - Architecture: x64
    InstallerUrl: {{distributionUrl}}
    InstallerSha256: {{distributionChecksumSha256}}
    InstallerType: zip
    NestedInstallerType: portable
    ArchiveBinariesDependOnPath: true
    NestedInstallerFiles:
      - RelativeFilePath: {{distributionArtifactRootEntryName}}\bin\{{distributionExecutableWindows}}
        PortableCommandAlias: {{distributionExecutableName}}
ManifestType: installer
ManifestVersion: 1.9.0
