using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Features.Dashboards.Page;

public sealed class DashboardEditorTargets
{
    private readonly IReadOnlyDictionary<
        DashboardWidgetKind,
        IReadOnlyList<DashboardTargetOption>
    > _options;

    internal DashboardEditorTargets(
        Dictionary<DashboardWidgetKind, IReadOnlyList<DashboardTargetOption>> options
    ) => _options = options;

    internal IReadOnlyList<DashboardTargetOption> Options(DashboardWidgetKind kind) =>
        _options.TryGetValue(kind, out var options) ? options : [];

    internal string? Label(DashboardWidgetConfiguration configuration) =>
        configuration.Binding is DashboardBinding.Configured binding
            ? Options(DashboardWidgetKinds.Kind(configuration))
                .FirstOrDefault(option => option.Target == binding.Target)
                ?.Label
            : null;

    internal static async Task<DashboardEditorTargets> ReadAsync(
        DashboardTargetCatalog catalog,
        AuthenticatedSession session,
        DashboardHostId host,
        CancellationToken ct
    )
    {
        var options = new Dictionary<DashboardWidgetKind, IReadOnlyList<DashboardTargetOption>>();
        foreach (
            var kind in Enum.GetValues<DashboardWidgetKind>()
                .Where(kind => kind != DashboardWidgetKind.Countdown)
        )
        {
            var result = await catalog.ReadAsync(session, host, kind, ct);
            options[kind] = result
                is DashboardResult<IReadOnlyList<DashboardTargetOption>>.Succeeded loaded
                ? loaded.Value
                : [];
        }
        return new(options);
    }
}
