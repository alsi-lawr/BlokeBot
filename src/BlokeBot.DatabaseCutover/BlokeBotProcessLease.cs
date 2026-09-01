namespace BlokeBot.DatabaseCutover;

public sealed class BlokeBotProcessLease : IDisposable
{
    private const string _fileName = ".blokebot-running.lock";
    private readonly FileStream _stream;

    private BlokeBotProcessLease(FileStream stream) => _stream = stream;

    public static BlokeBotProcessLease Acquire(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _ = Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(Path.GetFullPath(stateDirectory), _fileName);
        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None
            );
            ApplyOwnerOnlyPermissions(path);
            return new(stream);
        }
        catch (IOException)
        {
            throw new BlokeBotProcessOwnershipException(
                "BlokeBot is running for this state directory. Stop it before the database operation."
            );
        }
    }

    public void Dispose() => _stream.Dispose();

    internal static string LockPath(string stateDirectory) =>
        Path.Combine(Path.GetFullPath(stateDirectory), _fileName);

    private static void ApplyOwnerOnlyPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

public sealed class BlokeBotProcessOwnershipException(string message) : Exception(message);
