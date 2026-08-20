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
    "channelToolEnablement": {}
  }
}
```

## Section contracts

- `customCommands`: time zone, reusable replies, counters, commands, aliases, cooldowns,
  invocation limits, permissions, and reply routes.
- `announcements`: reusable replies, scheduled chat messages, and Twitch announcements.
- `guessing`: profiles, canonical slugs, accepted answers, aliases, rewards, and reply text.
- `points`: terminology, aliases, reply text, gambling rules, and giveaway rules.
- `channelToolEnablement`: one Boolean for each independent Chat Tools switch. Format 1 has 20
  switches and keeps Polls, Clips and Markers, Rewards and Redemptions, Predictions, and Raid
  Collaboration separate.

References use deterministic export-local identifiers such as `reply-0001`; database primary keys
are not part of the format. Object properties and collection order are deterministic where the
source configuration has a stable order.

## Compatibility and limits

- Version 1 rejects unknown properties and unknown enum values rather than dropping them.
- The explicit version 0 adapter migrates its top-level `channelLogin` into the version 1 `source`
  object. Other older or future versions are rejected.
- The upload limit is 2 MB. Every configuration collection is limited to 1,000 records.
- All export-local references, identifiers, canonical guessing slugs, and editor validation rules
  are checked before persistence.
- Imports never resolve URLs or fetch dependencies named by the document.

## Import behavior

The destination is always the currently selected channel. The review step selects sections,
chooses add-missing, merge, or replace behavior per section, and resolves individual conflicts
before one atomic commit.

Guessing profiles match an explicit target mapping first and otherwise match canonical slug.
History-bound profiles are updated in place. Replace deletes only absent profiles without retained
rounds; an absent history-bound profile must be retained or the import is aborted. Automation and
Overlay Cue commands require a whole-command skip-or-abort decision because those feature schemas
are not part of format 1.

Feature configuration can be imported while its Chat Tools switch remains off. Configuration does
not implicitly enable a feature. Explicitly selected enablement changes commit a durable activation
record; activation pending, complete, or failed state is reported separately from import success.
Activation repairs current subscriptions but does not replay work suppressed while disabled.

## Excluded data

Exports never include OAuth tokens, client secrets, application credentials, sessions, cookies,
server paths, deployment settings, point balances or ledgers, completed guessing rounds, votes,
leaderboards, giveaway entrants or draws, alerts, public-chat outbox data, delivery receipts,
stream runtime state, Automation or Overlay schemas, community runtime data, Lua configuration, or
raw database IDs.
