# QA-014 — Final Step 1 targeted regression and documentation consolidation

2026-11-17. **Step 1 acceptance is not declared.** Production, Developer regressions,
Architect documents and Steps 2–4 are untouched by this assignment.

**Ordinary Windows: 34 PASS, 4 FAIL, 0 BLOCKED, 0 NOT-RUN; exit 1.**
**Independent reference: 76 PASS, 0 FAIL.** The four failing cases represent two
implementation defects against current approved contracts, not four lost/corrupt
publications or a new architectural decision.

## Candidate and authority

| Item | Exact identity |
| --- | --- |
| Selected repository HEAD | `00b54475aef34181ce9f9290c0080aa9974d45de` |
| Last production-code revision | `8bc96f0f513faac57c1db23a8189288479d8a992` |
| src tree at both revisions | `aa671a5664071e1ea381ee69af9d62ff86528eb2` |
| SQLite tree, unchanged from preserved upstream tag | `9e69364cf105a1d38e1d17ee66290967f640f78d` |
| ledgerctl.dll SHA-256 | `a7b9c642bdb3939d7a705692778457dab4c12ecc22bb18cb397f487046905cf8` |
| UserDimensions.Ledger.dll SHA-256 | `71b5fadbd96a1dfed90e2290c2f2c9b62484cb017645419456535bcd13d55aa3` |

Repository/production paths were clean on entry. Source/build configuration and
SQLite have no differences from the selected production baseline. Actual supplied
DLLs equal the earlier executed QA-011/012/013 artifacts; no production build was
overwritten or inferred from documentation commit dates. Each run freezes binaries,
QA sources, the four authoritative design documents, SHA-256 manifests and Git history.

Recent design commits add Management API §13.3.1 BLOCK_ARCHIVED and §13.5 accepted-history
integrity diagnostics/phase boundaries, plus permanent portability requirements.
These are authoritative additions; the source has not yet implemented all responses.
The older OPEN archived-retry question in §17 is editorially stale relative to the
explicit approved §13.3.1. Expected behavior is not unresolved for the tested replies.

## Execution status

| Run | Independent reference | Targeted candidate | Exit |
| --- | --- | --- | ---: |
| NewOnly sandbox preflight | 76 PASS, 0 FAIL | 4 PASS, 4 FAIL, 0 BLOCKED, 30 NOT-RUN | 1 |
| Full targeted ordinary Windows | 76 PASS, 0 FAIL | **34 PASS, 4 FAIL, 0 BLOCKED, 0 NOT-RUN** | 1 |

Ordinary-Windows evidence:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa014-20261117-132749-e11315c1`.
The Owner ran the supplied command outside Codex. Receipt start/finish:
2026-11-17 19:27:49–19:28:29 UTC (13:27:49–13:28:29 America/Chicago).
Windows 10.0.26300.0, fixed NTFS, PowerShell 7.6.9, .NET SDK 10.0.403/runtime 10.0.14,
Python 3.12.14/cryptography 50.0.2; antivirus settings unchanged, scan state not recorded.
QA adapter: zero warnings/errors; environment probe exit 0. Selected HEAD/production
tree, both DLL hashes and original fixture hashes match the reviewed candidate.

`QA014-receipt-validation.json` independently verifies 198 command receipts and
stdout/stderr hashes, frozen source/design/binary hashes, original archived signatures,
before/after no-mutation proofs, three exact restored signed blocks and their
independent final canonical classifications. No candidate was rerun by that review.

Sandbox evidence:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa014-20261117-131118-36f5684b`.
It is a preflight, not ordinary-Windows acceptance. The eight new checks executed;
the thirty existing selected cases were deliberately NOT-RUN in that preflight.
All earlier QA results are preserved and are not counted as new QA-014 executions.

The full targeted selection contains **38 cases: 30 reused unchanged existing
assertion bodies and eight new current-contract checks**. It covers CLI envelopes/
errors; canonical/inverted DDLB order, grant durability, duplicate/partial results;
missing/unauthorized/restored historical authority; exact BLK/XBLK restoration and
archive get/export exclusion; active/archived publication retry; raw/invalid/legacy
join; basic no-overwrite and original-key recovery gates. It does not rerun unrelated
REAL/schema/compression/config/fault campaigns or introduce elaborate GUID collisions.

The QA adapter builds against a frozen candidate in excluded run space, with cleared
package sources. No dependencies are installed. Antivirus configuration is unchanged.
Each CLI invocation saves exact arguments, stdout/stderr, exit and their hashes.
The preflight adapter build succeeded with zero warnings/errors. QA014-receipt-validation.json
verifies 51 command receipts, 76 reference checks and three exact signed-byte
restorations without reexecuting the candidate.

## Confirmed findings

Both findings below are also **confirmed in ordinary Windows**. All thirty reused
existing cases PASS, including JOIN-NEG-ddlb, canonical/inverted batches, grant
dependencies, partial/duplicate accounting, missing/unauthorized/restored authority,
archive retrieval/export exclusion, no-overwrite and missing/corrupt-key gates.

### QA014-D01 — archived publication retries violate the approved response contract

Cases: ARCH-RETRY-quarantined, ARCH-RETRY-pending and ARCH-RETRY-revocation.

Expected by Management API §13.3.1: BLOCK_ARCHIVED, exit 4, retryable:false, with
the exact original publication metadata and historical active BLK path; a separate
current archived state/status/authorization/reason/XBLK path/retained object; and
new_publication:false, mutated:false. Revocation retry must resolve the historical
record before considering a fresh consent-expiry/publication attempt.

Observed in all three cases: **NOT_AUTHORIZED, exit 4, retryable:false**, with no
required historical/current details. File inventories and counts are unchanged;
original signed bytes remain intact. The response defect is not a successful
publication, a lost block, or evidence of a new signature being persisted.

Each scenario independently authenticates the original and canonical authority,
confirms the correct archive classification, tests exact-digest get exclusion and
normal-export exclusion, then records the complete actual/expected response in
`ARCH-RETRY-<kind>.contract.json`. Before/after retry inventories are identical.

All three ARCH-RESTORE cases PASS: independently signed late grants restore historical
authority, the exact original XBLK becomes BLK, and active idempotent retry returns
the original timestamp/hash/path/metadata without filesystem mutation. The revocation
fixture deliberately has an old signed consent; restored retry still returns the
original successful historical publication rather than signing anew.

Proposed separate Developer correction: handle authenticated matching archived
intent in LedgerInstance.ExistingPublication before fresh-publication checks,
constructing §13.3.1 metadata for both ordinary and revocation workflows. Preserve
active retry, nonmatching-GUID denial, identity gates, original bytes and counters.
No production correction is applied here.

Affected implementation location: `src/UserDimensions.Ledger/LedgerInstance.cs:262`
(`ExistingPublication`; ordinary caller at 290, revocation caller at 326).

### QA014-D02 — corrupt accepted-history diagnostics use incoming-wire errors

Case: INTEGRITY-STATE-CONTRACT. In a fresh test instance, independently authenticate
an acknowledged block, preserve original bytes, then flip one deterministic payload
bit while retaining its original SHA-512 filename. A healthy key/manifest and other
valid history remain byte-identical.

Expected by Management API §13.5: history-dependent normal operations fail
**RECOVERY_REQUIRED, exit 4, error.details.reason LEDGER_HISTORY_CORRUPT**.
Observed block get, block export, ordinary publication retry and valid incoming
import: **HASH_MISMATCH, exit 2**, without the required retained-history reason.
Status detects the problem, returns null aggregate block counts and does not assert
authorization. No corrupted output is published, no implicit repair occurs, and
the original identity, valid history and corrupted diagnostic bytes remain intact.

The actual status and all four command errors are saved in
`INTEGRITY-STATE-CONTRACT.contract.json`, with exact before/corrupted/after inventories
and mutation hashes. Detection success is preserved separately from response failure.

Proposed separate Developer correction: translate verification failure **while reading
previously accepted retained history** into the approved recovery diagnostic, with
safe trusted path/hash evidence. Preserve BAD_FRAME/HASH_MISMATCH/BAD_SIGNATURE for
invalid **new incoming** wire bytes; do not globally remap BlockCodec errors or hide
key/manifest failures. Keep truthful counts/authorization and original data. No fix
is applied by QA.

Relevant implementation boundaries: `src/UserDimensions.Ledger/LedgerInstance.cs:92`
(`Inventory`, retained-history parse/verification) and
`src/UserDimensions.Ledger/Management.cs:249` (`Status`, key/history diagnostic handling).

JOIN-LEGACY-GENESIS PASS independently exercises all three historical signed formats
at the raw-genesis join boundary, with UNSUPPORTED_VERSION/2 and unchanged instance
inventory. NewOnly excluded the other thirty already authored targeted assertions.

## QA_TEST_PLAN.md consolidation

The Owner explicitly authorized this QA-owned edit. The previous complete edition
is preserved byte-for-byte in QA_TEST_PLAN_PRE_QA014_20261109.md (SHA-256
`4bdc5d76e30d7bbd4acd3d6bbda3c23ee369c0d2686e41540880a6a13d4fa367`).
The active root plan now:

- Uses Protocol 1.03, full genesis-key ledger identity, current consent/manifest
  versions, five SQLite storage classes and canonical GUID-before-signer order.
  Retires old DLID, exact DECIMAL, direct incoming XBLK and special dispute assumptions.
- Records exact QA-010/011/012/013 candidates, native evidence paths and original
  outcomes, including failures, blocked observations and the later non-executed
  QA-013 commit-pin attempt. Historical counts are not rewritten or summed as new.
- Records six FI-07 and five FI-15 replacement software cases as PASS; original
  OS-internal observations remain REPLACED/NOT EXECUTED. FI09-LIVE-RESULT-SNAPSHOT is
  NOT OBSERVED with the Owner-approved paired storage/termination and completed-result
  evidence, rather than a fabricated live private result or new PASS execution.
- Retains QA-013's 22 candidate integrity PASS and 12 reference PASS. The newer
  Step 1 history-state error contract receives the separate QA-014 test. Automatic
  peer repair is deferred to Step 4, not a Step 1 BLOCKED requirement; restored-history
  database replay is Step 3. No repair is represented as executed.
- Preserves original chronology in an exact archived plan, updates current phase
  responsibilities and records remaining implementation/specification/review items.
  Core-first portable tests and independent OS backend qualification remain policy.

No Architect text or Developer regression is changed. The QA README points to the
consolidation and preserves QA-013's original proposal as historical provenance.

## Reproduction and handoff

This command runs the 38 selected cases and 76 independent reference checks. It
builds only a QA adapter, writes fresh test instances/logs under .git/dev-003-work,
and corrupts only one explicitly isolated test file. It changes no production code,
installed software or antivirus settings. Allow about 1–3 minutes. Exit 1 is expected
until the demonstrated conformance defects are corrected and independently rerun.

```powershell
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\tests\IndependentQA\Invoke-QA014.ps1' -RunContext OrdinaryWindowsPowerShell -Selection Targeted -ExpectedCommit '00b54475aef34181ce9f9290c0080aa9974d45de' -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

Remaining Step 1 items are separated:

1. **Implementation:** D01 archived ordinary/revocation response and D02 retained-history
   recovery diagnostic; candidate acceptance cannot be inferred while these fail.
2. **Specification/editorial:** §17 stale OPEN archived-retry text should be reconciled
   by Architect; the approved expected reply is already unambiguous. Exact history
   recovery_codes/key-availability display choices are not fully enumerated; QA does
   not invent an additional mandatory state mapping. No automatic-repair criterion
   or later-phase implementation is silently added to Step 1.
3. **Security/acceptance records:** QA-Tester reviews frozen evidence and approved
   paired/replacement dispositions; PM verifies required Security/Owner Windows
   backend qualification records. Software interruption results do not establish
   physical-storage durability. This report neither claims absent external sign-offs
   nor introduces unrequested hardware certification as a gate.

Return this evidence to QA-Tester for independent review and recommendation. Separate
Developer authorization is required for production fixes. Do not declare Step 1
accepted or begin Step 2 from these results.
