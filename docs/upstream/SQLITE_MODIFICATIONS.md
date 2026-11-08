# UserDimensions SQLite modifications

Administrative maintenance record for the permanent downstream SQLite fork.
Original project documentation is MIT-licensed; upstream notices remain intact.

## Initial baseline

SQLite 3.54.1 was imported pristine on 2026-11-07.
Baseline commit: `cc0dd4aa2103947dfc019d8e8103c298906ccf36`.
Annotated tag: `sqlite/upstream-3.54.1`.

**No ledger-specific modifications have yet been made.** No custom signature,
.DLDB behavior, .BLK storage, parser/pager/B-tree/WAL/VFS changes or feature
omissions are part of the baseline. The root Git attributes rule preserves
bytes and does not change the engine. Provenance and baseline verification
are recorded in [SQLITE_PROVENANCE.md](SQLITE_PROVENANCE.md).

## Required future entry format

For each significant downstream modification, record:

- Date, assignment or issue identifier, and responsible reviewer.
- Purpose and rationale, affected upstream files and components.
- Full downstream Git commit identifiers.
- Native API, file format, SQL behavior, portability and compatibility effects.
- Durability, security, performance and upstream-porting considerations.
- Regression test names/commands, configuration and actual results; identify
  failures, skips and limitations rather than claiming unperformed tests pass.
- Related upstream fix-log entry, if applicable.

Keep modifications focused and avoid unrelated reformatting. Edit canonical
source rather than hand-editing generated amalgamations. Preserve the original
baseline and notices. Follow project ownership and approval rules; this record
does not authorize architecture changes or additional implementation work.

## Modification entries

None at import.
