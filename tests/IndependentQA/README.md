# Independent Step 1 QA — Protocol 1.03

This directory versions independent QA programs, public NONPRODUCTION fixtures,
coverage and historical result summaries. It is separate from Developer regression
tests. A committed report is a record of the named candidate, not a declaration of
formal Step 1 acceptance or of support on an untested platform.

## Suite guide

| Suite | Entry point / report | Purpose and current recorded result |
| --- | --- | --- |
| QA-010 | Invoke-QA010.ps1; QA010_README.md; QA010_FINDINGS.md | Broad independent Protocol 1.03 checks on 6328bbc: reference 76 PASS; candidate 130 PASS, 1 FAIL, 4 BLOCKED. Preserve that historical failure; later work corrects it. |
| QA-011 | Invoke-QA011.ps1; QA011_REPORT.md | Focused verification on 8bc96f0: 10 PASS, 0 FAIL, 3 BLOCKED, including the corrected DDLB join error and actual batch callbacks. |
| QA-012 | Invoke-QA012.ps1; QA012_REPORT.md; QA012_PORTABILITY.md | Observable publication/configuration qualification: 11 PASS, 0 FAIL. Original inside-OS checkpoints NOT EXECUTED. Includes a read-only portability assessment. |
| QA-013 | Invoke-QA013.ps1; QA013_REPORT.md; QA013_COVERAGE.md | File-content corruption: reference 12 PASS; Windows candidate 22 PASS, 0 FAIL, 2 policy checks BLOCKED. This is not physical-storage/power-loss certification. |
| QA-014 | Invoke-QA014.ps1; QA014_REPORT.md; root QA_TEST_PLAN.md | Final affected Step 1 regression: reference 76 PASS; Windows candidate 34 PASS, 4 FAIL, 0 BLOCKED, 0 NOT-RUN. Two current implementation defects remain. Owner-authorized plan consolidation preserves historical counts. |

The eight originals in ORIGINALS.manifest.json, Invoke-IndependentQA.ps1 and the
20261110 integration/evidence files are **historical Protocol 1.01** artifacts.
Their existing bytes are preserved. Do not apply their old expectations to 1.03.

## Running a reviewed candidate

Use existing PowerShell 7, .NET 10 and Python 3 with cryptography/lzma. Launchers
install nothing, build only QA adapters against supplied frozen binaries and create
fresh excluded run directories. Use ordinary Windows PowerShell outside Codex for
Windows filesystem/process acceptance; Codex runs must be labeled sandbox preflights.
See each report for the exact command and environment used in its completed run.

The launchers deliberately pin historical reviewed commits. **A new QA-only commit
also changes HEAD**, so a current checkout may require explicit `-ExpectedCommit`
selection after reviewing its changes. Do not remove the guard or automatically
assume an arbitrary new HEAD is an equivalent production candidate. `-LedgerCtlDll`
selects existing candidate binaries; actual DLL hashes are separate from the source
commit identity. No rebuild, rerun or new qualification is implied by publishing
this QA checkpoint.

QA-012's candidate orchestrator deliberately validates/carries forward the original
ordinary-Windows QA-011 receipt at
`.git/dev-003-work/qa011-20261117-073333-c359b240`. **That local evidence is not included
in Git**, and a fresh clone cannot run that orchestrator unchanged without restoring
the receipt plus matching frozen candidate binaries. QA012_RUN.py accepts an explicit
`--qa011-evidence` path. Do not fabricate the prerequisite or substitute summary JSON
as the full receipt. A general fresh-clone carry-forward interface is follow-up QA
harness work; the existing qualified runner and its provenance are retained here.
QA-010/011/013 candidate runs do not require that historical QA-011 receipt.

The independent Python reference programs can run without Windows or .NET. Their
source portability does not qualify Linux/macOS production filesystem backends.
No Linux/macOS support or formal acceptance is claimed by this directory.

## Tracked records versus local outputs

- Track runners, QA adapter sources/project files, immutable reference fixtures,
  coverage, findings, portability reviews and dated per-case result summaries.
- Keep generated candidate copies, builds, test instances/keys, complete command
  logs, barrier receipts and diagnostic corrupt bytes in `.git/dev-003-work/`.
  Git does not track its own .git directory. Preserve these local records separately
  if full forensic/recovery evidence must survive loss of the workstation; the
  committed summaries contain pointers/hashes, not all underlying runtime evidence.
- Python caches, build outputs and the extracted ARCH-014 download folder are
  ignored. Existing historical tracked QA ZIP dependencies remain tracked; no blanket
  ZIP exclusion is applied.
- Fixture seeds are explicitly public NONPRODUCTION identities used for reproducible
  tests. They must never be used for real ledgers. Live/generated identity, key and
  configuration files are excluded from Git.

QA013_TEST_PLAN_PROPOSAL.md preserves the original QA-013 proposal. The Owner later
authorized consolidation through QA-014 into the root QA_TEST_PLAN.md, including
current integrity phase boundaries and approved fault-evidence dispositions. The
complete prior plan is archived as QA_TEST_PLAN_PRE_QA014_20261109.md. Earlier blocked
or failed outcomes are retained; updated acceptance dispositions are recorded
separately. QA-Tester, coordinated through PM/Owner, retains formal acceptance authority.

## QA-015 historical evidence publication — 2026-11-17

QA015_REPORT.md, QA015_D01_DEVELOPER_HANDOFF.md and
QA014_INTEGRITY_CONFORMANCE.md preserve results against production
`c001fc4a0d50067e2b8c9a2685625bf43aeea464`, at repository/document HEAD
`06af60d969fddab1045146ecaed7775320d6c49d`. QA-015's ordinary Windows result remains
**47 PASS, 2 FAIL, 0 BLOCKED, 0 NOT-RUN; 76 reference PASS**, exit 1. The two failures
are the missing-pinned-genesis scenarios recorded as QA015-D01. The earlier
source-order finding is also historical; it is not a new result on a later build.

Subsequent Developer commits `3e33f5e0bafb1e092aaf4ffcea06dc4c13879368`
(pre-signing gate) and `fbb38781faa29eb5cead9de3dcda0e760b63c7a1`
(missing-genesis diagnostics) supersede that production candidate. Their Developer
test success does not rewrite QA-015's failures or constitute independent QA
acceptance. **Step 1 remains NOT READY pending independent retest.**

The published QA-014/015 launchers intentionally retain the historical source and
binary selection guards. They refuse the newer production tree/binaries. Historical
execution requires the selected source revision and matching candidate artifacts;
changing only `-ExpectedCommit` does not approve a different production baseline.
Preserve guards and assertions when preparing a separately reviewed retest.

QA015_VALIDATE.py validates saved receipts without candidate re-execution and
requires the complete local evidence at its recorded paths. Full command receipts,
candidate snapshots, test instances and synthetic keys remain under
`.git/dev-003-work/`; they are deliberately excluded from this Git publication.
The committed reports and result summary point to those local records. A fresh
clone does not contain the full evidence or frozen historical binaries.

QA-016's optional portable source-order checker and focused instructions are a
separate QA work item. No QA-016 artifact was present in this checkout when this
publication was prepared, and no QA-016 result is claimed here. The three historical
reports, original assertions/fixtures/results and approved FI-07/FI-15/FI09
dispositions are preserved unchanged.
