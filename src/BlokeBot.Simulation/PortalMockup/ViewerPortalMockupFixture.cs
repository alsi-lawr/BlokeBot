namespace BlokeBot.Simulation.PortalMockup;

/// <summary>Hand-authored portal states over the simulation's sample channel.</summary>
internal static class ViewerPortalMockupFixture
{
    private const string _channelLogin = "samplechannel";
    private const string _missingLogin = "nosuchchannel";

    private static readonly ViewerPortalMockupChannel _liveChannel = new(
        _channelLogin,
        "Sample Channel",
        true,
        "Live now · BlokeQuest"
    );

    private static readonly ViewerPortalMockupChannel _offlineChannel = new(
        _channelLogin,
        "Sample Channel",
        false,
        "Offline · last live 2 days ago"
    );

    private static readonly ViewerPortalMockupIdentity _viewer = new("NightOwl", "nightowl");

    public static ViewerPortalMockupPage Build(
        ViewerPortalMockupState state,
        ViewerPortalMockupViewer viewer,
        string theme
    ) =>
        state switch
        {
            ViewerPortalMockupState.NotFound => new(
                state,
                viewer,
                theme,
                _missingLogin,
                null,
                null,
                [],
                [],
                []
            ),
            ViewerPortalMockupState.NoPublicFeatures => new(
                state,
                viewer,
                theme,
                _channelLogin,
                _offlineChannel,
                IdentityFor(viewer),
                [],
                [],
                []
            ),
            ViewerPortalMockupState.Sparse => new(
                state,
                viewer,
                theme,
                _channelLogin,
                _offlineChannel,
                IdentityFor(viewer),
                SparseFeatures(),
                viewer is ViewerPortalMockupViewer.Authenticated ? SparsePersonal() : [],
                []
            ),
            ViewerPortalMockupState.Loading => new(
                state,
                viewer,
                theme,
                _channelLogin,
                _liveChannel,
                IdentityFor(viewer),
                [
                    .. PopulatedFeatures()
                        .Select(feature =>
                            feature with
                            {
                                State = ViewerPortalMockupFeatureState.Loading,
                            }
                        ),
                ],
                [],
                []
            ),
            ViewerPortalMockupState.PartialFailure => new(
                state,
                viewer,
                theme,
                _channelLogin,
                _liveChannel,
                IdentityFor(viewer),
                [
                    .. PopulatedFeatures()
                        .Select(feature =>
                            feature.Key == "bingo"
                                ? feature with
                                {
                                    State = ViewerPortalMockupFeatureState.Failed,
                                }
                                : feature
                        ),
                ],
                viewer is ViewerPortalMockupViewer.Authenticated ? PopulatedPersonal() : [],
                PopulatedRecent()
            ),
            _ => new(
                ViewerPortalMockupState.Populated,
                viewer,
                theme,
                _channelLogin,
                _liveChannel,
                IdentityFor(viewer),
                PopulatedFeatures(),
                viewer is ViewerPortalMockupViewer.Authenticated ? PopulatedPersonal() : [],
                PopulatedRecent()
            ),
        };

    private static ViewerPortalMockupIdentity? IdentityFor(ViewerPortalMockupViewer viewer) =>
        viewer is ViewerPortalMockupViewer.Authenticated ? _viewer : null;

    private static ViewerPortalMockupFeature[] PopulatedFeatures() =>
        [
            new(
                "bingo",
                "Bingo",
                $"/bingo/{_channelLogin}",
                ViewerPortalMockupFeatureState.Active,
                "Live",
                "green",
                "Tonight's stream moments",
                "Teams · 4×4 · 1 row win so far",
                "Open Bingo"
            ),
            new(
                "queue",
                "Play queue",
                $"/queues/{_channelLogin}/main",
                ViewerPortalMockupFeatureState.Active,
                "Open",
                "green",
                "Community night",
                "3 waiting · party of 2 · BlokeQuest",
                "Open the queue"
            ),
            new(
                "bounties",
                "Bounties",
                $"/bounties/{_channelLogin}",
                ViewerPortalMockupFeatureState.Active,
                "2 open",
                "amber",
                "Community speedrun challenge",
                "1,250 of 2,000 points pledged",
                "See bounties"
            ),
            new(
                "raid",
                "BlokeRaid",
                $"/raid/{_channelLogin}",
                ViewerPortalMockupFeatureState.Active,
                "Boss fight",
                "violet",
                "The Null Wyrm",
                "Boss at 62% health · 4 raiders in",
                "Open BlokeRaid"
            ),
            new(
                "competitions",
                "Tournaments",
                $"/competitions/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "Quarter-finals",
                "blue",
                "Summer Cup",
                "Next matches when the stream is live",
                "See the bracket"
            ),
            new(
                "community",
                "Community",
                $"/community/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "Week 3",
                "blue",
                "Summer community climb",
                "12 quests · 8 achievements",
                "See the season"
            ),
            new(
                "requests",
                "Requests",
                $"/requests/{_channelLogin}/requests",
                ViewerPortalMockupFeatureState.Quiet,
                "Open",
                "green",
                "Game night requests",
                "14 requests · voting open",
                "Open the board"
            ),
            new(
                "moments",
                "Moments",
                $"/moments/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "5 this week",
                "slate",
                "Weekly recap",
                "Vote on this week's moments",
                "See moments"
            ),
            new(
                "points",
                "Points leaderboard",
                $"/points/leaderboard/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "Standings",
                "slate",
                "NightOwl leads with 4,820",
                "Top 25 shown",
                "See standings"
            ),
            new(
                "guessing",
                "Guessing leaderboard",
                $"/guessing/leaderboard/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "Standings",
                "slate",
                "PixelPilot leads",
                "18 rounds this season",
                "See standings"
            ),
            new(
                "collectives",
                "Collectives",
                $"/collectives/{_channelLogin}/3f78b947-a0f8-4872-ae3b-a876a27e58a0",
                ViewerPortalMockupFeatureState.Quiet,
                "Raid relay",
                "slate",
                "Cosy Circuit",
                "3 channels · relay in progress",
                "Open the collective"
            ),
        ];

    private static ViewerPortalMockupFeature[] SparseFeatures() =>
        [
            new(
                "bingo",
                "Bingo",
                $"/bingo/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "No live game",
                "slate",
                "No Bingo card is live",
                "Past games stay in the archive",
                "Open Bingo"
            ),
            new(
                "queue",
                "Play queue",
                $"/queues/{_channelLogin}/main",
                ViewerPortalMockupFeatureState.Quiet,
                "Closed",
                "slate",
                "Community night",
                "Opens when the stream is live",
                "Open the queue"
            ),
            new(
                "points",
                "Points leaderboard",
                $"/points/leaderboard/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "Standings",
                "slate",
                "ChatRegular leads with 640",
                "Top 25 shown",
                "See standings"
            ),
            new(
                "guessing",
                "Guessing leaderboard",
                $"/guessing/leaderboard/{_channelLogin}",
                ViewerPortalMockupFeatureState.Quiet,
                "Standings",
                "slate",
                "No rounds yet",
                "Standings appear after the first round",
                "See standings"
            ),
        ];

    private static ViewerPortalMockupPersonalItem[] PopulatedPersonal() =>
        [
            new(
                "Points",
                "4,820",
                "First on the leaderboard",
                $"/points/leaderboard/{_channelLogin}",
                "See standings",
                1
            ),
            new(
                "Passport",
                "Public",
                "Summer star · 3 achievements shown",
                $"/passports/{_channelLogin}/me",
                "Edit passport",
                null
            ),
            new(
                "Play queue",
                "#2 of 3",
                "Community night · ready check soon",
                $"/queues/{_channelLogin}/main",
                "Open the queue",
                null
            ),
            new(
                "Requests",
                "2 active",
                "1 vote left on Game night requests",
                $"/requests/{_channelLogin}/requests",
                "Open the board",
                null
            ),
            new(
                "Bingo",
                "Team Aurora",
                "6 of 16 squares marked",
                $"/bingo/{_channelLogin}",
                "Open your card",
                null
            ),
        ];

    private static ViewerPortalMockupPersonalItem[] SparsePersonal() =>
        [
            new(
                "Points",
                "120",
                "14th on the leaderboard",
                $"/points/leaderboard/{_channelLogin}",
                "See standings",
                14
            ),
            new(
                "Passport",
                "Not set up",
                "Choose what this channel can show about you",
                $"/passports/{_channelLogin}/me",
                "Set up passport",
                null
            ),
            new(
                "Play queue",
                "Not queued",
                "Community night is closed",
                $"/queues/{_channelLogin}/main",
                "Open the queue",
                null
            ),
        ];

    private static ViewerPortalMockupEvent[] PopulatedRecent() =>
        [
            new("20 min ago", "Bingo", "Team Aurora completed a row on Tonight's stream moments"),
            new("1 h ago", "Bounties", "Zero-health comeback was completed and paid out"),
            new("Yesterday", "Moments", "Community clutch save was published to the weekly recap"),
            new("Yesterday", "Community", "PixelPilot unlocked Daily regular"),
            new("2 days ago", "Tournaments", "Summer Cup quarter-finals were drawn"),
        ];
}
