using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Competitions;

public sealed record CompetitionScheduleSlot(int Round, int Position, int? EntrantA, int? EntrantB);

public static class CompetitionSchedule
{
    public const string AlgorithmVersion = "blokebot-shuffle-v1";

    public static IReadOnlyList<int> Order(
        int entrantCount,
        CompetitionSeeding seeding,
        string seed
    )
    {
        var order = Enumerable.Range(0, entrantCount).ToArray();
        if (seeding == CompetitionSeeding.Seeded)
        {
            return order;
        }

        var random = new StableRandom(seed);
        for (var index = order.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }
        return order;
    }

    public static IReadOnlyList<CompetitionScheduleSlot> GenerateTournament(
        IReadOnlyList<int> order
    )
    {
        var size = 2;
        while (size < order.Count)
        {
            size *= 2;
        }
        var bracket = order
            .Select(value => (int?)value)
            .Concat(Enumerable.Repeat<int?>(null, size - order.Count))
            .ToArray();
        var slots = new List<CompetitionScheduleSlot>();
        for (var position = 0; position < size / 2; position++)
        {
            slots.Add(new(1, position, bracket[position * 2], bracket[(position * 2) + 1]));
        }
        var matches = size / 4;
        for (var round = 2; matches > 0; round++, matches /= 2)
        {
            for (var position = 0; position < matches; position++)
            {
                slots.Add(new(round, position, null, null));
            }
        }
        return slots;
    }

    public static IReadOnlyList<CompetitionScheduleSlot> GenerateLeague(IReadOnlyList<int> order)
    {
        var rotating = order.Select(value => (int?)value).ToList();
        if (rotating.Count % 2 != 0)
        {
            rotating.Add(null);
        }
        var rounds = rotating.Count - 1;
        var half = rotating.Count / 2;
        var slots = new List<CompetitionScheduleSlot>();
        for (var round = 1; round <= rounds; round++)
        {
            var position = 0;
            for (var index = 0; index < half; index++)
            {
                var a = rotating[index];
                var b = rotating[rotating.Count - 1 - index];
                if (a is not null && b is not null)
                {
                    slots.Add(new(round, position++, a, b));
                }
            }
            var last = rotating[^1];
            rotating.RemoveAt(rotating.Count - 1);
            rotating.Insert(1, last);
        }
        return slots;
    }

    private sealed class StableRandom(string seed)
    {
        private ulong _state = InitialState(seed);

        public int Next(int exclusiveMaximum)
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return (int)(_state % (uint)exclusiveMaximum);
        }

        private static ulong InitialState(string seed)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            var state = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return state == 0 ? 0x9E3779B97F4A7C15UL : state;
        }
    }
}
