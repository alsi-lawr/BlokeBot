namespace CommandBot.Store;

public sealed class CounterRow
{
    public required string Key { get; set; }
    public int Value { get; set; }
}
