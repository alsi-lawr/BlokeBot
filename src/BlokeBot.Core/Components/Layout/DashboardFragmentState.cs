namespace BlokeBot.Core.Components.Layout;

/// <summary>
/// Tracks the canonical fragment selection for the current fragment-owned dashboard page.
/// Same-page fragment pushes never reach <c>NavigationManager.LocationChanged</c> on the server,
/// so fragment-aware consumers such as page help read the selection from this circuit-scoped state.
/// </summary>
public sealed class DashboardFragmentState
{
    public string? Path { get; private set; }

    public string? Fragment { get; private set; }

    public event Action? Changed;

    public void Set(string path, string fragment)
    {
        if (
            string.Equals(Path, path, StringComparison.Ordinal)
            && string.Equals(Fragment, fragment, StringComparison.Ordinal)
        )
        {
            return;
        }

        Path = path;
        Fragment = fragment;
        Changed?.Invoke();
    }
}
