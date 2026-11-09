# Distributed Ledger Management API — SQLite Candidate

**Revision:** 2026-11-09 — Owner-approved onboarding and fingerprint confirmation, exact Ed25519 key-file/AAD bytes and canonical-order amendment; SQLite management flags remain draft v0.5.
**Status:** Product requirements and underlying ledger invariants are Owner-approved; **exact new CLI flags, JSON field shapes, and exit codes are a draft pending separate review**, not a frozen interoperability contract.  
**License:** Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).

## 1. Scope and supersession

This document replaces the **PostgreSQL 18.6-specific v0.4 management adapter** as the current working interface specification. The previous `--pgdata`, `initdb`, PostgreSQL socket/process, populated-cluster adoption and AI-verifier-specific provisions are **retired**; they are not required of the SQLite implementation. Retiring those adapter-specific obligations **does not alter** Version 1 signed-block bytes, authenticated authority, `.BLK` exact-byte retention, `DDLB` transport, dispute policy or recovery guarantees defined in [Database_Distributed_Ledger_Technical_Design.md](Database_Distributed_Ledger_Technical_Design.md). **Separately, the Owner amended the canonical ordering rule on 2026-11-09.**

[SQLite_Architecture_and_Implementation_Plan.md](SQLite_Architecture_and_Implementation_Plan.md) controls SQLite file format, SQL boundary, signing-key storage and incremental delivery. The ledger operates **independently of a relational database**. Commands must report which components are available rather than imply a projection exists in Phase 1.

## 2. General CLI behavior — draft interface

**Proposed syntax:** `ledgerctl --instance-root ABS COMMAND ...`, with an explicitly selected local instance root. The name `ledgerctl` is retained from the original product design; spelling of `--instance-root` and subordinate options is proposed, not yet final-approved. On any platform, paths must be validated and contained within intended configured storage roots when appropriate. No PostgreSQL executable, daemon, port, user, or cluster is assumed.

Finite CLI commands should produce exactly one UTF-8 JSON object terminated by a newline on stdout; errors/diagnostics go to stderr. Proposed envelopes:

```json
{"ok":true,"data":{},"error":null}
```

```json
{"ok":false,"data":null,"error":{"code":"CODE","message":"Human-readable text","retryable":false,"details":{}}}
```

The raw signed bytes are **always transferred by files or binary peer streams**, not JSON reserialization. Canonical UUID strings are lowercase hyphenated; SHA-512 hashes and Ed25519 public keys are lowercase hex in JSON. Private signing material never appears in status, logs, or successful responses. Exact response schemas and exit codes require later freeze and automated validation.

## 3. Proposed commands and lifecycle

| Command family | Proposed operation | Meaning / required guard |
| --- | --- | --- |
| `instance` | `init`, `status`, proposed `recover` | Explicit `init` durably persists a new instance identity, even for replica; an existing unavailable identity enters recovery, never implicit reinitialization |
| `ledger` | `init`, `join` | New pinned genesis or binding to existing authenticated ledger GUID + full genesis SHA-512 |
| `key` | `status`, `backup`, `restore`; `create`, `import`, `select`, `clear` remain **unfrozen proposals** | `restore` of the **original** identity is allowed in recovery state after expected public-key verification; creation of a new identity only via explicit *new instance* initialization; selection never grants authority |
| `block` | `import`, `publish`, `get`, `export`, `status` | Store/get exact `.BLK` bytes; no SQL dependence; retain signatures |
| `projection` | `create`, `status`, `rebuild` | Available only after SQLite projector integration; `rebuild` blocks affected queries |
| `peer` | `add`, `remove`, `list`, `sync` | Available in peer phase; configured peers have no signing authority |
| `config` | `get`, `set` | Resource bounds, storage policies, peer limits, timeouts |
| `status` | consolidated | Binding, signing mode, retained blocks, current projection state if installed, configured peers |

**Ledger initialization:** On explicit first opening of a demonstrably new empty `.DLDB` lacking `.DLID`/support history, offer a user choice: **create a new ledger** or **join an existing ledger**. An existing/orphaned `.DLDB` with missing identity records instead enters `recovery_required`; it is never silently given a new identity. `ledger init` creates a new ledger GUID and self-signed **Version 1 211-byte genesis block**, stored durably as `.BLK`, using the **already persisted instance signing key** created by explicit `instance init`. It must not silently generate or substitute a key during ledger creation. Genesis authority arises solely from the authenticated block; no key selection or local admin command manufactures authority. A ledger can be initialized **before creating `.DLDB`**. `ledger join` requires the user-supplied ledger GUID, discovery/import and signature verification of a candidate genesis, **display of its full signed-block SHA-512 fingerprint, and explicit user confirmation against trusted expected identity** before persistent binding; the GUID alone is insufficient. Missing/unconfirmed candidate keeps the instance unbound. Joining an existing unrelated populated projection is not permitted. Exact UI and CLI flag syntax remain draft.

**Master and replica modes:** Every initialized instance owns an Ed25519 signing identity. An **available** instance key authorized under canonical history enables master user mutations; an available but unauthorized identity operates as a read-only replica. **An unavailable key is not an ordinary replica state:** it forces recovery pause before routine SQL, import, export, verification, replay, relaying or materialization can proceed. Diagnostics/status and explicit original-key restoration remain available. Healthy replicas can import, validate, store, export, relay and materialize signed history. The selected key may become unauthorized after later historical replay. Signing and publishing must obey authorization at the proposed block's canonical position.

**Projection initialization:** A brand-new empty `.DLDB` may be constructed from an authenticated pinned ledger, even after the ledger already has blocks. Rebuilding a derived projection must not modify ledger block history or delete signing keys. A status operation must distinguish `ledger_only`, `ready`, `reconciling`, `rebuilding`, and `failed` projections (the exact serialized names are proposed).

## 4. Signed-block import, storage, status and export

### Storage organization

The source of truth is the set of independently authenticated **exact-byte** `.BLK` files. **Approved:** keep them under `<database-basename>/BLK/` where `<database-basename>` is the name (without `.DLDB`) of the database file; matching advisory status resides under a separate sibling `<database-basename>/STAT/`, with identical relative UTC-date paths and filename stems:

```text
<database-basename>/BLK/YYYY/YYYY-MM/YYYY-MM-DD/
  YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.BLK
<database-basename>/STAT/YYYY/YYYY-MM/YYYY-MM-DD/
  YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.STAT
```

This filename layout is not a protocol encoding. Verify actual bytes even if the name appears valid. `ssss` is four-digit fractional seconds with final `0` for millisecond timestamps. Support a documented non-calendar fallback for valid signed int64 timestamps outside host calendar range. The SHA-512 suffix is over the **entire raw signed block**, including the signature, and distinguishes disputing variants of one block GUID.

**Proposed inventory design (NOT an approved peer protocol):** Daily leaf inventory hashing sorts exact `.BLK` filename stems (remove only the `.BLK` extension) by ordinal ASCII bytes and hashes a versioned, binary, length-delimited serialization with SHA-256. Parent folders hash sorted child-folder names together with raw child digests. Filenames include the full-block SHA-512 and thus distinguish disputed variants; cache files are rebuildable and never authoritative. Hash comparisons are meaningful only for the same pinned ledger GUID and full genesis SHA-512, not merely the same local folder name.

### Retention and durability

- Parse, bound, hash, and verify received blocks before official projection effects. Unknown-future-version or missing historical authority/dependencies may be retained safely as opaque/pending; neither can be applied prematurely.
- Write a unique temporary file in the target filesystem; flush exact bytes; publish with a safe atomic no-overwrite rename; ensure directory-entry persistence as supported on the host; only then report acknowledged durable receipt. Partial `.BLK` files must never be treated as accepted.
- Identical exact-byte blocks are idempotent duplicates. Distinct authenticated bytes sharing `(ledger_guid, block_guid)` are retained and **quarantined without a chosen winner**. The original Version 1 conflict policy remains in force.
- `.STAT` sidecars are **advisory, replaceable and potentially stale**. Rebuild them as needed; they never authorize materialization or override current canonical history. Projection generation/checkpoint records are committed in SQLite **with** official state changes.

A block's reported disposition follows the existing vocabulary `applied`, `pending`, `quarantined`, `opaque`, `rejected` with a machine-readable reason. **Retained**, **durable**, and **applied** are distinct. Imported history can remain pending while no `.DLDB` exists. A full replay or late earlier block can change statuses without changing original `.BLK` bytes.

### Import/export behavior

- Import exact single-block bytes or the existing `DDLB` container; validate framing, hash, signature and ledger identity safely. A partially completed batch retains independently durable preceding blocks; never claim all-or-nothing batch import.
- `block get` chooses by ledger/block GUID, and by full SHA-512 when identity is disputed; a disputed lookup without exact variant selection must fail clearly.
- Single-block export returns the **exact original signed bytes**. Batch export remains the existing Version 1 `DDLB` framing: `DDLB` magic (4 bytes), version uint16 big-endian, count uint64 big-endian, then count repetitions of `(length uint64 big-endian, original full signed block bytes)`; no re-signing or reserialization.
- Deterministic batch ordering uses updated canonical event order **`(timestamp_utc_ms, block_guid, signer_public_key)`** with full-block digest as tie-breaker when needed. Incremental missing-set exchange must compare hashes/IDs, not only timestamps.
- Limits on raw bytes, decoded bytes, import memory, CPU/time and peer resources remain required and configurable. Earlier PostgreSQL adapter exact min/max validation tuples and test-harness timing conventions are **not automatically inherited**.

## 5. SQL behavior, read barriers, and errors

Except in identity-recovery state, this CLI is an operational surface **alongside** ordinary SQLite C API/SQL access; it is **not a substitute** for standard supported SQL mutation submission. An authorized master's supported SQL mutation path publishes authenticated blocks first. Only the trusted projector changes official `.DLDB`. Unsupported SQL commands or alternate mutation interfaces must fail safely rather than bypass the ledger. A replica refuses user mutations.

Supported SQL reads observe a consistent locally projected state and may be network-stale. Affected SELECTs pause while an already-known reconciliation/rebuild would expose intermediate inconsistent state; they never wait for hypothetical unreceived remote blocks. A successful write publication acknowledgment need not make the value immediately SELECT-visible. `RETURNING`, transaction result tags, and partial-publication reports must describe what actually happened, without promising group-atomic multi-block commits.

**Important distinction:** The original PostgreSQL-specific SQLSTATE codes `ZL001`, `ZL002`, `ZL003`, `08007`, server error fields, `COPY FROM STDIN`, `COPY TO STDOUT`, and PostgreSQL `MERGE` requirements do not automatically apply to SQLite clients. The SQLite implementation needs an explicit, separately reviewed mapping to SQLite's result/extended-error APIs and normal SQLite transaction behavior. This specification **does not claim that mapping has been finalized**.

## 6. Private signing keys and recovery

**SEC-002 approved baseline (2026-11-08):** Every explicitly initialized instance, master or replica, generates an Ed25519 keypair using secure randomness and durably persists it before initialization succeeds. Private material resides in a separate **versioned key file**, encrypted at rest with **AES-256-GCM**, a **fresh random nonce for each encryption**, and a **versioned application-wide symmetric key embedded in the public source**. The embedded wrapping key is recoverable by anyone reading the source: this is **obfuscation against casual inspection, not protection against determined file holders**. Use a portable `ISigningKeyStore` abstraction and appropriate best-effort OS file permissions; no mandatory DPAPI, Keychain, Keystore, hardware service, cloud provider or startup password/PIN. Keep the key and nonsecret instance identity record outside `.DLDB`, `.BLK`, `.STAT`, repository and normal projection backups. The identity survives projection deletion/rebuild. Verification/replay are unattended and require only public keys; loss of the original private key triggers **recovery-required suspension of ordinary SQLite and ledger operations** (including read-only replay), never silent regeneration; status/diagnostics/original-identity restoration alone remain available. Optional disaster recovery must restore **the identical Ed25519 public key**. Exact file bytes and command flags remain unfrozen; the candidate representation is in [SQLite_Architecture_and_Implementation_Plan.md](SQLite_Architecture_and_Implementation_Plan.md) §8.

This project uses **digital signatures**, not private-key encryption of public ledger bytes. The ledger GUID is an identity, not a signing credential. A private key's mere possession is not canonical master authorization. Normal startup, replay and reconstruction **must not prompt** for a phrase, PIN, password or key-unlock action. Existing keys whose file is missing, malformed, undecryptable or whose derived public key differs from the persistent instance-identity record put the instance in recovery-required state; do not silently replace them or serve ordinary read-only SQL/ledger operations. Key rotation, restore and version upgrades must preserve identity unless the Owner separately approves an identity replacement operation.

## 7. Replay, status, and diagnostics

Once identity availability is verified (or the original identity has been restored), the projector must reproduce state from signed retained history including genesis, authority history, schema events and row changes. Full deterministic replay is an acceptable first implementation. Without the expected private key, ordinary replay is paused by Owner operational policy even though signature verification itself needs only public keys. It should guard affected reads and official writers, use SQLite-supported recovery lifecycle, and atomically persist projection progress alongside projected changes. Reconciliation/failure states must be visible through management commands and persistent diagnostics; a failed rebuild must never silently expose half-reconstructed state. Selective replay and zero-downtime projection swaps are future optimizations, not exceptions to full-replay correctness.

A status report should distinguish current canonical applied work from unapplied retained variants, pending authority/schema dependencies, disputed identities, and missing projection. The status is a local snapshot and **cannot prove global network completeness**.

## 8. Incremental implementation and contract freeze

1. **Independent ledger:** instance identity, genesis/keys, Version 1 codec, signing, verification, durable `.BLK`, import/export/status; no database required.
2. **Native SQL interception:** supported SQLite C API writes on authorized masters produce durable authenticated `.BLK` history or fail safely, while official `.DLDB` state remains **unmodified**. Limited isolated provisional SQL evaluation for tests may be used before any projector exists; no persistent alternate official store.
3. **Ledger-to-SQLite projection:** customized `.DLDB`, projector-only writes, exact types, `.STAT` regeneration, progress transactions, full rebuild and read consistency.
4. **Peer exchange:** configured peers, independent offline masters, missing-block reconciliation and eventual convergence.

**Before declaring the CLI stable**, freeze exact flags, output schemas, exit/error mapping, key-file and instance-identity metadata grammars/version migration, signing-availability diagnostics, durability-acknowledgment policy per supported filesystem, timestamp filename fallback, status sidecar format, and peer protocol. These operational details are *draft engineering obligations*, not changes to the Version 1 signed-wire format. Tests and implementation are assigned by the Project Manager to the Developer and QA under their respective permissions.

## 9. ARCH-003 recovery and path policy — operational decisions, exact API DRAFT

**Required recovery gate (approved):** At ordinary instance startup, check the persistent instance identity metadata and protected key file *before* enabling any SQL or routine ledger command. An existing identity whose key is missing, malformed, undecryptable or whose Ed25519 public key mismatches the recorded key enters `recovery_required` (proposed serialized status name). Reject ordinary `SELECT`, writes, ledger `block import/get/export/publish`, peer relay, projection, replay and rebuild. Allow nonmutating `instance status`, `key status`, recovery diagnostics and an explicitly requested restore of the **same original key identity**. A successful restore must derive the exact previously recorded public key; only then resume ordinary operations. A new identity is permitted exclusively through explicit creation of a genuinely distinct instance and may never be advertised as restoration of the old one. The error shape/code (e.g., proposed `SIGNING_IDENTITY_RECOVERY_REQUIRED`) and restore/backup flags require separate public review. No change to Ed25519 wire semantics.

**Instance placement — Owner approved (revised November 9):** For `ABC.DLDB`, the adjacent `ABC.DLID` maps to the persistent original instance GUID and the same-parent **`ABC/`** support directory. `ABC/` directly contains `instance.identity.json`, `signing-key.v1.json`, `BLK/`, and `STAT/` (with temporary `DELn/` retired-status directories). There is **no nested GUID-named directory**. Two different `.DLDB` files in one parent have their own distinct basename directories; preflight rejects conflicting names or unrelated existing content. The instance identity record retains the pinned ledger GUID and full signed-genesis SHA-512 outside `.DLDB`. A Phase 1 ledger-only instance may reserve the intended path without a database file; moving or renaming it requires an explicit identity-preserving relink. Reject impractical paths **before** creating any identity, descriptor or projection. Paths never appear in signed ledger bytes; §11 of the Architecture Plan covers the open-mode and path-preflight considerations.

**Separate status trees — Owner approved (2026-11-09):** The per-instance sibling directories are exactly `BLK/` and `STAT/`. During full replay the sole STAT manager first renames `STAT` to an available `DELn` (e.g. `DEL1`); ordinary operations are paused. An external rename-preventing lock blocks replay and exposes an actionable error until it clears; after successful rename create a fresh `STAT` and clean retired `DELn` asynchronously. `.STAT` remains advisory. See Architecture Plan §14.

**Test policy — Owner approved (2026-11-09):** preserve pristine upstream SQLite tests and classify any fork-specific exclusions/replacements by exact test identity and documented Owner/PM approval; use projection status/checkpoints with bounded waiting rather than arbitrary sleeps. See Architecture Plan §13.


## 10. Persistent-key and genesis-identity local contract — Approved key-file/AAD; metadata formats proposed

**Approved requirements:** Every explicit `instance init`, including one intended to become a replica, generates and durably stores exactly one Ed25519 identity before reporting success; AES-256-GCM uses a fresh nonce and a publicly embedded versioned application-wide key; unattended operation requires no mandatory OS keystore, PIN, phrase or cloud; loss of the original key halts **all ordinary** ledger/SQLite operations in recovery state without identity replacement. `ledger init` uses the already persisted key, never creates another silently. Key possession does not grant authorization.

**Approved on-disk placement:** Beside `ABC.DLDB` is `ABC.DLID`, identifying the persistent instance GUID and **same-basename `ABC/`** support directory, directly containing `instance.identity.json`, `signing-key.v1.json`, `BLK/` and `STAT/`. The folder name derives from the database basename; it does not replace the stored instance GUID. The nonsecret `instance.identity.json` also retains **`ledger_binding:{ledger_guid,genesis_sha512}`** after a verified join/init, where `genesis_sha512` is over the complete signed genesis block. `.DLDB` rebuild/absence cannot clear or silently alter that binding. Missing/mismatched `.DLID`, key, identity record or persistent genesis is a recovery error. A sidecar/key restore must match the **original** instance GUID and Ed25519 public key; a proposed new identity must use a distinct instance.

**Approved key-file/AAD:** The Owner approved the complete strict Version 1 signing-key JSON grammar, 32-byte seed, 12-byte random AES-GCM nonce, 16-byte tag, `wrap_key_id=application-wide-v1`, canonical Base64/hex and exact 88-byte authenticated additional data in SQLite Architecture Plan §8.3. The `.DLID` and `instance.identity.json` exact JSON byte grammar remains a design proposal in §8.4; no additional signed block bytes are changed. The proposed CLI restore/status errors, backup format, and wrapper-key migration flags remain unfrozen. A restoration of the original key must verify the exact expected public key **and** preserve the pinned ledger/genesis binding. Normal backup of `.DLDB` alone never backs up signing identity.

**Future feature and evaluation separation:** Selectively reconstructed *new* ledger history with explicit peer opt-in is a **planned, not implemented/approved recovery capability**. Per-directory SHA-256 inventory plus immediate best-effort peer push and periodic missing-set pull are **only evaluated proposals**, not frozen peer-management commands. See Architecture Plan §§15–16. No signed-block/Ed25519 **byte encoding** changes are introduced; the Owner independently approved the amended canonical block order.

## 11. November 9 approved onboarding, local key format and canonical replay

- **Open an unconfigured `.DLDB`:** a demonstrably new empty database with no previous identity traces uses explicit new-ledger/create-or-join onboarding; a previously initialized database with missing `.DLID`/support files enters `recovery_required` and cannot quietly acquire a new identity. The CLI/GUI wizard syntax is a proposal.
- **Join:** the user supplies the target ledger GUID; the service discovers/imports and validates its signed genesis, displays the **full-block SHA-512** and requires explicit user confirmation against independent trusted identity evidence before pinning the binding. No genesis available / no confirmation means no join.
- **Key record:** SQLite Architecture Plan §8.3's Version 1 key-file JSON and **88-byte AES-GCM AAD** are approved. The public embedded wrapping key remains obfuscation only. Descriptor/manifest exact byte format and optional backup/restore command spelling remain separately reviewable.
- **Canonical event ordering:** Owner-approved **`(timestamp_utc_ms, block_guid, signer_public_key)`** ascending, replacing the earlier signer-first tie rule. This affects interpretation and compatibility of equal-timestamp history but not Version 1 raw block bytes. Batch exports/replay and conformance vectors must use the same new order. No automatic migration or wire version bump is approved.
- **Peer inventory:** SHA-256 folder-summary serialization remains a proposal and not a required implementation feature. Filename alphabetical ordering is a separate set-inventory convention, not a substitute for canonical authorization/event ordering.
