namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// The keyed open-state ledger behind a page's <see cref="StudioStage"/> and
/// <see cref="StudioFold"/> bindings. A page holds one instance per family of collapsibles and
/// binds each member's <c>Open</c> to <see cref="IsOpen"/> and <c>OpenChanged</c> to
/// <see cref="Set"/>; neither component's API changes. Keys are whatever the page already names
/// its sections with: a stage enum, an entity id, a reply key, a draft object.
/// </summary>
public sealed class StudioOpenSet<TKey>
    where TKey : notnull
{
    private readonly HashSet<TKey> _open;
    private bool _seeded;

    /// <summary>Starts with the given keys open and every other key closed.</summary>
    public StudioOpenSet(params TKey[] open) => _open = [.. open];

    public bool IsOpen(TKey key) => _open.Contains(key);

    public void Set(TKey key, bool open) => _ = open ? _open.Add(key) : _open.Remove(key);

    /// <summary>Opens one key in code, for reveal flows such as edit-opens-the-editor.</summary>
    public void Open(TKey key) => _ = _open.Add(key);

    public void Toggle(TKey key) => _ = _open.Add(key) || _open.Remove(key);

    /// <summary>Closes everything, then opens the given keys, for select-and-start-over flows.</summary>
    public void Reset(params TKey[] open)
    {
        _open.Clear();
        _open.UnionWith(open);
    }

    /// <summary>
    /// Applies a data-derived initial state exactly once: the first call opens or leaves closed as
    /// told, and every later call does nothing, so reloads never fight what the user has opened or
    /// closed since.
    /// </summary>
    public void SeedOnce(TKey key, bool open)
    {
        if (_seeded)
        {
            return;
        }

        _seeded = true;
        Set(key, open);
    }
}
