using System.Security.Cryptography;
using System.Text;

namespace BlokeBot.DatabaseCutover;

internal static class LocalStateFingerprint
{
    internal static async Task<string> CalculateAsync(
        string stateDirectory,
        string sourceDatabasePath,
        CutoverReceiptStore receiptStore,
        CancellationToken cancellationToken
    )
    {
        var root = Path.GetFullPath(stateDirectory);
        var excludedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetFullPath(sourceDatabasePath),
            Path.GetFullPath(sourceDatabasePath + "-shm"),
            Path.GetFullPath(sourceDatabasePath + "-wal"),
            Path.GetFullPath(sourceDatabasePath + "-journal"),
            BlokeBotProcessLease.LockPath(root),
        };
        var receiptDirectory = Path.GetFullPath(receiptStore.DirectoryPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (
            var path in Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(path =>
                    !excludedFiles.Contains(path)
                    && !path.StartsWith(
                        receiptDirectory + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal
                    )
                )
                .Order(StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path)));
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        _ = BitConverter.TryWriteBytes(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
