namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateStreamMediaPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/overlays/cues",
            Eyebrow = "Stream presentation · Cues",
            Title = "Build and trigger reusable Cues",
            Summary =
                "Combine media in a reusable Cue. Then play the saved Cue through a Cue player Browser Source.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/overlays/phone-dark-overlay-cues.png",
                LightPhoneSource: "media/overlays/phone-light-overlay-cues.png",
                DarkLaptopSource: "media/overlays/laptop-dark-overlay-cues.png",
                LightLaptopSource: "media/overlays/laptop-light-overlay-cues.png",
                PhoneAlt: "Cues page on a phone that shows the saved Cue list and task-focused content editor.",
                LaptopAlt: "Cues page that shows attached saved Cues and editor columns with a reusable web layer.",
                "Saved Cues and their editor stay together. Test playback targets a Cue player Browser Source."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Cues can combine uploaded media.",
                        "Cues can combine online media.",
                        "Cues can combine web pages.",
                    ],
                    Heading = "Prepare a Cue player",
                    Steps =
                    [
                        "Turn on Overlays in Channel setup.",
                        "On Sources, create an enabled Cue player Browser Source.",
                        "Copy its private URL.",
                        "Add it to OBS at 1920 × 1080.",
                        "Open Cues at /overlays#cues.",
                        "Select the saved Cue player under Test playback.",
                    ],
                    Note =
                        "If Overlays is off, BlokeBot pauses Cue edits and playback. Saved Cues remain. If you enable Overlays again, BlokeBot does not play Cue requests missed while the feature was off.",
                },
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Fit option: Show all.",
                        "Fit option: Fill and crop.",
                        "Fit option: Stretch to fill.",
                        "Content type: uploaded media.",
                        "Content type: online media.",
                        "Content type: a web page.",
                    ],
                    Heading = "Build reusable content",
                    Paragraphs =
                    [
                        "Content lower in the list appears in front when stacking values match.",
                    ],
                    Steps =
                    [
                        "Select New cue.",
                        "Name the Cue.",
                        "Set its total duration.",
                        "Select what happens when another Cue plays.",
                        "Add content from the supported types.",
                        "Reorder or remove content as needed.",
                        "For each item, set its start time.",
                        "Set how long it plays.",
                        "Set its stacking order.",
                        "Set its left and top positions.",
                        "Set its width and height.",
                        "For image content, set the available volume.",
                        "For audio content, set the available volume.",
                        "For video content, set the available volume.",
                        "Select one fit option.",
                        "Turn Cue enabled on.",
                        "Select Create cue or Save cue.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose overlap and test playback",
                    Bullets =
                    [
                        "Play after the current cue waits.",
                        "Replace the current cue interrupts it.",
                        "Skip while another cue plays drops the new request.",
                        "Play at the same time overlaps them.",
                        "Select an enabled Cue player.",
                        "Select Play test cue.",
                        "Watch the embedded preview or OBS source for the saved result.",
                        "A test can wait briefly when the Cue player is disconnected.",
                        "If the test expires or BlokeBot rejects it, reconnect the player.",
                        "Try one fresh test.",
                        "Do not add repeated requests.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Trigger a Cue from chat",
                    Steps =
                    [
                        "Open Custom commands.",
                        "Create or edit a command.",
                        "Under What happens, select Play an overlay cue.",
                        "Select the Cue player.",
                        "Select the saved Cue.",
                        "Select the busy-player behavior.",
                        "Select whether the chat reply occurs before or after Cue acceptance.",
                        "Use the command's Test cue action.",
                        "Save the command.",
                        "Send its main command word in chat.",
                    ],
                    Bullets =
                    [
                        "For playback, enable the command.",
                        "For playback, enable the Cue.",
                        "For playback, enable the Cue player.",
                        "For playback, enable the Overlays feature.",
                        "BlokeBot reports a replaced or deleted Cue or target as unavailable. Select a current saved Cue and Browser Source. Save the command again.",
                        "The selected Cue can use safe chat context. It does not expose the private Browser Source URL.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover embedded content",
                    Bullets =
                    [
                        "Use complete secure addresses that begin with https:// for online media and web pages.",
                        "Correct a blocked address at its source.",
                        "Correct an invalid address at its source.",
                        "Correct an unreachable address at its source.",
                        "Some sites prevent embedded use. Use an embeddable page or media address instead. Do not weaken Browser Source safety settings.",
                        "If uploaded media is unavailable or replaced, open Media.",
                        "Repair that asset.",
                        "Return to the Cue.",
                        "Check the saved selection.",
                        "If the layer layout is wrong, correct its timing as necessary.",
                        "Correct its order as necessary.",
                        "Correct its percentage geometry as necessary.",
                        "Save the change. Run one new test.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Manage media for Cues", "overlays/media"),
                new SiteLink("Create Custom Commands", "commands"),
            ],
        };

        yield return new SiteGuidePage
        {
            Route = "/overlays/media",
            Eyebrow = "Stream presentation · Media library",
            Title = "Manage media for Cues",
            Summary = "Manage channel media.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/overlays/phone-dark-overlay-media.png",
                LightPhoneSource: "media/overlays/phone-light-overlay-media.png",
                DarkLaptopSource: "media/overlays/laptop-dark-overlay-media.png",
                LightLaptopSource: "media/overlays/laptop-light-overlay-media.png",
                PhoneAlt: "Media library on a phone that shows private upload controls and the saved-media area.",
                LaptopAlt: "The Media library shows channel storage use and drag-and-drop upload. It also shows saved media management.",
                "Media stays in the selected channel's private storage and is available to its Cues."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Bullets =
                    [
                        "Upload private channel media.",
                        "Preview saved files.",
                        "Repair the assets that reusable Cues use.",
                        "Accepted upload: an image file.",
                        "Accepted upload: an audio file.",
                        "Accepted upload: a video file.",
                    ],
                    Heading = "Upload accepted browser media",
                    Steps =
                    [
                        "Turn on Overlays.",
                        "Select the channel.",
                        "Open Media at /overlays#media.",
                        "Enter a clear Media name.",
                        "Drag one accepted file onto the Media file area.",
                        "Alternatively, use the file picker.",
                        "Wait for the upload result.",
                        "Check that the saved file appears under Saved media.",
                        "Open Cues.",
                        "Add Uploaded media.",
                        "Select the saved name.",
                    ],
                    Note =
                        "Uploads stay in private channel storage. The page shows current use and capacity. Another channel cannot select or serve this channel's media.",
                },
                new SiteGuideSection
                {
                    Heading = "Saved media",
                    LegacyAnchor = "preview-replace-or-delete",
                    Bullets =
                    [
                        "Before you assign a saved image to a live Cue, preview it.",
                        "Before you assign saved audio to a live Cue, preview it.",
                        "Before you assign saved video to a live Cue, preview it.",
                        "Replace file keeps the saved media item and updates its content for future playback. Test every Cue that depends on it before you go live.",
                        "Delete only after you check dependent Cues. A Cue does not silently substitute another file when its selected media is unavailable.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover an upload or playback failure",
                    Bullets =
                    [
                        "If the file is unsupported, select one supported replacement:",
                        "Supported replacement: an ordinary browser-supported image file.",
                        "Supported replacement: an ordinary browser-supported audio file.",
                        "Supported replacement: an ordinary browser-supported video file.",
                        "Do not rename an incompatible file.",
                        "If storage is full, delete unused media or replace a large file with a smaller browser-ready version. Upload once.",
                        "If an upload stops before completion, keep the original file.",
                        "Reload the page.",
                        "Before you retry, check whether a saved item exists.",
                        "If a Cue cannot play the file, preview the saved media.",
                        "Replace a damaged or unsupported file.",
                        "Save the dependent Cue.",
                        "Test it again.",
                        "If the Media page is unavailable, restore Overlays in Channel setup. Saved media remains while the feature is off.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Create Custom Commands", "commands"),
                new SiteLink("Troubleshoot the bot", "troubleshooting"),
            ],
        };
    }
}
