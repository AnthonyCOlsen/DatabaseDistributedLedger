# QA-013-CORRUPTION — independent execution report

2026-11-17. Candidate `8bc96f0f513faac57c1db23a8189288479d8a992`.
**Full ordinary-Windows run: 22 PASS, 0 FAIL, 2 BLOCKED/SPEC-PENDING, 0 NOT-RUN.**
**Independent reference: 12 PASS, 0 FAIL, 0 BLOCKED, 0 NOT-RUN.**
Focused sandbox preflight: 6 PASS, 0 FAIL, 2 BLOCKED, 16 NOT-RUN. Its results remain
separate from the completed ordinary-Windows run. No formal Step 1 acceptance.

## Execution and frozen candidate

- Full evidence, recorded context OrdinaryWindowsPowerShell:
  `C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa013-20261117-101908-0dbd780d`.
  Run 16:19:08–16:20:30 UTC (10:19:08–10:20:30 America/Chicago); exit **2**.
  Both QA builds: 0 warnings, 0 errors; the native dotnet --info probe exited 0.
  Full `QA013-receipt-validation.json` verifies 440 command receipts, eleven
  positive READY PID barriers and twenty-two exact deterministic mutations,
  preserved original signed references, diagnostic targets and candidate/source hashes.
- Focused evidence:
  `C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa013-20261117-101228-b0895114`.
- Context: CodexSandboxPreflight; 2026-11-17 16:12:28–16:13:07 UTC
  (10:12:28–10:13:07 America/Chicago). Recorded suite exit **2**, reflecting
  SPEC-PENDING coverage, not a candidate integrity failure.
- Windows build 10.0.26300.0, fixed local NTFS, PowerShell 7.6.9,
  .NET SDK 10.0.403/runtime 10.0.14, Python 3.12.14/cryptography 50.0.2.
  Antivirus settings unchanged; scan activity was not recorded by the operator.
- CLI SHA-256:
  `a7b9c642bdb3939d7a705692778457dab4c12ecc22bb18cb397f487046905cf8`.
- Ledger library SHA-256:
  `71b5fadbd96a1dfed90e2290c2f2c9b62484cb017645419456535bcd13d55aa3`.
- In the focused preflight both QA builds succeeded with **0 warnings, 0 errors**. The separate
  `dotnet --info` environment probe returned exit 1 because sandbox access to the
  Windows Service Control Manager was denied while inspecting workloads; its
  stdout still identifies the SDK/runtime. A first-run workload diagnostic also
  appears in build stderr. These logs are retained; candidate/parser execution and
  both builds succeeded. No workload update, installation or production build was
  performed. The subsequent ordinary-Windows environment collection succeeded.
- `QA013-receipt-validation.json` independently rechecks 120 command receipts,
  three positive READY PID barriers, six deterministic mutations, original
  signatures/diagnostic hashes and snapshot hashes without rerunning the candidate.

All preceding QA-010/011/012 results and original assertions remain intact. This
suite supplements on-disk corruption coverage; it does not rerun those suites or
reinterpret the historical OS-internal checkpoint dispositions.

### Concurrent documentation update after execution

After the native run finished, a separate documentation merge advanced repository
HEAD to `cae7fe3d09e49ee17162bcf56bfe19113f29e6e6`. Only the four root design/overview
documents differ from tested `8bc96f0`; their new portability and BLOCK_ARCHIVED
requirements were reviewed for impact. No accepted-corruption repair policy was
supplied. Source tree hashes are identical
(`aa671a5664071e1ea381ee69af9d62ff86528eb2`), as are SQLite trees and the actual two
candidate DLL SHA-256s. The archived-publication retry change is separate from these
corruption tests and is not newly qualified by QA-013.

A final protected-file comparison detected the four changed document hashes; this
was a concurrent-state/provenance observation, not a candidate test failure. The
original run/source/doc snapshots remain untouched. The separate merge was neither
performed nor reverted by this QA assignment. See
`QA013-concurrent-documentation-update.json` and `QA013-protected-file-validation.json`
in the full evidence folder. Results remain explicitly attributed to the executed
8bc96f0 candidate, not retrospectively relabeled as a run at the newer HEAD.

The Owner subsequently supplied a second execution attempt at
`.git/dev-003-work/qa013-20261117-102758-5e43bebd` (10:27:58 America/Chicago).
It exited 1 before reference, adapter build or candidate execution: the old launcher
default selected 8bc96f0 but actual HEAD was cae7fe3. Both suites are NOT-RUN in that
attempt. This is a stale candidate-selection guard, not a corruption-test failure;
its logs are preserved and do not replace the completed 22-PASS/12-reference result.
After verifying the documentation-only diff and unchanged source/binary hashes, the
current launcher's default was explicitly updated to reviewed cae7fe3. The HEAD guard
and every integrity assertion remain intact. No further test run is required solely
for this default-selection correction.

## Integrity results versus candidate observations

Focused patterns were truncate-1, flip-payload and append-8 at each of two locations.
The remaining eight patterns per location are NOT-RUN only in that focused receipt.
The subsequent full run executed all eleven patterns at both locations successfully.
See [QA013_COVERAGE.md](QA013_COVERAGE.md) for exact offsets, assertions and limits.

| Observation | Full native result | Qualification |
| --- | --- | --- |
| Original pre-rename temp complete/authentic, positive READY, worker killed before publication | 11 PASS | Existing production-disabled callback; no extra hook. Original signed bytes are saved separately before damage. |
| Corrupted surviving temp rejected by independent oracle and actual candidate parser/healthy receiver | 11 PASS | Temp is not promoted to BLK, export contains only valid prior history, retry is fenced; corrupted evidence and identity survive unchanged. |
| Acknowledged accepted BLK authentic before corruption; original SHA-512 filename retained afterward | 11 PASS | Independent Ed25519/payload/full-signed hashes, authority and actual durable acknowledgment verified. |
| Corrupted accepted bytes rejected; no corrupted authenticated get/export; unrelated valid file bytes/identity retained | 11 PASS | Candidate parser and healthy receiver reject; actual disk inventory/Snapshot is exercised. These observations share the same eleven accepted cases, not eleven additional tests. |
| Accepted-file required state, counters, diagnostic precedence and serving/gating policy | BLOCKED/SPEC-PENDING | Actual behavior recorded; no invented acceptance state/code. |
| Accepted-file explicit repair/re-fetch/restoration procedure | BLOCKED/SPEC-PENDING | No restore, deletion, replacement or re-signing attempted. |

Do not add table rows as independent counts: there are **twenty-two distinct executed
candidate cases**, each with multiple assertions, plus two blocked policy checks.
The twelve independent reference checks are separate from candidate execution.

Current full-run CLI/library behavior, recorded without prescribing recovery policy:

- Corrupt orphan temps: `status` still reports bound with two valid retained blocks;
  get for the unpublished target returns BLOCK_NOT_FOUND/4; full DDLB export contains
  exactly the two authenticated prior originals; unrelated get works. Snapshot and
  pinned recover-genesis succeed for existing valid history but do not repair/promote
  the corrupt temp. Exact-GUID publication retry returns RECOVERY_REQUIRED/4.
- Corrupt accepted block: status succeeds diagnostically with recovery_required,
  null block counters and signing unavailable/unauthorized. Across the full run,
  get (including original digest), full/selected export, unrelated get, Snapshot,
  recover-genesis and publication retry fail with BAD_FRAME/2, HASH_MISMATCH/2 or
  BAD_SIGNATURE/2, depending on the damaged field.
  No output containing corrupted history is published. The unrelated valid bytes
  and original key/manifest/trust binding remain unchanged on disk.
- Damaged input on a separate healthy receiver is rejected with BAD_FRAME/2,
  HASH_MISMATCH/2 or BAD_SIGNATURE/2 and **no receiver mutation**. Across both disk
  locations, direct candidate parser rejections were 14 BAD_FRAME, 4 HASH_MISMATCH
  and 4 BAD_SIGNATURE. This distinguishes direct corruption
  detection from failure caused only by a damaged local inventory.
- There is no standalone production block-verify CLI. `QA013_Verify` invokes the
  actual public BlockCodec.Parse on exact saved byte copies; Snapshot probes read
  the actual disk inventory. The independent Python verifier supplies expectations,
  not production parsing results. Original SHA-512 filenames do not bypass validation.

## Evidence preservation and safety boundary

Each case records an immutable separate ORIGINAL.BLK and DAMAGED.bin, mutation
offsets/masks/truncation/append lengths and both SHA-512s. Instance inventories are
captured before corruption, after corruption and after candidate operations, plus
before/at/after external termination for orphan tests. Per-command stdout/stderr,
arguments, exit codes, library parser receipts and full status/error JSON are saved.
Failed/rejected results and diagnostic corrupt targets are retained.

Only newly generated isolated nonproduction instances below this run's `instances/`
are deliberately corrupted. A path-boundary check precedes every mutation. No real
instance, shared fixture, candidate snapshot or repository source is damaged. All
candidate processes exit or are explicitly terminated before byte mutation.

This is deterministic simulated **file-content** damage. It neither exercises nor
certifies media failure, OS functionality, physical power-loss, device caches or
the original unobservable FI-07/FI-15 internal checkpoints.

## Reproduction of the completed ordinary-Windows run

The command below runs all 22 new candidate corruption cases and 12 independent
reference checks, building only QA adapters against frozen supplied binaries. It
creates fresh instances, test-only keys, damaged diagnostic files and logs under
`.git/dev-003-work`. It does not change production code, shared fixtures, antivirus
configuration or installed software. Expect approximately 1–3 minutes and exit 2
when only the two SPEC-PENDING policy checks remain blocked.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA013.ps1' -RunContext OrdinaryWindowsPowerShell -Selection All -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

That is the exact historical command used while HEAD was 8bc96f0, using the old
launcher frozen in that run's suite-snapshot. The current launcher defaults to the
reviewed docs-only cae7fe3 HEAD and still refuses any other HEAD. **No further run
is requested now.** To make candidate selection explicit for a future rerun,
append `-ExpectedCommit 'cae7fe3d09e49ee17162bcf56bfe19113f29e6e6'` and use
`-LedgerCtlDll 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa013-20261117-101908-0dbd780d\candidate-snapshot\ledgerctl.dll'`
to select these exact frozen binaries. A different production revision requires
explicit candidate review, not an automatic expected-commit override. The read-only
receipt validator likewise accepts an explicit `--expected-commit` and preserves
existing validation receipts rather than overwriting them.

Portable reference-only validation uses QA013_MUTATIONS.py, existing fixtures and a
fresh output JSON path; it does not execute the candidate or need .NET/Windows:

```text
python3 -B QA013_MUTATIONS.py --fixtures QA013_FIXTURES.json --results <fresh-output-json>
```

Byte mutations, fixtures, independent validation and the C# parsing probe are portable
in source. Current candidate publication requires Windows fixed NTFS. Process control
and filesystem-specific behavior are isolated through the existing QA adapter and
launcher gate. Linux/macOS candidate testing is NOT-RUN/unqualified. No platform
support is claimed merely from portable source.

## Deliverables, policy questions and recommendations

New QA-owned files: Invoke-QA013.ps1, QA013_RUN.py, QA013_MUTATIONS.py,
QA013_FIXTURES.json, QA013_Verify.cs/.csproj, QA013_VALIDATE.py, QA013_COVERAGE.md,
QA013_REPORT.md, QA013_RESULTS_20261117.json and QA013_TEST_PLAN_PROPOSAL.md.
QA010_COVERAGE.md receives only a historical-preserving addendum; old test programs,
assertions and expected results are unchanged.

[QA013_TEST_PLAN_PROPOSAL.md](QA013_TEST_PLAN_PROPOSAL.md) proposes coverage rows and
lists Architect questions: global versus local operation gating, status/counters/key
health, explicit trusted exact-byte restoration, preservation/quarantine of corrupt
evidence without misusing XBLK, interrupted-repair acknowledgment and pinned-genesis
damage. Root QA_TEST_PLAN.md is untouched pending QA-Tester review.

Recommended next steps: QA reviews the full ordinary-Windows evidence, obtains
Architect/Owner recovery-policy clarification, then have QA approve any plan changes
and specify separate repair acceptance tests. No production correction is proposed
from the integrity results; observed gating/diagnostic choices need a policy
decision before they can be called conforming or defective.

Accepted-genesis/XBLK corruption, concurrent external mutation, missing/renamed files
and exhaustive fuzzing are not covered. No Step 2–4 work is performed. Production,
Developer regressions, root QA_TEST_PLAN.md and SQLite baseline remain unchanged by
this assignment. Architect documents and Git history were not modified by this
assignment; the separately observed documentation merge is described above. No QA
commit or push was authorized or performed.
