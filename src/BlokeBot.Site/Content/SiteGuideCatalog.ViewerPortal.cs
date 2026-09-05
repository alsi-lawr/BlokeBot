namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateViewerPortalPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/community/viewer-portal",
            Eyebrow = "Community interaction",
            Title = "Find a channel's activities",
            Summary =
                "Open one channel page for public activities and links. Sign in only when you need your own participation details or an action that requires your Twitch account.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-viewer-portal.png",
                LightPhoneSource: "media/community/phone-light-viewer-portal.png",
                DarkLaptopSource: "media/community/laptop-dark-viewer-portal.png",
                LightLaptopSource: "media/community/laptop-light-viewer-portal.png",
                PhoneAlt: "The Sample Channel page on a phone, with activity links and current public activities.",
                LaptopAlt: "The Sample Channel page with current activities and the signed-in viewer's private participation summary.",
                "Open a channel without signing in. Sign in to add your own participation details."
            ),
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Open the channel page",
                    Paragraphs =
                    [
                        "Use the channel link on the BlokeBot application, not on this guide website. Replace samplechannel with the channel's Twitch login. You do not need a BlokeBot channel of your own.",
                    ],
                    Code = "https://bot.example.com/channel/samplechannel",
                    Bullets =
                    [
                        "An activity button opens its first listed destination. When there are several queues, request boards or Collectives, use the nearby labelled list to choose one.",
                        "A channel page lists up to five destinations for each activity. Existing direct links still work when a destination is not listed.",
                        "Use Back to channel on an activity page. Browser Back and Forward also keep the selected channel and page together.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "See your participation",
                    Paragraphs =
                    [
                        "Sign in with Twitch to see your points, queue position, request allowance and issued Bingo card when those activities are available. The channel page does not make the channel's moderator or administrator tools public.",
                    ],
                    Bullets =
                    [
                        "Your queue and request records use your verified Twitch account, so a rename does not give another account your records. Older login-only history remains subject to each activity's existing rules.",
                        "Edit passport opens your protected editor. Export in that editor downloads your own data; it is not a public sharing link.",
                        "Sign out returns to the same channel without your personal cards. A changed sign-in or a server restart can require a page reload.",
                    ],
                    Links = [new SiteLink("Choose passport visibility", "community/passports")],
                },
                new SiteGuideSection
                {
                    Heading = "Wait for updates or recover",
                    Paragraphs =
                    [
                        "The channel page groups activity updates, no more than once every ten seconds while connected. A disconnected page can retain its last visible information; it is not a promise that the activity is still current.",
                        "If a section is unavailable, try the other working activities. A busy-page notice means you should wait before trying again. Repeated refreshes or opening more tabs do not reset the limits.",
                        "Public channel pages are marked not to appear in search indexes, but that is not access control. Anyone with a public link can read its public content. Do not put private information in public activity fields.",
                    ],
                },
            ],
            Next =
            [
                new SiteLink("Join a play queue", "community/play-with-viewers"),
                new SiteLink("Use request boards", "community/request-boards"),
                new SiteLink("Operate public viewer pages", "server-owners#public-viewer-pages"),
            ],
        };
    }

    private static IEnumerable<SiteGuideSection> ViewerPortalOperationSections() =>
        [
            new SiteGuideSection
            {
                Heading = "Public viewer pages",
                Paragraphs =
                [
                    "Keep the normal Blazor connection endpoints reachable. BlokeBot uses the same hub for public and protected documents. A Data Protection marker classifies the initial document before public transport admission; it never grants page, feature or administrator permission.",
                    "Markers have no independent expiry, so an ordinary long-lived page can reconnect while its key and sign-in remain valid. Normal key rotation retains decryptable keys. Existing Data Protection key-ring caches can delay recognition of revocation; this marker is not an immediate session-revocation mechanism. Deleting the key ring or restarting an ephemeral Simulation requires a full reload. A private marker fails after logout or an account change. Reissuing or replaying a marker does not reset client budgets.",
                    "Public HTML and protected self exports use private, no-store responses. Do not add a reverse-proxy HTML cache. The document CSP permits BlokeBot's nonce-bearing scripts/import map and required inline styles; do not replace it with a policy that blocks Blazor, themes or existing forms. Crossing between public and unrelated protected pages loads a new document so its policy is applied.",
                    "Public links clicked before the interactive router is ready load a fresh document rather than merging HTML with a different nonce. Once ready, public-page navigation stays in the existing circuit. Normal document closure ends its public connection; a temporary disconnect can still reconnect the retained circuit.",
                ],
                Bullets =
                [
                    "HTTP document/negotiation attempts: 60 per minute for an anonymous client, 120 for a signed-in account, and 240 in aggregate per source address. Shared NAT users therefore share an address budget.",
                    "Owner reads: 60/120 per minute across channels and 30/60 per resolved channel, anonymous/signed-in respectively. Actions: 30/60 across channels and 15/30 per resolved channel. Failed attempts can consume an applicable budget; request and action budgets are separate.",
                    "Public active logical transports, including unfinished handshakes: 2 per anonymous address, 4 per signed-in account, 8 per address and 256 per process. Active or framework-retained circuits: 24/32 per client, 64 per address and 256 per process. The transport lease spans the whole framework connection, not individual long polls. Closing a socket releases its transport, not a retained circuit. Negotiate-only contexts use framework expiry and HTTP attempt budgets; they are not active transport leases. Existing framework retention and actual circuit closure release retained ownership.",
                    "Connection attempts are limited to 30/60 per minute. Protocol activity, including acknowledgements and transport continuations, has a separate 600-per-minute client safety ceiling with a 120-activity burst. This is not a substitute for semantic action limits.",
                    "Limiter storage has 4,096 entries. Only idle entries without owned leases can expire after ten minutes; a full store rejects new entries instead of evicting active budgets. These are one-process limits, not a distributed ingress service.",
                ],
            },
            new SiteGuideSection
            {
                Heading = "Configure the trusted proxy boundary",
                Paragraphs =
                [
                    "Forwarding headers are ignored by this boundary until you name the actual trusted proxy addresses or networks. Configure only the immediate proxy hop. The boundary accepts X-Forwarded-For and X-Forwarded-Proto from those peers, not arbitrary client headers or X-Forwarded-Host. Other application routes retain their existing forwarding behaviour.",
                    "Without this configuration, reverse-proxied visitors share the proxy's address budget. Never work around this by trusting every address. Confirm real source-address and HTTPS handling in your own deployment; the local synthetic checks are not a production proxy verification.",
                    "Exclude query strings on /_blazor from proxy access logs and traces. The document classification parameter is protected but must not be logged. Do not enable sensitive SQL parameter logging or record viewer identifiers, form payloads or authentication material.",
                ],
                Code =
                    "PublicViewer__ForwardedHeaders__KnownProxies__0=127.0.0.1\n# Or an explicitly trusted proxy network:\nPublicViewer__ForwardedHeaders__KnownNetworks__0=10.20.0.0/24",
                Note =
                    "These are examples, not a request to trust these addresses on every installation.",
            },
            new SiteGuideSection
            {
                Heading = "Watch public read health",
                Paragraphs =
                [
                    "The portal runs at most four owner reads at once per request/circuit scope. Each summary is capped at 4 KiB; lists contain at most five items. The existing full activity pages are not silently truncated to these portal limits.",
                    "Points and the portal's passport summary accept at most 10,000 balance candidates, 128 characters per amount and 160 per login. Passport summaries accept at most 100 historical logins. Exceeding a limit makes that summary unavailable, rather than publishing a partial ranking. The full owner/editor APIs retain their existing behaviour.",
                    "Read cancellation and five-second owner/revalidation waits bound how long a caller waits for each phase. An unfinished owner keeps its concurrency slot until it really finishes. These limits do not guarantee a database statement scans a fixed number of rows or that every failed first visit finishes within a warm benchmark time.",
                    "BlokeBot.ViewerPortal and BlokeBot.PublicViewer expose in-process metrics and traces for duration, outcome, rejection and lifecycle. Labels identify bounded feature/audience/outcome categories, not hosts or viewers. No telemetry exporter is installed by this feature.",
                    "An owner with at least ten failed, budget-exhausted or one-second-plus reads within two minutes, spanning at least thirty seconds, can create one aggregate alert in the existing Alerts page. Three healthy reads resolve it. Acknowledgement does not cause per-read re-alerting; a recovered new episode must also pass the thirty-minute cooldown. Aggregation is capped at 1,024 host/owner/audience states and survives neither process restart nor loss of in-memory state. At capacity, new fault aggregations are skipped; outcome metrics remain available.",
                ],
                Links =
                [
                    new SiteLink("Main database operations", "server-owners/database"),
                    new SiteLink("Viewer channel guide", "community/viewer-portal"),
                ],
            },
        ];
}
