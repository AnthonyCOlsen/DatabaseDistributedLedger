# Database Distributed Ledger — SQLite Architecture and Implementation Plan

**Revision:** 2026-11-09 — ARCH-003 approved implementation sequence and identity recovery policy; operational path/test/status alternatives are proposals. ARCH-002 SQLite 3.54.1 provenance and signed Version 1 ledger protocol retained.  
**Authority:** Project Owner approvals confirmed through ARCH-001 and acknowledged by the Project Manager.  
**Status:** Approved direction with implementation details identified below as *proposed* or *to be validated*. No implementation is represented as complete.  
**License:** Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).

## 1. Scope and controlling documents

This document specifies the **current SQLite implementation direction** for [Database_Distributed_Ledger_Technical_Design.md](Database_Distributed_Ledger_Technical_Design.md). The October 23, 2026 integrated v7 design remains the source for the **Version 1 portable signed-block format and ledger semantics**, including genesis fingerprints, Ed25519 signatures, canonical order, grant/revoke behavior, GUID identities, replay, and conflicts. This document supersedes **PostgreSQL-specific implementation choices**, not the underlying portable ledger protocol. The former PostgreSQL v0.4 management contract is retired as a live adapter specification; a SQLite-oriented replacement is [Distributed_Ledger_Management_API_Specification.md](Distributed_Ledger_Management_API_Specification.md).

**Non-negotiable guarantees:** ledger history is authoritative; signed blocks remain immutable and independent (not hash-linked); multiple authorized masters publish while disconnected; replicas verify and relay; all supported user modifications create durable authenticated block history *before* official relational changes; official state is a reconstructible deterministic projection; missing earlier blocks can change that projection; ordinary SQL reads need not await unknown remote history.

**Accepted initial threat boundary:** Changing SQLite's file header and extension deters ordinary external SQLite tools from accidentally opening or modifying the official database. It is **not** encryption, authentication, or protection against administrators or deliberately hostile local processes. The Project Owner explicitly does **not** require full ledger-versus-projection verification before each new block is signed. SQL mutation-path closure inside the supported fork remains mandatory.

## 2. Engine, fork, and distribution

- **SQLite baseline:** official complete SQLite 3.54.1 release (upstream C implementation), held as an internally maintained fork with a pristine upstream baseline commit and documented provenance. New application components target **C#/.NET 10 LTS**; Visual Studio 2026 Community is a supported development environment.

**Exact upstream source provenance (SQLite 3.54.1):**

| Attribute | Baseline reference |
| --- | --- |
| SQLite release | **3.54.1**, released 2026-10-16 |
| Official full canonical source archive | `sqlite-src-3540100.zip` (not the amalgamation archive) |
| Official archive URL | https://www.sqlite.org/2026/sqlite-src-3540100.zip |
| Archive size | **14,899,528 bytes** |
| **Archive SHA-256** | `387e7f6cad4797b17848c07d453dc6606917bb356a6cfe5a3abafd38c240c71b` |
| **Archive SHA3-256** (as published by SQLite) | `877c6f4996ce7adb368686bce21638de9e3d91534832ad3d9d119037d1bc22ad` |

The SHA3-256 is published on the [official SQLite download page](https://www.sqlite.org/download.html). The SHA-256 and byte count are independently recorded in the [APSW SQLite release checksum list](https://github.com/rogerbinns/apsw/blob/master/checksums) and FreeBSD's SQLite distribution metadata. These are **reference checksums for the original ZIP archive**, not checksums of the extracted tree or customized fork. The Developer must verify the actual downloaded source ZIP against these values before recording the imported baseline as verified; the documentation update does not constitute an inspection of the Developer's local imported archive. Preserve that verification result and document future native changes and upstream update decisions in repository provenance records.
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

**Owner-approved public behavior:** Keep ordinary SQLite API and SQL submission, including native `sqlite3_prepare*` / `sqlite3_step` client workflows. For **supported** `INSERT`, `UPDATE`, `DELETE`, `CREATE TABLE`, `ALTER TABLE`, `DROP TABLE`, transaction commands, and other supported mutations, the native library must cause the concrete effects to become signed ledger events **without directly changing official materialization**. A replica rejects writes. A master may publish blocks even while its projector is catching up or peers are disconnected, subject to local state/authority preconditions. **ARCH-003 delivery order:** native write interception is implemented in Phase 2, *before* the official projector in Phase 3. At that stage, supported SQL writes either produce valid durably stored signed blocks while leaving official `.DLDB` unchanged, or fail safely.

A managed C# gateway alone is **not** transparent to arbitrary native SQLite API clients; the Developer must establish a **native interception and official-write authorization boundary**, or obtain an explicitly approved revision to the compatibility guarantee. Ordinary clients must not gain projector write authority through a PRAGMA, special SQL statement, callback, alternate API, `ATTACH`, incremental BLOB write, backup destination, extension, trigger, or other supported path. Reject unsupported mutation paths rather than silently performing an unlogged change.

Reuse SQLite's native parser, planner, expressions, and transaction execution wherever safe. Provisional mutation evaluation is separate from official projection. `BEGIN`, savepoints, `ROLLBACK`, and `COMMIT` preserve the earlier ledger contract: rollback emits no blocks; final effective changed rows are emitted at COMMIT; multi-block publication **is not group-atomic**. If publication is partially durable, report partial or uncertain outcome honestly; never claim already durable blocks were rolled back. `RETURNING` and command counts are provisional and do not guarantee immediate `SELECT` visibility. PostgreSQL-specific `COPY` and `MERGE` syntax and SQLSTATE codes are not required SQLite features solely because the old adapter described them.

**Read behavior:** With a valid available instance identity and a completed consistent projector, SELECT may serve consistent, locally projected but network-stale data. Do **not** block awaiting unknown blocks on remote nodes. Once a locally known block starts reconciliation/replay that could expose inconsistent intermediate state, gate affected SELECTs until a consistent generation is available; an unsuccessful rebuild yields a clear error. Under sustained arrivals, reconcile finite batches and admit reads between consistent batches to prevent starvation. Handle active read transactions/prepared statements across projection generation changes explicitly.

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

## 8. Persistent signing identities and key storage — SEC-002 (OWNER APPROVED 2026-11-08)

### 8.1 Identity, lifecycle and authorization (approved requirements)

- **Every explicitly initialized instance, including a replica, generates its own Ed25519 keypair** with a cryptographically secure random generator. Its expected public key and private signing seed are durably persisted **before** initialization reports success. A new ledger uses the existing instance identity for self-signed genesis; a new replica joining an existing ledger keeps its independent identity, initially unauthorized to sign ledger changes. An authenticated `GRANT_MASTER` can authorize that existing identity later.
- Store the identity separately from `.DLDB`, `.BLK`, `.STAT`, the Git repository, and routine projection/ledger backups. **Never delete, regenerate, overwrite, or silently replace the identity during startup or rebuilding**. A database can be deleted or rebuilt without changing its signing identity. Normal startup/replay/rebuild are unattended, without a required phrase, PIN, password, manual approval, cloud service, or OS secure-store unlock.
- **Use Ed25519 signing and public verification.** A public key alone cannot sign; the ledger GUID is not a signing key. Possessing a private key does **not** grant master authority; canonical genesis/grant/revocation history exclusively determines it. Public-key verification/replay is cryptographically possible without a private key, **but ARCH-003 requires ordinary SQLite and ledger processing to pause in recovery state whenever the existing instance's private key is unavailable**. Do not continue normal read-only replica activity in that condition.
- Keep an instance-scoped `ISigningKeyStore`-style abstraction providing creation during explicit first-time initialization, load, public-key lookup, signing, backup/restore and diagnostic operations. Normal callers use an opaque instance/key handle and never receive private seed bytes. Separate non-destructive signing availability/selection from immutable instance identity.

### 8.2 Baseline confidentiality model (approved requirements)

The Version 1 default is a **portable encrypted key file**, not an OS-specific secret store. Encrypt Ed25519 seed bytes with **AES-256-GCM** using a **fresh cryptographically random nonce per encryption**, authenticated ciphertext/tag, and a **versioned application-wide 256-bit symmetric key embedded in publicly released source and binaries**. The key is publicly recoverable; this is **obfuscation against casual file inspection, not meaningful secrecy against a determined actor** who has the encrypted file and open-source application. Apply filesystem permissions where available and trust the host OS under the accepted Version 1 threat model.

**No mandatory** Windows DPAPI, Apple Keychain/Secure Enclave, Android Keystore, Linux Secret Service, TPM/HSM, cloud key management or interactive startup. These are optional future hardening. A hardware-specific provider is not required for portable Ed25519 support. The application must not misrepresent this file encryption as protecting against privileged host compromise or a holder of the encrypted file plus published wrapping key.

### 8.3 Proposed portable private-key file representation (NOT YET BYTE-FROZEN)

This is an **adapter-level implementation proposal**, not a new ledger block/event encoding or an approval of exact serialized bytes. Use a UTF-8 JSON object of explicitly named fields, with no embedded secrets outside authenticated ciphertext:

```json
{
  "format": "UDDL-ED25519-KEY",
  "version": 1,
  "instance_guid": "00000000-0000-4000-8000-000000000000",
  "public_key_hex": "<64 lowercase hex characters>",
  "wrap_key_id": "application-wide-v1",
  "encryption": "AES-256-GCM",
  "nonce_b64": "<12 random bytes, Base64>",
  "ciphertext_b64": "<32 encrypted seed bytes, Base64>",
  "tag_b64": "<16 authentication-tag bytes, Base64>"
}
```

The GUID shown is only a **schema example**, not a value that any implementation may generate as a real identity. Version 1 would encrypt a 32-byte Ed25519 seed (not the public key or arbitrary-length PKCS#8 text); derive its 32-byte public key using a well-reviewed Ed25519 implementation after decryption and require **exact equality** with `public_key_hex`. The proposed 96-bit nonce and 128-bit tag sizes use conventional AES-GCM parameters; enforce length checks and unique random nonce generation on every fresh encryption or migration. Do not treat the public, embedded `wrap_key_id` as a per-instance secret.

**Proposed authenticated associated data (AAD)**, independent of JSON whitespace/property order: ASCII literal `UDDL-ED25519-KEY-v1` followed by the instance GUID's 16 RFC 9562 bytes, the raw 32-byte expected public key, and a length-delimited UTF-8 wrapping-key identifier. This binds key-file metadata to the ciphertext; applications must agree on an exact byte grammar before implementing a long-lived file format. Always authenticate before using decrypted seed bytes. Future format/wrapping-key versions must either retain previous decoding support or provide explicit, crash-safe in-place migration **without changing the Ed25519 identity**.

### 8.4 Distinguishing first-time initialization from key loss

**Approved invariant:** Key loss is never permission for silent identity replacement. **Proposed mechanism:** Persist a small, nonsecret instance identity record (for example, `instance.identity.json`) separately from `.DLDB` and `.BLK`, with a version, instance GUID, expected public key and an opaque reference to the signing-key file. This is an application-controlled configuration record, not a signed ledger block, and it survives `.DLDB` recreation. Physical paths and filenames are **not yet frozen** and must not embed an individual developer's local directory.

- **Explicit new-instance initialization:** Only when no prior metadata/key remnants indicate an existing identity, create one new keypair; durably publish the key record, then the identity record; acknowledge initialization **only after both are persisted**. If a crash leaves an orphan key file or partial identity record, flag the incomplete initialization and recover/repair it explicitly rather than overwriting or silently generating a different key.
- **Ordinary restart:** Load the persistent identity record and matching key file **before enabling any ordinary SQL or ledger processing**. A missing/malformed/undecryptable key, bad tag, unsupported format, or derived-public-key mismatch yields explicit `recovery_required` (exact public error name pending review); do not regenerate. All ordinary SQLite and ledger operations (including SELECT, import/export/relay/replay and rebuild) remain paused; only status, diagnostics and explicit recovery of the **original** identity remain callable. Once the restored key reproduces the original expected public key, normal unattended operation may resume subject to canonical authority.
- **Database deletion/rebuild:** Never delete identity/key metadata as a side effect; preserve and reload the same signing identity. This applies whether the local database is master-authorized or only a replica.
- **All metadata lost:** Without any surviving identity evidence, no software can distinguish a completely wiped former instance from a genuinely new installation. Ordinary startup must not create a key. Only a deliberate **new instance** initialization may generate a new key, under a distinct identity; it must not be presented as restoration of the prior one. Detect remnants and require explicit recovery rather than silently reusing the old instance path.
- **Optional disaster recovery:** Accept explicitly restored seed material from a portable encrypted backup/recovery phrase only as a user-requested operation, and verify that the derived public key matches the original expected identity before activation. The precise backup format is separately reviewable and **never required for normal operation**.

Key-file and manifest publication must be crash resistant using platform-appropriate flush/rename semantics and should not rely on `.DLDB` commit progress. The public embedded wrapping key and expected public key must not appear in logs as *secrets*; never log decrypted seed/private key material. Stronger OS isolation remains optional hardening.

### 8.5 SEC-002 test obligations

QA and Security shall verify initial persistence on master and replica, restart and unattended signing, replica-to-master promotion on canonical `GRANT_MASTER`, `.DLDB` deletion/rebuild without identity replacement, **all ordinary SQL and ledger activity blocked during identity-loss recovery**, allowed status/diagnostic/restore only, exact original-key restoration, missing/corrupted key failures, AES-GCM tag validation, public-key identity matching, portable file decoding and format upgrades, and no secret leakage through logs or routine backups. Existing revoked/uncooperative/lost-master limitations remain part of the unchanged ledger protocol.

## 9. Incremental delivery plan and quality gates

| Phase | Owner-approved sequence | Acceptance gate before advancing |
| --- | --- | --- |
| **1. Independent ledger** | Version 1 codec, exact signed bytes, genesis, grant/revoke, persistent identity, `.BLK` persistence, import/export/status and canonical history model; **no database dependency** | Deterministic protocol vectors, dispute/duplicate coverage, key lifecycle and durable `.BLK` crash tests |
| **2. Native SQLite SQL interception** | Native SQLite C API-compatible supported mutation requests, provisional test-only SQL evaluation, master-only signed durable block creation, replica write rejection and prohibition of direct official `.DLDB` changes | Every tested supported write either durably publishes authenticated block(s) or errors safely; official state remains unchanged; no bypass; rollback emits nothing |
| **3. Ledger-to-SQLite projection** | Customized `.DLDB`, projector-only physical writes, exact types, consistent SQL reads, transaction-coupled progress, `.STAT` regeneration, full replay and recovery | Stock SQLite rejects `.DLDB`; signed events alone materialize; exact types, restart/rebuild, WAL and read barriers verified; delayed visibility tested by observable checkpoint/status |
| **4. Peer synchronization** | Manual/import and configured peer exchange, missing-block reconciliation, independent offline masters/replicas and late-history convergence | Disconnected masters publish, replicas relay, canonical convergence follows differing block arrival order; no quorum required |

Engineering experiments may occur *within implementation work* and be reverted in Git if unsuccessful; no separate throwaway prototype is mandatory. The PM coordinates assignments and sequencing, the Developer owns all production code/build integration, QA owns test work under current permissions, Security reviews reliability, and the Architect exclusively owns design documents. **No code implementation is authorized by this document alone.**

## 10. Decisions not silently filled in

The following are engineering details or product limitations requiring validation or a targeted design decision, not invitations to relax approved invariants (see the new operational proposals in §§11–13): precise native SQLite interception mechanism; projector-only native write token/authority boundary; full native API behavioral compatibility limits; SQL DECIMAL expression and bind/result matrix including division; durable directory-entry publication on each supported OS; timestamp out-of-range filename fallback; versioned portable signing-key file grammar, identity manifest and migration; and the original protocol's unspecified competing CREATE/INSERT events for one still-live entity GUID. Accidental UUIDv4 collisions are negligible under the project generator assumption; the existing block-identity dispute rule remains mandatory.

**Change control:** Any modification to signed-block bytes, event codes, identity semantics, authorization, supported ordinary SQLite SQL interface, or ledger-first publication requires separate Owner approval. Historical PostgreSQL-only constraints and AI-verifier fixture requirements are not inherited into this SQLite implementation solely by reference.

## 11. Runtime instance location, opening modes and path preflight — PROPOSED / OWNER REVIEW REQUIRED

**Location recommendation, not yet frozen:** Place application-managed runtime history and identity under the parent of the **resolved, file-backed main `.DLDB` path**, not under the source repository or a global machine-wide secrets directory. Use a per-instance GUID namespace, plus an independently persisted mapping from the canonical main-database path to that instance GUID. The binding/manifest and key must survive deletion of `.DLDB`. The path-based mapping is an **index**, not authority; it must not overwrite an existing identity or automatically create a new one after projection removal. Relocation to another drive/path needs an explicit relink procedure that verifies the original instance identity. For Phase 1 without a `.DLDB`, select/reserve an intended future main path or explicit instance root and reconcile that binding before Phase 3 creates the projection.

**Candidate conceptual layout (not a directory-creation authorization):**

```text
<resolved-main-parent>/
  <database-name>.DLDB                       # derived; may not yet exist
  .uddl/
    bindings/<hash-of-resolved-main-path>.json # local path -> stable instance GUID; proposal
    instances/<instance-guid>/
      instance.identity.json                 # nonsecret, independent of .DLDB
      signing-key.v1.json                    # SEC-002 seed; AES-GCM obfuscation
      ledger-binding.json                    # pinned ledger/genesis, if bound
      blocks/YYYY/YYYY-MM/YYYY-MM-DD/<UTC>-<block-guid>-<sha512>.BLK
      status/YYYY/YYYY-MM/YYYY-MM-DD/<UTC>-<block-guid>-<sha512>.STAT
      diagnostics/
```

Here sibling `blocks/` and `status/` trees are an **unapproved PM proposal**; retain the approved same-directory `.BLK`/`.STAT` sidecar layout if separate trees are not approved. `<instance-guid>` avoids collisions between multiple database instances in a common parent; canonical-path binding prevents silent reinitialization when `.DLDB` is missing. Runtime files are not source/repository files and are created by the Developer only in authorized implementation work.

**Open/create policy:** Apply preflight before writing any new `.DLDB`, key, manifest or block. Resolve relative paths, `file:` URIs and VFS-specific names through an explicitly supported local filesystem/VFS contract; use the native SQLite resolved `main` filename (`sqlite3_db_filename`) as a post-open consistency check, but do **not** rely on opening with create permissions merely to learn the path. Support existing ordinary file-backed SQLite open modes (`ro`, `rw`, `rwc`) only within approved authority rules; user SQL mutations on a replica fail even through a read-write SQLite connection. `:memory:`, `mode=memory`, empty-name temporary databases and anonymous temporary main databases have no stable path: **do not register them as official persistent `.DLDB` instances**. Isolated, ephemeral Phase 2 test evaluation may use them outside official storage. `immutable=1`, `nolock=1`, alternate VFS, `ATTACH`, backup destination and direct incremental BLOB writes must be rejected or safely isolated where they could bypass the ledger/write barrier; reading with special modes must not suppress required change detection. Exact supported open-flag/VFS matrix remains for Developer and QA to freeze.

**Preflight must reject impractical paths without side effects.** Validate parent exists or can safely be created only when authorized; canonical destination/path binding, aliases, symlinks/junctions, supported filesystem and local/UNC behavior, existing instance association, case sensitivity, ACLs, disk availability, permitted filename characters, filesystem component-length and total-path limits, and the longest prospective `.BLK`, `.STAT`, temporary-rename, `-wal`, `-shm` and backup suffixes. On Windows, ordinary `MAX_PATH`-limited operations may fail near 260 characters even when the filesystem allows longer names; extended paths need verified OS/application support and individual components commonly cap at about 255 characters. The long GUID-plus-128-hex-digest `.BLK` basename can make the full date-tree path exceed conventional Windows limits even for moderately short database paths. Do **not** silently shorten a signed identity or filename against approved conventions. Where supported long-path operations cannot be proven for every affected operation, fail before initialization with a targeted diagnostic stating the failed path, estimated required capacity and remediation (shorter main path or approved alternative layout). Paths remain local operational metadata, never published personal filesystem paths in documentation or ledger blocks.

**External references:** [SQLite connection and URI filename modes](https://www.sqlite.org/c3ref/open.html), [resolved connection filenames](https://sqlite.org/c3ref/db_filename.html), [Windows path-length limitations](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation).

## 12. Separate `blocks` and `status` trees — PM PROPOSAL, OWNER APPROVAL PENDING

**Baseline remains approved:** UTC-organized immutable `.BLK` files plus companion advisory `.STAT` files. The PM proposes placing the two kinds of files into **separate sibling per-instance trees**, with matching relative UTC date paths and exact same filename stems. This can make `.STAT` garbage collection simpler, but lengthens paths and adds directory/manifest management. It does not change signed block bytes or the meaning of `.STAT`.

**Candidate rebuild rotation:** Generate a new advisory status generation under `status/generations/<generation-id>/...`, retaining older generations until their readers finish. After the authenticated `.DLDB` projection and its checkpoint/generation commit atomically, switch a small `status/current` pointer using a validated single-file atomic replace/rename, **not a claimed atomic swap of two populated directory trees**. A crash at any step may leave a missing/stale pointer or unused generation: compare the generation with the committed SQLite checkpoint and regenerate or discard status metadata. SQLite projection progress and authenticated `.BLK` history always control truth. Asynchronously clean obsolete status generations after verifying no reader holds them; on Windows deletion/rename may be temporarily blocked by open handles, so retry safely and never delete blocks or active status. Directory-entry persistence and staging/cleanup path overhead require platform testing. This is a **candidate approach**, not a frozen interface.

**Owner decision requested:** Approve or decline separate sibling trees and optional generation-pointer rotation after QA, Developer and Security review. Until approved, avoid moving existing statuses or assuming the new status layout in production code.

## 13. Upstream SQLite and ledger test classification — PROPOSED POLICY

**Never alter or delete upstream baseline tests to conceal failures.** Preserve pristine upstream SQLite 3.54.1 tests in source control and preserve a separately reproducible unmodified upstream baseline. QA proposes a named-case classification; Developer checks technical consequences; PM and Owner approve any exclusion or changed expectation **before** it becomes part of normal acceptance. Candidate categories:

| Class | Handling | Evidence required |
| --- | --- | --- |
| U0 — unchanged applicable | Run original upstream test unmodified | Pass/fail and environment |
| U1 — environment-sensitive | Run original unmodified in appropriate supported environment (e.g., native Windows shell for locking tests) | Reproduction, environment and rerun receipts |
| U2 — approved fork-inapplicable | Reversibly exclude from fork-specific acceptance **only after explicit review**; keep original test intact | Exact test IDs, policy rationale and no loss of applicable behavior |
| U3 — superseded behavior | Retain upstream test; add ledger-aware acceptance and document scoped conflict/replacement | Exact test mapping, approved product semantic difference and replacement coverage |
| U4 — unresolved failure | Do not silently exclude; keep failure open pending investigation | Logs, owner and remediation plan |

For each approved exclusion, version-control an explicit manifest with test identity, upstream source version, reason, new expected behavior, replacement test(s), platforms, approving Owner/PM and review date. Reversal must be simple and results must count excluded tests separately from passes. Do not change upstream assertions, retry limits or test durations as a workaround; environment-sensitive locking/concurrency acceptance belongs in a normal Windows terminal outside the Codex sandbox. Record antivirus configuration without asserting an unproved Avast cause.

**No sleep-based ledger visibility assertions:** Delayed projection is permitted, so ledger-specific asynchronous tests must wait on explicit ledger/block durability signals, projection-state/generation/checkpoint notifications or bounded status polling, with a deadline and clear failure, **not arbitrary fixed sleeps** and not a claim that globally unknown remote history is complete. A Phase 2 test can prove a block is durably published and that official state stayed unchanged; Phase 3 tests can additionally assert eventual local checkpoint application and query barriers.

**Baseline qualification:** The Owner accepted the upstream SQLite 3.54.1 verification using the completed Windows release matrix plus a normal-PowerShell rerun of the sole environment-sensitive failing test job. This was **not** one clean full-suite invocation, and does not automatically qualify future fork modifications.
