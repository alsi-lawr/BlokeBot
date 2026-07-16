using System.Security;

namespace BlokeBot.Hosting;

internal enum BlokeBotOperatingSystem
{
    Linux,
    MacOS,
    Windows,
}

internal sealed record BlokeBotPlatformEnvironment(
    string? HomeDirectory,
    string? XdgStateHome,
    string? LocalApplicationData
)
{
    internal static (
        BlokeBotOperatingSystem OperatingSystem,
        BlokeBotPlatformEnvironment Environment
    ) Current()
    {
        var operatingSystem =
            OperatingSystem.IsWindows() ? BlokeBotOperatingSystem.Windows
            : OperatingSystem.IsMacOS() ? BlokeBotOperatingSystem.MacOS
            : BlokeBotOperatingSystem.Linux;
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            homeDirectory = Environment.GetEnvironmentVariable("HOME");
        }

        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            localApplicationData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        }

        return (
            operatingSystem,
            new BlokeBotPlatformEnvironment(
                homeDirectory,
                Environment.GetEnvironmentVariable("XDG_STATE_HOME"),
                localApplicationData
            )
        );
    }
}

internal sealed record BlokeBotStatePathRequest(
    BlokeBotOperatingSystem OperatingSystem,
    BlokeBotPlatformEnvironment Environment,
    string? DataDirectory,
    string? ExplicitDatabasePath,
    string? ExplicitTokenCachePath
);

internal sealed record BlokeBotStatePaths(string DatabasePath, string TokenCachePath);

internal abstract record BlokeBotStatePathResolution
{
    private BlokeBotStatePathResolution() { }

    internal sealed record Resolved(BlokeBotStatePaths Paths) : BlokeBotStatePathResolution;

    internal sealed record Failed(string Message) : BlokeBotStatePathResolution;
}

internal static class BlokeBotStatePathResolver
{
    private const string _databaseFileName = "blokebot.db";
    private const string _tokenCacheFileName = "twitch.tokens.json";

    internal static BlokeBotStatePathResolution Resolve(BlokeBotStatePathRequest request)
    {
        var databasePath = ExplicitPath(request.ExplicitDatabasePath);
        var tokenCachePath = ExplicitPath(request.ExplicitTokenCachePath);
        if (databasePath is not null && tokenCachePath is not null)
        {
            return Resolved(databasePath, tokenCachePath);
        }

        var dataDirectory = ExplicitPath(request.DataDirectory);
        if (dataDirectory is not null)
        {
            return Resolved(
                databasePath ?? Combine(request.OperatingSystem, dataDirectory, _databaseFileName),
                tokenCachePath
                    ?? Combine(request.OperatingSystem, dataDirectory, _tokenCacheFileName)
            );
        }

        var defaultDirectory = DefaultStateDirectory(request);
        return defaultDirectory switch
        {
            BlokeBotDefaultStateDirectory.Resolved resolved => Resolved(
                databasePath ?? Combine(request.OperatingSystem, resolved.Path, _databaseFileName),
                tokenCachePath
                    ?? Combine(request.OperatingSystem, resolved.Path, _tokenCacheFileName)
            ),
            BlokeBotDefaultStateDirectory.Failed failed => new BlokeBotStatePathResolution.Failed(
                failed.Message
            ),
            _ => throw new InvalidOperationException("Unknown default state-directory result."),
        };
    }

    private static BlokeBotDefaultStateDirectory DefaultStateDirectory(
        BlokeBotStatePathRequest request
    )
    {
        return request.OperatingSystem switch
        {
            BlokeBotOperatingSystem.Linux => LinuxStateDirectory(request.Environment),
            BlokeBotOperatingSystem.MacOS => HomeStateDirectory(
                request.OperatingSystem,
                request.Environment.HomeDirectory,
                "Library",
                "Application Support",
                "BlokeBot"
            ),
            BlokeBotOperatingSystem.Windows => string.IsNullOrWhiteSpace(
                request.Environment.LocalApplicationData
            )
                ? new BlokeBotDefaultStateDirectory.Failed(
                    "LOCALAPPDATA is unavailable. Use 'blokebot serve --data-dir PATH' or set the database and token-cache paths explicitly."
                )
                : new BlokeBotDefaultStateDirectory.Resolved(
                    Combine(
                        request.OperatingSystem,
                        request.Environment.LocalApplicationData,
                        "BlokeBot"
                    )
                ),
            _ => throw new InvalidOperationException("Unknown operating system."),
        };
    }

    private static BlokeBotDefaultStateDirectory LinuxStateDirectory(
        BlokeBotPlatformEnvironment environment
    )
    {
        if (!string.IsNullOrWhiteSpace(environment.XdgStateHome))
        {
            return new BlokeBotDefaultStateDirectory.Resolved(
                Combine(BlokeBotOperatingSystem.Linux, environment.XdgStateHome, "blokebot")
            );
        }

        return HomeStateDirectory(
            BlokeBotOperatingSystem.Linux,
            environment.HomeDirectory,
            ".local",
            "state",
            "blokebot"
        );
    }

    private static BlokeBotDefaultStateDirectory HomeStateDirectory(
        BlokeBotOperatingSystem operatingSystem,
        string? homeDirectory,
        params string[] segments
    )
    {
        return string.IsNullOrWhiteSpace(homeDirectory)
            ? new BlokeBotDefaultStateDirectory.Failed(
                "The user home directory is unavailable. Use 'blokebot serve --data-dir PATH' or set the database and token-cache paths explicitly."
            )
            : new BlokeBotDefaultStateDirectory.Resolved(
                Combine(operatingSystem, homeDirectory, segments)
            );
    }

    private static BlokeBotStatePathResolution.Resolved Resolved(
        string databasePath,
        string tokenCachePath
    )
    {
        return new(new BlokeBotStatePaths(databasePath, tokenCachePath));
    }

    private static string? ExplicitPath(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Combine(
        BlokeBotOperatingSystem operatingSystem,
        string root,
        params string[] segments
    )
    {
        var separator = operatingSystem == BlokeBotOperatingSystem.Windows ? '\\' : '/';
        var combined = root.Trim();
        foreach (var segment in segments)
        {
            var trimmedSegment = segment.Trim('/', '\\');
            var trimmedCombined = combined.TrimEnd('/', '\\');
            if (trimmedCombined.Length == 0 && combined.StartsWith(separator))
            {
                trimmedCombined = separator.ToString();
            }

            combined = trimmedCombined.EndsWith(separator)
                ? trimmedCombined + trimmedSegment
                : trimmedCombined + separator + trimmedSegment;
        }

        return combined;
    }

    private abstract record BlokeBotDefaultStateDirectory
    {
        private BlokeBotDefaultStateDirectory() { }

        internal sealed record Resolved(string Path) : BlokeBotDefaultStateDirectory;

        internal sealed record Failed(string Message) : BlokeBotDefaultStateDirectory;
    }
}

internal abstract record BlokeBotStatePathPreparation
{
    private BlokeBotStatePathPreparation() { }

    internal sealed record Prepared(BlokeBotStatePaths Paths) : BlokeBotStatePathPreparation;

    internal sealed record Failed(string Message) : BlokeBotStatePathPreparation;
}

internal static class BlokeBotStatePathPreparer
{
    internal static BlokeBotStatePathPreparation Prepare(BlokeBotStatePaths paths)
    {
        try
        {
            var prepared = new BlokeBotStatePaths(
                Path.GetFullPath(paths.DatabasePath),
                Path.GetFullPath(paths.TokenCachePath)
            );
            var directories = new HashSet<string>(StringComparer.Ordinal)
            {
                ParentDirectory(prepared.DatabasePath),
                ParentDirectory(prepared.TokenCachePath),
            };
            foreach (var directory in directories)
            {
                EnsureDirectoryWritable(directory);
            }

            EnsureExistingFileWritable(prepared.DatabasePath);
            EnsureExistingFileWritable(prepared.TokenCachePath);
            return new BlokeBotStatePathPreparation.Prepared(prepared);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            return new BlokeBotStatePathPreparation.Failed(
                $"blokebot could not prepare its state files: {exception.Message}{Environment.NewLine}Choose a writable directory with 'blokebot serve --data-dir PATH' or set BlokeBot__DatabasePath and TwitchBot__Identity__TokenCachePath explicitly."
            );
        }
    }

    private static string ParentDirectory(string path)
    {
        return Path.GetDirectoryName(path)
            ?? throw new IOException($"The state path '{path}' has no parent directory.");
    }

    private static void EnsureDirectoryWritable(string directory)
    {
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, $".blokebot-write-test-{Guid.NewGuid():N}");
        try
        {
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
        }
        finally
        {
            File.Delete(probePath);
        }
    }

    private static void EnsureExistingFileWritable(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
    }

    private static bool IsPathFailure(Exception exception)
    {
        return exception
            is IOException
                or UnauthorizedAccessException
                or SecurityException
                or ArgumentException
                or NotSupportedException;
    }
}
