# Database Distributed Ledger — SQLite Architecture and Implementation Plan

**Revision:** 2026-11-09 — Owner-approved new-instance onboarding, pinned genesis confirmation, Version 1 key-file/AAD byte grammar, and revised canonical ordering; previous ARCH-003/SEC-002 design retained.
**Authority:** Project Owner approvals confirmed through ARCH-001 and acknowledged by the Project Manager.  
**Status:** Approved direction with implementation details identified below as *proposed* or *to be validated*. No implementation is represented as complete.  
**License:** Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).

## 1. Scope and controlling documents

This document specifies the **current SQLite implementation direction** for [Database_Distributed_Ledger_Technical_Design.md](Database_Distributed_Ledger_Technical_Design.md). The October 23, 2026 integrated v7 design remains the source for the **Version 1 portable signed-block format and ledger semantics**, including genesis fingerprints, Ed25519 signatures, grant/revoke behavior, GUID identities, replay, and conflicts; **canonical order is amended by the Owner on 2026-11-09** as specified in Technical Design §4. This document supersedes **PostgreSQL-specific implementation choices**, not the underlying portable ledger protocol. The former PostgreSQL v0.4 management contract is retired as a live adapter specification; a SQLite-oriented replacement is [Distributed_Ledger_Management_API_Specification.md](Distributed_Ledger_Management_API_Specification.md).

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

**Approved storage:** One exact signed block per immutable `.BLK` file, with a separate advisory `.STAT` tree **using the same relative UTC-date path and basename** (per-instance roots in §8.4 and §12):

```text
<database-basename>/BLK/YYYY/YYYY-MM/YYYY-MM-DD/
    YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.BLK
<database-basename>/STAT/YYYY/YYYY-MM/YYYY-MM-DD/
    YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.STAT
```

`ssss` is four decimal digits of a second fraction at 1/10,000-second units; because Version 1 signed timestamps are milliseconds, the fourth digit is **0** (for example, `.123` seconds is `1230`). Use UTC, zero-padding, lowercase canonical GUIDs and SHA-512 hex, and filesystem-safe names. For signed int64 timestamps not representable as calendar dates on the host, use a documented safe, deterministic **non-calendar fallback** rather than rejecting otherwise valid signed blocks; its exact spelling is an implementation detail pending specification. Do not trust a filename as proof of block identity, signature, or timestamp; verify block bytes. The full-block SHA-512 suffix allows both authenticated variants of a disputed block GUID to remain retained.

**Durable publish sequence:** Create a unique temporary file on the destination filesystem; write complete original bytes; validate safely; flush file data; publish without overwrite using a same-filesystem atomic rename; ensure the directory entry is durable by a platform-appropriate mechanism; *then* acknowledge durable retention. Never expose partially written files as accepted `.BLK` entries. Duplicate identical bytes are idempotent. Different authenticated variants claiming one `(ledger_guid, block_guid)` are quarantined, never chosen by filename or arrival order. Paths and metadata are indexes, not signed protocol data.

`.STAT` is **replaceable diagnostic/cache metadata** with the same filename stem, never an input to block authenticity or the source of truth for current applicability. It may be absent, stale, or deleted. Its statuses may change after late history. Persistent projection generation and progress metadata must be committed **inside the same SQLite transaction as the corresponding official state change**. On recovery, derive statuses from signed blocks and current projection metadata and regenerate `.STAT` as necessary. Deleting all `.STAT` files does not itself roll back official state.

## 7. Reconciliation, crash recovery, and rebuilding

The ledger is immutable and can exist without SQLite. The initial projector may use **complete deterministic replay** from the pinned genesis and all retained relevant blocks; a later optimizer may use selective rollback/replay only if it yields the same state as a full replay. Canonical order is **`(timestamp_utc_ms, raw block_guid, raw signer_public_key)` ascending** (Owner-approved amendment 2026-11-09); it replaces the older signer-before-GUID interpretation. This is a semantic compatibility change even though signed block bytes and the Version 1 header remain unchanged. Authentication and authorization are evaluated at each block's canonical position, independent of import order. A late earlier block, including a grant or revocation, may require rebuilding and invalidating effects previously considered applicable.

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

### 8.3 Portable signing-key JSON representation — OWNER-APPROVED VERSION 1 FORMAT (2026-11-09)

**Authority boundary.** SEC-002 *already approves* an independently persisted Ed25519 identity for every initialized instance, AES-256-GCM obfuscation using a publicly embedded versioned 256-bit application-wide key, fresh nonces, unattended operation, and no silent replacement. The Owner **approved the exact Version 1 key-file JSON fields and the 88-byte AAD byte encoding** in §8.3 on 2026-11-09. Persistent `.DLID` / `instance.identity.json` exact JSON grammars in §8.4, optional recovery-phrase/backups, CLI spellings, and detailed migration mechanics remain design candidates unless separately frozen. All of these are local operational formats, never signed ledger-event bytes. These are local operational formats, never ledger-block fields; review with Security and QA before implementation.

**File:** `<database-basename>/signing-key.v1.json`. UTF-8 without BOM, exactly one top-level JSON object. Whitespace and property order have no semantic effect; duplicate property names, missing required fields, unknown fields for this version, invalid UTF-8, invalid JSON types, or extra trailing nonwhitespace data are errors. Names are case-sensitive; `version` is JSON integer `1` (not a string). Decode hexadecimal and Base64 strictly, with the exact decoded lengths below; use standard RFC 4648 Base64 with canonical padding and no whitespace. UUIDs use lowercase canonical hyphenated spelling, never the all-zero UUID, and are decoded into the 16 RFC 9562 network-order bytes. Bound input size to a documented safe limit; do not allocate unbounded memory for malicious JSON.

```json
{
  "format": "UDDL-ED25519-KEY",
  "version": 1,
  "instance_guid": "11111111-2222-4333-8444-555555555555",
  "public_key_hex": "<exactly 64 lowercase hex characters>",
  "wrap_key_id": "application-wide-v1",
  "encryption": "AES-256-GCM",
  "nonce_b64": "<standard Base64 encoding of 12 random bytes>",
  "ciphertext_b64": "<standard Base64 encoding of 32 ciphertext bytes>",
  "tag_b64": "<standard Base64 encoding of 16 authentication-tag bytes>"
}
```

The GUID in this example is **illustrative and must never be used as a fixed real instance ID**. The Ed25519 private signing material is exactly a 32-byte seed. Derive its 32-byte public key using the selected interoperable Ed25519 implementation and compare the exact raw public-key bytes with the expected values in **all three** persistent identity records before permitting signing. Never encrypt a variable-length PEM representation in this format. Key encryption uses one 32-byte wrapping key resolved by `wrap_key_id`, one freshly generated, cryptographically random **12-byte nonce per encryption**, 32-byte ciphertext, and a **16-byte GCM tag**. Never reuse a `(wrapping key, nonce)` pair; migrations and backups that re-encrypt must obtain a new nonce. The embedded key is available publicly, so GCM detects accidental corruption but does **not** establish tamper resistance against a deliberate holder of both the file and the published application key.

**Wrap-key selection:** The exact ASCII identifier `application-wide-v1` names a single, versioned 32-byte application-wide AES key compiled into the published application. Its actual 32-byte value belongs in the documented application source/build provenance, not in the private-key JSON. Unknown identifiers are **unavailable-key** errors, never reasons to substitute the latest key. All supported historical wrapping-key versions must remain decodable until their migration is safely completed. Do not claim that the publicly embedded key is secret.

**Exact Version 1 AES-GCM authenticated additional data (AAD), independent of JSON presentation:**

```text
ASCII("UDDL-ED25519-KEY-v1")             # 19 bytes; no NUL terminator
instance_guid                             # 16 raw UUID bytes, RFC 9562 network order
public_key                                # 32 raw Ed25519 public-key bytes
wrap_id_length                            # unsigned 16-bit integer, big-endian
wrap_id_utf8                              # exactly wrap_id_length UTF-8 bytes
```

Concatenate these fields **in exactly this order without padding or separators**. The `wrap_key_id` for Version 1 is the exact case-sensitive ASCII string `application-wide-v1` (encode those ASCII bytes as UTF-8). `wrap_id_length` counts its UTF-8 bytes, not characters. Do **not** hash the JSON, serialize a canonical JSON object as AAD, include `.DLDB` paths, or incorporate the ledger GUID/genesis binding into AAD: ledger binding is established *after* the instance key is persisted and must not require re-encrypting the key. The fixed AAD prefix binds the Version 1 format/encryption semantics; `instance_guid`, public key and wrapping-key identifier are also bound to the ciphertext. Verify the tag with this exact AAD **before** interpreting decrypted seed material; then rederive and compare the public key. Use a constant-time byte comparison where a library primitive is available; do not log plaintext seed bytes, recovered key material, or cryptographic buffers.

**Wire independence:** This private-key JSON/AAD format is not the ledger's Version 1 signed-block binary format and does not itself modify Ed25519 signing/verification or grant/revoke rules. **The Owner separately amended canonical event order** on 2026-11-09 as stated in Technical Design §4. A key-file copy does not establish master authorization; that remains a property of valid signed history.

### 8.4 Persistent identity, `.DLID`, pinned genesis and failure recovery

**Owner-approved location and relationship (ARCH-003, refined November 9):** Resolve the intended `.DLDB` main path (which may not exist in ledger-only Phase 1). Place the nonsecret descriptor with the **same basename** alongside it, and put **all instance-owned support files directly under a directory named after the database basename**. This **supersedes** the earlier sibling `<instance-guid>/` directory: the persistent instance GUID stays in metadata, not the directory name. No new global directory or nested GUID directory is introduced:

```text
ABC.DLDB                          # derived projection; may be absent/rebuilt
ABC.DLID                          # nonsecret, persistent instance descriptor
ABC/                              # database-basename directory (NOT GUID-named)
    instance.identity.json        # instance GUID, expected public key, pinned genesis
    signing-key.v1.json           # AES-GCM-obfuscated Ed25519 seed
    BLK/                          # immutable signed blocks (UTC date hierarchy)
    STAT/                         # replaceable processing metadata
    DEL1/                         # possible temporary retired STAT tree during cleanup
```

Both metadata files and the key file persist independently of `.DLDB` destruction/recreation. `ABC.DLID` binds the intended `ABC.DLDB` basename to the **persistent original instance GUID**; the corresponding support directory is `ABC/`, not a GUID-named folder. `.DLID` contains **no private key**, and none of these public specifications includes a developer's personal absolute path. Preserve the association while `.DLDB` is missing. A move/rename must explicitly and consistently relink the `.DLDB`, `.DLID` and same-basename support directory; never silently select or create another identity. Two different `.DLDB` basenames in one parent use distinct same-basename support directories, even if they hold separate or matching ledgers. Reject an existing conflicting same-basename directory or case-folding collision during preflight; never overwrite it.

**Exact `.DLID` and instance-identity JSON design candidates (not yet separately byte-frozen):** Each is UTF-8 (no BOM) with one object, required named fields, strict JSON type/length/casing validation, no duplicate or unknown fields in format v1, and independent of property order/whitespace. All GUIDs are lowercase RFC 9562 strings; `public_key_hex` and `genesis_sha512` are lowercase hex of **32 and 64 raw bytes**, respectively.

`ABC.DLID`:

```json
{
  "format": "UDDL-INSTANCE-DESCRIPTOR",
  "version": 1,
  "instance_guid": "11111111-2222-4333-8444-555555555555",
  "public_key_hex": "<64 lowercase hex characters>"
}
```

`ABC/instance.identity.json` on a new unbound instance:

```json
{
  "format": "UDDL-INSTANCE-IDENTITY",
  "version": 1,
  "instance_guid": "11111111-2222-4333-8444-555555555555",
  "public_key_hex": "<64 lowercase hex characters>",
  "key_file": "signing-key.v1.json",
  "ledger_binding": null
}
```

Once `ledger init` or `ledger join` establishes trust, persist **exactly one** binding **in that same `instance.identity.json`**, replacing `null` with:

```json
"ledger_binding": {
  "ledger_guid": "<canonical lowercase UUID>",
  "genesis_sha512": "<128 lowercase hex characters>"
}
```

These two values are the ledger's trusted GUID and **SHA-512 of the entire signed 211-byte Version 1 genesis block**. The `.DLID`, `instance.identity.json`, and signing-key file must agree on the original `instance_guid` and expected public key; the support-folder basename must match the intended `.DLDB` basename, not the GUID. Never infer a new binding from other `.BLK` candidates or silently accept a different genesis after `.DLDB` deletion/rebuild. Binding updates are an explicit, crash-safe state transition after verifying the intended authenticated genesis; if the `.BLK` genesis was retained but the binding commit failed, report **incomplete ledger initialization/join** and require checked recovery rather than publishing a fresh genesis or silently switching ledgers. The ledger GUID/genesis digest are public identity metadata, **not encryption secrets**.

**Crash-safe first-time initialization:** (1) Explicitly target a new instance/path and preflight files, aliases, permissions, and any existing `.DLID`, identity/key remnants; never initialize over them. (2) Generate exactly one new random instance GUID and Ed25519 seed in memory. (3) Publish the encrypted key file via a unique same-filesystem temporary file, flush, no-overwrite atomic rename and platform-appropriate directory durability. (4) Publish `instance.identity.json` in the same manner with `ledger_binding:null`. (5) Publish the adjacent `.DLID` **last**, with identical no-overwrite/durability rules. Only report successful initialization after re-reading and validating all three mutually consistent records and their durability. Use exclusive instance initialization coordination to prevent simultaneous creators; do not assume atomicity across the three individual files. Any interrupted stage (temporary files, orphan key/identity folder, existing `.DLID` with missing record) becomes an **incomplete initialization/recovery condition**, not an invitation to regenerate a new key or overwrite the evidence. Recovery may complete the *same* identity only after its key can be authenticated and its expected public key checked.

**Startup and key failure (Owner ARCH-003 override):** First validate `.DLID` ↔ identity metadata ↔ key file ↔ folder naming, the AES-GCM tag/AAD, the derived public key and pinned ledger binding (if present). Any missing descriptor, key, corrupt JSON, unknown file/wrap-key version, bad tag, wrong instance GUID, public-key mismatch or binding conflict enters **`recovery_required`** (draft public label). Existing-instance ordinary SQLite and ledger processing—including read-only SELECT, import/export, peer relay, replay and reconstruction—**remains paused** until the *original* identity is recovered. Status, diagnostics and explicit recovery/restore remain available. Deleting `.DLDB` cannot reset this gate. If all metadata was deliberately wiped, software cannot determine the former identity; only a deliberate **new instance** initialization with a new instance GUID is permitted, never an automatic replacement or a claim to restore the old instance.

**First opening of an unconfigured `.DLDB` (Owner-approved 2026-11-09):** If the `.DLDB` is demonstrably new and empty, has no same-basename `.DLID` or support directory, and no discoverable past identity/binding remnants exist, require **explicit interactive setup**: (a) initialize a new ledger with the newly persisted instance identity as genesis signer and report its new ledger GUID, or (b) join a specified existing ledger. In (b), accept the user's ledger GUID, locate/import the matching candidate genesis and display its **full 64-byte SHA-512 fingerprint** (128 lowercase hex digits); require explicit user confirmation against a trusted reference **before** writing the binding. Never interpret a GUID alone as genesis verification. If the signed genesis is missing or the fingerprint is not independently confirmed, keep the instance unbound and do not grant signing authority. The detailed CLI/GUI interaction and source of external trust evidence remain separately reviewable operational details.

**Existing instance with missing support:** If the `.DLDB` contains unrelated or previously projected state, any identity/projection marker survives, or `.DLID` / support files are missing or inconsistent for an existing instance, **do not** offer a normal new-ledger or join wizard as a shortcut. Enter `recovery_required`, suspend all ordinary SQLite/ledger work and preserve what remains. Only checked original-identity restore, diagnostics and status are available. A brand-new identity is permissible solely for a deliberately new instance, never to masquerade as recovery of the old one. When all metadata is missing, software cannot prove the distinction perfectly: require an explicit user declaration of *new instance* and reject detectable evidence of preexisting state. A support-directory move or database rename must relink the `.DLDB`, `.DLID` and basename directory as one validated association.

**Backup and restoration:** Backup the `.DLID`, `instance.identity.json`, and `signing-key.v1.json` together as *one identified instance set* when intentionally safeguarding identity; this is separate from normal `.DLDB`/ledger backups. Merely copying the encrypted seed file still exposes it to anyone who knows the public wrapping key. An optional portable recovery phrase or stronger independently encrypted backup has **no approved exact format** and is not part of normal startup. For explicit restoration, use the expected GUID/public key and ledger binding from independently retained identity records; decrypt and verify the candidate key, require its **exact original 32-byte public key**, and durably restore the missing record without changing identity/binding. A restored copy bearing an altered expected public key or different ledger/genesis must be rejected. If identity records are also missing, recovering an original identity requires explicit operator-provided, independently trusted identity evidence and a reviewed restore flow; do not quietly adopt a conflicting bundle.

**Format/wrapping-key migration:** Read older supported `format`/`version` and `wrap_key_id` exactly; never reinterpret unknown versions. To upgrade, decrypt using the identified old key, verify the original identity, re-encrypt the **same** seed using the new versioned wrapping key, new random nonce, and that version's specified AAD. Flush a new temporary key file and safely replace/activate it without losing the last valid recoverable old version. The `instance_guid`, derived Ed25519 public key, and pinned genesis binding **must not change**. If interrupted, startup must identify the fully valid old or new record and complete the same-identity transition explicitly; never use an incomplete record as authority. Keep old wrapping-key decoders available for existing files until migrations are verified; no forced migration during ordinary replay.

**Cross-platform and unattended:** The JSON/UTF-8, canonical UUID/hex, Base64, Ed25519-seed, AAD and AES-GCM bytes are independent of Windows, Linux, macOS, Android or iOS; use portable .NET 10 APIs where practical. No mandatory DPAPI/Keychain/Keystore, TPM/HSM, cloud or interactive password/PIN. Validate filesystem permissions and flush/rename semantics on each supported host; portable encoding does **not** guarantee every platform permits always-running background signing. The shared embedded wrapping key is obfuscation, not meaningful confidentiality against a determined attacker. No signing identity is stored in `.DLDB`, `.BLK`, `.STAT`, or source-controlled user data.

### 8.5 SEC-002 test obligations

QA and Security shall require **deterministic Owner-approved Version 1 key-file/AAD conformance vectors** using a nonproduction test wrapping key and seed. The AAD for `wrap_key_id=application-wide-v1` must be exactly **88 bytes** (19-byte domain prefix + 16-byte instance GUID + 32-byte expected public key + 2-byte big-endian string length + 19 identifier bytes), independent of JSON property order and whitespace. Changing any authenticated identity field must fail decryption; changing the nonce, tag or ciphertext must fail GCM authentication. Different nonce inputs must create distinguishable valid files with the **same expected public key**. Test invalid Base64, duplicate properties, wrong byte lengths, unknown format/version/wrapping key, truncated or corrupted writes and repeated initialization attempts.

Additional acceptance gates: (1) initial key creation on both master and replica survives restart and `.DLDB` deletion; (2) the matching `.DLID`, identity record and key file are verified before routine activity; (3) persisted ledger GUID and full genesis fingerprint survive reconstruction, and a mismatched genesis cannot silently bind; (4) crashes after each initialization file publication yield either a complete *same identity* or an explicit recoverable incomplete state, never a new identity; (5) missing/unavailable key **blocks every ordinary SQL and ledger operation**, while diagnosis and original-key recovery remain available; (6) restoration reproduces the exact original Ed25519 public key and does not change the pinned ledger; (7) replica promotion follows an authenticated `GRANT_MASTER` without key replacement; (8) migration after interrupted rewrapping preserves at least one recoverable original key and changes neither identity nor authorization; (9) Windows/Linux/macOS/Android/iOS-supported format decoding is byte-identical where implementable, with unattended operation and no mandatory platform secret provider; (10) private seed/plaintext never appears in diagnostics or normal backups. The original revoked/uncooperative/lost-master limitation remains unchanged. **QA execution and actual supported-platform coverage are not claimed by this design.**

## 9. Incremental delivery plan and quality gates

| Phase | Owner-approved sequence | Acceptance gate before advancing |
| --- | --- | --- |
| **1. Independent ledger** | Version 1 codec, exact signed bytes, genesis, grant/revoke, persistent identity, `.BLK` persistence, import/export/status and canonical history model; **no database dependency** | Deterministic protocol vectors, dispute/duplicate coverage, key lifecycle and durable `.BLK` crash tests |
| **2. Native SQLite SQL interception** | Native SQLite C API-compatible supported mutation requests, provisional test-only SQL evaluation, master-only signed durable block creation, replica write rejection and prohibition of direct official `.DLDB` changes | Every tested supported write either durably publishes authenticated block(s) or errors safely; official state remains unchanged; no bypass; rollback emits nothing |
| **3. Ledger-to-SQLite projection** | Customized `.DLDB`, projector-only physical writes, exact types, consistent SQL reads, transaction-coupled progress, `.STAT` regeneration, full replay and recovery | Stock SQLite rejects `.DLDB`; signed events alone materialize; exact types, restart/rebuild, WAL and read barriers verified; delayed visibility tested by observable checkpoint/status |
| **4. Peer synchronization** | Manual/import and configured peer exchange, missing-block reconciliation, independent offline masters/replicas and late-history convergence | Disconnected masters publish, replicas relay, canonical convergence follows differing block arrival order; no quorum required |

Engineering experiments may occur *within implementation work* and be reverted in Git if unsuccessful; no separate throwaway prototype is mandatory. The PM coordinates assignments and sequencing, the Developer owns all production code/build integration, QA owns test work under current permissions, Security reviews reliability, and the Architect exclusively owns design documents. **No code implementation is authorized by this document alone.**

## 10. Decisions not silently filled in

The following are engineering details or product limitations requiring validation or a targeted design decision, not invitations to relax approved invariants (see the approved layout/status/test policy in §§11–13 and approved key-file grammar in §8.3 and proposal evaluation in §§8.4 and 16): precise native SQLite interception mechanism; projector-only native write token/authority boundary; full native API behavioral compatibility limits; SQL DECIMAL expression and bind/result matrix including division; durable directory-entry publication on each supported OS; timestamp out-of-range filename fallback; precise locally serialized signing-key file grammar, identity manifest and migration (Architect candidate in §8; Owner review required); and the original protocol's unspecified competing CREATE/INSERT events for one still-live entity GUID. Accidental UUIDv4 collisions are negligible under the project generator assumption; the existing block-identity dispute rule remains mandatory.

**Change control:** Any modification to signed-block bytes, event codes, identity semantics, authorization, supported ordinary SQLite SQL interface, or ledger-first publication requires separate Owner approval. Historical PostgreSQL-only constraints and AI-verifier fixture requirements are not inherited into this SQLite implementation solely by reference.

## 11. Runtime location, opening modes, and path preflight — OWNER-APPROVED LAYOUT

**Approved November 9 (Owner revision to basename directory):** For a resolved intended file-backed main database `ABC.DLDB`, place `ABC.DLID` and the sibling **`ABC/`** support directory under the same parent. `ABC/` directly contains `instance.identity.json`, `signing-key.v1.json`, `BLK/` and `STAT/`; temporary `DELn/` retired status trees may appear during cleanup. **Do not also nest an `<instance-guid>/` directory.** The immutable GUID is stored in the `.DLID`, identity and key records. The `.DLID` anchors the database-basename-to-instance-GUID association; the **trusted ledger GUID/genesis SHA-512** remains in `ABC/instance.identity.json`. A `.DLDB` file may be absent in Phase 1 or after deletion, without changing that identity or trust. Multiple same-parent databases have different basenames and support directories. A renamed/moved database requires explicit checked relinking of the complete associated file/sidecar/directory set, never a silent new identity. Reject pre-existing unrelated `ABC/` content, collisions caused by filesystem case rules, and unsupported path/name forms before initialization. No `.uddl/`, `bindings/`, `instances/`, nested GUID folder, `ledger-binding.json`, lowercase `blocks`/`status`, or new `diagnostics/` folder is part of the approved layout.

**Open/create policy — interface details require Developer/QA validation:** Apply preflight before creating any `.DLDB`, key, descriptor or block. Resolve relative paths, `file:` URIs and VFS-specific names through an explicitly supported local-filesystem/VFS contract; use SQLite's resolved main filename (`sqlite3_db_filename`) as a post-open consistency check but do **not** open with create permission merely to discover a path. Ordinary file-backed SQLite open modes (`ro`, `rw`, `rwc`) are subject to the approved ledger/projector authority boundary; a replica cannot publish user mutations merely because a client requests read-write mode. `:memory:`, `mode=memory`, temporary unnamed databases and empty-name main databases have no durable `.DLDB` identity and **cannot be registered as persistent instances**; Phase 2 isolated test evaluation may use ephemeral storage. `immutable=1`, `nolock=1`, alternate VFS, `ATTACH`, backup destinations and incremental BLOB writes must not provide a direct physical write bypass. The exact supported open-mode and VFS matrix remains for review.

**Preflight paths and platform limits:** Validate the resolved parent, link/symlink/junction aliases, existing `.DLID`/instance remnants, case sensitivity, permissions, disk availability, local versus UNC behavior, and **maximum prospective paths** for `.DLDB`, `.DLID`, the database-basename folder, identity and key files, dated `.BLK`/`.STAT`, `DELn` rename/cleanup, temporary files, backups, SQLite `-wal`/`-shm`/journal suffixes. Both complete paths and **individual components** must be practical with the selected OS/API; Windows commonly imposes legacy total-path limits around 260 characters and per-component limits around 255 characters unless compatible long-path handling is enabled. The long full-SHA-512 `.BLK` suffix makes this important. Report actionable errors **before partial initialization**, indicating which derived path would exceed supported limits; do not silently shorten signed identities, discard digest suffixes, or create a new instance to work around a path failure. Never write any developer's local path into the public design or a signed ledger block.

**References:** [SQLite opening/URI modes](https://www.sqlite.org/c3ref/open.html), [resolved database filename](https://sqlite.org/c3ref/db_filename.html), [Windows path limits](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation).

## 12. Separate `BLK` and `STAT` trees and full-replay rotation — OWNER APPROVED

The two sibling roots under the database-basename support directory (for example `ABC/`) are **`BLK/`** and **`STAT/`**, each with the approved matching UTC date hierarchy when applicable. `.BLK` files remain immutable signed source history. `.STAT` files are disposable, potentially stale, and never authoritative; SQLite transaction-coupled projection progress controls state. Every `.STAT` file stem corresponds to its `.BLK` variant. No additional `status/generations` hierarchy or `status/current` pointer is approved.

A single instance **STAT manager** is the exclusive owner of writes, creation, rotation and removal of the *active* `STAT` folder. For **full replay**, freeze ordinary database/ledger activity before touching the status tree. **Before replay starts**, rotate the existing `STAT` directory to the first available `DEL1`, `DEL2`, etc., under the same instance root, using a collision-safe rename. If another application holds a handle/lock that prevents rename, show an actionable error naming the obstructed file/directory when reported by the OS, **remain fully paused**, and retry until the lock is released. Never begin replay while that rename remains blocked. After successful rotation, create a new `STAT` (also create it when missing in safe startup conditions), run authenticated deterministic replay, and schedule **asynchronous cleanup** of the old `DELn` tree. The STAT manager coordinates cleanup; background deleters may only operate on verified retired `DELn` trees, never the active `STAT` or `BLK`. Resume cleanup of safe remnants on restart. A cleanup lock on a retired tree need not block current replay after the initial rename has succeeded.

**Crash rules:** A crash before renaming leaves old `STAT` requiring rotation; a crash after renaming but before creating `STAT` allows the manager to create a fresh one while replay remains controlled. A crash during replay must never expose incomplete official `.DLDB` state; regenerated `.STAT` is not proof of projection application. Retry and error formatting, Windows handle diagnostics, durable directory rename/creation, and cross-process exclusive STAT-manager enforcement must be validated by Developer/QA/Security; do not invent a locked filename when the OS only supplies a directory-level error.

## 13. Upstream SQLite and ledger test classification — OWNER-APPROVED POLICY

**Never alter or delete upstream baseline tests to conceal failures.** Preserve pristine upstream SQLite 3.54.1 tests in source control and preserve a separately reproducible unmodified upstream baseline. QA proposes a named-case classification; Developer checks technical consequences; PM and Owner approve any exclusion or changed expectation **before** it becomes part of normal acceptance. Candidate categories:

| Class | Handling | Evidence required |
| --- | --- | --- |
| U0 — unchanged applicable | Run original upstream test unmodified | Pass/fail and environment |
| U1 — environment-sensitive | Run original unmodified in appropriate supported environment (e.g., native Windows shell for locking tests) | Reproduction, environment and rerun receipts |
| U2 — approved fork-inapplicable | Reversibly exclude from fork-specific acceptance **only after explicit review**; keep original test intact | Exact test IDs, policy rationale and no loss of applicable behavior |
| U3 — superseded behavior | Retain upstream test; add ledger-aware acceptance and document scoped conflict/replacement | Exact test mapping, approved product semantic difference and replacement coverage |
| U4 — unresolved failure | Do not silently exclude; keep failure open pending investigation | Logs, owner and remediation plan |

For each approved exclusion, version-control an explicit manifest with test identity, upstream source version, reason, new expected behavior, replacement test(s), platforms, approving Owner/PM and review date. Reversal must be simple and results must count excluded tests separately from passes. Do not change upstream assertions, retry limits or test durations as a workaround; environment-sensitive locking/concurrency acceptance belongs in a normal Windows terminal outside the Codex sandbox. Record antivirus configuration without asserting an unproved Avast cause.

**No sleep-based ledger visibility assertions (approved):** Delayed projection is permitted, so ledger-specific asynchronous tests must wait on explicit ledger/block durability signals, projection-state/generation/checkpoint notifications or bounded status polling, with a deadline and clear failure, **not arbitrary fixed sleeps** and not a claim that globally unknown remote history is complete. A Phase 2 test can prove a block is durably published and that official state stayed unchanged; Phase 3 tests can additionally assert eventual local checkpoint application and query barriers.

**Baseline qualification:** The Owner accepted the upstream SQLite 3.54.1 verification using the completed Windows release matrix plus a normal-PowerShell rerun of the sole environment-sensitive failing test job. This was **not** one clean full-suite invocation, and does not automatically qualify future fork modifications.

## 14. ARCH-003 decision record — approved operational layout and recovery gate

The approved `.DLID` mapping, instance-guid-identified identity and genesis binding are specified in §8.4 and §11. The approved single STAT manager, `STAT` → `DELn` full-replay rotation, external-lock pause, and asynchronous deletion appear in §12. The missing-key recovery gate is defined in §8.4. No signed Version 1 block **byte encoding** changes, while the Owner-approved canonical tie-order amendment is recorded in Technical Design §4; no repository runtime directory is created by these specifications. Path-length, blocked-handle reporting, process-lock coordination and JSON byte-format sign-off remain implementation/review obligations, not permission to invent an alternative folder layout.

## 15. Persistent ledger trust, accepted compromise risk, and proposed recovery

**Approved trust binding:** Persist the trusted ledger GUID **and** exact SHA-512 fingerprint of the pinned genesis block in `instance.identity.json` (already under the approved per-instance folder). This is **outside rebuildable `.DLDB`**, survives complete deletion/reconstruction, and may not silently change to another genesis or ledger. Identity recovery/restore must preserve the binding. See §8.4 for the proposed exact local JSON encoding; no new folder or signed-block field is introduced.

**Owner-accepted risk (Version 1):** A compromised authorized Ed25519 master key may sign blocks with backdated timestamps, including retroactive grants or authority-affecting history. Canonical replay can then revise other blocks' applicability or compromise the whole ledger's historical interpretation. The Owner accepts this risk under existing timestamp, grant/revoke and signing rules; **do not** introduce an unapproved clock cutoff, key-recovery override, checkpoint finality claim, or backdating restriction.

**Future capability only — NOT approved for implementation:** An operator may someday select historical data to reconstruct into an **entirely new ledger** with a fresh ledger GUID and pinned genesis binding, deliberately omitting or correcting undesired effects. The old signed history remains immutable and intact; nodes must explicitly opt into any replacement ledger. No automatic migration, deletion of old blocks, or reinterpretation of their signatures is approved. This is separate from rebuilding the same ledger's `.DLDB` projection, which must preserve all applicable authenticated history.

## 16. Hierarchical block-inventory reconciliation — EVALUATION ONLY; NOT OWNER-APPROVED

**Proposal under evaluation:** Preserve the approved `<database-basename>/BLK/YYYY/YYYY-MM/YYYY-MM-DD/` history tree; add **regenerable** per-directory inventory hashes, immediately send newly durable blocks **best-effort** to configured known peers, and periodically compare inventories to discover missing blocks after outages. Recommend standard **SHA-256** for folder summaries (portable .NET support); retain **full signed-block SHA-512** as the individual byte-variant identity. No signed-block format or mandated network protocol changes are authorized by this evaluation.

**Owner-preferred leaf data and order (November 9; inventory protocol still under evaluation):** For each daily `YYYY-MM-DD` folder, list the complete canonical `.BLK` **filename stems** (the full filename with **only the final `.BLK` extension removed**). Those stems already include the signed UTC timestamp, block GUID **and full serialized-block SHA-512**, which distinguishes different byte variants sharing a block GUID. Sort using deterministic **ordinal ASCII byte order** over validated portable canonical filenames, *not* locale-specific natural sorting; this sorting is for inventory only. It now agrees with the Owner-approved timestamp-then-block-GUID ordering for distinct normal block identities in a day, but **cannot be assumed identical** to full `(timestamp, block GUID, signer public key)` ordering when two disputed signed variants have the same block GUID or when filename digest rather than signer key resolves a tie. The inventory remains a set comparison, not canonical replay. Store the inventory-hash input as **binary length-delimited records**, not a CSV string or platform-default text encoding. Each stem should be encoded as its exact ASCII bytes with an unsigned big-endian length prefix; include an algorithm/version domain prefix and entry count. Hash the resulting bytes with SHA-256. At month, year and root levels, likewise sort canonical child-folder names by ordinal bytes and serialize each child's name **plus its 32-byte SHA-256 digest** with explicit lengths/counts and level-separated domain prefixes before hashing. The *exact* domain constants, integer widths, file name for the cache, record framing and protocol remain **proposed**, subject to Owner/Security/QA/Developer review; this paragraph does not approve peer-format implementation. File names/indexes are not cryptographic evidence: verify `.BLK` contents before accepting them as blocks. Consider signed int64 timestamps outside calendar range, safe temporary names, future opaque variants, excluded rejected diagnostics, and timestamp tie cases.

**Ledger identity correction:** The ledger GUID is 16 bytes and **is not** the genesis master's 32-byte Ed25519 public key. Genesis cryptographically binds the two. Every `.DLDB` instance has an independent identity and directory; compare hash roots across instances only when they share the **same pinned ledger GUID and complete genesis fingerprint**.

**Consistency and trust:** The hash files are *rebuildable caches*, never a new authoritative ledger or proof that globally unknown blocks do not exist. A peer root match is meaningful only when comparing the **same pinned ledger GUID and genesis SHA-512, inventory algorithm/version, consistent block-store snapshot and inclusion policy**. During arrivals and file publication, avoid displaying a transient mix of child/root generations as a settled match; publish complete inventory generations or retry comparisons on snapshot change. After a crash, rebuild stale hashes from actual durable `.BLK` files. Incremental recomputation is a performance optimization, not an authorization shortcut.

**Peer behavior:** After a durable locally produced block, push or announce its intact bytes to **known configured peers** without requiring their prior invitation; failures must not prevent the originating master from publishing independently. Periodic push/pull reconciliation walks unequal inventory branches down to exact missing `(ledger_guid, block_guid, sha512)` variants, then requests and independently verifies each block before applying any effects. Impose peer size/rate/CPU/memory limits, deduplicate broadcasts and avoid uncontrolled relaying. Replicas may relay, but a peer connection never confers signing authority. Equal roots prove only equivalence of the included local inventories—not global historical completeness or eventual network delivery.

**Candidate binary leaf layout for review (not frozen):** `ASCII("UDDL-INV-DAY-v1") || uint64_BE(entry_count) || each {uint16_BE(stem_ascii_length) || stem_ascii_bytes}`; reject malformed names or lengths instead of performing implicit text conversion. Parent-level inputs require their own distinct domain labels and length-delimited child names plus raw 32-byte SHA-256 digests. The digest summarizes a *set of durable filename-identified variants*, not a proof of their authorization, complete network history or file contents.

**Recommendation to PM/Owner:** Approve a separate, versioned inventory-cache format and a small initial configured-peer protocol only after Security/Developer/QA review of the canonical hash-input grammar, consistent snapshot publication, initial bulk synchronization and fallback timestamp handling. For now, **no new `TREE.HASH` file is authorized as an implementation requirement** and no peer-transport behavior is frozen.

## 17. Owner-approved November 9 interoperability and setup clarification

**Approved:** (1) explicit choice between fresh-ledger creation and existing-ledger joining only for a demonstrably new/empty unconfigured `.DLDB`; (2) original-identity recovery instead of silent reinitialization for previously configured/orphaned instances; (3) ledger-GUID discovery followed by explicit user confirmation of the full genesis SHA-512 before joining; (4) exact §8.3 Version 1 signing-key JSON/AAD layout; (5) canonical replay ordering `(timestamp_utc_ms, block_guid, signer_public_key)` instead of October 23 v7's signer-first tie rule. The key-file layout is independent of signed block bytes. **The canonical tie change is breaking for old implementations in the presence of equal timestamps**, so QA conformance tests must use the newly approved ordering and earlier vectors require review. The existing Version 1 outer signed-block `version=1` remains unchanged by Owner direction; no new version-tag or automatic historical migration is authorized.

**Not approved as new implementation requirements:** hierarchical peer inventory cache/protocol, specific user-interface wizard design/CLI flags, optional recovery-phrase encoding, or exact unreviewed `.DLID`/instance-manifest JSON byte-level freeze. Those remain proposals to be resolved before their respective implementation stages.
