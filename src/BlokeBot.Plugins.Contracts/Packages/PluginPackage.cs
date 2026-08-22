namespace BlokeBot.Plugins.Contracts;

public static class PluginPackage
{
    public const string ManifestPath = "blokebot.plugin.json";
}

public abstract record PluginPackageEntry
{
    private PluginPackageEntry(string path) => Path = path;

    public string Path { get; }

    public sealed record File(string FilePath, ReadOnlyMemory<byte> Content)
        : PluginPackageEntry(FilePath);

    public sealed record Directory(string DirectoryPath) : PluginPackageEntry(DirectoryPath);

    public sealed record SymbolicLink(string LinkPath, string Target)
        : PluginPackageEntry(LinkPath);

    public sealed record HardLink(string LinkPath, string Target) : PluginPackageEntry(LinkPath);
}

public enum PluginPackageEntryErrorCode
{
    TooManyEntries,
    PackageTooLarge,
    MissingManifest,
    InvalidPath,
    DuplicatePath,
    CaseCollidingPath,
    LinkNotPermitted,
    NativePayloadNotPermitted,
    DotNetPayloadNotPermitted,
    LuaRocksPayloadNotPermitted,
    UndeclaredContent,
    MissingDeclaredContent,
    EntryTooLarge,
    DirectoryNotRequired,
}

public abstract record PluginPackageError
{
    private PluginPackageError() { }

    public sealed record Entry(PluginPackageEntryErrorCode Code, string Path) : PluginPackageError;

    public sealed record Manifest(IReadOnlyList<PluginManifestError> Errors) : PluginPackageError;
}

public abstract record PluginPackageValidationOutcome
{
    private PluginPackageValidationOutcome() { }

    public sealed record Accepted(ValidatedPluginManifest Manifest)
        : PluginPackageValidationOutcome;

    public sealed record Rejected(IReadOnlyList<PluginPackageError> Errors)
        : PluginPackageValidationOutcome;
}
