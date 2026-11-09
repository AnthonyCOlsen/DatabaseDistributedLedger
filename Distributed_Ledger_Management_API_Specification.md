# Distributed Ledger Management API — SQLite Candidate

**Revision:** 2026-11-09 — Phase 1 Owner-approved CLI/JSON/exit groups, identity recovery and OUTOFRANGE paths; signed protocol **1.01** and five SQLite-native classes; `.DLID` removed.
**Status:** Phase 1 command families and JSON/exit-code conventions Owner-approved; the precise per-command field schemas below specify the current implementation-facing contract for QA review. Later peer/SQL/STAT commands are not Phase 1 implementation requirements.
**License:** Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).

## 1. Scope and supersession

This document replaces the **PostgreSQL 18.6-specific v0.4 management adapter** as the current working interface specification. The previous `--pgdata`, `initdb`, PostgreSQL socket/process, populated-cluster adoption and AI-verifier-specific provisions are **retired**; they are not required of the SQLite implementation. Retiring those adapter-specific obligations does not change Ed25519 signing, authenticated authority, `.BLK` exact-byte retention, `DDLB` transport, dispute policy or recovery guarantees defined in [Database_Distributed_Ledger_Technical_Design.md](Database_Distributed_Ledger_Technical_Design.md). **Separately, the Owner approved a breaking signed-wire 1.01 value/schema grammar and amended the canonical ordering rule on 2026-11-09.**

[SQLite_Architecture_and_Implementation_Plan.md](SQLite_Architecture_and_Implementation_Plan.md) controls SQLite file format, SQL boundary, signing-key storage and incremental delivery. The ledger operates **independently of a relational database**. Commands must report which components are available rather than imply a projection exists in Phase 1.

## 2. General CLI behavior — Owner-approved Phase 1 conventions

**Approved Phase 1 CLI syntax:** `ledgerctl --instance-root ABS COMMAND ...`, with an explicitly selected local support root. The name `ledgerctl` and the command structure reflect the Owner-approved direction; command-specific extensions remain subject to later QA schema review. On any platform, paths must be validated and contained within intended configured storage roots when appropriate. No PostgreSQL executable, daemon, port, user, or cluster is assumed.

Finite CLI commands should produce exactly one UTF-8 JSON object terminated by a newline on stdout; errors/diagnostics go to stderr. Approved JSON envelopes:

```json
{"ok":true,"data":{},"error":null}
```

```json
{"ok":false,"data":null,"error":{"code":"CODE","message":"Human-readable text","retryable":false,"details":{}}}
```

The raw signed bytes are **always transferred by files or binary peer streams**, not JSON reserialization. Canonical UUID strings are lowercase hyphenated; SHA-512 hashes and Ed25519 public keys are lowercase hex in JSON. Private signing material never appears in status, logs, or successful responses. Exit-code groups and essential command response fields are specified in §3; strict complete machine-readable JSON Schemas and conformance vectors require subsequent QA review.

## 3. Phase 1 approved commands and lifecycle

**Global selector:** `ledgerctl --instance-root ABS COMMAND ...`; ABS identifies the intended same-basename support directory (`ABC/`), which can exist before `ABC.DLDB` is constructed. On CLI errors print one JSON failure object to stdout, details to stderr, and never fabricate successful durable outcomes. Unknown flags, out-of-range integers, invalid UUID/digest syntax and incompatible options fail `INVALID_ARGUMENT` without changing state. The approved command families and flags are:

| Command | Required / optional inputs | Expected `data.operation` and essential success output |
| --- | --- | --- |
| `instance init` | No required extras | `instance.init`: `instance_guid`, `public_key_hex`, `instance_root`; durably persists key and manifest before success |
| `instance status` | None | `instance.status`: `state`, `instance_guid` (if known), `public_key_hex` (if known), `signing_available`, `error` |
| `instance recover --from ABS` | Explicit recovery source | `instance.recover`: original `instance_guid`, `public_key_hex`, `recovered` |
| `ledger init` | None | `ledger.init`: `ledger_guid`, `genesis_sha512`, `genesis_block_guid`; use existing instance key, never create another |
| `ledger join --ledger-guid UUID --genesis-sha512 HEX` | Both required; importing/discovering genesis prerequisite | `ledger.join`: `ledger_guid`, `genesis_sha512`; specified digest is explicit trust confirmation |
| `key backup --directory ABS` | Destination | `key.backup`: `directory`, `instance_guid`; identity records preserved together |
| `key restore --directory ABS` | Source | `key.restore`: `instance_guid`, `public_key_hex`, `restored`; strict original-identity match |
| `block import --input ABS` | Raw `.BLK` or `DDLB` | `block.import`: `results`, `durable_count`; per-item result contains `ledger_guid`, `block_guid`, `sha512`, `status`, `reason`, `duplicate`, `durable` |
| `block get --id UUID --output ABS` | `--sha512 HEX` optional for disputes | `block.get`: `path`, `bytes`, `sha512` |
| `block export --output ABS` | Optional `--id UUID`, timestamp range or missing-hash filter | `block.export`: `path`, `bytes`, `sha512`, `block_count` |
| `config get` / `config set` | `set` requires nonempty documented resource-limit subset | `config.get` / `config.set`: `limits` |
| `status` | None | `status`: `instance`, `binding`, `signing`, `blocks`, `projection` (or absent/not installed), `peers` (or empty) |

**Approved outcome conventions:** Exit `0` success; `2` malformed argument/frame/signature/event/identity; `3` truthful `PARTIAL_IMPORT`; `4` identity/recovery/authorization/state preconditions; `5` configured time/resource limits; `6` I/O/internal failure. JSON failure is `{"ok":false,"data":null,"error":{"code":"CODE","message":"TEXT","retryable":false,"details":{}}}` and success is `{"ok":true,"data":{},"error":null}`, with command-specific `data` fields listed above. `retryable` describes whether the original condition might clear; it does **not** mean resubmitting SQL writes is safe. Codes include `INVALID_ARGUMENT`, `BAD_FRAME`, `HASH_MISMATCH`, `BAD_SIGNATURE`, `BAD_EVENT`, `UNSUPPORTED_VERSION` (exit 2); `PARTIAL_IMPORT` (3); `RECOVERY_REQUIRED`, `SIGNING_IDENTITY_UNAVAILABLE`, `IDENTITY_MISMATCH`, `DUPLICATE_INSTANCE`, `NOT_AUTHORIZED`, `ALREADY_BOUND`, `GENESIS_MISMATCH`, `BLOCK_NOT_FOUND`, `BLOCK_ID_DISPUTED`, `LEDGER_IDENTITY_MISMATCH` (4); `RESOURCE_LIMIT` and `PROCESSING_TIMEOUT` (5); `IO_ERROR` and `INTERNAL_ERROR` (6). Error `details` should include applicable `path`, `reason`, `instance_guid`, expected/observed public-key fingerprints, and processed block results without ever exposing private material. Exact closed per-command JSON Schemas can be released with QA vectors without changing these approved command/result conventions.

**Initialization:** `instance init` succeeds only for a genuinely new, explicitly initialized target after path/identity preflight. Write/flush/publish the encrypted key record first, then the nonsecret instance manifest; no automatic startup initialization. Any detected partial temporary/key/manifest remnants require explicit recovery. The key must be durably persisted before acknowledging initialization. `ledger init` then creates one durable **Version 1.01** self-signed 211-byte genesis signed by that saved identity. A crash after durable genesis but before manifest binding requires explicit reconciliation and binding recovery; never silently create a second genesis. `ledger join` requires previously imported/discovered verifiable signed genesis and the full expected SHA-512; a bare GUID never establishes trust. `instance.identity.json` persists the pinned ledger GUID and genesis fingerprint outside future `.DLDB` projection.

**Identity gate:** Missing, corrupted, mismatched or undecryptable key/manifest suspends all ordinary SQL and ledger processing, including reads, replay, imports, export and peer synchronization. Only `instance status`, `status` (diagnostic subset), `instance recover`, `key restore`, and safe read-only diagnostics are allowed. Restore must reproduce the original expected Ed25519 public key; duplicate active identity is rejected on the same host. Removing `.DLDB` must never remove the manifest/key. No `.DLID` exists. `key backup` is an explicit operation, not permission for ordinary processing while key is unavailable.

## 4. Signed-block import, storage, status and export

### Storage organization

The source of truth is the set of independently authenticated **exact-byte** `.BLK` files. **Approved:** keep them under `<database-basename>/BLK/` where `<database-basename>` is the name (without `.DLDB`) of the database file; matching advisory status resides under a separate sibling `<database-basename>/STAT/`, with identical relative UTC-date paths and filename stems:

```text
<database-basename>/BLK/YYYY/YYYYMM/YYYYMMDD/
  YYYYMMDD-HHMMSSssss-<block-guid>-<full-block-sha512>.BLK
<database-basename>/STAT/YYYY/YYYYMM/YYYYMMDD/
  YYYYMMDD-HHMMSSssss-<block-guid>-<full-block-sha512>.STAT
```

This filename layout is not a protocol encoding. Verify actual bytes even if the name appears valid. `HHMMSSssss` consists of UTC `HHMMSS` plus four fractional digits with the last digit `0` at millisecond resolution. Support a documented non-calendar fallback for valid signed int64 timestamps outside host calendar range. The SHA-512 suffix is over the **entire raw signed block**, including the signature, and distinguishes disputing variants of one block GUID.

**Owner-approved inventory approach (exact peer/cache byte grammar under review):** Daily leaf inventory hashing sorts exact `.BLK` filename stems (remove only the `.BLK` extension) by ordinal ASCII bytes and hashes a versioned, binary, length-delimited serialization with SHA-256. Parent folders hash sorted child-folder names together with raw child digests. Filenames include the full-block SHA-512 and thus distinguish disputed variants; cache files are rebuildable and never authoritative. Hash comparisons are meaningful only for the same pinned ledger GUID and full genesis SHA-512, not merely the same local folder name.

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
- Deterministic batch ordering uses canonical event order with full-block digest as tie-breaker when needed. Incremental missing-set exchange compares hashes/IDs of **active `.BLK` files**, not only timestamps; local-only `.XBLK` files are not exported or included in peer reconciliation.
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

## 9. ARCH-003 recovery and path policy — current Owner direction

Support folder `ABC/` beside intended `ABC.DLDB` contains **no `.DLID`**; `instance.identity.json`, `signing-key.v1.json`, `BLK/`, `STAT/` and temporary `DELn/` are the entire approved basic layout. Manifest identifies the stable instance and ledger/genesis binding. A fresh instance requires explicit `instance init`; if any existing identity evidence is incomplete/corrupt, the instance enters `recovery_required` and exposes original-identity restoration only.

Single STAT manager owns status mutations and full-replay `STAT` → next-unused `DELn` renaming. A filesystem lock preventing rename pauses ordinary processing with an actionable path error until it clears. `.STAT` is never authoritative over projection transaction progress. Windows 11 is the initial acceptance platform; documented SQLite upstream-test exclusions require Owner review.

## 10. Persistent signing-key and genesis identity — OWNER APPROVED

The Owner-approved **Version 1 key-file JSON/AAD** remains in SQLite Architecture Plan §8.3: 32-byte Ed25519 seed AES-256-GCM-obfuscated under publicly embedded `application-wide-v1`, fresh 12-byte nonce, 16-byte tag and exact **88-byte** binary AAD. This key-file version is **independent** of signed protocol 1.01. The nonsecret `instance.identity.json` holds instance GUID, expected public key, key-file name and nullable trusted `{ledger_guid, genesis_sha512}` binding, independently of `.DLDB` and signed `.BLK`. `.DLID` is removed; its information is merged into the manifest. No mandatory OS keystore or human unlock, and no silent key replacement. Expired/unavailable original keys enter recovery state; all ordinary processing pauses. Recovery remains an explicit operation with old public key/ledger binding verification.

## 11. Protocol 1.01 and Phase 1 interoperability

Signed ledger block header `version=0x0101` (major/minor one byte each); all `0x0101` row-value tags are **five native SQLite classes** (`0x00` NULL, `0x01` INTEGER, `0x02` REAL, `0x03` TEXT, `0x04` BLOB). Column definitions encode the actual declared type text, not historical `declared_type_tag` or DECIMAL parameters. The `0x0001` historical protocol is incompatible; never reinterpret its signed bytes under the 1.01 grammar. The independent `DDLB` transport batch version `0x0001` remains unchanged. Full signed genesis remains 211 bytes. Canonical event order is `(timestamp_utc_ms, block_guid, signer_public_key)`.

**Approved out-of-range timestamp fallback:** A signed int64 epoch millisecond value outside UTC years 0001–9999 maps to `BLK/OUTOFRANGE/T<16 lower hex>-<block-guid>-<full-sha512>.BLK`, where 16 hex digits are the signed timestamp's 64-bit pattern XOR `0x8000000000000000`. Mirror the stem in `STAT/OUTOFRANGE/`; include this branch in inventory reconciliation. Timestamp age alone never makes an otherwise authenticated block invalid.

## 12. Local `.XBLK` historical archive (Owner-directed) and approved projection-delay setting (2026-11-09)

**Compact local names — Owner-approved directory/file convention:** `BLK/YYYY/YYYYMM/YYYYMMDD/` and separate matching `STAT/YYYY/YYYYMM/YYYYMMDD/` contain `YYYYMMDD-HHMMSSssss-<block-guid>-<full-block-sha512>.BLK` and the same stem with `.STAT`. `HHMMSSssss` is derived from the signed UTC millisecond timestamp with the fourth fractional digit zero. The proposed invalidation archive uses `YYYYMMDD-HHMMSSssss-<block-guid>-<signer-public-key>.XBLK`, where the signer public key is 64 lowercase hex digits. File naming is metadata, not a signed wire change. Owner direction: no specialized same-timestamp/GUID/signer `.XBLK` collision feature is required for Version 1; rename operations must never overwrite an existing file and must preserve original bytes if a destination unexpectedly exists.

**Historical invalidation — Owner-directed:** When newly available earlier authority history makes a formerly authorized signed block inapplicable, retain its exact bytes locally but remove/invalidate its advisory `.STAT` through the STAT manager, rename `.BLK` to `.XBLK` without overwriting existing files, and reconcile projection through full replay (or future equivalent selective replay). While archived, `.XBLK` **never propagates**: exclude it from peer inventory, announcements, ordinary block export and peer service. Reevaluate archived files whenever canonical historical authorization changes. If a former `.XBLK` is authorized again at its signed position, validate the retained signed bytes, recover the full-block SHA-512, rename it back to the standard `.BLK` filename, update `TREE.HASH` and restore normal push/pull eligibility; materialization still follows normal projection rules. `.XBLK` is not a corrupt-signature category. A no-overwrite failure pauses the transition and reports an error without deleting retained bytes. This approved local-only exception means equality of advertised active inventories does not imply equality of locally archived historical bytes.

**Projection delay — Owner-approved behavior:** Expose configurable nonnegative `projection_delay_seconds`, **default 0**, delaying **only** ordinary row/schema projection until `signed_timestamp_utc_ms <= node_UTC_ms - delay_seconds*1000`, with overflow-safe arithmetic. Inbound verification, durable acceptance, current canonical authority evaluation and master-signing eligibility are not intentionally delayed. Report `pending_by_age` separately from `pending_authority`, `reconciling`, and `failed` (serialized field names are draft). Return consistent but intentionally stale SELECT results while eligible work is held; apply normal read barriers during replay. Re-evaluate cutoff safely when configuration changes; never promise finality, missing-peer completeness, or absence of late-block rebuild. Exact flag/JSON schema, maximum value and clock-jump behavior remain to be finalized by Developer/QA and approved before public interface freeze.
