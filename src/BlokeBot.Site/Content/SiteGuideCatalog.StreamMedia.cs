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
                "Combine uploaded media, online media and web pages, then play the saved Cue through a Cue player Browser Source.",
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
                    Heading = "Prepare a Cue player",
                    Steps =
                    [
                        "Turn on Overlays in Channel setup.",
                        "On Sources, create an enabled Cue player Browser Source. Copy its private URL. Add it to OBS at 1920 × 1080.",
                        "Open Cues at /overlays#cues and choose the saved Cue player under Test playback.",
                    ],
                    Note =
                        "If Overlays is off, BlokeBot pauses Cue edits and playback. Saved Cues remain. If you enable Overlays again, BlokeBot does not play Cue requests missed while the feature was off.",
                },
                new SiteGuideSection
                {
                    Heading = "Build reusable content",
                    Steps =
                    [
                        "Select New cue and name it. Set its total duration. Choose what happens when another Cue plays.",
                        "Add uploaded media, online media or a web page. Reorder or remove content as needed. Content lower in the list appears in front when stacking values match.",
                        "For each item, set when it starts, how long it plays, stacking order, left, top, width and height.",
                        "For image, audio and video content, set the available volume. Choose Show all, Fill and crop or Stretch to fill.",
                        "Turn Cue enabled on and select Create cue or Save cue.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Choose overlap and test playback",
                    Bullets =
                    [
                        "Play after the current cue waits. Replace the current cue interrupts it. Skip while another cue plays drops the new request. Play at the same time overlaps them.",
                        "Choose an enabled Cue player and select Play test cue. Watch the embedded preview or OBS source for the saved result.",
                        "A test can wait briefly when the Cue player is disconnected. If the test expires or BlokeBot rejects it, reconnect the player. Try one fresh test. Do not add repeated requests.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Trigger a Cue from chat",
                    Steps =
                    [
                        "Open Custom commands and create or edit a command.",
                        "Under What happens, choose Play an overlay cue. Choose the Cue player, saved Cue and busy-player behavior. Choose whether the chat reply occurs before or after Cue acceptance.",
                        "Use the command's Test cue action, save the command, and send its main command word in chat.",
                    ],
                    Bullets =
                    [
                        "Enable the command, Cue, Cue player and Overlays feature for playback.",
                        "BlokeBot reports a replaced or deleted Cue or target as unavailable. Choose a current saved Cue and Browser Source, then save the command again.",
                        "The selected Cue can use safe chat context. It does not expose the private Browser Source URL.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover embedded content",
                    Bullets =
                    [
                        "Use complete secure addresses that begin with https:// for online media and web pages. A blocked, invalid or unreachable address must be corrected at its source.",
                        "Some sites prevent embedded use. Use an embeddable page or media address instead. Do not weaken Browser Source safety settings.",
                        "If uploaded media is unavailable or replaced, open Media and repair that asset. Return to the Cue and confirm the saved selection.",
                        "If the layer layout is wrong, correct its timing, order or percentage geometry, save, and run one new test.",
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
            Summary =
                "Upload private channel media, preview saved files and repair the assets used by reusable Cues.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/overlays/phone-dark-overlay-media.png",
                LightPhoneSource: "media/overlays/phone-light-overlay-media.png",
                DarkLaptopSource: "media/overlays/laptop-dark-overlay-media.png",
                LightLaptopSource: "media/overlays/laptop-light-overlay-media.png",
                PhoneAlt: "Media library on a phone that shows private upload controls and the saved-media area.",
                LaptopAlt: "Media library that shows channel storage use, drag-and-drop upload and saved media management.",
                "Media stays in the selected channel's private storage and is available to its Cues."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Upload accepted browser media",
                    Steps =
                    [
                        "Turn on Overlays, choose the channel and open Media at /overlays#media.",
                        "Enter a clear Media name. Drag an image, audio or video file onto the Media file area. You can also use the file picker.",
                        "Wait for the upload result and confirm the saved file appears under Saved media.",
                        "Open Cues, add Uploaded media and choose the saved name.",
                    ],
                    Note =
                        "Uploads stay in private channel storage. The page shows current use and capacity. Another channel cannot select or serve this channel's media.",
                },
                new SiteGuideSection
                {
                    Heading = "Preview, replace or delete",
                    Bullets =
                    [
                        "Preview a saved image, audio or video before you assign it to a live Cue.",
                        "Replace file keeps the saved media item and updates its content for future playback. Test every Cue that depends on it before you go live.",
                        "Delete only after you check dependent Cues. A Cue does not silently substitute another file when its selected media is unavailable.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Recover an upload or playback failure",
                    Bullets =
                    [
                        "Unsupported file: choose an ordinary browser-supported image, audio or video file. Do not rename an incompatible file.",
                        "Storage full: delete unused media or replace a large file with a smaller browser-ready version. Upload once.",
                        "Upload interrupted: keep the original file and reload the page. Confirm whether a saved item exists before you retry.",
                        "Cue cannot play the file: preview the saved media. Replace a damaged or unsupported file. Save and test the dependent Cue again.",
                        "Media page unavailable: restore Overlays in Channel setup. Saved media remains while the feature is off.",
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
