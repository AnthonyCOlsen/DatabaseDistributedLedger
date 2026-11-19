# Repository layout and tooling

Owner-authorized organization change, 2026-11-19. Production source organization,
Protocol 1.03, SQLite and acceptance expectations are unchanged. The first commit
contains only 100% Git renames; the next contains path/tooling/index updates.

```text
src/                         managed production projects (unchanged)
tests/UserDimensions.Ledger.Tests/   Developer regressions
tests/IndependentQA/
  runners/                   PowerShell historical suite entry points
  suites/                    Python suites, independent reference and validation modules
    legacy-dev003/           eight unchanged originals, ZIP dependency and manifest
  fixtures/                  current public nonproduction protocol/corruption fixtures
  adapters/                  separate FaultDriver, BlockVerifier and Equivalence projects
docs/
  architecture/              three authoritative design documents
  development/               current Developer implementation and tooling guides
  diagrams/                  editable sources and matching image exports
  qa/                        plan, active plans/, reports/qaNNN/, compact evidence/
  archive/                   historical Developer/QA records
  upstream/                  SQLite provenance and maintenance
scripts/                     build, shared path discovery, combined tests
third_party/sqlite/          maintained, modifiable fork; upstream baseline tracked in Git
artifacts/                   ignored, reproducible builds and tool caches
local/qa/                    ignored, retained test receipts and disposable instances
local-notes/                 ignored personal notes
```

Python suite/reference modules stay together so their existing sibling imports
remain simple. This organization does not require extra runtime packages or change
independent calculations. New QA tools belong in the relevant existing category.
Do not put production implementation into the test directories.

## Ownership and history

Architect owns normative design contracts; QA owns fixtures, assertions and
acceptance reports; Developer owns production, regressions and tooling. Both roles
may run both suites. Common execution does not equate Developer success with QA
acceptance. No new Step 2 implementation is authorized by this reorganization.

Frozen QA reports/results, published evidence archives, and the original DEV-003
bundle are preserved byte-for-byte. Original dated commands and paths remain
historical. Use `git log --follow` on a moved file to inspect its earlier history.
Active Markdown links and tooling resolve the new paths.

## Build and test

The build command compiles Release into `artifacts/build` and uses caches under
`artifacts/cache`; it does not run a real ledger instance. Adding `-Test` executes
all Developer regressions with fresh instances under `local/qa`.

```powershell
pwsh -NoProfile -File ./scripts/build.ps1 -Test
```

The combined command builds/tests the Developer suite, compiles QA adapters and
runs current independent tests, reference checks and source-order checks. It
preserves the two suites' results separately. Synthetic test blocks/keys are
created, corrupted or interrupted only inside new disposable instances. Existing
receipts and antivirus settings are unchanged.

```powershell
pwsh -NoProfile -File ./scripts/test.ps1 -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\path\to\python.exe'
```

Use `CodexSandboxPreflight` only for agent preflights; it does not establish an
ordinary-Windows qualification. Original unavailable OS-internal FI-07/FI-15
checkpoints remain replaced/not executed; their observable replacements run.
FI09's literal live-result snapshot remains not observed with approved paired
evidence. Older missing-hook/policy declarations are reported separately as
superseded or out of Step 1 scope, never converted into new PASS results.
Historical launchers retain exact candidate guards; they are not a command to
run every old acceptance campaign against arbitrary new HEADs.

`Repository.Paths.ps1` finds the solution marker rather than assuming `.git` is a
directory. It resolves inputs across the new folders and understands the current
artifact path, with a legacy build-output fallback for explicit historical runs.
The solution, shared build properties and production projects remain in place.

See [CONTRIBUTING.md](../../CONTRIBUTING.md) for coding, review and attribution
standards. Its SQLite fork policy permits justified downstream changes while
preserving the tagged import baseline and applicable notices.

## Local output and evidence policy

`artifacts/` is reproducible output/cache and may be recreated. `local/qa/` contains
retained receipts and synthetic instances; it is not a cache-cleaning target.
Neither is published to GitHub. Include important receipts in the Owner's backup
plan; Git does not back up ignored data. Committed reports keep original evidence
paths and hashes, not generated instance trees or signing keys.

Existing `.git/dev-003-work` evidence, caches and registered worktrees remain in
place. Do not manually relocate or delete those registered worktrees. Their old
absolute receipts are still valid. Future ordinary test outputs use `local/qa`;
future build outputs use `artifacts`. `local-notes/` is now a shared ignore rule in
addition to the original local exclude. The `.vs/` exclusion remains intact.

See the [validation report](REPOSITORY_LAYOUT_VALIDATION_20261119.md) for actual
test results and the [shared handoff](REPOSITORY_LAYOUT_HANDOFF.md) for a prompt
the Owner can forward to the other project chats.
