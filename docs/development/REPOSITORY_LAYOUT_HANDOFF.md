# Shared handoff — repository layout update

FROM: Project Owner / Codex\
TO: Project Manager, Architect, Developer, QA and Security chats\
SUBJECT: Completed repository organization and validated tooling

The Owner-authorized repository cleanup is complete on `main`. The first commit,
`73a86f0c41a65299891396f504f054602b1f1ef6`, contains 88 unchanged-content Git
renames; the following commit updates references, navigation and tooling. The
[move map](FILE_MOVES.json) lists every old/new path. Use `git log --follow` to
inspect individual file history.

Use these locations for future work:

| Location | Purpose / owner |
| --- | --- |
| `docs/architecture/` | Three authoritative design documents; Architect |
| `docs/qa/QA_TEST_PLAN.md` | Current QA plan; QA |
| `docs/qa/plans/` | Active QA design/acceptance planning |
| `docs/qa/reports/qa010/` through `qa017/` | Preserved historical reports and compact results |
| `docs/qa/evidence/` | Selected public evidence archives/manifests |
| `docs/development/` | Current implementation, maintenance and tooling guides |
| `docs/archive/development/`, `docs/archive/qa/` | Historical handoffs and superseded records |
| `docs/diagrams/` | Editable `.drawio` sources with matching PNG exports; existing SVG retained |
| `tests/IndependentQA/runners/` | PowerShell launchers |
| `tests/IndependentQA/suites/` | Python suites, reference checks and receipt validators |
| `tests/IndependentQA/fixtures/` | Public synthetic fixtures |
| `tests/IndependentQA/adapters/` | FaultDriver, BlockVerifier and Equivalence QA projects |
| `tests/IndependentQA/suites/legacy-dev003/` | All eight original QA bundle files plus manifest, kept together unchanged |
| `tests/UserDimensions.Ledger.Tests/` | Developer regressions, separate ownership with shared execution |
| `artifacts/` | Ignored, reproducible build outputs and caches |
| `local/qa/` | Ignored, retained test receipts and disposable instances; include important evidence in backups |
| `local-notes/` | Ignored personal notes |

The canonical filenames remain
`Database_Distributed_Ledger_Technical_Design.md`,
`Distributed_Ledger_Management_API_Specification.md` and
`SQLite_Architecture_and_Implementation_Plan.md`, now under `docs/architecture/`.
[docs/README.md](../README.md) identifies navigation and ownership.

Both Developer and QA may run each other's suites. Ownership remains separate so
acceptance expectations cannot be silently changed by implementation work.
Historical reports, assertions, results and the original bundle are preserved.
Historical embedded commands refer to their original revisions; use the current
entry point below for current regression testing. Older launchers retain exact
candidate guards and must not be indiscriminately retargeted.

The following command builds Release and runs Developer regressions, using fresh
test instances under `local/qa` and build/cache output under `artifacts`:

```powershell
pwsh -NoProfile -File ./scripts/build.ps1 -Test
```

The following command additionally runs the current independent QA suites,
reference checks and source checks, compiling QA adapters and preserving command
receipts. It corrupts or interrupts only fresh synthetic instances. Supply an
existing Python with `cryptography`/`lzma`; it installs nothing and does not change
real instances or antivirus settings. Use ordinary Windows PowerShell outside
Codex for Windows filesystem qualification.

```powershell
pwsh -NoProfile -File ./scripts/test.ps1 -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\path\to\python.exe'
```

Validation on 2026-11-19 passed: Release zero warnings/errors; 87 Developer tests;
203 independent candidate cases; 76 protocol, 12 corruption and 4 integrity
reference checks; 6 source checks; original-bundle reference verification; and
three QA adapter builds. See the [validation report](REPOSITORY_LAYOUT_VALIDATION_20261119.md)
for tested hashes, scope, retained initial harness-error evidence and exact paths.
Approved FI-07/FI-15 and FI09 dispositions remain unchanged.

Production source, solution/project layout, Protocol 1.03 and pristine SQLite are
unchanged. Accepted baseline `fbb38781faa29eb5cead9de3dcda0e760b63c7a1` and
approved maintenance successor `83cbcb5ec7933d1d83cb61a9712353c169ba7788`
remain identifiable. Existing `.git/dev-003-work` evidence and registered
worktrees were preserved; use the new ignored locations for future outputs.
Ignored evidence is not backed up by Git and must not be treated as disposable
cache. No new Step 1 acceptance decision or Step 2 implementation is authorized.
