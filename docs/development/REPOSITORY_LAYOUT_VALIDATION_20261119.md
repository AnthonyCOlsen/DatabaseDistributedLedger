# Repository layout validation — 2026-11-19

The Owner-authorized reorganization passed ordinary Windows regression testing.
This is repository-maintenance evidence, not a new Step 1 acceptance decision.
The [layout guide](REPOSITORY_LAYOUT.md), [move map](FILE_MOVES.json) and
[shared handoff](REPOSITORY_LAYOUT_HANDOFF.md) describe the resulting conventions.

## Revisions and scope

- Migration base: `2b948c3faa3a730aada1b0fe27153eefb203fcd9`.
- Move-only commit: `73a86f0c41a65299891396f504f054602b1f1ef6`.
  All 88 moves were performed with `git mv`, verified as R100 renames with
  identical Git blobs and file bytes, and committed before reference edits.
- The following commit updates paths, navigation, ignore rules and test tooling.
  Both commits are intended for publication together after validation.
- Accepted production baseline remains
  `fbb38781faa29eb5cead9de3dcda0e760b63c7a1`; the Owner-approved maintenance
  successor remains `83cbcb5ec7933d1d83cb61a9712353c169ba7788`.
- Production `src` tree: `f7766464788ac4991a02ea0fc3bb981f57a4d00f`.
- Pristine SQLite tree: `9e69364cf105a1d38e1d17ee66290967f640f78d`.

Production classes, projects, solution and shared build properties were not
reorganized. The Developer test helper changes only repository discovery and the
scratch location. Historical QA launchers retain their candidate guards. The new
combined runner invokes unchanged existing test bodies with current path discovery
and records superseded declarations separately. It does not rewrite assertions or
historical outcomes.

## Executed Windows results

The Owner executed `scripts/test.ps1` in ordinary Windows PowerShell on
2026-11-19, 08:17:38–08:21:51 America/Chicago. Git HEAD was the move-only commit;
the path/tooling changes were present in the working tree. The runner froze its
28 suite/fixture/adapter inputs and 11 binary/support files and recorded their
SHA-256 values. Those inputs were checked against the final working files.

| Check | Result |
| --- | --- |
| Release solution build | PASS; zero warnings, zero errors |
| Developer regressions | 87 PASS, 0 FAIL |
| Independent candidate cases | 203 PASS, 0 FAIL, 0 BLOCKED, 0 NOT-RUN |
| Protocol reference checks | 76 PASS |
| Corruption reference checks | 12 PASS |
| Integrity reference checks | 4 PASS |
| Pre-signing source checks | 6 PASS |
| Original DEV-003 offline verifier | PASS; reference validation only |
| Three QA adapter builds | PASS; zero warnings, zero errors |

The 203 candidate cases comprise 131 broad functional/fault cases, 10 batch
callback cases, 11 observable durability cases, 22 corruption cases, 8 archived
contract cases, 11 duplicate/missing-genesis cases, 7 integrity diagnostic cases
and 3 fresh-publication cases. Reference checks are counted separately.

Original inaccessible FI-07/FI-15 checkpoints remain replaced/not executed;
their observable replacement tests were executed. FI09's literal live-result
snapshot remains not observed with the approved paired evidence. Superseded
missing-hook declarations and Step 4 repair policy are recorded separately,
without counting them as new PASS results. The original bundle's archived
Developer test log is not evidence of newly executed Developer tests.

## Provenance and receipts

Combined evidence, retained locally and ignored by Git:

```text
C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\local\qa\layout-20261119-081738-be87e6a7
```

`summary.json`, `developer.log` and `independent/run-status.json` preserve execution
context and exit codes. The independent directory contains per-case results,
frozen inputs, exact commands, stdout/stderr and inventories. A separate read-only
audit verified all 1,222 process receipts, including 2,360 recorded stream hashes;
receipts without hash fields retain their output files. Expected rejection and
terminated-worker exit codes remain in their original receipts.

Tested binary SHA-256 values:

| Binary | SHA-256 |
| --- | --- |
| `ledgerctl.dll` | `5875a869618e13a9c3c45b8522b822f0b714805701d9ce586423a843c701b2f5` |
| `UserDimensions.Ledger.dll` | `0a22548b96599b8553392f20c8ff9f7752d88c3d6a73d1a81f8a2f73001fc0d3` |

These hashes identify the tested build, not a future rebuild after committing
documentation. .NET embeds commit/build identity, so later byte hashes may differ
without executable changes. The independently rebuilt equivalence tool compared
the preserved maintenance build with the new artifact-path build: 951 library
method definitions (950 IL methods plus one native import) and 2 CLI IL methods;
no executable or API/metadata differences. MVID, debug records and informational
version were recorded separately. This is not a formal proof of equivalence.

Additional retained evidence:

```text
local/qa/layout-equivalence-20261119-075504/
local/qa/layout-20261119-075016-020e4634/
local/qa/layout-durability-recheck-20261119-075559/
.git/dev-003-work/layout-migration-20261119-072802-f2ecd6f1/native-validation.json
```

The first sandbox preflight passed 87 Developer and 192 candidate cases but hit a
new-wrapper dependency-path error before the 11 durability cases could run.
Its failing receipt is preserved. The wrapper was corrected to validate this
run's completed QA-011 callback evidence against the same candidate and context,
instead of requiring an unrelated historical QA-011 directory. A focused recheck
passed all 11 cases, followed by the successful complete ordinary Windows run.
Original QA-012 assertions and historical receipts were not changed.

## Preservation and limits

Static checks verify 45 moved historical/bundle/evidence files byte-for-byte,
both current fixture files and all 17 existing diagram assets unchanged. All eight
Mermaid diagram bodies remain unchanged. Active Markdown links, Python syntax,
PowerShell syntax, ignore rules and `git diff --check` are checked before commit.
Authoritative document edits are relative links and equivalent Markdown hard
breaks only; no contract prose changes.

Frozen reports retain historical paths and commands. Their indexes explain the
current locations; the move map and original Git revisions support historical
navigation. Existing `.git/dev-003-work` receipts and registered worktrees remain
in place. No generated instance, synthetic key or cache is committed.

The complete current managed Developer/independent suites were exercised.
SQLite's upstream suites were not repeated because its source and configuration
were untouched. No Linux/macOS qualification, physical power-loss certification,
new acceptance disposition or Step 2 implementation is implied.

## Reproduction

From the repository root, this command builds Release, runs Developer and current
independent QA separately, compiles QA adapters and retains fresh synthetic test
instances/receipts under ignored `local/qa`. Builds and caches use ignored
`artifacts`. Supply an existing Python with `cryptography` and `lzma`; nothing is
installed. Run outside Codex for ordinary Windows filesystem qualification.

```powershell
pwsh -NoProfile -File ./scripts/test.ps1 -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\path\to\python.exe'
```
