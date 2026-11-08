# Database Distributed Ledger — SQLite Architecture and Implementation Plan

**Revision:** 2026-11-07 — ARCH-002 documentation draft  
**Authority:** Project Owner approvals confirmed through ARCH-001 and acknowledged by the Project Manager.  
**Status:** Approved direction with implementation details identified below as *proposed* or *to be validated*. No implementation is represented as complete.  
**License:** Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).

## 1. Scope and controlling documents

This document specifies the **current SQLite implementation direction** for [Database_Distributed_Ledger_Technical_Design.md](Database_Distributed_Ledger_Technical_Design.md). The October 23, 2026 integrated v7 design remains the source for the **Version 1 portable signed-block format and ledger semantics**, including genesis fingerprints, Ed25519 signatures, canonical order, grant/revoke behavior, GUID identities, replay, and conflicts. This document supersedes **PostgreSQL-specific implementation choices**, not the underlying portable ledger protocol. The former PostgreSQL v0.4 management contract is retired as a live adapter specification; a SQLite-oriented replacement is [Distributed_Ledger_Management_API_Specification.md](Distributed_Ledger_Management_API_Specification.md).

**Non-negotiable guarantees:** ledger history is authoritative; signed blocks remain immutable and independent (not hash-linked); multiple authorized masters publish while disconnected; replicas verify and relay; all supported user modifications create durable authenticated block history *before* official relational changes; official state is a reconstructible deterministic projection; missing earlier blocks can change that projection; ordinary SQL reads need not await unknown remote history.

**Accepted initial threat boundary:** Changing SQLite's file header and extension deters ordinary external SQLite tools from accidentally opening or modifying the official database. It is **not** encryption, authentication, or protection against administrators or deliberately hostile local processes. The Project Owner explicitly does **not** require full ledger-versus-projection verification before each new block is signed. SQL mutation-path closure inside the supported fork remains mandatory.

## 2. Engine, fork, and distribution

- **SQLite baseline:** official complete SQLite 3.54.1 release (upstream C implementation), held as an internally maintained fork with a pristine upstream baseline commit and documented provenance. New application components target **C#/.NET 10 LTS**; Visual Studio 2026 Community is a supported development environment.
- **Official database extension:** `.DLDB` (Distributed Ledger Database).
- **Exact file-identification signature:** fifteen ASCII bytes `UDDL database 1` followed by one NUL byte, for a total of **16 bytes**. This is the value of the `SQLITE_FILE_HEADER` compile-time override. Do not change the SQLite page layout merely to achieve this signature.
- **Compatibility:** standard SQLite libraries should reject `.DLDB` content under ordinary database APIs, even if renamed. The customized native library should open it. Test this rather than treating it as proof of security.
- **SQLite internals:** preserve upstream parser, planner, B-tree, pager, WAL and journal implementation where possible. Add only narrowly justified native changes for native SQL mutation interception and projector-only official-write authority. Keep build options and selective source differences documented for subsequent upstream patch adoption.
- **Native library loading:** ensure the .NET provider loads the customized SQLite library, not a bundled stock SQLite. Identify loaded native module and version during diagnostics/tests. Preserve existing SQLite C API signatures and normal client SQL interface as the intended product interface.
- **WAL and backups:** begin by testing WAL with `synchronous=FULL`; use SQLite-supported backup/recovery mechanisms. The `.DLDB-wal`, `.DLDB-shm`, and journal files are part of an active database's persistence/recovery state. Do not delete the main `.DLDB` independently of active SQLite connections and their recovery files.

## 3. Logical components and trust boundaries

| Component | Responsibility | May update official `.DLDB`? |
| --- | --- | --- |
| Ledger codec / verifier | Strict Version 1 parsing, SHA-512, Ed25519 signatures, canonical event encoding, resource limits | No |
| Ledger block store | Durable, exact-byte `.BLK` files; duplicates, variants and export | No |
| Authorization / canonical replay | Genesis binding, grant/revoke validation, ordering and event applicability | No |
| SQL publisher | Accept normal SQLite SQL writes on an authorized master; evaluate provisional effects, sign and durably publish independently meaningful blocks | **No** |
| Ledger projector | Ingest applicable signed blocks; build/repair schema and rows; transactionally record projection progress | **Yes, exclusively** |
| SQL reader | Ordinary SQLite SELECT/query interface against consistent local projection | No |
| Peer exchange | Announce, request, transmit and receive **exact signed bytes**; relay without becoming a master | No |
| Management | Initialization, joining, keys, status, import/export, rebuild and configuration | Only by invoking the trusted projector path |

The **publisher and projector are independent workers** connected by durable ledger history, not an in-memory queue as their only recovery source. Implementers may use dedicated threads, scheduling loops, or separate processes provided these guarantees hold. Locally published blocks pass through the **same verification and projector ingestion** path as remotely received blocks. Merely selecting a private key never grants authority; an instance must be authorized by canonical ledger history.

## 4. Native SQLite client behavior and SQL boundaries

**Owner-approved public behavior:** Keep ordinary SQLite API and SQL submission, including native `sqlite3_prepare*` / `sqlite3_step` client workflows. For **supported** `INSERT`, `UPDATE`, `DELETE`, `CREATE TABLE`, `ALTER TABLE`, `DROP TABLE`, transaction commands, and other supported mutations, the native library must cause the concrete effects to become signed ledger events **without directly changing official materialization**. A replica rejects writes. A master may publish blocks even while its projector is catching up or peers are disconnected, subject to local state/authority preconditions.

A managed C# gateway alone is **not** transparent to arbitrary native SQLite API clients; the Developer must establish a **native interception and official-write authorization boundary**, or obtain an explicitly approved revision to the compatibility guarantee. Ordinary clients must not gain projector write authority through a PRAGMA, special SQL statement, callback, alternate API, `ATTACH`, incremental BLOB write, backup destination, extension, trigger, or other supported path. Reject unsupported mutation paths rather than silently performing an unlogged change.

Reuse SQLite's native parser, planner, expressions, and transaction execution wherever safe. Provisional mutation evaluation is separate from official projection. `BEGIN`, savepoints, `ROLLBACK`, and `COMMIT` preserve the earlier ledger contract: rollback emits no blocks; final effective changed rows are emitted at COMMIT; multi-block publication **is not group-atomic**. If publication is partially durable, report partial or uncertain outcome honestly; never claim already durable blocks were rolled back. `RETURNING` and command counts are provisional and do not guarantee immediate `SELECT` visibility. PostgreSQL-specific `COPY` and `MERGE` syntax and SQLSTATE codes are not required SQLite features solely because the old adapter described them.

**Read behavior:** SELECT may serve consistent, locally projected but network-stale data. Do **not** block awaiting unknown blocks on remote nodes. Once a locally known block starts reconciliation/replay that could expose inconsistent intermediate state, gate affected SELECTs until a consistent generation is available; an unsuccessful rebuild yields a clear error. Under sustained arrivals, reconcile finite batches and admit reads between consistent batches to prevent starvation. Handle active read transactions/prepared statements across projection generation changes explicitly.

**No additional historical-state auditing before signing:** The publisher may trust the application-controlled local `.DLDB` projection under the accepted threat model. This waiver does **not** remove signature validation, historical authorization replay, ledger-first publication, or the ban on supported direct SQLite write bypasses.

## 5. Portable typed values and exactness

Keep the Version 1 wire tags, including **signed 64-bit INTEGER**, **IEEE-754 binary64 FLOAT**, **arbitrary-precision DECIMAL coefficient and scale**, **signed int64 UTC-millisecond DATETIME**, BOOLEAN, GUID, TEXT, BLOB and explicit NULL. No wire grammar changes are authorized by this SQLite migration.

- **DECIMAL:** Canonical `coefficient × 10^(−scale)` must survive insert, materialization, retrieval, filtering, ordering, supported arithmetic, and SQL-to-new-ledger capture **without silent precision loss**. Never implicitly route exact decimals through SQLite numeric affinity, REAL, or .NET `decimal` when precision would change. Underlying storage may use canonical bytes or canonical text only with exact comparison/arithmetic, documented bind/result behavior and safe indexing. Reject unsupported exact expressions or unrepresentable declared precision/scale, rather than approximating. Resource bounds remain allowed. Division by a denominator that produces a nonterminating decimal requires an explicit supported policy; until specified, reject rather than silently round.
- **INTEGER:** Preserve full Version 1 signed int64 range, with errors for overflows or unsupported coercions. This does not silently add arbitrary-precision INTEGER to the existing wire format.
- **FLOAT:** Preserve documented binary64 semantics, reject nonfinite inputs, and canonicalize negative zero to positive zero as the protocol requires. FLOAT itself is approximate by definition; do not misrepresent it as arbitrary-precision numeric.
- **DATETIME:** Represent exactly as signed int64 milliseconds since UTC epoch. Reject unrepresentable submillisecond input and leap-second literals under existing rules; no intermediate floating-point time arithmetic.
- **GUID, BOOLEAN, TEXT, NULL:** Preserve canonical GUID byte ordering, strict Boolean encoding, exact valid UTF-8 without implicit normalization, and distinction between omitted values and explicit NULL.

Exact SQL DECIMAL integration into SQLite's standard operators and parameter/result bindings is a **design-and-test obligation**, not something supplied automatically by declaring a column `DECIMAL` or enabling SQLite's `decimal.c` extension. Define the supported syntax and coercion matrix before claiming transparent SQL compatibility.

## 6. Filesystem block store: `.BLK` and `.STAT`

**Approved storage:** One exact signed block per immutable `.BLK` file, organized by **signed UTC block timestamp** when representable as a calendar date:

```text
YYYY/
  YYYY-MM/
    YYYY-MM-DD/
      YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.BLK
      YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.STAT
```

`ssss` is four decimal digits of a second fraction at 1/10,000-second units; because Version 1 signed timestamps are milliseconds, the fourth digit is **0** (for example, `.123` seconds is `1230`). Use UTC, zero-padding, lowercase canonical GUIDs and SHA-512 hex, and filesystem-safe names. For signed int64 timestamps not representable as calendar dates on the host, use a documented safe, deterministic **non-calendar fallback** rather than rejecting otherwise valid signed blocks; its exact spelling is an implementation detail pending specification. Do not trust a filename as proof of block identity, signature, or timestamp; verify block bytes. The full-block SHA-512 suffix allows both authenticated variants of a disputed block GUID to remain retained.

**Durable publish sequence:** Create a unique temporary file on the destination filesystem; write complete original bytes; validate safely; flush file data; publish without overwrite using a same-filesystem atomic rename; ensure the directory entry is durable by a platform-appropriate mechanism; *then* acknowledge durable retention. Never expose partially written files as accepted `.BLK` entries. Duplicate identical bytes are idempotent. Different authenticated variants claiming one `(ledger_guid, block_guid)` are quarantined, never chosen by filename or arrival order. Paths and metadata are indexes, not signed protocol data.

`.STAT` is **replaceable diagnostic/cache metadata** with the same filename stem, never an input to block authenticity or the source of truth for current applicability. It may be absent, stale, or deleted. Its statuses may change after late history. Persistent projection generation and progress metadata must be committed **inside the same SQLite transaction as the corresponding official state change**. On recovery, derive statuses from signed blocks and current projection metadata and regenerate `.STAT` as necessary. Deleting all `.STAT` files does not itself roll back official state.

## 7. Reconciliation, crash recovery, and rebuilding

The ledger is immutable and can exist without SQLite. The initial projector may use **complete deterministic replay** from the pinned genesis and all retained relevant blocks; a later optimizer may use selective rollback/replay only if it yields the same state as a full replay. Canonical order is `(timestamp_utc_ms, raw signer_public_key, raw block_guid)` ascending. Authentication and authorization are evaluated at each block's canonical position, independent of import order. A late earlier block, including a grant or revocation, may require rebuilding and invalidating effects previously considered applicable.

**Crash boundary:** (1) durable `.BLK` publication; (2) SQLite transaction committing projection changes **and** progress metadata together; (3) optional `.STAT` update. Crashes before (2) cause safe replay; crashes after (2) do not justify blind duplicate application merely because `.STAT` was not updated. All startup/recovery paths must account for late-block invalidation and current projection generation, not merely a per-file applied flag.

**Full rebuild:** Stop/guard projector writes and affected reads; recover or close SQLite correctly; reconstruct into a clean `.DLDB` from authenticated block history; commit consistent projection/progress; regenerate `.STAT`; reopen reads only after a consistent projection is established. If rebuild fails, remain unavailable for affected reads and persist diagnostics. Do not merely delete `.DLDB` while it has active handles or associated WAL/journal recovery state. A staged rebuild followed by atomic activation may be added later.

## 8. Signing keys and genesis

**Approved:** A portable `ISigningKeyStore`-style abstraction stores **Ed25519 private signing keys outside** `.DLDB`, `.BLK`/`.STAT` stores, code repository and routine projection backups. Calls to sign should normally use an opaque key identifier rather than exposing private bytes to ordinary components. Native protected storage candidates: Windows DPAPI under a dedicated account, macOS/iOS Keychain, Android Keystore-backed wrapping, Linux Secret Service or restricted service-account secrets; platform capabilities and unattended operation differ. Provide separately protected **encrypted offline recovery backups**, verified restoration, and explicit error if a previously selected key is missing. Key lifetime is independent of database rebuilding.

Use Ed25519 **sign/verify**, not private-key encryption/public-key decryption. Initial sole authority derives from the self-signed, pinned genesis block; later authority from `GRANT_MASTER` and the existing two-party `REVOKE_MASTER` event. The ledger GUID is an identifier, **not an authorization key**. The existing protocol cannot ordinarily revoke an unavailable/uncooperative target key without its required signature; this remains a documented limitation, not a change approved here.

## 9. Incremental delivery plan and quality gates

| Phase | Owner-approved sequence | Acceptance gate before advancing |
| --- | --- | --- |
| **1. Independent ledger** | Version 1 codec, exact signed bytes, genesis, grant/revoke, key storage, `.BLK` persistence, import/export/status, canonical replay model — **no SQLite dependency** | Deterministic vectors, duplicates/disputed variants, durable block crash tests |
| **2. Ledger-driven SQLite** | Compile-time `.DLDB` signature; projector-only official SQLite state, typed values, SQL reads, complete rebuild and `.STAT` regeneration | Standard SQLite rejects `.DLDB`; custom fork opens it; exact types; WAL/rebuild/crash correctness |
| **3. SQLite SQL interception** | Native SQLite C API-compatible mutation submission on authorized masters; provisional evaluation, durable publication, replica rejection, read barriers | Every supported SQL mutation/bypass path tested; no direct official change; honest partial outcomes |
| **4. Peer synchronization** | Manual/import and configured peer exchange, missing-block reconciliation, offline masters/replicas, late history convergence | Independent masters converge after exchanges and reversed arrival order; no quorum required |

Engineering experiments may occur *within implementation work* and be reverted in Git if unsuccessful; no separate throwaway prototype is mandatory. The PM coordinates assignments and sequencing, the Developer owns all production code/build integration, QA owns test work under current permissions, Security reviews reliability, and the Architect exclusively owns design documents. **No code implementation is authorized by this document alone.**

## 10. Decisions not silently filled in

The following are engineering details or product limitations requiring validation or a targeted design decision, not invitations to relax approved invariants: precise native SQLite interception mechanism; projector-only native write token/authority boundary; full native API behavioral compatibility limits; SQL DECIMAL expression and bind/result matrix including division; durable directory-entry publication on each supported OS; timestamp out-of-range filename fallback; platform-specific key backup/unattended availability; and the original protocol's unspecified competing CREATE/INSERT events for one still-live entity GUID. Accidental UUIDv4 collisions are negligible under the project generator assumption; the existing block-identity dispute rule remains mandatory.

**Change control:** Any modification to signed-block bytes, event codes, identity semantics, authorization, supported ordinary SQLite SQL interface, or ledger-first publication requires separate Owner approval. Historical PostgreSQL-only constraints and AI-verifier fixture requirements are not inherited into this SQLite implementation solely by reference.
