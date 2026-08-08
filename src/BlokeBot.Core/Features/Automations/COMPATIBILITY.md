# Automation catalog compatibility

Persisted nodes store the catalog definition ID, its integer schema version, and configuration JSON.
They never store a CLR type name. Definition, port, and configuration-field IDs are durable: do not
rename or reuse them for a different meaning.

A schema version may stay unchanged for display-only corrections and additive metadata that does not
invalidate saved configurations or edges. Removing or renaming a field or port, changing a value
type, changing validation so a previously valid value becomes invalid, or changing the meaning of a
saved value requires a new schema version and an explicit upgrade.

`Current` is the only version saved or executed. `OldestReadable` says how far back the definition's
decoder can read for an upgrade. A version between those bounds must be upgraded, decoded, and
validated before it can be saved or executed. Versions older than `OldestReadable` and versions newer
than `Current` are rejected without guessing. Adding support for a new current version requires the
catalog's supported-version constant to advance with upgrade tests.

Both save and pre-execution paths use the catalog decoder and the same typed validator. Pre-execution
validation is required after an upgrade and is not replaced by earlier save-time validation.

Fields and ports marked `Sensitive` are excluded from overlay, log, and script-facing projections by
default. Actions also declare retry safety separately from their functional capabilities. A runtime
must never infer retry safety from an action such as chat or overlay delivery.
