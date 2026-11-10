DEV-003 — Independent QA Windows CLI test harness (Step 1 only)

Files:
- QA_DEV003_WINDOWS_INDEPENDENT_CLI.ps1 — new QA-owned candidate tests; NOT RUN by ChatGPT
- QA_PHASE1_REFERENCE_FIXTURES.json — independently signed, PUBLIC nonproduction vectors
- QA_DEV003_OFFLINE_INDEPENDENT_CHECK.py — independent offline fixture verifier
- QA_DEV003_OFFLINE_CHECK_RESULTS_RERUN_20261110.json — fixture rerun PASS, no candidate run
- QA_PHASE1_FAULT_MATRIX_DEV003.md — 20 deterministic crash-hook tests, STILL UNRUN
- QA_DEV003_EXECUTION_STATUS_20261110.md — evidence, limitations, acceptance recommendation

NATIVE RUN — Operator executes in ordinary Windows PowerShell outside Codex sandbox,
against an isolated nonproduction directory (created automatically by the script):

 $repo = 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger'
 $qa = 'C:\path\to\extracted_QA_zip'
 & "$qa\QA_DEV003_WINDOWS_INDEPENDENT_CLI.ps1" `
    -LedgerCtlDll "$repo\src\ledgerctl\bin\Release\net10.0\ledgerctl.dll" `
    -FixturesJson "$qa\QA_PHASE1_REFERENCE_FIXTURES.json"

Requirements: Windows 11, .NET 10 runtime, PowerShell 5.1+, Developer candidate
already compiled at verified commit 5c585ce, local NTFS scratch. Ensure dotnet exists.
The QA test creates a unique UDDL-DEV003-QA-* directory under %TEMP%, preserving
command stdout and stderr, a QA_CASE_RESULTS.jsonl, inputs and outputs on disk.
It NEVER runs against an existing instance, touches antivirus settings, or modifies
production data. The script is new and not yet validated on Windows; investigate
script errors separately from product defects. Do not promote a script PASS to
full Step 1 acceptance: FI-01..FI-20 native crash/durability runs are separate.

Share the new QA evidence directory or a ZIP of it with the QA-Tester thread.
Do not include production private signing keys or real user data.
