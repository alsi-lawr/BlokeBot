namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateChannelPluginPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/plugins",
            Eyebrow = "Channel plugins",
            Title = "Use plugin features in your channel",
            Summary =
                "A trusted plugin can add channel tools. Each channel controls its own plugin features and settings.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/dashboard/phone-dark-channel-setup.png",
                LightPhoneSource: "media/dashboard/phone-light-channel-setup.png",
                DarkLaptopSource: "media/dashboard/laptop-dark-channel-setup.png",
                LightLaptopSource: "media/dashboard/laptop-light-channel-setup.png",
                PhoneAlt: "Channel setup for Sample Channel with connection, bot status, and chat tool controls.",
                LaptopAlt: "Channel setup for Sample Channel with connection, bot status, and chat tool controls.",
                "Use Channel setup to select a channel and find its feature controls."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Enable one feature",
                    Paragraphs =
                    [
                        "Every plugin feature is Disabled after installation. The dashboard labels this state Off.",
                    ],
                    Steps =
                    [
                        "Open Channel setup and select the channel.",
                        "Open the plugin feature that you want to use.",
                        "Enter the generated settings.",
                        "Save the settings.",
                        "Enable the feature.",
                    ],
                    Note =
                        "A secret field never shows its saved value. Enter a new value only when you must replace it.",
                },
                new SiteGuideSection
                {
                    Heading = "Read feature readiness",
                    Facts =
                    [
                        new(
                            "Disabled",
                            "The feature is off. It receives no commands, events, schedules, actions, pages, or automatic work."
                        ),
                        new(
                            "Ready",
                            "The feature is on. BlokeBot admits work that matches the current installation and channel."
                        ),
                        new(
                            "EnabledDegraded",
                            "The feature is on, but some declared work is unavailable. The dashboard labels this state Needs attention."
                        ),
                    ],
                    Paragraphs =
                    [
                        "An absent Twitch scope or incomplete EventSub subscription can cause EnabledDegraded. Work that does not need that readiness can continue.",
                        "Use the action on the feature page to reconnect Twitch or retry the subscription check.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Know how plugin work joins the channel",
                    Bullets =
                    [
                        "Commands use built-in, custom, then plugin precedence. A built-in or custom route shadows the plugin command. The feature stays enabled.",
                        "A second enabled plugin cannot claim the same plugin command route. BlokeBot rejects that feature enablement.",
                        "Typed events, schedules, and actions run only while their feature and required readiness are admitted.",
                        "A plugin can store private data in its own SQLite database. The plugin must separate rows for each channel.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "Use pages and automatic flows",
                    Bullets =
                    [
                        "A generated page uses BlokeBot controls and validation.",
                        "An embedded page stays in a contained frame. The frame is not a security sandbox for the trusted plugin.",
                        "A declared template can create one flow when you enable the feature. BlokeBot does not show a separate preview or confirmation.",
                        "Repeated startup checks do not create duplicate flows. BlokeBot never overwrites or silently renames a flow that already exists.",
                    ],
                    Note =
                        "A structural template error or name collision rejects enablement. If Twitch readiness is absent, BlokeBot enables the valid feature and flow as EnabledDegraded.",
                },
                new SiteGuideSection
                {
                    Heading = "Recover a channel feature",
                    Paragraphs =
                    [
                        "If you deleted an automatic flow, disable and enable the feature. BlokeBot creates the flow again.",
                        "If the plugin is faulted or absent, ask a BlokeBot administrator to recover the installation.",
                        "Format 1 transfer files exclude plugin settings, plugin definitions, and plugin automation nodes.",
                    ],
                    Links =
                    [
                        new SiteLink("Open plugin administration help", "server-owners/plugins"),
                        new SiteLink("Open the visual flow editor guide", "automations"),
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Use the visual flow editor", "automations"),
                new SiteLink("Administer plugins", "server-owners/plugins"),
            ],
        };
    }
}
