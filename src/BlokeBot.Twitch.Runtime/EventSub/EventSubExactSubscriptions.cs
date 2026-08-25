namespace BlokeBot.Twitch.Runtime;

public sealed record EventSubExactSubscription(string Type, string Version);

public interface IEventSubExactRequirementSource
{
    ValueTask<IReadOnlyList<EventSubExactSubscription>> GetRequirementsAsync(
        string channel,
        CancellationToken cancellationToken
    );
}
