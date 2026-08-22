namespace BlokeBot.Plugins.Contracts;

public static class PluginPackageValidator
{
    private static readonly HashSet<string> _nativeExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".so",
        ".dylib",
        ".a",
        ".lib",
        ".o",
        ".obj",
        ".wasm",
    };

    private static readonly HashSet<string> _dotNetExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".dll",
        ".exe",
        ".pdb",
    };

    public static PluginPackageValidationOutcome Validate(
        IReadOnlyList<PluginPackageEntry> entries,
        PluginHostCompatibilityTarget target
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(target);
        var errors = ValidateEntryStructure(entries);
        var manifestFile = entries
            .OfType<PluginPackageEntry.File>()
            .FirstOrDefault(entry => entry.Path == PluginPackage.ManifestPath);
        if (manifestFile is null)
        {
            errors.Add(
                new PluginPackageError.Entry(
                    PluginPackageEntryErrorCode.MissingManifest,
                    PluginPackage.ManifestPath
                )
            );
            return new PluginPackageValidationOutcome.Rejected(errors.AsReadOnly());
        }

        if (errors.Count > 0)
        {
            return new PluginPackageValidationOutcome.Rejected(errors.AsReadOnly());
        }

        var manifestOutcome = PluginManifestJson.Validate(manifestFile.Content, target);
        if (manifestOutcome is PluginManifestValidationOutcome.Rejected rejected)
        {
            return new PluginPackageValidationOutcome.Rejected(
                Array.AsReadOnly<PluginPackageError>([
                    new PluginPackageError.Manifest(rejected.Errors),
                ])
            );
        }

        var accepted = (PluginManifestValidationOutcome.Accepted)manifestOutcome;
        ValidateDeclaredContent(entries, accepted.Manifest.Manifest, errors);
        return errors.Count == 0
            ? new PluginPackageValidationOutcome.Accepted(accepted.Manifest)
            : new PluginPackageValidationOutcome.Rejected(errors.AsReadOnly());
    }

    private static List<PluginPackageError> ValidateEntryStructure(
        IReadOnlyList<PluginPackageEntry> entries
    )
    {
        var errors = new List<PluginPackageError>();
        if (entries.Count > PluginContractLimits.MaximumPackageEntries)
        {
            errors.Add(
                new PluginPackageError.Entry(PluginPackageEntryErrorCode.TooManyEntries, "$package")
            );
        }

        var exactPaths = new HashSet<string>(StringComparer.Ordinal);
        var foldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            ValidateEntry(entry, exactPaths, foldedPaths, errors);
            if (entry is PluginPackageEntry.File file)
            {
                totalBytes += file.Content.Length;
                ValidateDisallowedPayload(file, errors);
            }
        }

        if (totalBytes > PluginContractLimits.MaximumPackageBytes)
        {
            errors.Add(
                new PluginPackageError.Entry(
                    PluginPackageEntryErrorCode.PackageTooLarge,
                    "$package"
                )
            );
        }

        return errors;
    }

    private static void ValidateEntry(
        PluginPackageEntry entry,
        HashSet<string> exactPaths,
        HashSet<string> foldedPaths,
        List<PluginPackageError> errors
    )
    {
        if (!PluginPackagePath.TryCanonicalize(entry.Path, out var canonicalPath))
        {
            errors.Add(
                new PluginPackageError.Entry(PluginPackageEntryErrorCode.InvalidPath, entry.Path)
            );
        }
        else if (!exactPaths.Add(canonicalPath))
        {
            errors.Add(
                new PluginPackageError.Entry(PluginPackageEntryErrorCode.DuplicatePath, entry.Path)
            );
        }
        else if (!foldedPaths.Add(canonicalPath))
        {
            errors.Add(
                new PluginPackageError.Entry(
                    PluginPackageEntryErrorCode.CaseCollidingPath,
                    entry.Path
                )
            );
        }

        if (entry is PluginPackageEntry.SymbolicLink or PluginPackageEntry.HardLink)
        {
            errors.Add(
                new PluginPackageError.Entry(
                    PluginPackageEntryErrorCode.LinkNotPermitted,
                    entry.Path
                )
            );
        }
    }

    private static void ValidateDisallowedPayload(
        PluginPackageEntry.File file,
        List<PluginPackageError> errors
    )
    {
        var extension = Path.GetExtension(file.Path);
        var code =
            _nativeExtensions.Contains(extension)
                ? PluginPackageEntryErrorCode.NativePayloadNotPermitted
            : _dotNetExtensions.Contains(extension)
                ? PluginPackageEntryErrorCode.DotNetPayloadNotPermitted
            : extension.Equals(".rock", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rockspec", StringComparison.OrdinalIgnoreCase)
                ? PluginPackageEntryErrorCode.LuaRocksPayloadNotPermitted
            : (PluginPackageEntryErrorCode?)null;
        if (code is { } disallowed)
        {
            errors.Add(new PluginPackageError.Entry(disallowed, file.Path));
            return;
        }

        if (PluginPackagePayloadPolicy.Classify(file.Content.Span) is { } signature)
        {
            errors.Add(new PluginPackageError.Entry(signature, file.Path));
        }
    }

    private static void ValidateDeclaredContent(
        IReadOnlyList<PluginPackageEntry> entries,
        PluginManifest manifest,
        List<PluginPackageError> errors
    )
    {
        var declarations = manifest
            .LuaModules.Select(module => new DeclaredFile(
                module.Path,
                PluginContractLimits.MaximumLuaModuleBytes
            ))
            .Concat(
                manifest.Assets.Select(asset => new DeclaredFile(asset.Path, asset.MaximumBytes))
            )
            .Prepend(new(PluginPackage.ManifestPath, PluginContractLimits.MaximumManifestBytes))
            .ToDictionary(file => file.Path, StringComparer.Ordinal);
        var actualFiles = entries.OfType<PluginPackageEntry.File>().ToDictionary(file => file.Path);

        foreach (var file in actualFiles.Values)
        {
            if (!declarations.TryGetValue(file.Path, out var declared))
            {
                errors.Add(
                    new PluginPackageError.Entry(
                        PluginPackageEntryErrorCode.UndeclaredContent,
                        file.Path
                    )
                );
            }
            else if (file.Content.Length > declared.MaximumBytes)
            {
                errors.Add(
                    new PluginPackageError.Entry(
                        PluginPackageEntryErrorCode.EntryTooLarge,
                        file.Path
                    )
                );
            }
        }

        foreach (var declaration in declarations.Values)
        {
            if (!actualFiles.ContainsKey(declaration.Path))
            {
                errors.Add(
                    new PluginPackageError.Entry(
                        PluginPackageEntryErrorCode.MissingDeclaredContent,
                        declaration.Path
                    )
                );
            }
        }

        foreach (var directory in entries.OfType<PluginPackageEntry.Directory>())
        {
            if (
                !declarations.Keys.Any(path =>
                    path.StartsWith($"{directory.Path}/", StringComparison.Ordinal)
                )
            )
            {
                errors.Add(
                    new PluginPackageError.Entry(
                        PluginPackageEntryErrorCode.DirectoryNotRequired,
                        directory.Path
                    )
                );
            }
        }
    }

    private sealed record DeclaredFile(string Path, long MaximumBytes);
}
