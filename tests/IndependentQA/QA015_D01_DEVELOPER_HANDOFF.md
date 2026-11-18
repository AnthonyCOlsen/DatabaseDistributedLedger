# QA015-D01 — Missing accepted genesis misclassified as signing unavailability

**Candidate:** `c001fc4a0d50067e2b8c9a2685625bf43aeea464`.
**Status:** independently reproduced in ordinary Windows PowerShell and Codex preflight. Step 1 disposition NOT READY. Native totals: 47 candidate PASS, 2 FAIL, 0 BLOCKED, 0 NOT-RUN; 76 reference PASS; exit 1. The two failures are the two missing-genesis scenarios below.

Fresh synthetic instances reproduce both cases:

1. Exact pinned genesis absent, another valid accepted block still present.
2. Entire `BLK` directory absent after successful binding.

Expected from the current Technical Design accepted-history integrity clause and QA-015 §3:

- Ordinary history-dependent operations: `RECOVERY_REQUIRED`, exit 4, `error.details.reason="LEDGER_HISTORY_INCOMPLETE"`.
- Diagnostic status and instance-status: healthy original signing key remains available; authority cannot be established, aggregate blocks are null, and missing history is distinguished from key corruption.
- Original identity, trusted ledger/genesis binding and unaffected signed bytes remain unchanged.

Actual:

- Publish/get/export/countersign return recovery exit 4 with `details:{}`.
- Valid import aggregates `details.reason="RECOVERY_REQUIRED"`.
- Status/instance-status exit 0 but report signing availability false and `recovery_codes:["RECOVERY_REQUIRED"]`.
- Safe rejection, null aggregates, preserved identity/pin/good files and malformed-input wire diagnostics already work. No publication, overwrite, automatic repair or data loss was observed.

Likely minimal correction for Developer review:

- At both missing-genesis branches in `src/UserDimensions.Ledger/LedgerInstance.cs:101` and `:127–128`, attach the existing typed history failure with reason `LEDGER_HISTORY_INCOMPLETE` and a safe relevant storage path.
- Verify the existing typed-history handling in `Management.Status` (`Management.cs:266–268`) preserves healthy key availability and unavailable authorization/counts, and import aggregation (`:246`) retains the incomplete-history reason.
- Preserve resource/I/O distinctions, missing-key precedence and malformed **new input** errors. Add Developer regressions for both missing-genesis branches. No protocol fields, permanent recovery flag, repair workflow or production hook is requested.

No production changes have been made by QA.

Native evidence: `C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa015-20261117-170913-72b92553\additional\MISSING-GENESIS-FILE.contract.json` and `MISSING-BLK-DIRECTORY.contract.json`; matching before/impaired/after inventories and original withdrawn bytes are beside them. Root `QA015-receipt-validation.json` verifies all 313 command receipts and exact candidate hashes. The earlier preflight evidence remains at `.git/dev-003-work/qa015-20261117-170505-ab13ec54/`. Full candidate hashes, commands, counts and native execution status are in `QA015_REPORT.md`.
