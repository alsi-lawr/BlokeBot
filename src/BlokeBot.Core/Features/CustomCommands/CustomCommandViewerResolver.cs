using BlokeBot.Core.Features.HostedChannels.Status;

namespace BlokeBot.Core.Features.CustomCommands;

public interface ICustomCommandViewerResolver
{
    Task<CustomCommandViewerResolution> ResolveAsync(string login, CancellationToken ct);
}

public sealed class CustomCommandViewerResolver(
    IHostBotAppAccessTokenSource appTokens,
    HelixClient helix,
    BotSettings settings
) : ICustomCommandViewerResolver
{
    public async Task<CustomCommandViewerResolution> ResolveAsync(
        string login,
        CancellationToken ct
    )
    {
        var normalized = Login.Normalize(login);
        if (normalized.Length == 0)
        {
            return new CustomCommandViewerResolution.NotFound();
        }

        var token = await appTokens.GetAccessTokenAsync(ct);
        var users = await helix.GetUsersByLoginAsync(
            new HelixRequestContext(settings.Identity.ClientId, token),
            [normalized],
            ct
        );
        return
            users.FirstOrDefault(user =>
                string.Equals(Login.Normalize(user.Login), normalized, StringComparison.Ordinal)
            )
                is { } user
            ? new CustomCommandViewerResolution.Found(
                new CustomCommandViewer(user.Id, Login.Normalize(user.Login))
            )
            : new CustomCommandViewerResolution.NotFound();
    }
}

public sealed record CustomCommandViewer(string TwitchUserId, string Login);

public abstract record CustomCommandViewerResolution
{
    private CustomCommandViewerResolution() { }

    public sealed record Found(CustomCommandViewer Viewer) : CustomCommandViewerResolution;

    public sealed record NotFound : CustomCommandViewerResolution;
}

internal sealed class UnavailableCustomCommandViewerResolver : ICustomCommandViewerResolver
{
    public Task<CustomCommandViewerResolution> ResolveAsync(string login, CancellationToken ct) =>
        Task.FromResult<CustomCommandViewerResolution>(
            new CustomCommandViewerResolution.NotFound()
        );
}
