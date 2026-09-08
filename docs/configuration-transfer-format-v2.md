# Channel configuration transfer format 2

BlokeBot exports channel configuration as UTF-8 JSON with the format identifier
`blokebot.channel-configuration` and schema version `2`. The dashboard can export one section or
a selected bundle and can import only selected sections from a bundle.

```json
{
  "format": "blokebot.channel-configuration",
  "version": 2,
  "exportedAtUtc": "2026-08-20T12:00:00Z",
  "source": {
    "channelLogin": "example_channel",
    "blokeBotVersion": "0.16.0"
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
- `channelToolEnablement`: one Boolean for each independent Chat Tools switch. Format 2 has 20
  switches and keeps Polls, Clips and Markers, Rewards and Redemptions, Predictions, and Raid
  Collaboration separate.
- `overlays`: portable core Browser Source instances, typed appearance and configuration, cues,
  queue policies, and independently selected URL layers and media-document links. Community Goal
  and Viewer-funded Bounty instances are reported as omitted because Community is not in format 2.
- `automations`: explicitly selected flows, their complete reachable current subflow definitions,
  graph layout, typed interfaces, bindings, expressions, failure policies, aliases and positions.
  Saved generated test scenarios can be selected independently for each selected flow.
  Each subflow has one export-local `id`, `description`, `interface` and `graph`. Invocation bindings
  contain `subflowId`, `interface` and `fixedInputsJson`; boundary bindings have a null `subflowId`.
  Revision IDs, revision numbers and history are not portable authoring fields.

References use deterministic export-local identifiers such as `reply-0001`; database primary keys
are not part of the format. Object properties and collection order are deterministic where the
source configuration has a stable order.

## Compatibility and limits

- Only version 2 is supported. Versions 0 and 1 receive an unsupported-version error. There is no
  conversion reader. Unknown properties and enum values are rejected.
- The upload limit is 2 MB; configuration collections remain limited to 1,000 records. Each
  automation graph is limited to 256 nodes and 1,024 edges; subflow closures are limited to 128
  subflows, eight levels and 1,024 visits. Each flow has at most 32 saved scenarios.
- Node schemas, graphs, caller interfaces, complete subflow dependencies, host references and
  installed plugin contracts are validated before staging. Invalid automations cannot be imported
  for later repair: fix them in the source application first.
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
provider-reference plan. An unresolved Automation dependency blocks the import. Select an available destination dependency
before importing.

Overlay instances and cues receive destination identities; imported access keys, revisions,
timestamps, events, and live queues do not transfer. Existing destination records matched by the
normalized-name contract update in place. Replace retains referenced destination Overlay records
unless the review explicitly aborts.

Automation flows match by normalized name. A matched flow updates in place so its frozen runs and
history remain attached. Replace never deletes an absent flow that has runs; the review must retain
it or abort. Imported graph, subflow and scenario identities are mapped deterministically within
this destination host and document. Referenced immutable revisions and admitted frozen runs are
not rewritten or removed. Subflows, callers, scenarios, feature changes and the import audit share
one transaction; any failure rolls back the complete import.

Saved calls reference a stable channel-local subflow identity. New production and isolated scenario
runs use its current definition; repeated and nested calls resolve consistently for that operation.
Compatible publications require no caller edit. An incompatible current interface blocks affected
callers until their interface and bindings are repaired. Already admitted runs keep their exact frozen
execution. Upgrading existing current calls converts their identities; where a current nested graph
needs conversion, the application appends a successor rather than rewriting an immutable snapshot.

Plugin nodes reference an already installed, available, compatible definition. The document contains
stable plugin code/definition identifiers only; destination lifecycle and feature generations are
resolved locally. No package is fetched or installed, and no plugin settings or secrets transfer.

Fixed values are checked using their declared types, sensitivity and provenance. Typed identifying
values and nonportable resolved inputs are rejected, not anonymized; these checks do not detect
secrets or real-world identities in arbitrary authored text constants. Explicitly selected test
scenarios must originate from the canonical generated-fixture recipe and still match it exactly.
The recipe carries source schema, virtual clock, seed, typed generated inputs and declared effect
outcomes, not a serialized event context. Custom or edited local scenarios remain available locally;
recreate them with generated inputs or deselect them before export. No arbitrary text, viewer/event
record or secret can be smuggled into a generated value parameter.

Disabled valid flows can be stored while host features are off. An enabled imported caller must
have every required feature of its complete closure enabled by the resulting import. Missing or
unavailable plugin contracts, invalid schemas and unsafe values always block import.

Feature configuration can be imported while its Chat Tools switch remains off. Configuration does
not implicitly enable a feature. Explicitly selected enablement changes commit a durable activation
record; activation pending, complete, or failed state is reported separately from import success.
Activation repairs current subscriptions but does not replay work suppressed while disabled.

Weekly announcement recurrence is fixed UTC domain data. A channel's time zone is only the editor
and display projection: changing it can change the local weekday or time shown without changing the
stored recurrence or due instant. Announcements-only import does not change the destination time
zone, so the same imported UTC recurrence can display differently for different channels.

## Excluded data

The exporter does not read private or operational state such as OAuth tokens, client secrets,
application credentials, sessions, cookies,
server paths, deployment settings, point balances or ledgers, completed guessing rounds, votes,
leaderboards, giveaway entrants or draws, alerts, public-chat outbox data, delivery receipts,
viewer IDs, viewer logins, viewer display names, command viewer allow lists, stream runtime state,
Overlay events or playback queues, Automation runs, frozen contexts, checkpoints, delays, leases
or receipts, traces, invocation contexts, simulator results or operational diagnostics, community data, Lua or plugin configuration, media bytes, or raw database IDs.
Community and plugin settings remain excluded. Plugin node contract references are the only plugin
metadata included.
