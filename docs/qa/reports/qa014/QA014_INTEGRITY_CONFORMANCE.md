# Corrupted `.BLK` diagnostic conformance review — 2026-11-17

**Observable diagnostic contract: PASS in focused ordinary Windows PowerShell verification and Codex preflight. Overall source conformance: FAIL for the pre-signing gate ordering described below.** No production correction has been applied under this investigation. Formal Step 1 acceptance remains pending.

## Candidate and authoritative contract

Repository: `C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger`.

- Repository/document HEAD: `06af60d969fddab1045146ecaed7775320d6c49d`.
- Production revision: `c001fc4a0d50067e2b8c9a2685625bf43aeea464`.
- The intervening GitHub commits changed the four design documents only. Source provenance and binary hashes were checked; documentation commits are not treated as production rebuilds.
- `ledgerctl.dll` SHA-256: `7ad53dd36931831df14bc8ecb82f9a53576d71c2fff28a0d22f085191c9c504b`.
- `UserDimensions.Ledger.dll` SHA-256: `9cbc07e403386ec87ed9a576666fc1c0a3bb3a1a40814e5789ae925477a50941`.

All four latest local design documents were reviewed. Management API §§13.5 and 13.7 distinguish invalid incoming bytes and direct inspection from impaired accepted history. Section 13.7 also requires the recovery gate before signing new blocks once history corruption is detected. No architectural decision is needed to select the documented error codes.

## Historical QA-013 observations classified

Preserved ordinary-Windows evidence:
`.git/dev-003-work/qa013-20261117-101908-0dbd780d/`.
Its production revision was `8bc96f0f513faac57c1db23a8189288479d8a992`, preceding the correction in `c001fc4`. Its CLI/library SHA-256 values were respectively `a7b9c642bdb3939d7a705692778457dab4c12ecc22bb18cb397f487046905cf8` and `71b5fadbd96a1dfed90e2290c2f2c9b62484cb017645419456535bcd13d55aa3`.

| QA-013 evidence | Observed result | Assessment against current contract |
| --- | --- | --- |
| `CORRUPT-ACCEPTED-truncate-1.candidate-parse.json` | Direct `BlockCodec.Parse`: `BAD_FRAME`, exit 2 | Appropriate direct-byte diagnostic |
| `CORRUPT-ACCEPTED-flip-payload.candidate-parse.json` | Direct parser: `HASH_MISMATCH`, exit 2 | Appropriate direct-byte diagnostic |
| `CORRUPT-ACCEPTED-flip-signature.candidate-parse.json` | Direct parser: `BAD_SIGNATURE`, exit 2 | Appropriate direct-byte diagnostic |
| The corresponding `*.observations.json`, normal `block get/export/publish` after status had detected corruption | Same verifier codes, exit 2 | Historical ordinary-operation mismatch; not merely direct inspection |
| Those historical status responses | Exit 0; `recovery_required`; `blocks:null`; healthy key reported unavailable, with verifier recovery code | Historical key/history diagnostic distinction was incomplete |

Thus the old evidence includes **both contexts**. It would be incorrect to dismiss every QA-013 exit-2 observation as direct validation. The old corruption tests recorded preservation/detection under their then-pending recovery policy; their historical PASS counts have not been rewritten. QA-014 later made the ordinary-operation mismatch an explicit failure (`INTEGRITY-STATE-CONTRACT` in `.git/dev-003-work/qa014-20261117-132749-e11315c1/`).

## Focused current-candidate observations

New preflight evidence:
`.git/dev-003-work/qa014-integrity-20261117-161024-fd461ddf/`.

**7 candidate PASS, 0 FAIL, 0 BLOCKED, 0 NOT-RUN; 4 independent reference PASS; exit 0.** QA byte-verification adapter build: zero warnings/errors. Existing production binaries were copied and hash-verified, not rebuilt. Environment: Windows NT `10.0.26300.0`, fixed NTFS, PowerShell `7.6.9`, .NET SDK `10.0.403`. This run is explicitly `CodexSandboxPreflight`, not ordinary-Windows filesystem acceptance.

Owner-run ordinary Windows confirmation:
`.git/dev-003-work/qa014-integrity-20261117-161439-2ce90d05/`.

**7 candidate PASS, 0 FAIL, 0 BLOCKED, 0 NOT-RUN; 4 independent reference PASS; exit 0.** Adapter build: zero warnings/errors. Exact candidate revision and binary hashes match those above. Execution took approximately 20 seconds. The Owner supplied the ordinary PowerShell transcript; the saved run records the same execution context. All 52 candidate command stdout/stderr hashes, source/design/binary snapshots, recovery responses and preserved-instance inventories were independently checked afterward; see `followup-evidence-audit.json`. These are a separate confirmation run, not additional distinct coverage to add to the preflight's case count.

Three mutation classes were checked separately: one-byte truncation, payload-bit corruption and signature-bit corruption. Each has two candidate cases: rejected supplied input in a healthy replica, and corruption of a block first successfully published and independently authenticated as durable accepted history. A seventh case damages only a synthetic test signing-key file to establish the key/history diagnostic distinction.

| Context | Current observation |
| --- | --- |
| Direct candidate parser, including the same bytes copied from damaged accepted history | Corresponding `BAD_FRAME` / `HASH_MISMATCH` / `BAD_SIGNATURE`, exit 2 |
| Invalid new import into healthy history | Corresponding verifier error, exit 2; instance unchanged and healthy |
| Normal explicit-GUID publication retry, get, complete export, valid incoming import using damaged accepted history | `RECOVERY_REQUIRED`, exit 4; `error.details.reason="LEDGER_HISTORY_CORRUPT"`; no instance mutation or output file |
| Invalid new import while history is restricted | Its own verifier error, exit 2; not relabeled history corruption |
| `status` and `instance status` with accepted-history corruption | Exit 0; `recovery_required`; `LEDGER_HISTORY_CORRUPT`; healthy key remains available; authorization false; aggregate `blocks:null` |
| Test-only signing-key corruption control | Diagnostics exit 0; signing unavailable; `IDENTITY_MISMATCH`; ordinary export returns `RECOVERY_REQUIRED` with that distinct reason |

For example, normal get/export after payload corruption produces the following envelope (only the absolute test-file path is shortened here; original full JSON is in command receipts):

```json
{"ok":false,"data":null,"error":{"code":"RECOVERY_REQUIRED","message":"Retained signed history failed verification (HASH_MISMATCH).","retryable":false,"details":{"reason":"LEDGER_HISTORY_CORRUPT","path":"<isolated-test-instance>/BLK/...BLK"}}}
```

The same damaged bytes passed directly to the QA byte-inspection adapter produce:

```json
{"accepted":false,"code":"HASH_MISMATCH","message":"Transmitted payload hash mismatch."}
```

Valid incoming import into impaired history also preserves the aggregate reason `LEDGER_HISTORY_CORRUPT`; its rejected item reports `RECOVERY_REQUIRED`, `durable:false`, authorization `not_evaluated`. Identity/manifest/key bytes, pinned genesis, unaffected signed blocks and directory inventories are unchanged by restricted operations. Corrupt bytes remain available locally as diagnostic evidence and are never exported. No repair is attempted. Recovery state is re-established by verification when each process opens/uses history; this report does not claim a newly persisted recovery-state file.

## Remaining source-order discrepancy: fresh publication signs before gate

`src/UserDimensions.Ledger/LedgerInstance.cs:319–325`, `PublishCore`, performs:

1. `Bound()` / size checks.
2. `ExistingPublication(...)` **only if an explicit GUID is present**.
3. `identity.CreateBlock(...)` at line 323.
4. `Inventory()` at line 325, which discovers corruption and throws the recovery error.

For ordinary publication without `--block-guid`, step 3 signs in memory before the accepted-history gate. `SigningIdentity.CreateBlock` in `Persistence.cs:160` calls `BlockCodec.Create`; `Protocol.cs:217` actually invokes `Signing.Sign`. `Bound()` checks config/binding, and `Open()` checks identity; neither scans history. A prior diagnostic invocation does not change this order in a subsequent CLI process.

Supplemental preflight evidence:
`.git/dev-003-work/qa014-gate-order-review-20261117-161242-b8c52821/results.json`.
Three fresh-publication calls against the already diagnosed damaged test instances all returned the correct recovery error and left inventories unchanged. These **3 additional observable checks PASS** are separate from the seven-case suite. They do not observe internal signing; the ordering finding is established by source inspection. The receipt's initial section-label shorthand refers to the gate requirement now located in **Management API §13.7**.

This is a narrow source-order nonconformance with §13.7's requirement to gate before signing new blocks. No signed output, durable publication, identity replacement or filesystem mutation was observed. It must not be described as an observed publication/data-loss defect or as one of the old four QA-014 failures.

**Required action:** Developer/Owner review and a minimal separately authorized correction moving the accepted-history integrity gate before fresh ordinary signing, preserving incoming-input diagnostics and all retry/authorization behavior. Add a Developer regression covering the no-explicit-GUID branch and gate ordering. No permanent recovery flag, protocol change, automatic repair or new production test hook is requested. Production code has not been changed here.

## Relevant production locations

| File/line | Responsibility |
| --- | --- |
| `src/UserDimensions.Ledger/Protocol.cs:219` | Low-level incoming-byte parser; retains wire validation diagnostics |
| `src/UserDimensions.Ledger/LedgerInstance.cs:96` | `Inventory`: accepted-history boundary remaps verification faults and filename mismatches to typed recovery failure |
| `src/UserDimensions.Ledger/LedgerInstance.cs:172` | `Snapshot` / retained authorization replay uses verified inventory |
| `src/UserDimensions.Ledger/LedgerInstance.cs:319` | Fresh publication gate-order exception described above |
| `src/UserDimensions.Ledger/LedgerInstance.cs:335` | Revocation consent authority preparation scans history before signing |
| `src/UserDimensions.Ledger/LedgerInstance.cs:369` and `:381` | Get/export scan history before writing output |
| `src/UserDimensions.Ledger/Management.cs:131` and `:246` | Incoming validation and preservation of typed corruption reason in import aggregate response |
| `src/UserDimensions.Ledger/Management.cs:250` | Diagnostic status retains healthy-key availability and suppresses unreliable authorization/counts |

## Reproduction and handoff

From ordinary PowerShell, the following runs only the seven focused candidate cases and four reference checks against the selected existing binaries. It builds only the QA adapter, creates fresh disposable instances/evidence, and damages only their synthetic test files. Original copies are preserved; production files, real keys, installed software and antivirus settings are unchanged. Typical runtime: about 25 seconds; expected exit: 0 for these observable checks.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA014IntegrityReview.ps1' -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

The launcher pins the reviewed repository HEAD, checks production equivalence to `c001fc4`, rejects dirty Developer source, validates binary hashes, and saves source/design/binary snapshots and complete command receipts. A future documentation/QA commit requires an explicit `-ExpectedCommit` selection after provenance review; a changed production baseline or binary requires a reviewed launcher update. Do not bypass these guards to make an old test receipt look current.

Ordinary-Windows confirmation of the seven-case suite is **COMPLETE: PASS**. The original observable diagnostic discrepancy is not reproduced on current binaries. The pre-signing ordering finding remains open for Developer disposition and is a Step 1 conformance blocker unless the Owner/Architect approves a different interpretation. This investigation does not rerun archived-publication acceptance, the full corruption corpus or unrelated suites; their existing receipts and pending acceptance decisions remain separate. No Linux/macOS qualification, physical power-loss qualification, automatic recovery or formal Step 1 acceptance is claimed.
