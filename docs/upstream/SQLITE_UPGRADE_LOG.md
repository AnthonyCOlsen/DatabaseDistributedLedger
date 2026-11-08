# SQLite upstream fix review and port log

Administrative maintenance record for the permanent UserDimensions SQLite fork.
Original project documentation is MIT-licensed; upstream notices remain intact.

## Permanent maintenance policy

Our SQLite fork is permanently maintained independently. Future official
SQLite releases do not automatically replace our source. We manually evaluate
and selectively port upstream security and correctness fixes into our modified
codebase. Automatic upstream merging and wholesale replacement are prohibited
without a new Project Owner decision. Preserve downstream changes and the
pristine `sqlite/upstream-3.54.1` baseline.

Every adopted port must document the upstream Fossil check-in, dependencies,
manual adaptation, testing and resulting downstream Git commit. An upstream
Git mirror identifier may be added, but does not replace Fossil provenance.
Follow upstream advisories and release/check-in records; assess prerequisite
changes and interacting fixes before porting. This policy requires active
review and does not establish an automatic update service.

## Required review entry format

- Date and review/assignment identifier.
- Upstream release, bug, advisory or fix identifier and official URL.
- Full upstream Fossil check-in identifier(s), including prerequisites.
- Applicability to our baseline, compile options and downstream changes.
- Decision: **adopt**, **defer** or **reject**, with rationale and any follow-up.
- Manually applied changes and affected files; explain differences from upstream.
- Tests/configurations actually run, results and remaining limitations.
- Full associated downstream Git commit identifiers, or none for unapplied work.
- Related entry in SQLITE_MODIFICATIONS.md and reviewer.

## Initial baseline

SQLite 3.54.1, released 2026-10-16; imported 2026-11-07.
Fossil: `555e07ef33f69a0957d24d8e555b702464a131d6f1607f4c3c1e4551e8e6ca76`.
Downstream baseline: `cc0dd4aa2103947dfc019d8e8103c298906ccf36`.
Tag: `sqlite/upstream-3.54.1`.
See [SQLITE_PROVENANCE.md](SQLITE_PROVENANCE.md) for archive verification,
licensing, extraction and baseline build evidence.

## Fix review entries

None. No post-baseline upstream fix has been ported by this import assignment.
