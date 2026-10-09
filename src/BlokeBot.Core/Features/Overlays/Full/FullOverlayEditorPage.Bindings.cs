using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    [Inject]
    private PlayQueueService _queues { get; set; } = default!;

    [Inject]
    private CommunityProgressionService _progression { get; set; } = default!;

    [Inject]
    private BountyService _bounties { get; set; } = default!;
    private IReadOnlyList<FullOverlayBindingChoice> _queueBindings = [];
    private IReadOnlyList<FullOverlayBindingChoice> _goalBindings = [];
    private IReadOnlyList<FullOverlayBindingChoice> _bountyBindings = [];

    private async Task LoadBindingsAsync()
    {
        _queueBindings = (await _queues.GetQueuesForHostAsync(HostId, _lifetime.Token))
            .Select(queue => new FullOverlayBindingChoice(
                queue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                queue.Name
            ))
            .ToArray();
        _goalBindings = (await _progression.GetModeratorSeasonsAsync(HostId, _lifetime.Token))
            .Where(season =>
                season.Visibility == CommunityVisibility.Public
                && season.Status != CommunitySeasonStatus.Draft
            )
            .SelectMany(season =>
                season
                    .Definitions.Where(definition =>
                        definition.Scope == CommunityProgressScope.Communal
                    )
                    .Select(definition => new FullOverlayBindingChoice(
                        definition.Id.Value.ToString(),
                        $"{season.Name} · {definition.Name}"
                    ))
            )
            .ToArray();
        _bountyBindings = (await _bounties.GetModeratorBoardAsync(HostId, _lifetime.Token))
            .Select(value => value.Bounty)
            .Where(value =>
                value.Visibility == BountyVisibility.Public
                && value.Status is not (BountyStatus.Proposed or BountyStatus.Cancelled)
            )
            .Select(value => new FullOverlayBindingChoice(value.PublicId.ToString(), value.Title))
            .ToArray();
    }

    private IReadOnlyList<FullOverlayBindingChoice> BindingsFor(FullOverlayWidget widget) =>
        widget.Kind.Value switch
        {
            "viewer-queue" => _queueBindings,
            "community-goal" => _goalBindings,
            "viewer-funded-bounty" => _bountyBindings,
            _ => [],
        };
}
