# UDDL Step 2 — QA Acceptance Strategy and Design Findings

**Prepared by:** Lead QA / Test Engineer  
**Updated:** 2026-11-19 — consolidated Revision 3 (Owner-confirmed per-row events; Developer/Security source research incorporated)  
**Status:** DESIGN-ONLY — Owner-approved transaction grammar, provisional SQL/API and fault expectations; no Step 2 execution or implementation authorization  
**Current Owner-approved production baseline:** `83cbcb5ec7933d1d83cb61a9712353c169ba7788`  
**Historical Step 1 acceptance baseline:** `fbb38781faa29eb5cead9de3dcda0e760b63c7a1`  
**Published Step 1 evidence:** QA-016 `70d4d77888bdf869925da7193d8ca44e01d93dd7`; QA-017 `300dde5b8af7fb3cc5f6108b288217e60456a1a8` (QA branch; verify integration independently).  
**Scope:** Step 2 native SQLite SQL interception, not Step 3 materialization or Step 4 peer networking.  
**Companion canonical matrix:** [`QA_STEP2_BYPASS_AND_FAULT_MATRIX.md`](QA_STEP2_BYPASS_AND_FAULT_MATRIX.md) (42 BP/TX/FT cases). This Revision 3 consolidates the earlier Security Revision 2 and Developer research review; those earlier documents may remain in history but are not current QA policy.

## 1. Decision boundaries and test principles

### Approved invariants (acceptance requirements)

1. Normal **supported** SQLite C APIs and SQL entry points—including `sqlite3_prepare*`, `sqlite3_step`, and `sqlite3_exec`—must route mutating user operations through authenticated Step 1 ledger publication. A managed-only SQL wrapper is insufficient for arbitrary native clients.
2. An authorized master may publish effective user row/schema changes as independent, exact signed protocol **1.03** (`0x0103`) ledger blocks. Replicas cannot publish user mutations. Identity and historical authority must be valid; signed block publication is *durable before a successful write acknowledgment*.
3. **Only the future Step 3 projector** may change official `.DLDB` storage; Step 2 may use explicitly isolated provisional execution state but must not directly update the official file or its authoritative logical contents. Do not require local SQL `SELECT` to reflect unprojected writes.
4. **Owner-confirmed:** Transactions are provisional before publication: `ROLLBACK` and rolled-back savepoints emit no blocks; attempted `COMMIT` publishes final *effective* changes as **one independently signed Protocol 1.03 block per changed row** (plus separately typed schema events as applicable). A change to 100 rows may produce 100 independently durable row blocks. Multiple blocks are **not ledger-atomic**: once a block is durable it cannot be rolled back due to later failure. Partial/uncertain publication must be reported truthfully; DDLB batches are transport only, not atomic commitments. No new transaction-envelope grammar or protocol version is authorized.
5. Unsupported schema/SQL features and alternative write routes must fail safely, not modify official storage or silently bypass logging. Preserve upstream SQLite tests; investigate necessary changes, do not simply drop tests of disallowed behavior.
6. Wire/event values are **NULL, INTEGER, REAL, TEXT, BLOB only**. The 32-byte ledger ID is the genesis signer **public** key (not a private signing key); the obsolete `.DLID` descriptor is absent from the current approved architecture; no extra DECIMAL/BOOLEAN/DATETIME/GUID wire value types. Preserve schema declaration text and SQLite affinity where supported. NaN -> SQLite NULL, signed NaN rejected, infinities allowed and negative-zero bit behavior follows approved ledger contract.
7. Preserve portable SQLite C/VFS and .NET 10 integration; Windows 11 is initial native acceptance only. Provenance must match the explicit production candidate and the actual loaded native SQLite library.
8. Do not implement or assert Step 3 projector, `.STAT` reconciliation, read barriers or Step 4 networking as a Step 2 requirement. Step 1 history-integrity and identity recovery gates remain operative.

### Frozen Step 2 transaction-policy baseline (Owner decision)

- **Single-event signed blocks only.** Each final effective changed row is its own signed `INSERT_ROW`, `UPDATE_ROW` or `DELETE_ROW` event, while applicable schema changes use their existing typed schema-event grammar. No aggregate transaction block, transaction-envelope event or protocol revision. The Developer has **withdrawn** the earlier one-aggregate-block recommendation.
- **No multi-event atomicity.** Preserve all independently durable events if publication later fails. Acceptance examples include a 100-row transaction with a crash after block 60: independently authenticate the 60 durable blocks, verify their preservation on restart, and verify any remaining events are not incorrectly reported durable. Exact client-visible COMMIT outcome, retry correlation and unknown-outcome recovery are **Architect/Owner decisions not yet frozen**.
- **Provisional rollback is pre-publication only.** `ROLLBACK` and `ROLLBACK TO SAVEPOINT` discard work not yet signed/published. A later native error must never describe a durable prefix as rolled back. Do not assert an all-or-nothing SQLite transaction guarantee that this ledger design cannot provide.
- **Dependency and canonical-order proof.** Verify row/table/column GUID identity, final-effective-event folding and schema prerequisites against the approved canonical order `(timestamp_utc_ms, block_guid raw bytes, signer_public_key raw bytes)`. Do not assume filesystem publication sequence alone establishes canonical applicability; the design must specify and test what happens when an authenticated late block changes that order.
- **Explicit scope separation.** An isolated provisional SQL workspace is an evaluator, **not** a Step 3 projector or authoritative stored projection; authorized signed `.BLK` persistence is the only Step 2 result. Step 3 alone may write official `.DLDB` storage.

### QA evidence methodology

- **Oracle A — signed history:** independent protocol 1.03 fixture parser + Ed25519 and SHA-512 verification; validate raw block identity, signer, authorization, timestamp, event-kind/column/row GUIDs, typed values, immutable durable bytes and no duplicate publications.
- **Oracle B — official storage:** independently snapshot `.DLDB` and its SQLite logical state **before** and **after** Step 2 operations. Include WAL/journal sidecars in the investigation; distinguish transient SQLite-managed bookkeeping from forbidden persisted user/schema changes. Where possible compare quiescent database pages and logical schema/rows. For an absent official projection, assert it remains absent rather than creating one as a test convenience.
- **Oracle C — provisional semantics:** independently calculate expected effective row/schema deltas using a **disposable, isolated** reference SQLite 3.54.1 database (not a production projection). Do not treat the reference engine's transient journal writes as official storage writes.
- **Oracle D — evidence trace:** source and library provenance, commands/API calls, parameters, outcomes, raw stdout/stderr and result codes, pre/post block inventories, exact signed-byte hashes, identity/key/pin preservation, fault-point identification, restart outcomes.
- No fixed sleeps to infer durability or publication; use observable hooks or bounded polling. Distinguish planned, not applicable, blocked and actually executed evidence.

## 2. Proposed P0 acceptance matrix (design-stage only)

All identifiers below are **PLANNED** and require Architect-approved SQL/API coverage, error semantics and testable interfaces before they can be executable acceptance tests.

| ID | Acceptance objective | Independent evidence / expected result |
|---|---|---|
| S2-N01 | Native entry-point provenance and continuity | Identify actual loaded customized SQLite 3.54.1 DLL/shared library; `sqlite3_open*`, `prepare_v2/v3`, `bind_*`, `step`, `reset`, `finalize`, `exec` operate as specified. Test direct C client independent of C# wrapper. |
| S2-N02 | Autocommit `INSERT` | One effective inserted row produces one independently authenticated durable `INSERT_ROW`; no official `.DLDB` change. |
| S2-N03 | Autocommit `UPDATE` | Affected row(s) produce GUID-identified `UPDATE_ROW` with only effective changed columns, no unrelated values overwritten. |
| S2-N04 | Autocommit `DELETE` | Each effective deleted row produces one durable `DELETE_ROW`; zero matching rows produce zero blocks. |
| S2-N05 | `CREATE TABLE` / supported `ALTER TABLE` / `DROP TABLE` | Correct signed schema event grammar, persistent GUID identities, declared type text, unsupported DDL rejected before publication; official schema unchanged. |
| S2-N06 | Prepared statement lifecycle | Rebinding/reset/repeated stepping, cached statements and finalize do not duplicate, omit or defer unauthorized blocks. Explicitly cover `RETURNING` stepping order. |
| S2-N07 | SQL feature matrix | Selected supported WHERE, expressions, parameter bindings, NULL/affinity, INSERT DEFAULT, multi-row SQL, UPSERT/REPLACE/CTE/RETURNING **according to Architect's explicitly frozen matrix**; reject unapproved syntax safely. |
| S2-N08 | Value encoding | Independent five-class fixtures: int64 boundaries, REAL ±0/±∞/NaN-to-NULL, UTF-8 and embedded NUL, empty/large BLOB, NULL vs omitted, affinity/default conversion and deterministic event bytes. |
| S2-N09 | No-op mutations | `UPDATE` with no effective final change, zero affected rows and insert-then-delete transaction yield zero row-event blocks; record separate DDL/no-op policy. |
| S2-A01 | Authority | Master authorized at canonical position succeeds; replica, revoked master and unbound/unknown identity are rejected with no new block or official write. |
| S2-A02 | Identity/history faults | Missing/corrupt key, missing pinned genesis, damaged accepted history return recovery/diagnostic behavior consistent with Step 1; no SQL-triggered signing, block or direct `.DLDB` write. |
| S2-A03 | Independent direct-native clients | Same policy applies to raw C caller, native provider and C# wrapper; no reliance on application-specific SQL gateway. |
| S2-T01 | Begin/rollback | Staged mutations and DDL followed by `ROLLBACK` publish no block and do not modify official storage. |
| S2-T02 | Savepoints | Nested `SAVEPOINT`, `ROLLBACK TO`, `RELEASE`, final `COMMIT` publish only surviving effective changes; rollback-to-savepoint has zero durable effect for discarded work. |
| S2-T03 | Effective-change folding | UPDATE 1→2→3 -> final 3; 1→2→1 -> none; INSERT→UPDATE -> final INSERT; INSERT→DELETE -> none; UPDATE→DELETE -> DELETE. |
| S2-T04 | Multi-row commit | **Owner-confirmed one signed block per final effective changed row** (e.g. 100 rows → 100 independently durable signed row blocks). Independently verify order, schema dependencies and GUID references under canonical ordering; reject transaction-envelope events and false transaction-wide atomicity. |
| S2-T05 | Publication partial failure | E.g. interrupt after event 60 of 100: independently authenticate exactly the actual durable prefix; preserve its bytes and applicability on restart; report partial/uncertain outcome and stable retry correlation accurately, never claim rollback. Failure point and exact native result mapping remain to be frozen. |
| S2-T06 | `RETURNING`, `changes()`, last inserted ID | Outcomes are explicitly labeled or otherwise contractually distinguishable as provisional where appropriate; do not imply official `SELECT` visibility or full publication before durable commit. |
| S2-T07 | Multiple connections / concurrency | Writer ownership, prepared transactions, busy/locked responses, interleaved mutation/DDL and rollback isolation; no unlogged/direct mutation or duplicate blocks. |
| S2-B01 | ATTACH/DETACH | Reject or rigorously scope alternate destination mutation; no official storage modifications or unlogged writes, including attached `.DLDB` paths. |
| S2-B02 | PRAGMA/file controls | Attempt `writable_schema`, `schema_version`, journal/WAL/checkpoint, `sqlite3_file_control`, and PRAGMA settings with mutation effects; protect official schema/pager/write authority. |
| S2-B03 | Incremental BLOB | `sqlite3_blob_open` write mode and `sqlite3_blob_write` cannot mutate official storage outside authenticated event path. |
| S2-B04 | Backup/serialization | `sqlite3_backup_*`, `VACUUM`, `VACUUM INTO`, `sqlite3_serialize`/`deserialize` and alternate VFS/open flags cannot create a supported official-write bypass. Define ordinary external export separately. |
| S2-B05 | Extensions/virtual tables/functions | `load_extension`, virtual table module write callbacks, side-effecting custom functions, authorizer/trace hooks and user-trigger paths cannot bypass event publication; unsupported features fail clearly. |
| S2-B06 | Schema/constraint side effects | Cascades, deferred foreign keys, conflict actions, generated/default expressions and constraint failures are either faithfully converted to final events or explicitly unsupported. No implicit unlogged row change. |
| S2-B07 | File header and stock-library separation | Confirm loaded fork/official open policy; stock SQLite rejection is defense against accidents, **not** a security boundary against a privileged file-writing adversary. Custom header qualification when implemented, not assumed from Step 1. |
| S2-F01 | Pre-publication fault | Crash/fail after provisional evaluation but before signed durable block; no block and no official user-write. |
| S2-F02 | Publish durability cut points | Crash after signing/before temp-file durability, after flush/before atomic publish, after durable publish/before ack; enumerate observed surviving blocks and truthful restart responses. |
| S2-F03 | Partial multi-block crash/restart | Exactly durable validated prefix persists; no phantom commits, no overwritten blocks, no unauthorized projection; deterministic inventory and retry/duplicate safety. |
| S2-F04 | Native-managed failure bridge | .NET process crash, native callback failure, cancellation/timeout, resource exhaustion, exception/error mapping and key/ledger fault do not yield success-without-durability or inaccessible diagnostic evidence. |
| S2-F05 | Restart/admission | Pending provisional uncommitted state must not be treated as acknowledged signed history after restart; known durable blocks remain accessible independently of projector. |

## 3. Proposed P1 coverage / breadth

- C clients built with different supported calling styles; mixed `sqlite3_exec` and prepared flows; Unicode filenames, zero-length bound values, large bounded SQL/parameter workloads.
- Interactions with ordinary SELECT, including `SELECT` immediately after unprojected publication (may still show old projection), read-only connections and multiple SQLite open flags. Do not demand global/remote freshness.
- Statement/connection teardown while provisional transactions are open; nested savepoint edge cases and retry after an uncertain COMMIT.
- Performance/resource bounds for large multi-row statements and finite memory staging; ensure no unbounded buildup or accidental n² behavior.
- Preserve ordinary upstream SQLite tests, classify every native behavior difference and document justified, reversible exclusions with replacement tests; not blanket-delete test suites.
- Portability audit for Linux/macOS adapters in source and tests, without claiming native platform acceptance before execution.

## 4. Fault-hook/interface requirements to propose to Developer (not authorized for implementation yet)

These are **testability recommendations**, not permission to add hooks or prototypes now.

| Hook | Required observable evidence | Crash/fail variants |
|---|---|---|
| H2-01 After SQL provisional evaluation, before signing | transaction ID / statement ID, planned effective deltas and block count, no secret material | cancel, process kill, rollback |
| H2-02 Immediately before ledger publication and after each per-block durable ACK | planned/attempted/durable counts, immutable block GUID and raw signed-block hash, correct ordering | fail Nth block, kill between blocks |
| H2-03 Before/after durable block store operation | reuse approved Step 1 observable READY/RELEASE durability hooks | fault before/after publication; restart |
| H2-04 Before returning COMMIT result/acknowledgment | known outcome versus uncertain outcome, durable prefix, mapped native SQL error | client disconnect, managed callback exception |
| H2-05 Before any official SQLite pager write | **test-only deny-and-record gate** on ordinary user connections; record denial without authorizing writes | exercise all alternate entry points |
| H2-06 Identity/authority/library-selection boundary | key availability and historical authorization decision, actual loaded native module identity | missing key, wrong module, revoked master |

Hook discipline: isolate instrumentation in test builds, fail closed when a required bridge is missing, preserve native/managed ABI and SQLite portability, no key/seed/plaintext in logs, and pair internal hooks with observable filesystem/state oracles. Prefer deterministic barriers over timing delays. Do not use instrumentation as the sole evidence of no official-storage mutation.

## 5. Acceptance gaps and Owner/Architect decisions needed before executable test freeze

**D2-01 — Supported SQLite surface:** List supported SQL syntax and exact C APIs/SQLite configurations; explicitly classify triggers, user UDFs, virtual tables, views, indexes, foreign keys/cascades, generated/default columns, PRAGMAs, ATTACH, backup, incremental BLOB, schema writes, extensions and TEMP objects. Clarify what “supported” means on read-only versus writable APIs.

**D2-02 — Native interception boundary:** Identify which low-level paths bypass a parser/authorizer-only design and specify how projector-only privilege is unforgeable to ordinary supported connections. Confirm C clients without C# wrapper work, or submit compatibility exception to Owner.

**D2-03 — Provisional execution:** Where/how to maintain per-connection provisional data/schema without changing official `.DLDB`; determine initial schema source before any Step 3 projector exists; how SQL evaluates against local snapshot and how expected row GUIDs are associated.

**D2-04 — Transaction completion (grammar already approved):** **Do not reopen the Owner-confirmed per-row Protocol 1.03 model.** Freeze autocommit boundaries, savepoint semantics, statement failure and constraint behavior, effective DDL folding, dependency-safe canonical row/schema ordering, and outcomes when post-publication errors leave a durable prefix or an uncertain result.

**D2-05 — Result/error contracts:** Define SQLite error/extended-code mapping, partial versus uncertain COMMIT reporting, stable retry/correlation identifiers, affected-row counts, `RETURNING`/`sqlite3_reset()`/`sqlite3_finalize()` and provisional `last_insert_rowid()` semantics. A returned `SQLITE_ROW` is not a durability acknowledgment; do not report `SQLITE_OK` for an unverified full commit or silently reuse historical PostgreSQL SQLSTATE conventions.

**D2-06 — Storage-mutability oracle:** Define precise official-file scope (`.DLDB`, `-wal`, `-shm`, rollback journals), expected read-only operational side effects, and the testable rule for quiescent logical/data nonmutation. Establish how/when a Step 2 database file is created, if at all.

**D2-07 — Native-to-ledger bridge:** Freeze process lifetime, callback/thread ownership, ABI version/error mapping, crash handling, deadlock avoidance, lost-callback fail-closed behavior and whether .NET must be present for direct C API calls.

**D2-08 — Resource/concurrency policy:** Define statement/event/transaction size limits, bounded provisional memory, SQLite locking on multiple connections/threads, and deterministic duplicate/error behavior after retry or crash.

**D2-09 — Traceable hook design:** Determine accepted test-only observable checkpoints and whether H2-05 file-write deny/record instrumentation can be implemented without changing release semantics.

**D2-10 — Phase boundary:** Decide the minimum demonstrable native SQL schema/provisional state fixture for successful Step 2 `CREATE/INSERT/UPDATE/DELETE` *without* any Step 3 projector. Do not accidentally convert Step 2 acceptance into an unauthorized mini-projector.

**D2-11 — Documentation reconciliation:** The earlier `QA_TEST_PLAN.md` snapshot contains historical `.DLID`, exact DECIMAL and Version-1 assumptions. Verify current GitHub plan and update only QA-owned test expectations to active Protocol 1.03/Version 2 layout and current Step 1 completed status; do not rewrite historical run evidence.


## 5A. Developer/Security research crosswalk and acceptance gates (incorporated 2026-11-19)

The Developer completed a **read-only source audit** of the pinned SQLite 3.54.1 fork and withdrew its earlier aggregate-transaction proposal. The Security Engineer independently endorsed Protocol 1.03 per-row publication and highlighted bypass/lock risks. These are **design findings, not evidence of implemented Step 2 controls**.

| Research outcome | QA acceptance consequence | Detailed cases |
|---|---|---|
| Native admission and physical enforcement are layered | Inspect actual selected native call graph and verify every enabled public write route, including `backup.c` direct-write destination behavior, `vdbeblob.c`, `memdb.c`, virtual tables and file controls; a VDBE opcode or authorizer alone cannot prove coverage | S2-B01..B07; BP-01..BP-20; FT-10 |
| Private provisional workspace lacks Step 3 projection | Independently fingerprint schema/data/immutable GUID map and historical watermark at beginning and commit admission; test stale local history, restarts and cross-process isolation without claiming a hidden projector | S2-T03,T07; TX-04,08,09,10; FT-01,08,09 |
| Native `.NET 10` ledger integration remains undecided | Verify plain native C clients via selected versioned ABI/IPC, error translation, callback lifetime, lock order, managed exceptions and no fallback to official pager writes | S2-A03,F04; BP-01,02,12,20; TX-12; FT-02,03,09 |
| Owner confirmed independent per-row signed events | Independently decode one Protocol 1.03 typed event per changed row; assert no aggregate envelope, DDLB-as-atomicity or phantom rollback; deterministically capture partial durable prefix/retry | S2-T04,T05,F03; TX-01,06,07,12; FT-05,06 |
| `RETURNING`/step/reset/finalize can surface late failures | Test rows returned before final durable outcome, premature reset/finalize/close, implicit and explicit transactions, and observable error timing | S2-N06,T06; TX-01,05,11,12; FT-07 |
| Step 2 cannot write official physical state | Compare official `.DLDB`, `-wal`, `-shm`, rollback journals and selected path roles separately from approved scratch, including negative-case/bridge failures; threat model excludes arbitrary host OS file writers | S2-B01..B07,F01..F05; BP-06..20; FT-10 |

**Review gates before implementation authorization:** Architect/Owner-approved SQLite API coverage and enforcement location; provisional state origin/history conflict policy; native bridge contract; exact SQL COMMIT/result/partial-retry contract; official physical-file invariance oracle. Existing six proposed test hooks remain **unapproved** and must receive necessity/safety review before any implementation.

## 6. Recommended execution structure and release evidence (future, after authorization)

1. **Source/static audit**: capture current reviewed production commit, native provenance, build matrix, entry-point coverage and loaded DLL; freeze approved SQL/API feature matrix. No testing against an ambiguous HEAD.
2. **Pure reference fixtures**: generate independent expected effective SQL mutations and Protocol 1.03 signed wire expectations; keep QA vectors distinct from Developer's helpers.
3. **Direct-native acceptance**: run disposable master/replica instances through C SQLite API, then the C# provider and CLI routes; compare signed blocks and official-file oracles.
4. **Bypass/fault tests**: alternate native paths, test-only barriers, restart, partial commits, concurrency and identity faults; preserve original command receipts and original failed runs.
5. **Regression & provenance**: run retained upstream SQLite regression matrix with exactly documented fork exceptions; rerun Step 1 signed-ledger safety regression against the final production candidate.
6. **Final report:** exact production source/binaries, environment, planned/executed/PASS/FAIL/BLOCKED/NOT-RUN counts, QA independent block/file/hash inventories and partial-result evidence, known limitations and READY/NOT READY. Do not mix Step 2 passes with Step 3 projector or Step 4 peer-sync claims.

## 7. Preliminary QA disposition

**Step 2 DESIGN: QA planning updated (Revision 3).** The **per-row Protocol 1.03 event decision is closed by Owner approval**; implementation choices and SQL completion semantics are not. No Step 2 tests executed. **Step 2 IMPLEMENTATION: NOT AUTHORIZED.** No production code, prototype or test hook is authorized by these plans. At implementation gate, open decisions D2-01 through D2-03 and D2-05 through D2-10 (plus D2-04 completion details) must have approved acceptance contracts; D2-11 is QA document maintenance. Historical Step 1 evidence stays untouched.
