# QA-011 — focused independent verification

Candidate Git HEAD: `8bc96f0f513faac57c1db23a8189288479d8a992`.
QA-only changes; no production, Developer regression, architecture, SQLite or
`QA_TEST_PLAN.md` changes. Nothing is committed or pushed by this assignment.

## Execution status

| Execution | Independent reference | Focused candidate | Exit | Scope |
| --- | --- | --- | ---: | --- |
| Codex sandbox preflight | 76 PASS / 0 FAIL | 10 PASS / 0 FAIL / 3 BLOCKED | 2 | Diagnostic, not ordinary Windows acceptance |
| Owner-run ordinary Windows | 76 PASS / 0 FAIL | 10 PASS / 0 FAIL / 3 BLOCKED | 2 | Owner-reported outside Codex; receipts and recovery evidence verified |

Sandbox evidence:

```text
.git/dev-003-work/qa011-20261117-071609-9ecf227d/
```

Windows NT 10.0.26300.0, fixed NTFS, PowerShell 7.6.9. QA adapter Release build:
zero warnings/errors. Actual candidate hashes:

| Artifact | SHA-256 |
| --- | --- |
| `ledgerctl.dll` | `a7b9c642bdb3939d7a705692778457dab4c12ecc22bb18cb397f487046905cf8` |
| `UserDimensions.Ledger.dll` | `71b5fadbd96a1dfed90e2290c2f2c9b62484cb017645419456535bcd13d55aa3` |

Candidate binaries and QA sources/fixtures are copied into immutable run snapshots.
The adapter is built only in excluded run space against the frozen candidate.
No Developer build output is overwritten. Historical QA evidence remains intact.

### Owner-run ordinary Windows receipt

```text
.git/dev-003-work/qa011-20261117-073333-c359b240/
```

The Owner ran the supplied single-line command outside Codex. The receipt records
`OrdinaryWindowsPowerShell`, candidate HEAD `8bc96f0f513faac57c1db23a8189288479d8a992`,
start `2026-11-17T13:33:33Z`, finish `2026-11-17T13:33:47Z` (07:33 America/Chicago),
and exit 2 for blocked coverage. Reference and candidate/library hashes match the
preflight. The QA adapter built with zero warnings/errors. All 13 named case
outcomes match the preflight: 10 PASS, zero FAIL, three BLOCKED.

The native evidence was inspected beyond summary counts: frozen source and binary
SHA-256 manifests; unchanged original join assertions and identical rejected-input
inventories; completed actual import-result flags; callback sequence `[1,1,0]`
versus canonical ordinals `[0,0,1]`; READY PIDs and external termination receipts;
grant-only inventories during and immediately after termination; and unchanged
original/recovered signed hashes. `QA011-receipt-validation.json` records this review.
The ordinary Windows execution handoff is complete for these cases. No new failures
or additional native-only limitations were observed. This remains independent QA
evidence for review, not formal acceptance or physical power-loss proof.

## Focused results and evidence

| Case | Preflight and Owner-run Windows | What is established |
| --- | --- | --- |
| `JOIN-NEG-ddlb` | PASS | Existing unchanged BAD_FRAME/exit-2 assertion, exact before/after file and directory inventory equality |
| `JOIN-01` | PASS | Existing raw-genesis join, distinct replica identity, restart binding and idempotent recovery assertions |
| `FI09-READY-RELEASE-RESULT` | PASS | Positive READY/RELEASE; after completion, actual returned grant/dependent results are durable=true, duplicate=false; original indices, hashes and counts match independent fixtures |
| `FI-09` | PASS for storage/crash/recovery | Actual Management/DDLB item-completed barrier at input index 1 / canonical ordinal 0; exact final grant, absent dependent/temp; external termination; restart/reimport accounting |
| `FI-09-GRANT` | PASS for storage/crash/recovery | Actual Management/DDLB grant-durable barrier at input index 1 / canonical ordinal 0; identical independent checks, kill/restart/reimport |
| `CALLBACK-DUPLICATE` | PASS | No additional grant-durable callback for duplicate grant; item-completed still fires; actual new-durable count 0 and duplicate count 2 |
| `CALLBACK-REJECTED` | PASS | Parsed rejects/duplicates can emit item-completed without a new publication; actual results and exact on-disk bytes distinguish them; malformed noncandidate has no callback |
| `CALLBACK-THROW-batch-grant-durable` | PASS | Injected observer exception preserves one durable grant, stops dependent writes, reports truthful PARTIAL_IMPORT and recovers without double-counting |
| `CALLBACK-THROW-batch-item-completed` | PASS | Same guarantees for interruption after item completion |
| `CALLBACK-NORMAL-RUNTIME` | PASS | Normal three-argument API has no injected callbacks; actual CLI contract matches; ordinary CLI rejects a fault-control flag without mutation |
| `FI-07` | BLOCKED | No artificial callback inside MoveFileExW |
| `FI-15` | BLOCKED | No artificial callback inside MoveFileExW |
| `FI09-LIVE-RESULT-SNAPSHOT` | BLOCKED limitation | The implemented Action<string> callback has no live import-result object; see the evidence distinction below |

The original `negative_join` and `joined` test bodies/assertions were compared as
Python ASTs with the previous Owner-run QA-010 snapshot and are unchanged. The QA-010
dispatcher gained optional focused-case selection only; default full-suite assertions
and fixtures are not weakened. The new runner selects the existing two join cases.
Broader independent regressions are explicitly NOT-RUN in this focused assignment.

Each callback receipt records PID, operation, exact name, observed checkpoint history,
original DDLB input index, canonical ordinal and actual full signed-block SHA-512.
At READY, Python independently verifies the grant signature/event/authority and exact
final BLK path/bytes, and checks that neither a dependent nor incoming temp exists.
The QA parent then RELEASES or terminates its own worker. All polling waits for an
explicit marker with a deadline; no fixed delay is used as proof of durability.

For the inverted batch `[dependent, grant]`, the expected successful sequence is:

```text
batch-grant-durable:1:<independent-grant-sha512>      canonical ordinal 0
batch-item-completed:1:<independent-grant-sha512>     canonical ordinal 0
batch-item-completed:0:<independent-dependent-sha512> canonical ordinal 1
```

Terminated trials return no fabricated success JSON. Restart first finds genesis
and the exact unchanged grant only. Reimport reports one duplicate grant and one
new dependent; repeating reports zero new / two duplicates. Original and recovered
signed hashes are recorded in `FI-09*.recovery-hashes.json`.

## Live-result visibility limitation

The callbacks expose a string containing index/hash, not the in-flight private
ImportResult object. The final response is written only after the batch completes.
Therefore an actual per-item `durable`/`duplicate` result cannot be read **inside
the terminated worker's READY callback** through the implemented interface.

The suite deliberately separates:

1. A completed READY/RELEASE trial, verifying the real returned new-durable result
   flags, exact callback sequence, indices and counts.
2. Independent filesystem/signature/authority checks while the worker is held at
   READY, plus external termination and restart/reimport evidence.

READY receipts explicitly say `current_import_result_exposed=false`. No callback
name alone is interpreted as proof of durability. Duplicates and rejected candidates
are tested specifically to demonstrate the distinction.

`FI09-LIVE-RESULT-SNAPSHOT` remains BLOCKED for the literal live-result requirement.
QA should determine whether the paired completed-result and READY filesystem evidence
is sufficient, or request a separately authorized read-only result observation API.
This assignment does not add any production interface or claim that unavailable
fields were observed. FI-09/FI-09-GRANT PASS rows cover the executed storage/crash/recovery
checks, not this unavailable live-result sub-observation.

## FI-07 / FI-15 qualification recommendation

Preserve the existing before/after filesystem tests and WRITE_THROUGH procedure.
Developer/Security should review their evidence and approve either an explicitly
documented alternate qualification/disposition or an isolated OS/storage fault-injection
plan capable of testing the required internal persistence boundary. Until then both
cases remain BLOCKED. A post-return callback must not be relabeled as a pre-directory-
confirmation checkpoint. Process termination does not establish physical power-loss
durability. No artificial callbacks, native persistence edits or acceptance waivers
are introduced here.

## Recommendation on broader independent regression

After focused ordinary Windows evidence is reviewed, run the affected independent
normal-CLI batch subset: canonical/inverted ordering, failed grant, mixed partial
imports, duplicate accounting, malformed/truncated DDLB, memory limits, authorization
and archive reconsideration, plus join negatives. These exercise the shared Management
path changed by the Developer. Developer's 71 passing regressions do not replace this
independent follow-up. A native SQLite baseline rerun is not indicated by these managed
changes alone; SQLite was not modified. QA decides and authorizes broader scope.

## Files and commands

New QA-owned files: `Invoke-QA011.ps1`, `QA011_RUN.py`, this report and
`QA011_RESULTS_20261117.json`. Updated QA-owned files: `QA010_FaultDriver.cs`
(batch Management.Run adapter, exact trace/receipts, injected observer exceptions)
and `QA010_RUN.py` (focused dispatch only). Existing QA assertions/fixtures, original
eight-file manifest and historical evidence are preserved.

Run the following **single line** from ordinary Windows PowerShell outside Codex:

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA011.ps1' -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

Expected exit is 2 for blocked coverage when there are no observed failures. Exit 1
indicates a failure. Explicit Python avoids ordinary-terminal PATH discovery problems.
No software is installed and antivirus settings are not changed. Each run refuses
reused evidence directories and records operator-reported execution context.
Both requested runs are now complete. No identical rerun is needed absent a relevant
candidate/harness change, additional approved observation interface, or QA direction.

The printed evidence directory contains `run-status.json`, `candidate-results.json`,
`reference-results.json`, `cases.jsonl`, `coverage-results.csv`, full command stdout/
stderr/process receipts under `commands/`, callback trace/READY files under `outputs/`,
and before/barrier/after-driver inventories and independent hash receipts at its root.

**Formal Step 1 acceptance is not declared.**
