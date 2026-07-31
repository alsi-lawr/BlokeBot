namespace BlokeBot.Core.Features.Overlays;

internal interface IOverlayMediaFileDeletion
{
    OverlayMediaFileDeletionOutcome Delete(string path);
}

internal abstract record OverlayMediaFileDeletionOutcome
{
    private OverlayMediaFileDeletionOutcome() { }

    internal sealed record Deleted : OverlayMediaFileDeletionOutcome;

    internal sealed record Unavailable : OverlayMediaFileDeletionOutcome;
}

internal sealed class SystemOverlayMediaFileDeletion : IOverlayMediaFileDeletion
{
    public OverlayMediaFileDeletionOutcome Delete(string path)
    {
        try
        {
            File.Delete(path);
            return new OverlayMediaFileDeletionOutcome.Deleted();
        }
        catch (IOException)
        {
            return new OverlayMediaFileDeletionOutcome.Unavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return new OverlayMediaFileDeletionOutcome.Unavailable();
        }
    }
}
