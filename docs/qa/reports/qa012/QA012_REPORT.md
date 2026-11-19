# QA-012 — Observable durability qualification and portability review

2026-11-17. Candidate `8bc96f0f513faac57c1db23a8189288479d8a992`.
**Owner-run ordinary Windows PowerShell: 11 PASS, 0 FAIL, 0 BLOCKED; exit 0.**
These counts cover only the eleven new observable software cases below. Original
FI-07/FI-15 OS-internal checkpoints are **NOT EXECUTED**, not counted as PASS.
Physical power-loss durability and formal Step 1 acceptance are **not declared**.

## Candidate, environment and evidence

- `ledgerctl.dll` SHA-256: `a7b9c642bdb3939d7a705692778457dab4c12ecc22bb18cb397f487046905cf8`.
- `UserDimensions.Ledger.dll` SHA-256: `71b5fadbd96a1dfed90e2290c2f2c9b62484cb017645419456535bcd13d55aa3`.
- Windows 11 build `10.0.26300.0`, local fixed NTFS; PowerShell 7.6.9;
  .NET SDK 10.0.403 / runtime 10.0.14, win-x64. Antivirus settings unchanged.
- Native evidence: `C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa012-20261117-082002-d2581f93`.
  Run 14:20:02–14:20:16 UTC. Adapter build: 0 warnings, 0 errors.
- Corrected sandbox preflight: `.git/dev-003-work/qa012-20261117-081305-6d4377b0`:
  same eleven cases PASS, exit 0; explicitly not ordinary-Windows acceptance evidence.
- Initial harness-error evidence retained unchanged:
  `.git/dev-003-work/qa012-20261117-081048-1479bca4` (1 PASS, 2 FAIL, 8 BLOCKED).
  The newly added QA adapter operation mistakenly selected the non-injected
  Management overload, so barriers/throw checkpoints were not armed. This was
  reported before correction. Only QA dispatch was fixed; production, expected
  results and existing assertions were not changed. The final frozen snapshots
  identify the corrected adapter used for both successful runs.

Each evidence folder preserves source/binary hashes, adapter build stdout/stderr,
environment and Git receipts, per-command arguments/exit/stdout/stderr, positive
READY markers, RELEASE files, callback traces and before/at/after worker inventories.
`*.independent-ready.json` authenticates publication bytes and authority independently;
`*.config-ready.json` records complete old/new configuration and original binding.
Post-run `QA012-receipt-validation.json` verifies all 58 command receipts, ten
positive PID barriers, candidate/source snapshot hashes, four signed publication
proofs, four configuration proofs and the unchanged protected working tree.
`portability-source-audit.json` records the read-only source assessment.

## Observed software cases

All cases below passed in ordinary Windows PowerShell. Parent/worker coordination
uses positive READY/RELEASE receipts with bounded deadlines, not timing sleeps.

| Case | Observed assertions |
| --- | --- |
| FI07-SW-BEFORE-KILL | Complete authentic flushed temp, absent final path and no acknowledgment before rename. Kill preserves temp and accepted history. Ordinary uncertain retry fails truthfully; explicit import of the exact temp bytes recovers, then idempotent retry returns the original block. |
| FI07-SW-AFTER-KILL | After successful OS-call return, exact final signed bytes exist with no new temp or acknowledgment. Kill/restart/retry preserves those bytes and counters. |
| FI07-SW-AFTER-RELEASE | No acknowledgment at post-call READY; after RELEASE, success reports the matching durable hash and final path. Retry makes no instance mutation. |
| FI07-SW-HELD-SOURCE | A separate QA worker exclusively holds the flushed temp at pre-rename READY. Actual Windows rename fails with IO_ERROR/6 without false success; bytes survive and exact recovery succeeds after release. |
| FI07-SW-NO-OVERWRITE | Duplicate accepted genesis remains unchanged and counts as duplicate only. Existing export output returns OUTPUT_EXISTS/4 and preserves contents/history. This is basic no-overwrite coverage, not a new exhaustive race campaign. |
| FI07-SW-POST-CALL-ERROR | Injected QA observer exception after rename yields IO_ERROR/6, never false success; already published exact bytes survive and retry counts the duplicate truthfully. This is an observer error, not a simulated disk flush failure. |
| FI15-SW-BEFORE-KILL | Old active config and new replacement temp are both complete valid images. Kill before replacement leaves old config; restart reads it and retry installs the exact new image. |
| FI15-SW-AFTER-KILL | Kill after successful replacement call leaves complete new config; restart/retry retains it. |
| FI15-SW-AFTER-RELEASE | No acknowledgment at post-call READY; RELEASE allows truthful success for the complete new config. |
| FI15-SW-HELD-TARGET | Separate worker exclusively holds old config. Actual Windows replacement fails with IO_ERROR/6; complete old config survives, and retry succeeds after release. |
| FI15-SW-POST-CALL-ERROR | Post-replacement observer exception yields IO_ERROR/6 although complete new config exists. Restart reports actual new settings, with no fabricated success acknowledgment. |

Configuration cases verify strict complete JSON, matching instance GUID and the
unchanged identity, pinned genesis binding, keys and prior signed-block inventory.
No checkpoints were inserted within `MoveFileExW`; production durability operations
were not changed. Before-call termination demonstrates a recoverable software state,
not that an unacknowledged temp is an accepted durable block.

## QA-011 retained without duplicate execution

Prior ordinary-Windows evidence:
`.git/dev-003-work/qa011-20261117-073333-c359b240`.
QA-012 verifies the same commit and both candidate DLL hashes before carrying forward
JOIN-01, JOIN-NEG-ddlb, FI09-READY-RELEASE-RESULT, FI-09 and FI-09-GRANT PASS receipts.
The prior 76 reference PASS results and 10 focused candidate PASS results are retained
as historical results, not added to QA-012's eleven-case count. Reference is correctly
reported NOT-RUN in QA-012.

The QA-011 literal live-import-result-at-READY limitation remains: the injected
`Action<string>` exposes index/hash, not the private in-flight result object.
Completed READY/RELEASE results and independently authenticated killed-worker storage
evidence remain separate. No live-result claim is fabricated. QA must disposition
whether this evidence is sufficient; no additional production hook is authorized here.

## Portability and OS qualification

See [QA012_PORTABILITY.md](QA012_PORTABILITY.md) for source locations, exceptions,
per-platform remediation proposals and official OS/VFS references. The SQLite vendor
tree is byte-for-byte unchanged from the preserved upstream tag. No protocol change
or permanent Windows restriction is necessary from the reviewed core source.

Current Windows storage uses file WriteThrough/Flush(true) and same-volume
MoveFileExW WRITE_THROUGH, with REPLACE_EXISTING for configuration. The exact scope
of directory/storage durability and cache/device guarantees requires Developer/Security
review. Observable software PASS does not independently prove those OS/hardware
guarantees. The original inside-OS FI-07/FI-15 points remain NOT EXECUTED.

Main proposed separate Developer tasks are a replaceable durable-store backend,
centralized capability preflight, identity-lease abstraction and filesystem-aware
path policy. Portable Python reference fixtures remain separate from Windows sharing/
filesystem tests. Linux, macOS, other operating systems and additional CPU targets
were not tested and are not claimed supported. The QA-012 reference-only launcher
removes Windows/.NET candidate gates; its non-Windows execution remains unqualified.

## Reproduction and next action

This command builds only the QA adapter against a frozen candidate snapshot and runs
the eleven new cases in fresh test instances under `.git/dev-003-work`. It writes logs,
test-only keys/files and evidence there; it neither installs software nor changes
production files or antivirus settings. It validates earlier QA-011 receipts without
rerunning those cases. Use ordinary PowerShell outside Codex for Windows qualification.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA012.ps1' -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

No additional rerun is requested now: the native result above is complete. Recommend
QA review the alternative FI-07/FI-15 qualification and retained FI-09 evidence,
Developer/Security review the Windows durability guarantees, and the PM assign the
bounded portability work separately. Broader independent regression selection remains
with QA; there is no new production change in QA-012 to justify automatic duplication
of completed runs. Formal Step 1 acceptance remains pending.

No production source, Developer regressions, Architect documents, QA_TEST_PLAN.md,
original QA files, SQLite baseline or Git history were changed. QA additions remain
uncommitted; no commit or push was authorized or performed.
