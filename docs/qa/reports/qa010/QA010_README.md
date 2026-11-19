# Active independent Step 1 QA — ARCH-014 / Protocol 1.03

QA-010, supplied by QA-Tester through the Project Owner, authorizes this QA-only
suite. It does not authorize production fixes, production hook changes, commits,
pushes, or Step 1 acceptance. `QA_TEST_PLAN.md` is unchanged.

## Active and historical entry points

**Active:** `Invoke-QA010.ps1`, using `QA010_REFERENCE.py`,
`QA010_FIXTURES_103.json`, `QA010_RUN.py` and the QA-only fault adapter.

**Historical only:** `Invoke-IndependentQA.ps1`, the eight files in
`ORIGINALS.manifest.json`, `INTEGRATION_*`, `FAULT_HOOK_REVIEW.txt`,
`RESUME_20261110.txt`, and `NATIVE_QA_EVIDENCE_20261110.*` describe Protocol 1.01
and the earlier handoff. Their bytes and expected results are preserved. Do not
run their 1.01 acceptance expectations against a 1.03 implementation. The old
verifier still requires the original Developer evidence ZIP beside it; the new
reference suite neither bypasses nor depends on that historical evidence.

## Independent fixture provenance

The new generator is original QA Python code using `struct`, `hashlib`, Python
`lzma` and `cryptography` Ed25519/AESGCM. It never imports or invokes the production
codec/signer or Developer fixtures to construct expected bytes. Public deterministic
test seeds, test-only identities, and a fixed test nonce are explicitly labeled
**NONPRODUCTION**. They must never be used for live ledgers. RFC 8032's first
Ed25519 vector independently anchors the cryptographic primitive. Header offsets,
the consent domain, canonical ordering, event parsing, full signed-byte SHA-512,
raw LZMA2, DDLB and AES-GCM AAD are checked by the QA reference implementation.

Regenerate fixtures only for reviewed QA changes, using existing Python:

```powershell
& $python -B "$repo\tests\IndependentQA\QA010_REFERENCE.py" --generate "$repo\tests\IndependentQA\QA010_FIXTURES_103.json"
```

Reference success is reported separately and is not candidate conformance. Applicable
positive and negative vectors are submitted to the actual candidate CLI. Candidate
output blocks and compressed streams are decoded independently. The adapter uses
the public candidate API only as the subject of testing, never as the expected-result
oracle. Reflection checks the normal public authoring API without adding time overrides.

## New-file inventory

| File | Purpose |
| --- | --- |
| `Invoke-QA010.ps1` | PowerShell 7 entry point, environment/evidence capture, immutable snapshots and isolated adapter build |
| `QA010_REFERENCE.py` | Independent fixture generator, strict verifier and canonical authority oracle |
| `QA010_FIXTURES_103.json` | 73 independently signed positive/negative vectors with labeled nonproduction test identities |
| `QA010_RUN.py` | Actual CLI/public-API candidate cases and checkpoint/termination observer |
| `QA010_FaultDriver.cs` | QA-only public-API adapter using existing production-disabled injected callbacks |
| `QA010_FaultDriver.csproj` | Adapter project, built exclusively in excluded evidence space |
| `QA010_COVERAGE.md` | Requirements, case IDs, FI mappings, exclusions and blocked interfaces |
| `QA010_FINDINGS.md` | Preserved failure triage, execution results and native acceptance prerequisites |
| `QA010_RESULTS_20261116.json` | Machine-readable QA checkpoint with every final diagnostic case result |
| `QA010_README.md` | Active/historical routing, commands, provenance and evidence instructions |

No existing QA original, historical evidence, Developer source/test, architecture
document or QA test plan is changed by these additions. New files remain uncommitted
until the Owner separately authorizes a commit/push.

## Reproducible execution

Use PowerShell **7**, .NET 10, and existing Python 3 with `cryptography` and `lzma`.
Nothing is installed and antivirus settings are never changed. All generated
identities, files, logs, candidate copies and adapter builds live in fresh excluded
`.git/dev-003-work/qa010-*` directories. No production instance is opened.

When using the Codex-bundled PowerShell below, Python is discovered beside that
PowerShell installation; bundled Git/Python need not be on the ordinary terminal's
PATH. An explicit `-PythonExe` still takes precedence. Other existing Python
discovery paths are retained as fallbacks.

This machine's existing PowerShell 7 is available at:

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA010.ps1' -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

Run that single line in an **ordinary Windows terminal outside Codex** for filesystem
locking and durability acceptance. `-RunContext OrdinaryWindowsPowerShell` is an
operator report recorded as such, not an automatic sandbox detector. Codex runs
must use `-RunContext CodexSandboxPreflight` and remain diagnostic only. Execution
policy Bypass is process-scoped; no persistent execution policy is changed.
This explicit-Python invocation produced the inspected Owner-run receipt
`qa010-20261116-201537-c7555114`: reference 76 PASS; candidate 130 PASS / 1 FAIL /
4 BLOCKED, exit 1. The remaining failure and checkpoint gaps are in the findings
report. An identical rerun is not needed until a relevant change is made.

`-Suite Reference`, `-Suite Candidate`, or default `-Suite All` select separate
reference/candidate reporting. `-FunctionalOnly` leaves fault coverage blocked;
it cannot produce full acceptance. The default candidate pin is
`6328bbc3a289b969b33dc365f6d8639cc005793f`. Explicitly select another reviewed
commit with `-ExpectedCommit`; never silently run a moving Developer checkout.
`-PythonExe` and `-LedgerCtlDll` select existing runtimes/builds if needed.
`-AntivirusState` records an operator-supplied description without configuring AV.

The launcher refuses dirty production/Developer regression/SQLite paths. It copies
and hashes the selected existing candidate binaries, without rebuilding or overwriting
Developer output. It also freezes the QA sources/fixtures for each run. The QA adapter
is built only in that run's excluded directory, with cleared NuGet package sources
and references to the frozen candidate DLLs. Build stdout/stderr and exit codes are
preserved. Source commit identity and actual binary hashes are separate evidence;
the launcher does not infer binary provenance solely from a Git SHA.

## Results and evidence

The printed evidence directory contains:

- `run-status.json`, `exit-code.txt`, Git before/after status, OS/filesystem and
  PowerShell metadata, `.NET --info`, Python/cryptography versions.
- Independent `reference-results.json` and candidate `candidate-results.json`.
- Every candidate command's arguments, exit code, full stdout and stderr under
  `commands/`, plus per-case `cases.jsonl` and `coverage-results.csv`.
- Source/document/candidate SHA-256 manifests, immutable `suite-snapshot/` and
  `candidate-snapshot/`, and isolated `instances/`, `inputs/`, `outputs/`.
- Fault requests, positive checkpoint/PID receipts, external termination results,
  and before/barrier/after inventories with signed-file SHA-512 hashes. A deliberately
  exclusively locked file is recorded as unreadable at the barrier; its bytes are
  compared after release. No hash is fabricated for an unreadable file.

Exit **1** means failure; **2** means blocked required coverage with no observed
failure; **0** means all requested cases passed without blocks. No exit code declares
formal acceptance. Missing checkpoints remain BLOCKED, never artificial PASS.
Failures remain in their original run directory even after harness corrections.
See `QA010_FINDINGS.md` for the execution record and `QA010_COVERAGE.md` for scope.

## Boundaries

Existing `DurableFiles` callbacks are injected only by the QA adapter. No production
hook is added. The parent waits for a positive named checkpoint/PID receipt, then
terminates only its own worker. Marker polling has a bounded deadline; no arbitrary
sleep substitutes for a durability or visibility assertion. The callback deadline
also releases abandoned test workers if the parent fails.

The memory-only receive gate is checked with before/after filesystem inventories
and an adapter that observes the existing write/rename callbacks. That proves the
instrumented boundaries were not reached; it is not a kernel-wide I/O trace.

FI-07, FI-09, the actual batch grant-durable/before-dependent boundary, and FI-15
need coordination. Before/after one `MoveFileEx(WRITE_THROUGH)` call cannot expose
its internal intermediate durability point. A post-return config test is separately
labeled and does not replace FI-15. Process termination does not prove physical
power-loss durability.

No accidental-GUID collision campaign, competing-GUID winner selection or specialized
conflict archive subsystem is tested. Ordinary idempotency, independently unique GUID
canonical-order tests, genesis trust substitution, and output no-overwrite safeguards
remain in scope. Steps 2–4 and formal Step 1 acceptance are excluded.
