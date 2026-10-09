namespace BlokeBot.Persistence.Models;

public sealed class FullOverlay
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DraftDocumentJson { get; set; } = string.Empty;
    public byte[] AccessKeyDigest { get; set; } = [];
    public string? ProtectedAccessKey { get; set; }
    public bool IsArchived { get; set; }
    public long Revision { get; set; }
    public long PublicationSequence { get; set; }
    public long? PublishedVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class FullOverlayPublication
{
    public long OverlayId { get; set; }
    public long Version { get; set; }
    public string DocumentJson { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public string AuthorLogin { get; set; } = string.Empty;
    public DateTime PublishedAtUtc { get; set; }
}
