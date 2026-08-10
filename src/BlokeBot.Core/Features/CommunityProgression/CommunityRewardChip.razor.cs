using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

/// <summary>
/// Renders a BLOKEBOT-D-206 reward exactly as viewers see it: badge tokens as inline icons,
/// cosmetic accents as their swatch, titles as bounded quoted text.
/// </summary>
public partial class CommunityRewardChip
{
    private const string _placeholderName = "Unnamed reward";

    [Parameter, EditorRequired]
    public required CommunityRewardKind Kind { get; set; }

    [Parameter, EditorRequired]
    public required string Name { get; set; }

    [Parameter, EditorRequired]
    public required string PresentationToken { get; set; }

    [Parameter]
    public bool ShowKind { get; set; }

    private string _token => PresentationToken?.Trim() ?? string.Empty;

    private bool _isAccent =>
        Kind == CommunityRewardKind.CosmeticAccent
        && CommunityPresentationCatalog.CosmeticAccents.Contains(_token);

    private string? _badgePath =>
        Kind == CommunityRewardKind.Badge
            ? _token switch
            {
                "star" =>
                    "M12 2.5l2.9 6.2 6.6.8-4.9 4.6 1.3 6.6L12 17.5l-5.9 3.2 1.3-6.6L2.5 9.5l6.6-.8z",
                "crown" => "M3.5 16.5L2 6.8l5.3 3.9L12 4l4.7 6.7L22 6.8l-1.5 9.7zM4 18h16v2H4z",
                "spark" => "M12 2l2.1 7.9L22 12l-7.9 2.1L12 22l-2.1-7.9L2 12l7.9-2.1z",
                "shield" => "M12 2l8 3.2v6.3c0 4.9-3.4 8.3-8 10.5-4.6-2.2-8-5.6-8-10.5V5.2z",
                _ => null,
            }
            : null;

    private string _chipClass =>
        _isAccent ? $"progression-reward progression-reward--{_token}" : "progression-reward";

    private string _displayName => string.IsNullOrWhiteSpace(Name) ? _placeholderName : Name.Trim();

    private string _label => Kind == CommunityRewardKind.Title ? $"“{_displayName}”" : _displayName;

    private string _kindLabel =>
        Kind switch
        {
            CommunityRewardKind.Badge => "badge",
            CommunityRewardKind.CosmeticAccent => "accent",
            _ => "title",
        };

    private string _title => string.IsNullOrEmpty(_token) ? _kindLabel : $"{_kindLabel} · {_token}";
}
