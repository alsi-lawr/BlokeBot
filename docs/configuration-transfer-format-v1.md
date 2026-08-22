# Channel configuration transfer format 1

BlokeBot exports channel configuration as UTF-8 JSON with the format identifier
`blokebot.channel-configuration` and schema version `1`. The dashboard can export one section or
a selected bundle and can import only selected sections from a bundle.

```json
{
  "format": "blokebot.channel-configuration",
  "version": 1,
  "exportedAtUtc": "2026-08-20T12:00:00Z",
  "source": {
    "channelLogin": "example_channel",
    "blokeBotVersion": "0.12.0"
  },
  "sections": {
    "customCommands": {},
    "announcements": {},
    "guessing": {},
    "points": {},
    "channelToolEnablement": {},
    "overlays": {},
    "automations": {}
  }
}
```

## Section contracts

- `customCommands`: time zone, reusable replies, counters, commands, aliases, cooldowns,
  general access rules, invocation limits, and reply routes. Viewer-specific allow lists stay local
  to the destination channel.
- `announcements`: reusable replies, scheduled chat messages, and Twitch announcements. Weekly
  schedules store their UTC weekday and UTC time directly.
- `guessing`: profiles, canonical slugs, accepted answers, aliases, rewards, and reply text.
- `points`: terminology, aliases, reply text, gambling rules, and giveaway rules.
- `channelToolEnablement`: one Boolean for each independent Chat Tools switch. Format 1 has 20
  switches and keeps Polls, Clips and Markers, Rewards and Redemptions, Predictions, and Raid
  Collaboration separate.
- `overlays`: portable core Browser Source instances, typed appearance and configuration, cues,
  queue policies, and independently selected URL layers and media-document links. Community Goal
  and Viewer-funded Bounty instances are reported as omitted because Community is not in format 1.
- `automations`: core flow definitions, graph layout, nodes, bindings, expressions, failure
  policies, aliases, positions, and edges.

References use deterministic export-local identifiers such as `reply-0001`; database primary keys
are not part of the format. Object properties and collection order are deterministic where the
source configuration has a stable order.

## Compatibility and limits

- The version 1 envelope and typed section records reject unknown properties and unknown enum
  values rather than dropping them. A known core Automation node can retain an invalid
  configuration object for repair in the destination editor.
- The explicit version 0 adapter migrates its top-level `channelLogin` into the version 1 `source`
  object. Other older or future versions are rejected.
- The upload limit is 2 MB. Every configuration collection is limited to 1,000 records.
- All export-local references, identifiers, canonical guessing slugs, and persistence limits are
  checked before persistence. Automation editor errors do not block a safely representable core
  flow.
- Overlay media links contain an immutable document ID, media metadata, and a channel-local name.
  They never contain media bytes, storage keys, paths, or generated browser URLs. Import succeeds
  only when that document is already available in the same BlokeBot instance.
- URL-layer export preserves the complete URL. The dashboard requires confirmation because query
  strings can contain access keys or other credentials.
- Imports never fetch dependencies named by the document.

## Import behavior

The destination is always the currently selected channel. The review step selects sections,
chooses add-missing, merge, or replace behavior per section, and resolves individual conflicts
before one atomic commit.

Guessing profiles match an explicit target mapping first and otherwise match canonical slug.
History-bound profiles are updated in place. Replace deletes only absent profiles without retained
rounds; an absent history-bound profile must be retained or the import is aborted. Overlay Cue
commands and Automation nodes resolve through the same export-local Overlay, cue, command, and
provider-reference plan. An unresolved Automation dependency becomes an identity-free placeholder.
The destination editor reports that node as invalid until the user selects a local dependency.

Overlay instances and cues receive destination identities; imported access keys, revisions,
timestamps, events, and live queues do not transfer. Existing destination records matched by the
normalized-name contract update in place. Replace retains referenced destination Overlay records
unless the review explicitly aborts.

Automation flows match by normalized name. A matched flow updates in place so its frozen runs and
history remain attached. Replace never deletes an absent flow that has runs; the review must retain
it or abort. Known core flows can transfer with invalid configuration, bindings, or graph layout
when the document remains safe to persist. Fixed non-null CEL Actor and Channel values become
explicit identity-free placeholders. A fixed nullable null stays null. In other invalid fixed
fields, any nested object with a `login` or `display-name` member becomes an identity-free
placeholder; member-name matching ignores letter case. Export and import write warning logs with
only the host, flow, node, and reason. Unknown and plugin-defined nodes are rejected in format 1.

Feature configuration can be imported while its Chat Tools switch remains off. Configuration does
not implicitly enable a feature. Explicitly selected enablement changes commit a durable activation
record; activation pending, complete, or failed state is reported separately from import success.
Activation repairs current subscriptions but does not replay work suppressed while disabled.

Weekly announcement recurrence is fixed UTC domain data. A channel's time zone is only the editor
and display projection: changing it can change the local weekday or time shown without changing the
stored recurrence or due instant. Announcements-only import does not change the destination time
zone, so the same imported UTC recurrence can display differently for different channels.

## Excluded data

Exports never include OAuth tokens, client secrets, application credentials, sessions, cookies,
server paths, deployment settings, point balances or ledgers, completed guessing rounds, votes,
leaderboards, giveaway entrants or draws, alerts, public-chat outbox data, delivery receipts,
viewer IDs, viewer logins, viewer display names, command viewer allow lists, stream runtime state,
Overlay events or playback queues, Automation runs, frozen contexts, checkpoints, delays, leases
or receipts, community data, Lua or plugin configuration, media bytes, or raw database IDs.
Community remains excluded. Plugin configuration and nodes first belong to a genuine format 2
after the v0.13 plugin platform exists.
