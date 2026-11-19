# QA-016 — final Step 1 independent verification, 2026-11-17

**Final independent disposition: QA READY for production `fbb38781faa29eb5cead9de3dcda0e760b63c7a1`.** Ordinary Windows execution and independent receipt validation confirm all applicable candidate, source and reference checks pass. No production changes or formal Owner acceptance are made by this assignment.

## Exact candidate

- Production: `fbb38781faa29eb5cead9de3dcda0e760b63c7a1`.
- Execution repository HEAD: `5475d8521c9d751f1393261dbfb8cd5558d8d251` (QA-only evidence publication after the production commit).
- Initial working tree: clean. Production, Developer regressions, Architect documents and SQLite remain unchanged throughout this assignment.
- `ledgerctl.dll` SHA-256: `3232c397f4a33b9e9132e2dafd7592589f56b59d17df53baa822a68f74ae3d7f`.
- `UserDimensions.Ledger.dll` SHA-256: `a71123aea438bf33c0fa142d357d4513614f53812d0b10169061040ad2d03798`.

Git source equivalence, clean Developer paths, exact copied binary hashes and frozen source snapshots are independently checked. Production binaries were supplied from the reviewed Release build, not rebuilt by QA or inferred from documentation commits. QA builds only the existing fault driver and byte-verification adapter.

## Execution and independent validation

| Run / group | PASS | FAIL | BLOCKED | NOT-RUN |
| --- | ---: | ---: | ---: | ---: |
| Codex preflight: original QA-015 candidate cases | 49 | 0 | 0 | 0 |
| Codex preflight: new fresh no-GUID runtime cases | 3 | 0 | 0 | 0 |
| Codex preflight: independent reference checks | 76 | 0 | 0 | 0 |
| Initial advisory source checker | 5 | 0 | 1 | 0 |
| Source-only follow-up after manual review/checker correction | 6 | 0 | 0 | 0 |
| Ordinary Windows: original QA-015 candidate cases | 49 | 0 | 0 | 0 |
| Ordinary Windows: fresh no-GUID runtime cases | 3 | 0 | 0 | 0 |
| **Ordinary Windows candidate total** | **52** | **0** | **0** | **0** |
| Ordinary Windows: source-order checks | 6 | 0 | 0 | 0 |
| Ordinary Windows: independent reference checks | 76 | 0 | 0 | 0 |

These are separate categories and runs, not added together as distinct coverage. Source-order evidence is not counted as a runtime cryptographic observation. The 49 reused cases plus three additional runtime cases total 52; the repeated preflight/confirmation runs are not double-counted.

Ordinary Windows evidence:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa016-20261117-204215-069dc0e9`.

The Owner's PowerShell transcript matches the completed run: **exit 0, QA READY**. Execution: 2026-11-17 20:42:15–20:43:23 America/Chicago (2026-11-18 02:42:15–02:43:23 UTC). Recorded environment: Windows NT `10.0.26300.0`, fixed NTFS, PowerShell `7.6.9`, .NET SDK `10.0.403`. Both QA adapter builds have zero warnings/errors. No antivirus settings were changed.

Preflight evidence:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa016-20261117-203347-335c1a87`.

Source-only follow-up:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa016-source-review-20261117-203711-f0345428`.

The initial run correctly exited 2 / review required: all 52 candidate cases passed but the new QA checker masked later declarations after encountering a C# verbatim Windows path literal. Manual inspection identified a **QA lexical limitation**, not an implementation defect. The original 5-PASS/1-BLOCKED source receipt and all original command receipts remain unchanged. The checker was corrected to recognize verbatim/character literals; a source-only follow-up against the exact same frozen production source passed all six checks. No candidate cases were rerun or assertions weakened for this correction.

Independent evidence validation verified **331 candidate command stdout/stderr receipts**, the original 49-case assertions, all frozen source/design/binary hashes, signed archive/restoration bytes, missing-history inventories and the healthy fresh publication. Preflight `QA016-receipt-validation.json` references the separate corrected source review and retains its native-NOT-RUN qualification. No result is attributed to a different candidate.

The later ordinary-Windows `QA016-receipt-validation.json` independently verifies its own **331 command receipts**, all 52 runtime cases, six source checks, 76 reference checks, exact reviewed binary hashes and source equivalence to `fbb3878`. All 49 original candidate cases pass unchanged, including both `MISSING-GENESIS-FILE` and `MISSING-BLK-DIRECTORY`. The final healthy no-GUID block is independently authenticated and its signature/hash/classification and exactly-one-new-file inventory are checked. Source and runtime evidence remain separate; no in-memory signing instrumentation is claimed.

## Former failures and affected regressions

Both `MISSING-GENESIS-FILE` and `MISSING-BLK-DIRECTORY` now pass the original QA-owned expectations: normal operations return `RECOVERY_REQUIRED`, exit 4, reason `LEDGER_HISTORY_INCOMPLETE`; diagnostics remain available with healthy original signing availability true, unavailable authorization false and `blocks:null`. Original identity, trusted ledger/genesis pin and unaffected bytes are preserved; raw and DDLB imports preserve the incomplete-history aggregate reason.

The unchanged regression also exercises ordinary and revocation archived retries (`BLOCK_ARCHIVED`), exact historical/current metadata and nonmutation, active/restored idempotency, duplicate pending/quarantined `.XBLK` imports including revocation, canonical/inverted DDLB and grant dependencies, truthful partial-import accounting, joins, damaged accepted-history restrictions and malformed-new-input precedence.

The three additional runtime cases use `block publish --event-file ...` **without** a block GUID:

1. Corrupted accepted history: `RECOVERY_REQUIRED` / 4 / `LEDGER_HISTORY_CORRUPT`, unchanged impaired inventory, no publication/temp/output.
2. Missing pinned genesis: `RECOVERY_REQUIRED` / 4 / `LEDGER_HISTORY_INCOMPLETE`, unchanged surviving history/identity/pin, no publication/temp/output.
3. Healthy bound instance: exactly one new correctly named durable `.BLK`, independently verified signature/full SHA-512/canonical authority, exact signed timestamp matching metadata, original files unchanged and truthful retained counts.

## Source-order proof and observation boundary

Manual review followed the actual source and helper calls, rather than requiring every caller to contain a direct `Inventory()` spelling:

| Path | Reviewed integrity-before-signing argument |
| --- | --- |
| Management ordinary publish → `Publish` → `PublishCore` | `PublishCore` obtains verified inventory unconditionally before the optional GUID retry branch, fresh UTC callback and `identity.CreateBlock`. No-GUID publication therefore reaches the same gate. |
| Explicit-GUID retry → `ExistingPublication` | Receives the already verified inventory from `PublishCore`; its other caller uses default-null inventory, which executes `Inventory()` before lookup. Active/archived/conflict paths do not freshly sign. |
| Revocation publish → `PublishConsent` | Default `ExistingPublication` verifies inventory before fresh consent-age UTC; fresh publication then passes through the same `PublishCore` gate. |
| Consent preparation → `CreateConsent` | Inventory verification and canonical authority evaluation precede `identity.Sign`. |
| Genesis creation → `InitializeLedger` | Inventory/prior-genesis checks precede the other `identity.CreateBlock` call site. |
| Signing primitives | `SigningIdentity.CreateBlock` → `BlockCodec.Create` → `Signing.Sign` → `Ed25519.Sign`; direct consent signing delegates through the same primitive. All two block-signing sites and the one consent-signing site in `LedgerInstance` are accounted for. |

Supporting source is frozen under `source-snapshot/`; `production-source-hashes.json` and `source-order-results.json` identify the exact files and reviewed graph. The validator compares every frozen source file with the production Git blobs, allowing only checkout newline normalization. `Management`, `LedgerInstance`, `Persistence` and `Protocol` were reviewed together. Low-level caller-seed cryptographic utilities are not ordinary ledger publication APIs.

The portable checker supports this specific manual call-graph review. An unfamiliar helper/layout requires re-review; the checker is not a general C# control-flow prover. **Runtime tests observe responses, exact bytes, counts and filesystem inventories. They do not instrument or directly observe in-memory signing.** The pre-signing guarantee is established by source order and helper provenance, with the runtime cases providing separate observable confirmation.

## Historical evidence and approved scope

QA-015's **47 PASS / 2 FAIL** on `c001fc4a0d50067e2b8c9a2685625bf43aeea464`, its reports and all QA-010–QA-015 historical records remain unchanged. Only QA-owned candidate-selection values were updated for this explicitly reviewed retest; original assertions are unchanged.

Carry forward the approved, separately evidenced dispositions without counting them as newly executed:

- FI-07/FI-15: **11 PASS** under Owner-approved observable replacement criteria; literal internal OS checkpoints **REPLACED / NOT EXECUTED**.
- FI09-LIVE-RESULT-SNAPSHOT: **NOT OBSERVED — OWNER-APPROVED EQUIVALENT EVIDENCE**.
- Automatic network/block repair remains Step 4 scope. No repair feature, SQL projection, later-phase work or physical power-loss certification is introduced here.
- Portable source/reference tools do not qualify Linux/macOS production storage; native acceptance evidence here is Windows-specific.

## Commands and handoff

Run the following in ordinary Windows PowerShell. It executes 49 unchanged regression cases, three fresh-publication cases, six source-order checks and 76 reference checks. It builds only QA adapters and damages/moves only fresh disposable test history under `.git/dev-003-work`, preserving originals. Production code, real instances/keys, installed software and antivirus settings are unchanged. Allow 1–2 minutes; expected exit 0.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA016.ps1' -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

Afterward, this command independently reviews saved evidence without rerunning candidates. Replace `EVIDENCE_PATH` with the printed absolute directory. It writes new validation receipts and refuses to overwrite prior validation receipts; it does not change original results or test-instance files.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe' -B 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\QA016_VALIDATE.py' --evidence 'EVIDENCE_PATH'
```

**Remaining defects: none reproduced in this assigned independent verification. QA015-D01 and the pre-signing recovery-gate finding are independently verified corrected on this exact candidate.** Historical failures remain unchanged for their original revisions. Carry-forward fault-test qualifications above retain their approved scope and are not new blocked cases.

Return this **QA READY** disposition, exact production revision, hashes, evidence and QA-only publication commit to Developer, Security and Project Manager **through the Project Owner**. Formal Step 1 acceptance belongs to the Owner's acceptance process and is not declared here. No Step 2 implementation has begun.
