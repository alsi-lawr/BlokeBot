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
            Summary = "Use your channel page.",
            Media = new SiteMedia(
                DarkPhoneSource: "media/community/phone-dark-viewer-portal.png",
                LightPhoneSource: "media/community/phone-light-viewer-portal.png",
                DarkLaptopSource: "media/community/laptop-dark-viewer-portal.png",
                LightLaptopSource: "media/community/laptop-light-viewer-portal.png",
                PhoneAlt: "The Sample Channel page on a phone, with activity links and current public activities.",
                LaptopAlt: "The Sample Channel page with current activities and the signed-in viewer's private participation summary.",
                "You can open a channel without sign-in. Sign in to add your own participation details."
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
                        "Open one channel page for public activities and links.",
                        "For your own participation details, sign in.",
                        "Also sign in for actions that require your Twitch account.",
                        "An activity button opens its first listed destination. If an activity has several destinations, use its nearby labeled list.",
                        "A destination list can contain queues.",
                        "A destination list can contain request boards.",
                        "A destination list can contain Collectives.",
                        "A channel page lists up to five destinations for each activity. Existing direct links still work when a destination is not listed.",
                        "Use Back to channel on an activity page. Browser Back and Forward also keep the selected channel and page together.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "See your participation",
                    Paragraphs =
                    [
                        "Sign in with Twitch to see your participation in available activities. Available participation details appear below. The channel page does not make the channel's moderator or administrator tools public.",
                    ],
                    Bullets =
                    [
                        "Your points.",
                        "Your queue position.",
                        "Your request allowance.",
                        "Your issued Bingo card.",
                        "Your queue and request records use your verified Twitch account. A rename does not give another account your records. Older login-only history remains subject to each activity's existing rules.",
                        "Edit passport opens your protected editor. Export in that editor downloads your own data. It is not a public link for others to use.",
                        "Sign out returns to the same channel without your personal cards. A changed sign-in or a server restart can require a page reload.",
                    ],
                    Links = [new SiteLink("Choose passport visibility", "community/passports")],
                },
                new SiteGuideSection
                {
                    Heading = "Updates and recovery",
                    LegacyAnchor = "wait-for-updates-or-recover",
                    Paragraphs =
                    [
                        "While connected, the channel page groups activity updates no more than once every ten seconds. A disconnected page can retain its last visible information. This information does not guarantee that the activity is still current.",
                        "If a section is unavailable, try the other available activities. A busy-page notice asks you to wait before another attempt. Repeated refreshes or more open tabs do not reset the limits.",
                        "BlokeBot marks public channel pages for exclusion from search indexes. This mark is not access control. Anyone with a public link can read its public content. Do not put private information in public activity fields.",
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
                    "Keep the normal Blazor connection endpoints reachable. BlokeBot uses the same hub for public and protected documents. A Data Protection marker classifies the initial document before public transport admission.",
                    "Markers have no independent expiry. An ordinary long-lived page can reconnect while its key and sign-in remain valid. Normal key rotation retains decryptable keys. Existing caches for the Data Protection key ring can delay recognition of revocation. This marker does not revoke sessions immediately.",
                    "After key-ring deletion or an ephemeral Simulation restart, a full reload is necessary. A private marker fails after logout or an account change. A reissued or replayed marker does not reset client budgets.",
                    "Public HTML and protected self exports use private, no-store responses. Do not add a reverse-proxy HTML cache.",
                    "The document CSP permits BlokeBot's nonce-bearing scripts/import map and required inline styles. Navigation between public and unrelated protected pages loads a new document. BlokeBot applies the new document's policy.",
                    "Before the interactive router is ready, public links load a fresh document. They do not merge HTML with a different nonce. Once the router is ready, public-page navigation stays in the existing circuit. Normal document closure ends its public connection. After a temporary disconnect, the retained circuit can reconnect.",
                ],
                Bullets =
                [
                    "Do not replace it with a policy that blocks Blazor.",
                    "Do not replace it with a policy that blocks themes.",
                    "Do not replace it with a policy that blocks existing forms.",
                    "The marker never grants page permission.",
                    "The marker never grants feature permission.",
                    "The marker never grants administrator permission.",
                    "HTTP document/negotiation attempts: 60 per minute for an anonymous client.",
                    "HTTP document/negotiation attempts: 120 per minute for a signed-in account.",
                    "HTTP document/negotiation attempts: 240 per minute in aggregate per source address. Shared NAT users share this address budget.",
                    "Owner reads: 60/120 per minute across channels and 30/60 per resolved channel, anonymous/signed-in respectively.",
                    "Actions: 30/60 per minute across channels and 15/30 per resolved channel, anonymous/signed-in respectively.",
                    "Failed attempts can consume an applicable budget. Request and action budgets are separate.",
                    "Active logical transports for public pages, including unfinished handshakes: 2 per anonymous address or 4 per signed-in account.",
                    "Active logical transports for public pages, including unfinished handshakes: 8 per address and 256 per process.",
                    "Active or framework-retained circuits: 24/32 per client, anonymous/signed-in respectively.",
                    "Active or framework-retained circuits: 64 per address.",
                    "Active or framework-retained circuits: 256 per process.",
                    "The transport lease spans the whole framework connection, not individual long polls. Socket closure releases its transport, not a retained circuit.",
                    "Negotiate-only contexts use framework expiry and HTTP attempt budgets. They are not active transport leases. Existing framework retention and actual circuit closure release retained ownership.",
                    "Connection attempts have a limit of 30/60 per minute.",
                    "Protocol activity includes acknowledgments and transport continuations.",
                    "Protocol activity has a separate client safety ceiling of 600 activities per minute.",
                    "The protocol-activity burst limit is 120 activities.",
                    "This ceiling does not replace semantic action limits.",
                    "Limiter storage has 4,096 entries. Only idle entries without owned leases can expire after ten minutes. A full store rejects new entries instead of active budget eviction. These limits apply to one process, not a distributed ingress service.",
                ],
            },
            new SiteGuideSection
            {
                Bullets =
                [
                    "Exclude query strings on /_blazor from proxy access logs and traces.",
                    "Do not record the document classification parameter in logs.",
                    "Do not enable sensitive SQL parameter logging.",
                    "Do not record viewer identifiers.",
                    "Do not record form payloads.",
                    "Do not record authentication material.",
                ],
                Heading = "Trusted proxy configuration",
                LegacyAnchor = "configure-the-trusted-proxy-boundary",
                Paragraphs =
                [
                    "This boundary ignores forwarded headers until you name the actual trusted proxy addresses or networks. Configure only the immediate proxy hop. The boundary accepts X-Forwarded-For and X-Forwarded-Proto from those peers. It does not accept arbitrary client headers or X-Forwarded-Host. Other application routes retain their existing behavior for forwarded headers.",
                    "Without this configuration, reverse-proxied visitors share the proxy's address budget. Do not trust every address to avoid this limit. Check real source-address and HTTPS behavior in your own deployment. Local synthetic checks do not verify a production proxy.",
                    "The document classification parameter is protected.",
                ],
                Code =
                    "PublicViewer__ForwardedHeaders__KnownProxies__0=127.0.0.1\n# Or an explicitly trusted proxy network:\nPublicViewer__ForwardedHeaders__KnownNetworks__0=10.20.0.0/24",
                Note =
                    "These are examples, not a request to trust these addresses on every installation.",
            },
            new SiteGuideSection
            {
                Bullets =
                [
                    "An alert requires at least ten reads that meet one or more of the listed alert conditions.",
                    "These reads must occur within two minutes.",
                    "The same reads must span at least thirty seconds.",
                    "Metric labels identify bounded feature categories.",
                    "Metric labels identify bounded audience categories.",
                    "Metric labels identify bounded outcome categories.",
                    "Metric labels do not identify hosts or viewers.",
                    "Points and the portal's passport summary accept at most 10,000 balance candidates.",
                    "Each amount has a limit of 128 characters.",
                    "Each login has a limit of 160 characters.",
                    "Passport summaries accept at most 100 historical logins.",
                    "The portal runs at most four owner reads at once per request/circuit scope.",
                    "Each portal summary has a limit of 4 KiB.",
                    "Portal lists contain at most five items.",
                    "A failed read counts toward the alert threshold.",
                    "A read that exhausts its budget counts toward the same threshold.",
                    "A read that takes one second or more counts toward the same threshold.",
                    "The in-process metrics and traces cover duration.",
                    "The in-process metrics and traces cover outcome.",
                    "The in-process metrics and traces cover rejection.",
                    "The in-process metrics and traces cover lifecycle.",
                ],
                Heading = "Public read health",
                LegacyAnchor = "watch-public-read-health",
                Paragraphs =
                [
                    "These portal limits do not truncate existing full activity pages.",
                    "Above a limit, that summary becomes unavailable instead of a partial ranking. The full owner/editor APIs retain their existing behavior.",
                    "Read cancellation and five-second owner/revalidation waits limit the caller's wait for each phase. An unfinished owner keeps its concurrency slot until it finishes. These limits do not guarantee a fixed number of rows for a database scan. They do not guarantee that every failed first visit finishes within a warm benchmark time.",
                    "BlokeBot.ViewerPortal and BlokeBot.PublicViewer expose in-process metrics and traces. This feature installs no telemetry exporter.",
                    "An owner can create one aggregate alert in the existing Alerts page under all these conditions. Three healthy reads resolve the alert.",
                    "Acknowledgment does not cause another alert for each read. After recovery, a new episode must also pass the thirty-minute cooldown.",
                    "Aggregation has a limit of 1,024 host/owner/audience states. These states do not survive a process restart or loss of in-memory state. At capacity, BlokeBot skips new fault aggregations. Outcome metrics remain available.",
                ],
                Links =
                [
                    new SiteLink("Main database operations", "server-owners/database"),
                    new SiteLink("Viewer channel guide", "community/viewer-portal"),
                ],
            },
        ];
}
