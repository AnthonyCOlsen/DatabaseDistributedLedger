DEV-003 independent QA integration — 2026-11-10

CHECKPOINT NOTE: The current implementation and this preserved QA package target protocol 1.01. Architect commit b636f0b subsequently approves incompatible 1.02/manifest Version 2. Read RESUME_20261110.txt before new development or reruns; the existing default ExpectedCommit pin remains deliberate. Historical 1.01 PASS results do not qualify 1.02.

Placement approved by Project Owner: tests/IndependentQA, with isolated evidence/data below the existing .git/dev-003-work directory.

Eight unchanged QA originals are together in this folder:
1 QA_DEV003_WINDOWS_INDEPENDENT_CLI.ps1
2 QA_PHASE1_REFERENCE_FIXTURES.json
3 QA_DEV003_OFFLINE_INDEPENDENT_CHECK.py
4 QA_DEV003_OFFLINE_CHECK_RESULTS_RERUN_20261110.json
5 QA_PHASE1_FAULT_MATRIX_DEV003.md
6 QA_DEV003_EXECUTION_STATUS_20261110.md
7 QA_DEV003_NATIVE_QA_README.txt
8 DEV003-Native-Acceptance(1).zip

The nested Developer evidence ZIP deliberately stays beside the unchanged Python verifier. Do not remove it or bypass its checks. ORIGINALS.manifest.json records all eight byte hashes and corrected source archive SHA-256 52948d60517882f5a45c4a5399b7870f1ce0382201f49171242628251f50ee23. The first seven originals are also identical to the earlier incomplete archive. Imported historical results/status remain historical artifacts, not new execution results.

Integration-owned additions: Invoke-IndependentQA.ps1, ORIGINALS.manifest.json, this README, FAULT_HOOK_REVIEW.txt and INTEGRATION_RESULTS_20261110.txt. They do not replace QA expectations. Developer source/tests and QA_TEST_PLAN.md are not modified. No new production hooks.

RUN FROM AN ORDINARY WINDOWS TERMINAL (outside Codex sandbox)
 $repo = 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger'
 powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$repo\tests\IndependentQA\Invoke-IndependentQA.ps1" -RunContext OrdinaryWindowsPowerShell -AntivirusState 'Record actual current Avast state'

The Bypass switch is process-scoped, matching QA's supplied invocation; no persistent execution-policy setting is changed. The launcher uses the same process-scoped switch for its child. It does not configure antivirus.
This machine has existing Python 3.12.14/cryptography 50.0.2 and PowerShell 7.6.9 bundled with Codex. Their paths are recorded in local, untracked .git/dev-003-work/independent-qa-runtimes.json; no software installation is performed. Explicit -PythonExe and -PowerShellExe override those optional machine-local hints. On another machine, use installed Python 3 with cryptography/lzma and PowerShell 7, or pass absolute executable paths. No test results are inferred from runtime discovery.

MODES
 -Suite All (default): unchanged Python reference verification, then unchanged candidate PowerShell suite.
 -Suite Reference: reference fixture/Developer-evidence checks only; never candidate conformance.
 -Suite Candidate: unchanged candidate suite only.
 -RunContext CodexSandboxPreflight labels diagnostic execution; it is not native filesystem acceptance.
 -RunContext OrdinaryWindowsPowerShell is an explicit operator report, not proof obtained by the launcher. Formal acceptance is never declared.
 -ExpectedCommit defaults to 5c585cefa59509f9fad8cba55000e374768685b7. Select a future approved candidate explicitly rather than silently changing it.

Every execution creates a fresh .git/dev-003-work/independent-qa-<time>-<nonce> folder and refuses reuse. It records source manifest, Git HEAD/status (including any failure), Windows/filesystem/host PowerShell, dotnet --info and its exit code, candidate/dependency file SHA-256s, complete stdout/stderr and process exit codes, unchanged QA_CASE_RESULTS.jsonl, reference-results.json, run-status.json and exit-code.txt. Test-only generated keys stay in this excluded scratch folder, not a production instance. The original candidate runner receives a fresh EvidenceRoot and never runs against a preexisting instance. No Developer regression runner is invoked.

KNOWN HARNESS COMPATIBILITY FINDING
The original QA PowerShell file is UTF-8 without BOM and contains non-ASCII punctuation. Windows PowerShell 5.1 on this machine fails parsing it before any candidate command, while PowerShell 7 parses and runs the exact same bytes. The original is preserved unchanged. Prefer the existing PowerShell 7 runtime. Adding a BOM or converting punctuation is a proposed QA-owned correction requiring separate permission, not an integration change made here.
The Python verifier prints a fixed historical message saying no candidate/.NET/Windows runtime is available. That text is preserved and is not a diagnosis of this Windows machine. Its reference output explicitly does not execute the candidate; the launcher reports candidate execution separately.

COVERAGE BOUNDARIES
Reference verifier checks 26 signed vectors, Ed25519, revocation consent, AES-GCM/AAD, DDLB, raw LZMA2 and semantic negatives, plus historical Developer evidence CRC/logs. That is reference validation.
The supplied candidate script tests fresh identity, external genesis import/join, exact genesis retrieval and one-block DDLB export, grant/revoke/regrant, variant disputes/counts, duplicate import, five negative imports, config persistence/corrupt-file restoration and missing-key gate. It does not execute all positive value/schema/compression/timestamp vectors against the candidate, create its own ledger genesis, or execute FI-01..FI-20. See FAULT_HOOK_REVIEW.txt for boundaries and gaps. Independent QA acceptance remains with QA/PM/Owner.
