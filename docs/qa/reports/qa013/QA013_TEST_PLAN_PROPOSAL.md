# Proposed QA_TEST_PLAN.md additions — QA-Tester review required

Proposal only, 2026-11-17. The root `QA_TEST_PLAN.md` has **not** been edited.
QA-Tester may reconcile numbering/current Protocol 1.03 text and incorporate the
following after review. These additions neither change the signed protocol nor
define the recovery policy reserved for Architect/Owner confirmation.

## Proposed Phase 1 coverage rows

| Proposed ID | Priority | Independent acceptance objective | Disposition |
| --- | --- | --- | --- |
| COR-01 | P0 | At an existing flushed-temp/pre-publication positive READY barrier, terminate the worker and deterministically truncate, empty, flip or append surviving bytes. Reject damaged bytes; never silently promote them to authenticated BLK history; preserve diagnostic remnant, original identity/trust and unrelated history. Verify exact mutation records, restart, real CLI/library verification and healthy-receiver rejection. | QA-013 integrity coverage; final execution counts/evidence in QA013_REPORT.md. |
| COR-02 | P0 | After acknowledged accepted publication, preserve an independent original reference, stop all candidate processes, damage the BLK without changing its filename, then exercise get/export/status/library verification/restart. No corrupted signed bytes may be returned as authenticated history; preserve other valid bytes and identity. | QA-013 integrity coverage. Exact recovery state, error precedence, gating and repair policy remain SPEC-PENDING. |
| COR-03 | P0 | Qualify an Architect-approved explicit repair/re-fetch procedure for damaged previously accepted history, with preservation of diagnostic bytes and exact original signed-block identity. | BLOCKED/SPEC-PENDING; not implemented or executed by this assignment. |

## Proposed running-log entry

QA-013 adds portable deterministic mutation/reference fixtures and isolated candidate
corruption tests. The immutable reference suite checks one authentic original and
eleven damaged variants; candidate suites exercise both disk locations separately.
Reference validation is not candidate acceptance. Report PASS/FAIL/BLOCKED/NOT-RUN
separately, retain prior QA evidence, and attach exact commit/DLL hashes and commands.
Process termination plus byte editing simulates damaged file contents; it is not a
physical power-loss, media reliability or operating-system certification.

## Questions requiring Architect/Owner resolution

1. For damaged previously accepted `.BLK`, must every ordinary operation pause, or
   may independently valid unrelated history be served? Is the current global
   inventory failure intended? How does this differ from corrupt orphan temps?
2. What exact status/recovery codes, signing availability and counters are required?
   Current diagnostics sometimes mark signing unavailable although original key bytes
   remain intact. Should key health and history-verification failure be distinct?
3. Which explicit operator workflow may restore exact original signed bytes from a
   trusted backup/peer? How are original full SHA-512, signature, ledger/genesis pin,
   historical authorization and original filename verified before replacement?
4. How is damaged evidence retained without misusing `.XBLK`, which currently denotes
   authenticated history invalidated by authorization rather than bad signatures?
   May anything be moved/deleted automatically, or is explicit confirmation required?
5. What resumes normal operations and prevents accidental trust/identity rebinding,
   silent re-signing or a false durable/repair acknowledgment if recovery is interrupted?
6. What policy applies if the pinned genesis itself is damaged or missing? That is a
   distinct future case, not established by ordinary-block corruption tests here.

Do not convert these questions or observed current error codes into production
requirements without authoritative confirmation. No production correction is
authorized by this proposal. Formal Step 1 acceptance remains pending.
