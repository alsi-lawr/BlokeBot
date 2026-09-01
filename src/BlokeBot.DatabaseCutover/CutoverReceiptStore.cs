using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.DatabaseCutover;

internal sealed class CutoverReceiptStore
{
    private const string _directoryName = "database-cutover";
    private const string _fileName = "sqlite-to-postgresql-v1.json";
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    internal CutoverReceiptStore(string stateDirectory)
    {
        DirectoryPath = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(stateDirectory),
            _directoryName
        );
        Path = System.IO.Path.Combine(DirectoryPath, _fileName);
    }

    internal string DirectoryPath { get; }
    internal string Path { get; }

    internal async Task<CutoverReceipt?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        await using var stream = new FileStream(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        var receipt = await JsonSerializer.DeserializeAsync<CutoverReceipt>(
            stream,
            _json,
            cancellationToken
        );
        return receipt is { FormatVersion: CutoverReceipt.CurrentFormatVersion }
            ? receipt
            : throw new InvalidDataException("The database cutover receipt is unsupported.");
    }

    internal async Task WriteAsync(CutoverReceipt receipt, CancellationToken cancellationToken)
    {
        _ = Directory.CreateDirectory(DirectoryPath);
        ApplyOwnerOnlyDirectoryPermissions(DirectoryPath);
        var temporaryPath = System.IO.Path.Combine(
            DirectoryPath,
            $".{_fileName}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            await using (
                var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough
                )
            )
            {
                ApplyOwnerOnlyFilePermissions(temporaryPath);
                await JsonSerializer.SerializeAsync(stream, receipt, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, Path, overwrite: true);
            ApplyOwnerOnlyFilePermissions(Path);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void ApplyOwnerOnlyDirectoryPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private static void ApplyOwnerOnlyFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
