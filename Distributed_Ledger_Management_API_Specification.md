# Distributed Ledger Management API — SQLite Candidate

**Revision:** 2026-11-08 — proposed SQLite adapter contract v0.5 with Owner-approved SEC-002 signing-key lifecycle  
**Status:** Product requirements and underlying ledger invariants are Owner-approved; **exact new CLI flags, JSON field shapes, and exit codes are a draft pending separate review**, not a frozen interoperability contract.  
**License:** Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).

## 1. Scope and supersession

This document replaces the **PostgreSQL 18.6-specific v0.4 management adapter** as the current working interface specification. The previous `--pgdata`, `initdb`, PostgreSQL socket/process, populated-cluster adoption and AI-verifier-specific provisions are **retired**; they are not required of the SQLite implementation. Replacing those adapter-specific obligations does **not** change the approved Version 1 signed-block wire format, authenticated authority, canonical ordering, `.BLK` exact-byte retention, `DDLB` container, conflicts, or recovery guarantees in [Database_Distributed_Ledger_Technical_Design.md](Database_Distributed_Ledger_Technical_Design.md).

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
| `instance` | `init`, `status` | Explicit `init` generates and durably persists the instance's own Ed25519 identity **even for a replica**; no SQLite required. Ordinary startup never regenerates an existing identity |
| `ledger` | `init`, `join` | New pinned genesis or binding to existing authenticated ledger GUID + full genesis SHA-512 |
| `key` | `status`, `backup`, `restore`; `create`, `import`, `select`, `clear` remain **unfrozen proposals** | Instance identity creation belongs to explicit first-time `instance init`. Import/restore must not replace an existing identity silently; selecting/clearing signing eligibility never deletes the instance identity or creates master authority |
| `block` | `import`, `publish`, `get`, `export`, `status` | Store/get exact `.BLK` bytes; no SQL dependence; retain signatures |
| `projection` | `create`, `status`, `rebuild` | Available only after SQLite projector integration; `rebuild` blocks affected queries |
| `peer` | `add`, `remove`, `list`, `sync` | Available in peer phase; configured peers have no signing authority |
| `config` | `get`, `set` | Resource bounds, storage policies, peer limits, timeouts |
| `status` | consolidated | Binding, signing mode, retained blocks, current projection state if installed, configured peers |

**Ledger initialization:** `ledger init` creates a new ledger GUID and self-signed **Version 1 211-byte genesis block**, stored durably as `.BLK`, using the **already persisted instance signing key** created by explicit `instance init`. It must not silently generate or substitute a key during ledger creation. Genesis authority arises solely from the authenticated block; no key selection or local admin command manufactures authority. A ledger can be initialized **before creating `.DLDB`**. `ledger join` selects a retained ledger and verifies its full genesis fingerprint; joining an existing unrelated populated projection is not permitted.

**Master and replica modes:** Every initialized instance owns an Ed25519 signing identity. A node whose **available** instance key is currently authorized under canonical history is a master for user mutations; otherwise it operates as a read-only replica. Replicas can import, validate, store, export, relay, and materialize signed history. The selected key may become unauthorized after later historical replay. Signing and publishing must obey authorization at the proposed block's canonical position.

**Projection initialization:** A brand-new empty `.DLDB` may be constructed from an authenticated pinned ledger, even after the ledger already has blocks. Rebuilding a derived projection must not modify ledger block history or delete signing keys. A status operation must distinguish `ledger_only`, `ready`, `reconciling`, `rebuilding`, and `failed` projections (the exact serialized names are proposed).

## 4. Signed-block import, storage, status and export

### Storage organization

The source of truth is the set of independently authenticated **exact-byte** `.BLK` files. Proposed on-disk names under UTC date directories:

```text
YYYY/YYYY-MM/YYYY-MM-DD/
  YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.BLK
  YYYY-MM-DD-HH-MM-SS-ssss-<block-guid>-<full-block-sha512>.STAT
```

This filename layout is not a protocol encoding. Verify actual bytes even if the name appears valid. `ssss` is four-digit fractional seconds with final `0` for millisecond timestamps. Support a documented non-calendar fallback for valid signed int64 timestamps outside host calendar range. The SHA-512 suffix is over the **entire raw signed block**, including the signature, and distinguishes disputing variants of one block GUID.

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
- Deterministic batch ordering uses canonical event order with full-block digest as tie-breaker when needed. Incremental missing-set exchange must compare hashes/IDs, not only timestamps.
- Limits on raw bytes, decoded bytes, import memory, CPU/time and peer resources remain required and configurable. Earlier PostgreSQL adapter exact min/max validation tuples and test-harness timing conventions are **not automatically inherited**.

## 5. SQL behavior, read barriers, and errors

This CLI is an operational surface **alongside** ordinary SQLite C API/SQL access; it is **not a substitute** for standard supported SQL mutation submission. An authorized master's supported SQL mutation path publishes authenticated blocks first. Only the trusted projector changes official `.DLDB`. Unsupported SQL commands or alternate mutation interfaces must fail safely rather than bypass the ledger. A replica refuses user mutations.

Supported SQL reads observe a consistent locally projected state and may be network-stale. Affected SELECTs pause while an already-known reconciliation/rebuild would expose intermediate inconsistent state; they never wait for hypothetical unreceived remote blocks. A successful write publication acknowledgment need not make the value immediately SELECT-visible. `RETURNING`, transaction result tags, and partial-publication reports must describe what actually happened, without promising group-atomic multi-block commits.

**Important distinction:** The original PostgreSQL-specific SQLSTATE codes `ZL001`, `ZL002`, `ZL003`, `08007`, server error fields, `COPY FROM STDIN`, `COPY TO STDOUT`, and PostgreSQL `MERGE` requirements do not automatically apply to SQLite clients. The SQLite implementation needs an explicit, separately reviewed mapping to SQLite's result/extended-error APIs and normal SQLite transaction behavior. This specification **does not claim that mapping has been finalized**.

## 6. Private signing keys and recovery

**SEC-002 approved baseline (2026-11-08):** Every explicitly initialized instance, master or replica, generates an Ed25519 keypair using secure randomness and durably persists it before initialization succeeds. Private material resides in a separate **versioned key file**, encrypted at rest with **AES-256-GCM**, a **fresh random nonce for each encryption**, and a **versioned application-wide symmetric key embedded in the public source**. The embedded wrapping key is recoverable by anyone reading the source: this is **obfuscation against casual inspection, not protection against determined file holders**. Use a portable `ISigningKeyStore` abstraction and appropriate best-effort OS file permissions; no mandatory DPAPI, Keychain, Keystore, hardware service, cloud provider or startup password/PIN. Keep the key and nonsecret instance identity record outside `.DLDB`, `.BLK`, `.STAT`, repository and normal projection backups. The identity survives projection deletion/rebuild. Verification/replay are unattended and require only public keys; loss of private key makes signing unavailable, never triggers silent regeneration, and does not prohibit safe read-only replay. Optional disaster recovery must restore **the identical Ed25519 public key**. Exact file bytes and command flags remain unfrozen; the candidate representation is in [SQLite_Architecture_and_Implementation_Plan.md](SQLite_Architecture_and_Implementation_Plan.md) §8.

This project uses **digital signatures**, not private-key encryption of public ledger bytes. The ledger GUID is an identity, not a signing credential. A private key's mere possession is not canonical master authorization. Normal startup, replay and reconstruction **must not prompt** for a phrase, PIN, password or key-unlock action. Existing keys whose file is missing, malformed, undecryptable or whose derived public key differs from the persistent instance-identity record are explicitly unavailable; do not silently replace them. Key rotation, restore and version upgrades must preserve identity unless the Owner separately approves an identity replacement operation.

## 7. Replay, status, and diagnostics

The projector must reproduce state from signed retained history including genesis, authority history, schema events and row changes. Full deterministic replay is an acceptable first implementation. It should guard affected reads and official writers, use SQLite-supported recovery lifecycle, and atomically persist projection progress alongside projected changes. Reconciliation/failure states must be visible through management commands and persistent diagnostics; a failed rebuild must never silently expose half-reconstructed state. Selective replay and zero-downtime projection swaps are future optimizations, not exceptions to full-replay correctness.

A status report should distinguish current canonical applied work from unapplied retained variants, pending authority/schema dependencies, disputed identities, and missing projection. The status is a local snapshot and **cannot prove global network completeness**.

## 8. Incremental implementation and contract freeze

1. **Ledger-only:** genesis/key selection, Version 1 codec, signing, verification, durable `.BLK`, import/export/status; no SQLite required.
2. **SQLite projector:** customized `.DLDB`, exact types, authenticated-only projection, `.STAT` regeneration, crash recovery and read consistency.
3. **Native SQL interception:** ordinary SQLite C API-compatible supported mutations, provisional evaluation, ledger-first publication, replica write rejection, secure official-write boundary.
4. **Peer exchange:** configured peers, independent offline masters, missing-block reconciliation and eventual convergence.

**Before declaring the CLI stable**, freeze exact flags, output schemas, exit/error mapping, key-file and instance-identity metadata grammars/version migration, signing-availability diagnostics, durability-acknowledgment policy per supported filesystem, timestamp filename fallback, status sidecar format, and peer protocol. These operational details are *draft engineering obligations*, not changes to the Version 1 signed-wire format. Tests and implementation are assigned by the Project Manager to the Developer and QA under their respective permissions.
