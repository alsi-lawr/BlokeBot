namespace BlokeBot.Persistence.Models;

public sealed class CustomMessageLibraryEntry
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public CustomMessageSelectionMode SelectionMode { get; set; } =
        CustomMessageSelectionMode.Sequential;

    public int CurrentVariantIndex { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<CustomMessageVariant> Variants { get; set; } = [];
}
