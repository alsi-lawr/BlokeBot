namespace BlokeBot.Core.Components.Layout;

public partial class PageHelpButton
{
    private static readonly HelpPage _raidCollaborationHelp = new(
        "Raid & collaboration",
        [
            new(
                "Choose where to raid",
                "The Hub shows live shortlist channels that match your filters.",
                [
                    "Approved channels always supply candidates.",
                    "You can also include live channels that the channel owner follows.",
                    "Reconnect Twitch to give BlokeBot permission to read followed channels.",
                    "A Twitch follow is not approval or safety evidence.",
                    "Prepare raid always asks you to confirm.",
                ]
            ),
            new(
                "Recommend a live channel now",
                "",
                [
                    "Use Send a shoutout with the Twitch name of a live channel with viewers.",
                    "If Twitch asks you to wait, read the next available time in the panel.",
                    "Use Approve channel on a history entry to add that channel to your approved list immediately.",
                ]
            ),
            new(
                "Welcome incoming raids",
                "A failed shoutout does not use the other delivery method.",
                [
                    "Turn on Automatic raid shoutouts.",
                    "Choose the smallest raid that receives a shoutout.",
                    "Choose a native Twitch shoutout.",
                    "Alternatively, choose one chat message.",
                    "Only shoutout approved channels limits automatic shoutouts to your approved list.",
                    "Chat delivery can use regular presentation.",
                    "Chat delivery can use pinned presentation.",
                    "Chat delivery can use announcement presentation.",
                    "A pinned shoutout replaces the current pin.",
                    "BlokeBot does not restore the previous pin.",
                ]
            ),
            new(
                "Customize the chat message",
                "Fallback text appears when Twitch has no value for the last game or stream title.",
                [
                    "The message can use <code>{twitch_handle}</code>.",
                    "The message can use <code>{display_name}</code>.",
                    "The message can use <code>{channel_url}</code>.",
                    "The message can use <code>{last_game|fallback}</code>.",
                    "The message can use <code>{stream_title|fallback}</code>.",
                    "The message can use <code>{viewer_count}</code>.",
                    "Add fallback text for the last game.",
                    "Add fallback text for the stream title.",
                ]
            ),
            new(
                "Change welcome and shortlist rules",
                "Twitch gives only the total viewer count for a raid.",
                [
                    "Open Settings.",
                    "Choose whether to include followed live channels.",
                    "Make your changes.",
                    "Save the changes.",
                    "BlokeBot records no individual viewer from the raid.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _collectivesHelp = new(
        "Collectives",
        [
            new(
                "Invite hosts you know",
                "A collective is an allowlist that you build here.",
                [
                    "Twitch raids never create membership.",
                    "Twitch follows never create membership.",
                    "Shared moderators never create membership.",
                ]
            ),
            new(
                "Coordinate without control of hosts",
                "",
                [
                    "A coordinator can invite hosts.",
                    "A coordinator can withdraw invitations.",
                    "A coordinator can edit shared workflows.",
                    "A coordinator can end participation.",
                    "Hosts accept only for themselves.",
                    "Hosts decline only for themselves.",
                    "Hosts leave only for themselves.",
                    "The collective always keeps one active coordinator.",
                ]
            ),
            new(
                "What is shared",
                "",
                [
                    "Members see tournament references in a shared summary.",
                    "Members see relay totals in a shared summary.",
                    "Members see goal progress in a shared summary.",
                    "Each host keeps its own contact details.",
                    "Each host keeps its own lobby information.",
                    "Each host keeps its own source mappings.",
                    "Each host keeps its own moderator notes.",
                    "Each host keeps its own rewards.",
                    "Each host keeps its own viewer identities.",
                ]
            ),
        ]
    );

    private static readonly HelpPage _playQueuesHelp = new(
        "Play with viewers",
        [
            new(
                "Create or edit a queue",
                "The viewer-page link appears after you save the queue.",
                ["Choose a saved queue to edit it.", "Select New queue to start a draft."]
            ),
            new(
                "Run the queue",
                "",
                [
                    "Use fair selection to form a party.",
                    "Use ready checks to form a party.",
                    "Send lobby details privately to your selected viewers.",
                ]
            ),
            new(
                "What viewers can see",
                "Entry answers appear on the viewer page and the Viewer Queue overlay.",
                ["Lobby messages stay private.", "Moderator notes stay private."]
            ),
        ]
    );

    private static readonly HelpPage _momentsHelp = new(
        "Moments",
        [
            new(
                "Capture and moderate",
                "Capture now saves the current live moment for moderation.",
                [
                    "Choose how nearby captures merge.",
                    "Choose whether a stream marker is a fallback.",
                    "Choose how point rewards work.",
                ]
            ),
            new(
                "Publish the weekly recap",
                "Open weekly recap opens the public recap in a new tab.",
                [
                    "The new tab keeps this workspace in place.",
                    "The new tab keeps unsaved inputs in place.",
                    "Finalize previous week records the winning moment.",
                ]
            ),
            new(
                "What viewers can see",
                "Public titles and categories appear in the recap.",
                ["Private moderator text never appears in the recap."]
            ),
        ]
    );

    private static readonly HelpPage _overlaysHelp = new(
        "Overlays",
        [
            new(
                "Set up a Browser Source",
                "The private Browser Source URL appears only after overlay creation or URL rotation.",
                [
                    "Create an overlay.",
                    "Copy its private Browser Source URL.",
                    "Add it to OBS at 1920 by 1080.",
                ]
            ),
            new(
                "Rotate a URL",
                "URL rotation stops each OBS source that uses the previous URL.",
                ["Paste the new URL into OBS immediately."]
            ),
            new(
                "Preview and test",
                "Live preview shows the Browser Source appearance without its URL.",
                [
                    "Sample buttons affect only the selected overlay.",
                    "Send test pulse affects only the selected overlay.",
                    "These tests never change a round.",
                    "These tests never change a giveaway.",
                    "These tests never change a goal.",
                    "These tests never change a bounty.",
                ]
            ),
            new(
                "What viewers can see",
                "",
                [
                    "Overlays show only public names.",
                    "Overlays show only public counts.",
                    "Overlays show only public progress.",
                    "Overlays show only public reward names.",
                    "Twitch user IDs never reach a Browser Source.",
                    "Balances never reach a Browser Source.",
                    "Moderator notes never reach a Browser Source.",
                    "Private eligibility details never reach a Browser Source.",
                ]
            ),
        ]
    );
}
