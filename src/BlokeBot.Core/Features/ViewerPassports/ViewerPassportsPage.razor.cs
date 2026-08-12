using System.Globalization;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.ViewerPassports;

public partial class ViewerPassportsPage
{
    [Parameter]
    public string? Channel { get; set; }

    private readonly IReadOnlyList<VisibilityOption> _visibilityOptions =
    [
        new(
            ViewerPassportVisibility.Public,
            "Public",
            "Anyone with the link can see the profile fields you allow, even without signing in.",
            "public"
        ),
        new(
            ViewerPassportVisibility.ChannelMembers,
            "Channel members",
            "Signed-in viewers who have a passport in this channel, and channel managers.",
            "members"
        ),
        new(
            ViewerPassportVisibility.Private,
            "Private",
            "Only you and channel managers can open this passport.",
            "private"
        ),
    ];
    private ViewerPassportView? _passport;
    private string _profileLine = string.Empty;
    private string _selectedTitle = string.Empty;
    private string _selectedBadge = string.Empty;
    private ViewerPassportVisibility _visibility = ViewerPassportVisibility.Private;
    private bool _hideAttendance = true;
    private bool _loaded;
    private bool _featureDisabled;
    private bool _confirmReset;
    private bool _failed;
    private string _feedback = string.Empty;

    private string _description =>
        _passport is null
            ? "Choose what this channel can show about your participation."
            : $"Choose what {_passport.HostDisplayName} can show about your participation.";
    private ViewerPassportRewardView? _previewBadge =>
        SelectedReward(_selectedBadge, _passport?.EarnedBadges);
    private ViewerPassportRewardView? _previewTitle =>
        SelectedReward(_selectedTitle, _passport?.EarnedTitles);

    protected override async Task OnParametersSetAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loaded = false;
        var identity = Identity();
        var outcome = string.IsNullOrWhiteSpace(Channel)
            ? HostId == 0
                ? new ViewerPassportQueryOutcome.NotFound()
                : await _passports.GetSelfAsync(HostId, identity, CancellationToken.None)
            : await _passports.GetSelfAsync(Channel, identity, CancellationToken.None);
        _featureDisabled = outcome is ViewerPassportQueryOutcome.FeatureDisabled;
        _passport = outcome is ViewerPassportQueryOutcome.Available available
            ? available.Passport
            : null;
        if (_passport is { } passport)
        {
            _profileLine = passport.ProfileLine;
            _selectedTitle =
                passport.SelectedTitle?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _selectedBadge =
                passport.SelectedBadge?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _visibility = passport.Visibility;
            _hideAttendance = passport.HideAttendance;
        }
        _loaded = true;
    }

    private async Task SaveAsync()
    {
        if (_passport is null)
        {
            return;
        }
        var outcome = await _passports.SaveAsync(
            new(
                _passport.HostId,
                Identity(),
                _profileLine,
                _visibility,
                _hideAttendance,
                ParseReward(_selectedTitle),
                ParseReward(_selectedBadge)
            ),
            CancellationToken.None
        );
        switch (outcome)
        {
            case ViewerPassportMutationOutcome.Succeeded succeeded:
                _passport = succeeded.Passport;
                _feedback = "Viewer passport saved.";
                _failed = false;
                break;
            case ViewerPassportMutationOutcome.UnearnedReward:
                Fail("Choose only titles and badges earned in this channel.");
                break;
            case ViewerPassportMutationOutcome.Invalid invalid:
                Fail(invalid.Message);
                break;
            case ViewerPassportMutationOutcome.FeatureDisabled:
                _featureDisabled = true;
                _passport = null;
                break;
            default:
                Fail("Viewer passport could not be saved.");
                break;
        }
    }

    private async Task ResetAsync()
    {
        if (_passport is null)
        {
            return;
        }
        var outcome = await _passports.ResetAsync(
            _passport.HostId,
            PageContext.Session.UserId,
            CancellationToken.None
        );
        if (outcome is ViewerPassportResetOutcome.Succeeded)
        {
            _feedback = "Viewer passport reset. Source feature history was not changed.";
            _failed = false;
            _confirmReset = false;
            await LoadAsync();
            return;
        }
        if (outcome is ViewerPassportResetOutcome.FeatureDisabled)
        {
            _featureDisabled = true;
            _passport = null;
            return;
        }
        Fail("Viewer passport could not be reset.");
    }

    private ViewerPassportIdentity Identity() =>
        new(PageContext.Session.UserId, PageContext.Session.Login, PageContext.Session.DisplayText);

    private void ToggleAttendance() => _hideAttendance = !_hideAttendance;

    private string VisibilityClass(VisibilityOption option) =>
        option.Value == _visibility
            ? $"passport-visibility-option passport-visibility-option--{option.Tone} passport-visibility-option--selected"
            : $"passport-visibility-option passport-visibility-option--{option.Tone}";

    private static long? ParseReward(string value) =>
        long.TryParse(value, out var parsed) ? parsed : null;

    private static ViewerPassportRewardView? SelectedReward(
        string value,
        IReadOnlyList<ViewerPassportRewardView>? rewards
    ) => rewards?.SingleOrDefault(reward => reward.Id == ParseReward(value));

    private string ExportUrl() =>
        _passport is null ? "#" : $"/passports/{Uri.EscapeDataString(_passport.HostLogin)}/export";

    private static string PublicUrl(ViewerPassportView passport) =>
        $"/passport/{Uri.EscapeDataString(passport.HostLogin)}/{Uri.EscapeDataString(passport.Login)}";

    private static string Initials(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private static RenderFragment Stat(string value, string label) =>
        builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "passport-preview__stat");
            builder.OpenElement(2, "b");
            builder.AddAttribute(3, "class", "passport-preview__stat-value");
            builder.AddContent(4, value);
            builder.CloseElement();
            builder.OpenElement(5, "span");
            builder.AddAttribute(6, "class", "passport-preview__stat-label");
            builder.AddContent(7, label);
            builder.CloseElement();
            builder.CloseElement();
        };

    private void Fail(string message)
    {
        _feedback = message;
        _failed = true;
    }

    private sealed record VisibilityOption(
        ViewerPassportVisibility Value,
        string Label,
        string Description,
        string Tone
    );
}
