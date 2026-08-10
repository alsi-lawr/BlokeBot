using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlokeBot.Persistence;

internal static class BingoIssuedLayout
{
    internal static IReadOnlyList<string> Generate(
        string seed,
        int templateRevision,
        int dimension,
        string assignmentKey,
        IEnumerable<string> squareKeys
    ) =>
        squareKeys
            .Select(key => new RankedSquare(
                key,
                Rank(seed, templateRevision, dimension, assignmentKey, key)
            ))
            .OrderBy(value => value.Rank, ByteArrayComparer.Instance)
            .ThenBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => value.Key)
            .ToArray();

    internal static string Serialize(IReadOnlyList<string> squareKeys) =>
        JsonSerializer.Serialize(squareKeys);

    internal static IReadOnlyList<string> Restore(
        string serializedLayout,
        int dimension,
        IReadOnlyCollection<string> squareKeys
    )
    {
        var restored = JsonSerializer.Deserialize<string[]>(serializedLayout);
        return
            restored is not null
            && restored.Length == checked(dimension * dimension)
            && restored.Distinct(StringComparer.Ordinal).Count() == restored.Length
            && restored.ToHashSet(StringComparer.Ordinal).SetEquals(squareKeys)
            ? restored
            : throw new InvalidOperationException(
                "The persisted issued Bingo layout does not match its template revision."
            );
    }

    private static byte[] Rank(
        string seed,
        int revision,
        int dimension,
        string assignmentKey,
        string squareKey
    ) =>
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"bingo-v1\0{seed}\0{revision}\0{dimension}\0{assignmentKey}\0{squareKey}"
            )
        );

    private sealed record RankedSquare(string Key, byte[] Rank);

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? left, byte[]? right) =>
            (left, right) switch
            {
                _ when ReferenceEquals(left, right) => 0,
                (null, _) => -1,
                (_, null) => 1,
                _ => left.AsSpan().SequenceCompareTo(right),
            };
    }
}
