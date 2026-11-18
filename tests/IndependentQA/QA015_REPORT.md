# QA-015 — executed Step 1 independent regression

**Disposition: NOT READY.** Two missing-genesis cases reproduce **QA015-D01** in ordinary Windows PowerShell and Codex preflight. The Owner's transcript matches the completed and independently validated native receipts. No production correction, formal acceptance or later-phase work is performed by this assignment.

## Candidate provenance

- Repository HEAD and observed GitHub `main`: `06af60d969fddab1045146ecaed7775320d6c49d`.
- Production revision: `c001fc4a0d50067e2b8c9a2685625bf43aeea464`.
- `ledgerctl.dll` SHA-256: `7ad53dd36931831df14bc8ecb82f9a53576d71c2fff28a0d22f085191c9c504b`.
- `UserDimensions.Ledger.dll` SHA-256: `9cbc07e403386ec87ed9a576666fc1c0a3bb3a1a40814e5789ae925477a50941`.
- Production source and SQLite match the reviewed baseline; Developer-owned paths are clean. Intervening commits changed design documents only. No production build was overwritten.

`Invoke-QA014.ps1` has only four candidate-selection value replacements: repository HEAD, production baseline and the two binary hashes. Its original byte/newline format is preserved. `QA014_RUN.py` and all 38 original assertions remain byte-identical to the committed version. The original launcher and SHA-256 manifest are retained in `.git/dev-003-work/qa015-preparation-20261117/` as well as existing historical run snapshots.

## Actual execution

Ordinary Windows evidence root: `C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa015-20261117-170913-72b92553`.

Separate preflight evidence root: `C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa015-20261117-170505-ab13ec54`.

| Context / executed group | PASS | FAIL | BLOCKED | NOT-RUN |
| --- | ---: | ---: | ---: | ---: |
| Codex preflight: unchanged QA-014 targeted suite | 38 | 0 | 0 | 0 |
| Codex preflight: archived duplicate import (pending, quarantined ordinary and revocation) | 3 | 0 | 0 | 0 |
| Codex preflight: missing genesis file / missing entire BLK directory | 0 | 2 | 0 | 0 |
| Codex preflight: three supplied-input / three accepted-history corruption cases | 6 | 0 | 0 | 0 |
| **Codex preflight candidate total** | **47** | **2** | **0** | **0** |
| Independent Protocol 1.03 reference checks | 76 | 0 | 0 | 0 |
| Ordinary Windows: unchanged QA-014 targeted suite | 38 | 0 | 0 | 0 |
| Ordinary Windows: archived duplicates | 3 | 0 | 0 | 0 |
| Ordinary Windows: missing pinned genesis / missing entire BLK directory | 0 | 2 | 0 | 0 |
| Ordinary Windows: representative supplied-input / accepted-history corruption | 6 | 0 | 0 | 0 |
| **Ordinary Windows candidate total** | **47** | **2** | **0** | **0** |

Both runs exit **1**, due solely to the two named missing-genesis cases. Both QA adapters build with zero warnings/errors in each run. All 313 candidate command stdout/stderr receipts per run, frozen source/design/binary hashes, six archived/restored signatures and classifications per run, and missing-history preservation inventories were independently verified. Validation receipt in each root: `QA015-receipt-validation.json`. The preflight receipt retains its historically correct native-NOT-RUN qualification; the later native receipt records all 49 executed cases. The confirmation rerun is not counted as extra distinct coverage.

Native environment: Windows NT `10.0.26300.0`, fixed local NTFS, PowerShell `7.6.9`, .NET SDK `10.0.403`. Native execution: 2026-11-17 17:09:13–17:10:24 America/Chicago (23:09:13–23:10:24 UTC). Exact production revision and DLL hashes match the reviewed values above. Original `QA014_RUN.py` bytes were compared directly with Git commit `5bbcd2d` during independent receipt validation.

Historical QA-010 through QA-014 results are preserved and are not added to these newly executed counts. The prior ordinary integrity follow-up (7 candidate / 4 reference PASS) is a separate receipt. All four formerly failing QA-014 assertions now pass on the reviewed candidate in both preflight and ordinary Windows.

## Archived duplicates and diagnostic precedence

Each duplicate case starts with a genuinely accepted signed `.BLK`, introduces independently verified earlier authority, and confirms its exact bytes move to `.XBLK`. Two exact reimports each return `duplicate:true`, `durable:true`, `retryable:false`, `durable_count:0`, `duplicate_count:1`, retaining the current pending/quarantined status, authorization and reason. Inclusive/disjoint counts, original signatures, archive filenames and instance inventories remain unchanged. Exact get still rejects archive access and full export excludes the archived original.

Representative truncation, payload-bit and signature-bit mutations establish all three diagnostics separately:

- Invalid newly supplied input: `BAD_FRAME`, `HASH_MISMATCH` or `BAD_SIGNATURE`, exit 2; also true when underlying history is already incomplete or corrupt.
- Corrupted previously accepted history: normal get/export/publication retry/valid import return `RECOVERY_REQUIRED`, exit 4, reason `LEDGER_HISTORY_CORRUPT`. Status remains available, signing availability stays true, authorization is false and aggregate blocks are null.
- Missing pinned genesis: ordinary operations fail safely with `RECOVERY_REQUIRED`, exit 4, but omit `LEDGER_HISTORY_INCOMPLETE`; status incorrectly reports the healthy original signing key unavailable. This is QA015-D01.

## QA015-D01 — missing-genesis diagnostics

Both `MISSING-GENESIS-FILE` and `MISSING-BLK-DIRECTORY` are fresh synthetic bound instances, with intact original key/manifest and known pinned genesis. Original test history is moved into preserved evidence, not deleted. The single-file scenario leaves another valid signed block in place.

Observed status subset (both scenarios):

```json
{"instance":{"state":"recovery_required","recovery_codes":["RECOVERY_REQUIRED"]},"signing":{"available":false,"authorized":false},"blocks":null}
```

Observed ordinary publish/get/export/countersign failure for the missing-file scenario:

```json
{"ok":false,"data":null,"error":{"code":"RECOVERY_REQUIRED","message":"The exact pinned signed genesis is missing; preserve the original identity and trust binding.","retryable":false,"details":{}}}
```

The missing-directory message is `Pinned signed genesis storage is missing.` Valid import returns an aggregate recovery failure with `details.reason="RECOVERY_REQUIRED"`, also incorrect. Expected: `details.reason="LEDGER_HISTORY_INCOMPLETE"`, healthy signing availability true, authorization unavailable/false, blocks null, diagnostic access preserved. Technical Design's accepted-history integrity clause and QA-015 §3 supply these expectations; no recovery or network repair feature is requested.

All five normal operations tested in each missing scenario are safely rejected. No output is created; identity, binding and unaffected bytes survive unchanged. The defect concerns classification/key-availability diagnostics, not observed unsafe acceptance or data loss. See `QA015_D01_DEVELOPER_HANDOFF.md` and the complete `additional/MISSING-*.contract.json` responses/inventories.

Relevant source: `LedgerInstance.cs:101` and `:127–128` throw recovery failures without typed history details; `Management.cs:266–268` treats the untyped failure as loss of signing availability. The import aggregate at `Management.cs:246` consequently has no incomplete-history reason to preserve.

## Reproduction and independent validation

The following runs all 49 candidate cases and 76 reference checks in fresh disposable instances. It builds only two QA adapters, moves/corrupts synthetic test history while preserving originals, and records commands, JSON, exits, stdout/stderr, snapshots and inventories under `.git/dev-003-work`. It does not alter production code, real instances, installed software or antivirus settings. Allow about 1–2 minutes; exit 1 is expected if QA015-D01 reproduces.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA015.ps1' -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

After execution, the following reviews the saved receipts without rerunning the candidate. Replace `EVIDENCE_PATH` with the printed absolute evidence directory. It writes one new validation receipt and refuses to overwrite an existing one; it changes no candidate files or test results.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe' -B 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\QA015_VALIDATE.py' --evidence 'EVIDENCE_PATH'
```

## Remaining disposition

Return QA015-D01 for minimal Developer correction and focused independent verification. Preserve malformed-new-input precedence, original identity/pin, all good bytes and status availability; no automatic repair is required. The separately reported pre-signing gate-order finding remains open and is not counted as a newly executed failing QA-015 case. FI-07/FI-15 observable replacement PASS, original checkpoints REPLACED/NOT EXECUTED, and FI09 live snapshot NOT OBSERVED with approved paired evidence remain unchanged. QA-Tester/Owner acceptance review remains required; Codex does not declare Step 1 accepted.
