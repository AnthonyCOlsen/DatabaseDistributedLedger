# UDDL Step 2 — Native Bypass, Transaction and Fault Acceptance Matrix

**Author:** Lead QA / Test Engineer  
**Updated:** 2026-11-19 — Revision 3, Owner-confirmed per-row event policy and completed Developer source audit incorporated  
**Status:** DESIGN-ONLY / SECURITY-ALIGNED REVISION 3; transaction wire grammar Owner-confirmed, other SQL/API and implementation policy still awaiting approval.  
**Baseline:** Step 1 accepted/maintained production `83cbcb5ec7933d1d83cb61a9712353c169ba7788`.  
**Companion:** [`QA_STEP2_DESIGN_ACCEPTANCE_PLAN.md`](QA_STEP2_DESIGN_ACCEPTANCE_PLAN.md) (31 general planned cases, six **unapproved** fault hooks, 11 decision areas).  
**Scope:** Step 2 native SQL interception and official-write exclusion only; no Step 3 projector or Step 4 peer networking. 20 BP + 12 TX + 10 FT scenarios remain **42 proposed scenarios**; this revision refines their acceptance oracles rather than claiming additional executed tests.
**Reviews incorporated:** Security/Reliability Engineer Step 2 Transaction Protocol and Security Boundaries; Developer read-only SQLite 3.54.1 native-source audit and withdrawn aggregate-transaction suggestion; **Owner decision preserving independent signed row events**. This document is the **canonical updated matrix**, superseding QA matrix Revision 1 and Security Revision 2 for planning without overwriting their historical files. **Design authority:** Architect/Owner; no test hooks or production code authorized.

## 1. Acceptance rules and classification legend

Every supported native mutation through the *actually loaded* customized SQLite 3.54.1 library must either (a) produce independently verifiable, authorized, durable Protocol 1.03 signed blocks representing final effective SQL changes, with honest acknowledgment of any already durable prefix, or (b) fail before an unlogged official-storage change. The official `.DLDB` and its authoritative materialization, including WAL/journal effects, must not receive ordinary-user modifications in Step 2. The sole future projector-only physical-write privilege must not be reachable by an ordinary C API, SQL command, connection flag, callback, user-visible PRAGMA or exposed public API.

Classification is a **proposal requiring Architect/Owner approval**, not a statement of currently implemented behavior:

- **INTERCEPT:** Supported mutation is provisionally evaluated and durably published through Step 1, with no official file write. Rollback discards unpublished effects.
- **ALLOW:** Safe read/diagnostic/export or expressly permitted nonauthoritative provisional action that cannot modify official storage or grant privileged access.
- **REJECT:** Unsupported/unsafe operation fails closed with specified SQLite/extended error and no ledger or official mutation. For choice rows, Architect must select the permitted branch before test freeze.

**Owner-confirmed binding decision:** Preserve Protocol 1.03 **one typed event per signed block**, including **one independently signed ledger block per changed database row**. A 100-row transaction may create 100 independently durable blocks. A DDLB batch is transport, **not** atomic publication. Already durable blocks cannot be rolled back when a later one fails. **Do not** introduce a transaction-envelope event, aggregate signed block or protocol version change. Developer has withdrawn that suggestion. Exact partial/uncertain client acknowledgment, retry correlation and provisional concurrency remain **open** and require Owner/Architect contracts.

All cases include an authorized master and unauthorized replica/identity variant where meaningful. Any acceptance of a user-controlled custom SQLite VFS, direct file manipulation by a privileged host, or replacement stock SQLite library is a **deployment/threat-model question**; a custom header is not a cryptographic OS-level access control. Do not promise protection against a process that can arbitrarily overwrite files.

## 2. Native API / SQL bypass matrix (proposed)

| ID | Route / representative test | Proposed policy | Required independent acceptance observation |
|---|---|---|---|
| BP-01 | Raw C `sqlite3_open`, `sqlite3_open_v2` + `sqlite3_exec` `INSERT/UPDATE/DELETE` | INTERCEPT | Direct native caller without managed SQL gateway; exact signed effective rows; no official writes. |
| BP-02 | `sqlite3_prepare_v2/v3`, `bind_*`, `step`, `reset`, `finalize` | INTERCEPT on mutating statements | Bind/rebind and repeat do not duplicate blocks; statement completion/durability boundary explicit. |
| BP-03 | Prepared supported DDL `CREATE/ALTER/DROP` | INTERCEPT | Exact schema event and stable table/column identities; DDL dependencies safely ordered; no official schema change. |
| BP-04 | `UPDATE FROM`, `REPLACE`, UPSERT, CTE mutation, `RETURNING`, multi-row INSERT | INTERCEPT if approved; else REJECT | Effects and conflict branches agree with approved SQLite feature matrix; `RETURNING` not confused with signed durability. |
| BP-05 | Implicit writes from foreign-key actions, conflict resolution, default/generated expression | INTERCEPT if supported; else REJECT | All final effective dependent rows represented, or no publication; no hidden writes. |
| BP-06 | `ATTACH`, `DETACH`; attached `.DLDB`, attached stock `.db` write | REJECT unapproved mutations; allow only strictly scoped read-only attachments if approved | No supported native-API escape into attached database or cross-ledger write; independently verify protected official-file/WAL/journal inventories and scratch-versus-official classification. |
| BP-07 | `PRAGMA writable_schema`, `schema_version`, `user_version`, `application_id`, journal/WAL/checkpoint PRAGMAs | REJECT any unapproved mutating form; ALLOW safe reads | Attempt does not alter official header, schema, WAL, journal or authority. |
| BP-08 | `VACUUM`, `VACUUM INTO`, `REINDEX`, `ANALYZE` | REJECT unapproved write path; possible ALLOW external export only if explicitly scoped | No official storage mutation; clear result for each command, including destination file handling. |
| BP-09 | Incremental BLOB `sqlite3_blob_open` writable + `sqlite3_blob_write` | INTERCEPT if independently supported; otherwise REJECT writable mode | No direct B-tree write; reads may remain ALLOW; BLOB bytes and block payload independently verified if supported. |
| BP-10 | `sqlite3_backup_init/step/finish` writing to destination `.DLDB` | REJECT official destination; safe external read/export may be ALLOW | No attacker-selected destination classified as projector authority; check source and destination independently. |
| BP-11 | `sqlite3_deserialize`/`sqlite3_serialize` and connection state replacement | REJECT bound writable replacement; ALLOW safe export only if approved | No indirect modification of an official database through replaced page image or later flush. |
| BP-12 | `sqlite3_file_control`, VFS controls, open flags, URI parameters, loadable custom VFS | REJECT unsupported physical-write requests; ALLOW documented harmless queries | Lower-layer official-storage guard cannot be disabled via ordinary connection controls or a caller-selected projector capability; verify actual loaded module and allow safe scratch operations only under frozen policy. |
| BP-13 | `sqlite3_load_extension`, extension entry points, virtual-table `xUpdate`, eponymous virtual tables | REJECT writes unless explicitly supported/intercepted | Extension/vtab mutation cannot call an unguarded pager or perform unlogged official writes. |
| BP-14 | `sqlite3_create_function*` / aggregate/window UDFs / `sqlite3_create_module*` / callbacks | REJECT user side-effectful functions and mutation callbacks per frozen policy | Callback reentrancy and exception cannot bypass authority, deadlock, or produce falsely acknowledged write. |
| BP-15 | Triggers, views with INSTEAD OF triggers, internal FK triggers, recursive triggers | REJECT unsupported cases; INTERCEPT expressly supported internal effects | Disabled external triggers do not accidentally disable required FK semantics; no hidden rows escape ledger events. |
| BP-16 | `TEMP` objects, `:memory:` connections, `sqlite3_db_config` configuration options | ALLOW only isolated nonauthoritative scratch operations if approved; otherwise REJECT | Scratch cannot be renamed, attached, backed up, deserialized or rebound into authoritative state. |
| BP-17 | `sqlite3_session`/changeset apply, RBU/bulk-update APIs if compiled, extension-assisted migration | REJECT unless individually approved/intercepted | SQLite build-option inventory and installed modules documented; no path applies unlogged effective writes. |
| BP-18 | Stock SQLite library and custom-header DB, dynamic library substitution | Out of direct trust boundary; detect and document | Confirm actual fork loaded by every acceptance client; stock SQLite cannot silently be mistaken for protected API. |
| BP-19 | Read-only `SELECT`, safe PRAGMA introspection, `sqlite3_blob_read`, `sqlite3_serialize` export | ALLOW under read-only policy | No signed blocks, no official user-data/schema mutation; accepted-history/key gates applied where required. |
| BP-20 | Private projector-write elevation attempts via SQL, PRAGMA, public C API, mutable instance config, callback | REJECT | Ordinary caller cannot activate privileged writer, including through connection flags, C callbacks, SQL, PRAGMAs, environment or mutable metadata; test with and without healthy signing key and on replica; confirm only a separately authenticated future projector authority can bypass the official-write barrier. |

**Completeness gate:** Architect and Developer produce an inventory of every enabled public SQLite mutation-capable entry point and build option, with disposition **INTERCEPT / ALLOW / REJECT**, rationale, proof of common enforcement location, and paired QA case. Unlisted accessible paths prevent test-matrix freeze; one successful unlogged official write is a P0 failure.

## 3. Transaction, provisional state and acknowledgment matrix

| ID | Scenario | Required acceptance outcome / design question |
|---|---|---|
| TX-01 | Autocommit one-row and multi-row statement | Define whether acknowledgment occurs at final `sqlite3_step(SQLITE_DONE)`, `reset`, or other point; durable block(s) must precede success acknowledgment. For multiple events, independently decode **one signed event per block**; reject an unapproved transaction-envelope format. If N blocks are durable when an error occurs, report that durable prefix truthfully. |
| TX-02 | `BEGIN` → mutation → `ROLLBACK` | No new signed blocks for discarded changes; official bytes/state unchanged. |
| TX-03 | Nested savepoints, `ROLLBACK TO`, `RELEASE`, `COMMIT` | Only final surviving effective row/schema changes published; no false durability for released savepoint alone. |
| TX-04 | Effective-change folding (insert-update, insert-delete, update-revert, update-delete) | Events reproduce final effective deltas; no redundant/intermediate row events unless Owner explicitly revises policy. |
| TX-05 | `RETURNING` yields rows, caller stops stepping, resets/finalizes, disconnects | Specify if row is merely provisional, which call completes/aborts the statement, and how publication/commit failure is surfaced. Never count an early row as durable ACK. |
| TX-06 | Multi-block COMMIT crashes after exactly N durable blocks | Test e.g. a 100-row commit interrupted after block 60. Independently authenticate exactly the actually durable row-block prefix and none falsely claimed beyond it; verify signatures and bytes after restart, actual partial/uncertain native result, retry correlation and no fake rollback. DDLB cannot make N blocks atomic. Do not infer canonical event applicability solely from file publication sequence. |
| TX-07 | Mixed DDL+DML within one transaction | Final schema and per-row event GUID dependencies remain reconstructible under the approved canonical `(timestamp_utc_ms, block_guid raw bytes, signer_public_key raw bytes)` order, including partial durable prefixes and later authenticated history; do not assume BLK filename/publication sequence guarantees dependency order. No Step 3 projection. |
| TX-08 | Two connections / two native processes to same identity | Run same-identity operations across two connections, threads **and** native processes. Capture provisional snapshot/version and accepted ledger head before evaluation and before publication; on conflicting history, prove the approved abort/revalidate/re-evaluate policy, serial signing/lock order and deterministic retry. No global peer consensus required. |
| TX-09 | Master loses authority, key or pinned genesis integrity between begin and commit | Verify current canonical authority and accepted-history integrity immediately before **each fresh signature**; include external local history/authority updates between provisional evaluation and signing. Fail closed or re-evaluate per approved conflict policy without publishing unauthorized or stale-row events. |
| TX-10 | Provisional state initial schema, isolated rows, read-after-write and reopen | Identify approved initial schema/rows and persistent table/column/row GUID mapping. Capture per-transaction snapshot, isolation across connections/processes, read-your-writes and reopened-connection visibility of unprojected events. Scratch must be nonauthoritative and must not become an undeclared Step 3 projector. |
| TX-11 | Constraint failure after some provisional rows or after `RETURNING` | No unjustified publication of discarded work; precise statement/transaction state and native error. |
| TX-12 | Partial or uncertain COMMIT result over native C/.NET bridge | Require attempted/durable/unknown counts, event identifiers or stable correlation, independently rechecked block inventory and unambiguous SQLite extended error/diagnostic mapping. Never return `SQLITE_OK` for an unverified COMMIT; never claim a durable prefix was rolled back. Record exact Native AOT/IPC exception and timeout outcomes. |

## 4. Crash and trust-boundary campaign (proposed, not executed)

| ID | Controlled fault position | QA observation |
|---|---|---|
| FT-01 | After provisional evaluation, before native-to-ledger request | No signed block, no official write, disposable scratch recovery deterministic; verify provisional snapshot and GUID-state lifecycle are not adopted as authoritative on restart. |
| FT-02 | C-to-.NET initialization unavailable or ABI version mismatch | Public native calls fail closed; no graceful fallback to ordinary SQLite pager writes. |
| FT-03 | Reentrant callback, managed exception, cancellation, callback lifetime expiration | No lock inversion/deadlock or false success; mapping to specified SQLite error and safe cleanup. |
| FT-04 | Before signature, after signature, after temp flush, after atomic block publish | Preserve accepted Step 1 durability semantics; no unauthorized signed block and no lost acknowledged durable event. |
| FT-05 | Between Nth and N+1th durable per-row publication | Durable prefix independently authenticated per event/block; disconnect/kill after block N, restart, classify precise partial versus uncertain state and retry safely without fabricating atomic batch success. |
| FT-06 | After all blocks durable, before SQL completion ACK / before client observes result | Restart discovers durable history; retry reports accurate duplicate/previous publication rather than silently producing new events. |
| FT-07 | Aborted process with active SQL statement/savepoints or early `RETURNING` | Unacknowledged staged data cannot reappear as committed signed history. |
| FT-08 | Authorized master and replica race, identity loss, historical corruption | Reverify authority and accepted history at the pre-sign gate; no unauthorized publication. |
| FT-09 | Simultaneous worker threads, native calls from different processes | No cross-context callback misuse or identity corruption; exercise lock acquisition order, two processes and threads, callback reentrancy, deadlock timeout, stale provisional snapshot and simultaneous authority changes; bounded resource and busy/error policy. |
| FT-10 | Attempt user write through each BP path while official-write trap enabled | No supported user-path official pager write; verify pre/post official `.DLDB`, WAL/journal and schema/data evidence after quiescence plus a separately classified scratch write. Test projector-flag spoofing; do not claim protection against arbitrary host OS file writes. |

Proposed deterministic hooks from companion plan H2-01 through H2-06 remain **recommendations only and MUST NOT be implemented** until separately reviewed for necessity, safety and authorization by Architect/Owner/Security. Pair each hook with actual exit/status, signed-block inventory and official-file oracle. Reuse approved Step 1 READY/RELEASE replacement fault tests; do not relabel unobservable literal checkpoints as executed.

## 5. Verification oracles and evidence rules

1. **Native provenance:** compile direct independent C harness against the reviewed candidate and capture runtime loaded DLL/so/dylib identity, fork revision and build configuration; verify both ordinary C and managed consumers use the same protected native boundary.
2. **Official storage:** before/after *quiescent* filesystem inventories of `.DLDB`, `-wal`, `-shm`, rollback journal and authorized sidecars; compare raw bytes when specified, and independent schema/data checks; include absent-file cases and attempted writes rejected before pager. Distinguish an OS metadata change from a logical SQL write. Architect to state exact byte-level versus logical immutability rule.
3. **Signed history:** independent Ed25519 signature, SHA-512, Protocol 1.03 **single-event** decode for each signed block, exact signer and ledger binding, canonical authorization, immutable GUID mapping, five SQLite storage classes, event count/order, distinct durable prefix and pristine bytes. DDLB packaging is independently inspected but never counted as atomicity. No Step 3 projector may be used as the QA oracle.
4. **Provisional result:** independent isolated upstream SQLite 3.54.1 reference engine for intended supported semantics; record starting schema/rows, schema/row GUID manifest, snapshot and transaction IDs, observed local history version, any stale-snapshot rejection/rebase, cross-connection reads and scratch lifetime. This does not establish Step 3 projection or full SQLite compatibility.
5. **Durability:** approved (when separately authorized) deterministic cut points or external process-kill boundaries, process-restart receipts, exact pre/post `.BLK` inventory, signing/key/history provenance and truthful ACK/partial/unknown response. Do not infer correctness from sleeps or Developer-only test totals.
6. **Coverage:** each row logs planned/runnable/executed/PASS/FAIL/BLOCKED, exact production source/binary SHA, OS and toolchain, controls used, source/command receipts and missing inputs. Preserve historical Step 1 QA-016/QA-017 evidence without relabeling.


## 6. Security alignment decision-and-evidence crosswalk (Revision 2)

| Security design question | Existing cases updated | Future independent verification evidence | Decision still required |
|---|---|---|---|
| S2-SEC-01: Owner-confirmed signed-event grammar | BP-01/03/04; TX-01/06/07/12; FT-05/06 | Separately decode every Protocol 1.03 single-event `.BLK`; correlate effective row/schema GUID identities, signatures and durable N-block prefix; no DDLB atomicity or aggregate signed envelope | **Grammar CLOSED by Owner**; define only the outstanding native partial/uncertain COMMIT and retry outcome contract |
| S2-SEC-02: isolate and version provisional state | BP-16; TX-04/08/09/10/11; FT-01/08/09 | Independently fingerprint starting schema, row data and GUID mappings; capture per-connection snapshot/version and ledger-history watermark before provisional execution and at commit admission; race in independent local blocks/authority changes before publish; verify no stale event signed and approved retry or abort behavior | Define source of initial schema/rows, visibility across connections/processes/restarts, stale-snapshot conflict policy, lock ordering and cleanup |
| S2-SEC-03: keep official files physically protected | BP-06/07/09/10/11/12/13/16/18/20; FT-02/03/10 | Prove actual fork loaded; use raw native C callers and alternative API routes; independent before/after official `.DLDB`/WAL/journal byte/state oracle, scratch controls and rejected projector-privilege spoof attempts; show no official write even when bridge unavailable | Freeze official versus provisional path labels, lower-layer guard, unforgeable future projector capability and physical-file oracle |
| S2-SEC-04: bridge, locks and uncertainty | TX-05/06/08/09/12; FT-02/03/04/05/06/09 | Correlated native return code, Step 1 durable-block receipt, crash/restart inventory, per-process lock acquisition traces without secret keys, deterministic no-deadlock and timeout evidence | Native AOT vs IPC, ABI exceptions/timeouts/locks, COMMIT ACK mapping and partial-prefix retry correlation |

**Pre-implementation gate:** For each approved policy choice the Architect should record `(decision ID, owner approval/date, supported interfaces, negative routes, API error model, matching BP/TX/FT IDs, evidence oracle)`. Security and QA review the resulting mapping before implementation authorization. A provisional matrix entry is not a final SQL contract.

**Fault-hook gate:** H2-01 through H2-06 are proposed only. Use noninstrumented, observable crash/restart and independent filesystem/ledger audits where sufficient. No hook design, production patch, fault-injection implementation or prototype is authorized by this revision.

**Threat-model boundary:** A user of supported native SQLite APIs must not reach the official writer. An actor with arbitrary host OS write privileges can change files outside SQLite and is not claimed prevented by this database-library guard. Neither the file header nor a private internal flag substitutes for OS-level protection.

## 6A. Latest Developer source-audit and Owner-decision alignment

**Owner decision now closed:** each changed row has one Protocol 1.03 signed event block, multi-row COMMIT may leave an independently durable partial prefix, and no aggregate transaction envelope/protocol change is authorized. This closes the earlier transaction-format disagreement; **TX-01/TX-06/TX-07/TX-12 and FT-05/FT-06 still need exact native result/error semantics**.

**Developer read-only source audit — investigation targets, not selected or implemented interception points:**

- VDBE execution/transaction handling: `vdbe.c`, `vdbeaux.c`, `vdbeapi.c`.
- SQL compilation: `parse.y`, `prepare.c`, `build.c`, `insert.c`, `update.c`, `delete.c`, `upsert.c`, `alter.c`.
- Alternative direct write/bypass paths: `backup.c`, `vdbeblob.c`, `memdb.c`, `pragma.c`, `attach.c`, `vacuum.c`, `vtab.c`, `loadext.c`.
- Lower-layer physical enforcement: `btree.c`, `pager.c`, `wal.c`, VFS, file-role/path classification. A single authorizer, opcode hook or pager callback is insufficient without source-backed bypass closure.

**Design choices that remain open:** How to initialize and version provisional schema/row GUID state while Step 3 is absent; stale-history revalidation versus abort; lock acquisition order across processes; hosted/native-exported C ABI versus local IPC; exact projector-only privilege, native SQLite error/ack timing, and nonprojector write-barrier negative proof. Do not create tests or hooks before authorization.

## 7. Decisions blocking test freeze / implementation authorization

- **D2-A — Feature/API policy:** Complete INTERCEPT/ALLOW/REJECT policy including compiled options and error/extended-code semantics; clarify read-only attachments/exports, UDFs, TEMP, virtual tables and SQLite sessions.
- **D2-B — Lowest common enforcement boundary:** Which verified native/pager enforcement mechanism protects official writes, independent of parser checks, C# wrapper and user-visible controls? How will future projector access be authenticated and isolated beyond ordinary client-controllable flags (Step 3 implementation not in scope)? State the OS-level arbitrary-file-writer threat boundary.
- **D2-C — Provisional state model:** Starting schema/data without Step 3; stable table/column/row GUID mapping; snapshot/history watermark, stale-history conflict resolution at commit admission, local/other-connection read-after-write visibility; scratch lifetime and lock acquisition order.
- **D2-D — COMMIT semantics:** **Owner has already frozen Protocol 1.03 independent per-row signed blocks; no envelope or new protocol.** Still unresolved: explicit vs autocommit completion point, `RETURNING` cancellation, `reset/finalize` late errors, SQLite extended codes, exact durable-prefix observation, uncertain/partial result and stable retry correlation. DDLB remains transport only.
- **D2-E — C/.NET integration and processes:** Host/runtime initialization, direct native callers, ABI versioning, exception boundary, reentrancy, locks and concurrency; decide in-process ABI vs service IPC.
- **D2-F — Official-file oracle:** Exact `.DLDB`/WAL/journal invariance rule, file creation requirements, and permissible ephemeral side effects.
- **D2-G — Fault observability:** Review the necessity and safety of each of six proposed hooks and portable crash barriers **before authorizing any hook implementation**; observable failure/restart oracles take priority.

**Design-review QA recommendation:** The per-row signed-event grammar is **approved and closed**, not a pending design alternative. Do not freeze the remaining executable acceptance criteria or authorize implementation until Architect/Owner resolve D2-A, D2-B, D2-C, D2-D native behavior, D2-E and a testable official-storage rule (D2-F). This matrix supplies preliminary coverage; it does not approve other features or implementation.

**Outstanding QA defects:** None claimed; Step 2 has not been implemented or tested. **Step 2 implementation remains NOT AUTHORIZED.**
