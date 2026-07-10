namespace BlokeBot.Persistence.Models;

public sealed class CustomMessageVariant
{
    public int Id { get; set; }

    public int CustomMessageLibraryEntryId { get; set; }

    public int SortOrder { get; set; }

    public string Text { get; set; } = string.Empty;

    public CustomMessageLibraryEntry? Entry { get; set; }
}
