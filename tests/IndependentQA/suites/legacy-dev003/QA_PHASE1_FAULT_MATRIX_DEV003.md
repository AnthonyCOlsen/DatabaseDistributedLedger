# UserDimensions DDL — Step 1 Deterministic Fault-Injection Matrix (DEV-003)

**Owner:** Database Distributed Ledger - QA-Tester. **Status:** Prepared for Developer coordination; **not executed**. **Baseline:** ARCH-011, signed protocol 1.01. **Acceptance platform:** native Windows 11, appropriate filesystem, outside restricted Codex sandboxes for locking/durability tests. **Project directory changes:** none approved by this document. These are *test case definitions*, not a new product API or imposed implementation technique.

## Test runner contract to agree with Developer

Each *test-only, disabled-in-release* hook should expose a positive barrier handshake identifying the operation, transition and named checkpoint. The harness arms one checkpoint, launches one operation, waits for acknowledgement with a bounded deadline, then either releases it or terminates the candidate process to simulate abrupt loss. The barrier must not silently skip a durability step; otherwise the test would prove the wrong thing. Report effective process exit, stdout single JSON response when process survives, and exact surviving files after recovery. Hooks must not expose signing seeds, permit forged official writes, or change signed bytes. The Developer may choose IPC/CI mechanics and internal names; the **logical boundaries** below are the requested observable points. Coordinate before introducing hooks, and ensure no two modifying agents edit the same production workspace concurrently.

| ID | Priority | Logical checkpoint | Injected failure | Independent expected observation |
|---|---|---|---|---|
| FI-01 | P0 | `instance init`: after original key temp flushed, before final key publication | Kill process | No successful init acknowledgment. Original seed/identity never silently replaced; explicit recovery or complete original initialization only |
| FI-02 | P0 | `instance init`: after canonical `signing-key.v1.json` publication, before manifest publication | Kill process | Recovery gate; `instance init` must not regenerate. Explicit `instance recover --from <canonical key> --confirm-unbound-initialization` reconstructs same unbound identity, no genesis evidence required |
| FI-03 | P0 | Key-only recovery after new manifest temp flush, before final publication | Kill process | Surviving original key intact; retry produces one matching unbound manifest or safe retryable recovery state, no new identity |
| FI-04 | P0 | After `ledger init` genesis `.BLK` durable but before binding manifest durable | Kill process | No second genesis; `ledger recover-genesis --ledger-guid ... --genesis-sha512 ...` validates original bytes and pins same full fingerprint |
| FI-05 | P0 | After bound-manifest temp flush, before atomic replacement/publish | Kill process | Old unbound or new correctly bound complete manifest; explicit reconcile when incomplete; never an arbitrary or mismatched binding |
| FI-06 | P0 | `block publish/import`: after signed block temp flushed, before active `.BLK` publication | Kill process | No accepted partial `.BLK`; no `durable:true` success; full bytes recoverable if already durably published, otherwise no false acknowledgment |
| FI-07 | P0 | After final `.BLK` rename, before durable directory-entry confirmation | Kill process | Restart reads only complete authenticated blocks, result may be uncertain; never duplicates, truncated accepted bytes or false acknowledged durability |
| FI-08 | P0 | After durable block publication, before normal success JSON | Kill process | Exact raw signed bytes survive restart; repeated identical import is idempotent, counts unchanged |
| FI-09 | P0 | `block import` DDLB batch: after deterministic accepted prefix, before next item | Kill process | Prefix acknowledged or durable from surviving bytes; remaining absent or complete, no partial file; repeat does not double-count |
| FI-10 | P0 | Historical authorization change: before `.BLK` → `.XBLK` archive | Kill process | Restart recomputes classification from signed history; no ineligible variant remains ordinarily get/exportable as an active winner |
| FI-11 | P0 | Historical invalidation: after safe `.XBLK` rename, before classification/cache completion | Kill process | Original raw bytes retained; exclusive pending/quarantine counts correct on restart; no unauthorized/disputed active export |
| FI-12 | P0 | Reauthorization: before `.XBLK` → `.BLK` restoration | Kill process | Exact bytes still retained locally; eligible variant eventually restored under deterministic restart/retry; no overwrite |
| FI-13 | P0 | Reauthorization: after active `.BLK` rename, before final bookkeeping | Kill process | Restart recovers correct single active state, raw SHA-512 unchanged, status counters coherent; no `.STAT`/peer dependency in Step 1 |
| FI-14 | P0 | Config set: after new `instance.config.json` temp flushed, before atomic replace | Kill process | Old complete config remains, or safe explicitly recoverable state; no defaults substituted due to corruption |
| FI-15 | P0 | Config set: after atomic replacement, before durable-dir acknowledgment | Kill process | Old **or** new complete configuration after restart, never partial/mixed JSON; identity, key and genesis pin unchanged |
| FI-16 | P1 | `master countersign-revocation`: after raw consent-signature temp flush, before output-file publication | Kill process | No partially visible accepted signature, private key never emitted; repeated attempt uses same intent and deterministic signature |
| FI-17 | P1 | Two concurrent processes open same instance identity during a held mutating call | Competing open | Exactly one active ordinary mutator; other yields `DUPLICATE_INSTANCE` (exit 4) with diagnosis; status diagnostics permitted |
| FI-18 | P0 | Identity key file intentionally deleted/corrupted before restart | Restart | `recovery_required` suspends all ordinary commands including get/export/import/publish and consent; safe status and original-key restore only |
| FI-19 | P1 | Config corrupted/instance GUID mismatched before restart | Restart | `CONFIG_CORRUPT` / `CONFIG_IDENTITY_MISMATCH` in status diagnostics; configuration-dependent commands blocked; no automatic defaults until operator removes invalid file deliberately |
| FI-20 | P0 | Create/move `.BLK` or `.XBLK` with destination already existing | Simulated collision / held path | No destructive overwrite or lost signed bytes; actionable error, safely recoverable state; **do not** run unsupported random-GUID same-ms collision campaign |

## Additional deterministic, non-fault cases

- **Protocol/verifier** — Independent crypto and byte fixtures in `QA_PHASE1_REFERENCE_FIXTURES.py` / `.json`: `0x0101` 147-byte header, 211-byte genesis, Ed25519 signatures, big-endian timestamps, ULEB128/ZigZag/UTF-8/BLOB, LZMA2 `p=20`, SHA-512, `DDLB` v1 container, all REAL zero/infinity/subnormal/NaN cases, unsupported `0x0001`.
- **Historical authority** — Import genesis; grant B; B event; late revocation signed by target B and A; grant B at intermediate historical position; assert `.BLK → .XBLK → .BLK`, exact original byte hash preserved and correct counters. For a separate missing-evidence scenario import valid signed B event without known grant, then import its earlier grant.
- **Disputes** — Two different, individually authenticated variants of one `(ledger_guid,block_guid)` with **different signer or timestamp**; both local `.XBLK`, `disputed_count=2`, `quarantined_count>=2`, `block get/export` never select an arrival-order winner. Do not create the accepted improbable same-timestamp/GUID/signer `.XBLK` name collision.
- **Count oracle** — `retained_count = active_authorized_count + pending_count + quarantined_count`, `disputed_count <= quarantined_count`, `opaque_count=0` in a fully classified healthy snapshot. Count unique *full signed SHA-512 variants*, including genesis; no repeated exact bytes.
- **CLI** — All commands and failure cases must output exactly one UTF-8 JSON object + LF, fixed types and nulls, stable exit groups. No successful Phase 1 output says `applied`; `projection.state=not_installed`.
- **Recovery/backup** — Check independent AES-GCM/AAD known-key test vectors, invalid JSON/nonce/tag/key IDs, pin preservation, explicit never-bound recovery and original-key restoration; no seed exposure.

## Test evidence for every executed scenario

Capture actual repository commit SHA and clean/dirty state, native Windows version/filesystem/volume and tool version, executable build identity, fixture SHA-256, test ID/arm checkpoint, process handle and termination evidence, command/exit/stdout/stderr, expected independent hash/signature/counter oracle, surviving file manifest with SHA-512, restart/recovery commands, observed classification and counts, verdict and bug references. Passing fixture self-tests alone is **not** candidate conformance. Failed/missing barrier support is **BLOCKED**, not PASS. Do not claim platform directory-entry durability without tested OS/filesystem semantics.

## Developer handoff questions

1. Which *production-disabled* positive checkpoint barriers can be added for FI-01–FI-20, and how can QA arm/await/release them without race-prone sleeps?
2. Which existing CLI build/launch command and Windows filesystem/volume configuration should QA test?
3. Which per-operation state transitions are already atomic, and which need hooks at pre/post rename/directory-flush boundaries?
4. How will QA run two independent local instances with independently persisted keys, stage signed fixtures into unbound/bound history, and observe a disputed identity after both signed variants are imported?
5. Can Developer provide a clean build/commit SHA and test execution instructions at the formal Step 1 handoff? Do **not** alter QA-owned documents.

**Unresolved specification clarification (route to Architect through PM/Owner):** Distinguish `PENDING_AUTHORITY` from `UNAUTHORIZED_AT_POSITION` given inherently incomplete historical knowledge, especially never-granted versus explicitly revoked keys; decide exact `block publish` authorization criteria for backdated signed timestamps and error precedence for corrupt config. Existing approvals control; do not invent a finality checkpoint.
