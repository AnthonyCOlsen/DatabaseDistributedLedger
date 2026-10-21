# Database Distributed Ledger — Technical Design

**Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).**  
**Public repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger  
**Revision:** 2026-10-21 — creation-order naming and authority replay clarified; authorization clarifications and proposed deterministic schema/binary protocol details

**Status:** Independently developed, database-engine-agnostic architectural design. **CONFIRMED** denotes decisions accepted by the originator. **OPEN** denotes matters needing resolution before a fully interoperable implementation. The document is not a released engine or completed protocol.

## 1. Purpose and layer boundaries — CONFIRMED

Build a public-data, disaster-resilient, cryptographically authenticated distributed event ledger supporting an eventually consistent relational database on top. **The immutable ledger, not a relational database, is the authoritative data history.** The ledger may exist without a database. Only an **empty** database can initially adopt an existing ledger; it derives schema and data solely by consuming ledger events. Existing database SQL interfaces and SELECT/query facilities may be retained, but there must be no ordinary direct SQL-to-official-storage mutation path.

An authorized master receives SQL write requests, evaluates them against the relational state it currently knows (or provisional transactional state), and publishes the resulting **concrete atomic changes** to the ledger before they are ever applied to official materialized storage. The same ledger consumer then applies locally produced blocks as it does remotely received blocks. A successful write acknowledgment can precede local SELECT visibility; client developers must accommodate stale reads. The database and application layers may implement stronger transactional protocols, but the underlying ledger neither promises global synchronous agreement nor interprets a client's business transaction as an atomic group.

**Every block is independent. No block references any predecessor, previous block, head, fork, or chain link.** “Blockchain” is a descriptive term for the authenticated historical collection and must not be interpreted as a hash-linked chain. There is no fork-choice election or dropping otherwise valid blocks to choose a preferred branch. A single logical order is calculated over the accepted blocks presently known to a participant.

## 2. Participation, identity, and authority — CONFIRMED

- **Ledger GUID:** immutable globally unique identifier shared by every block belonging to one ledger.
- **Block GUID:** immutable identifier of an individual block, independent of its ledger GUID.
- **Master:** participant holding an authorized Ed25519 private signing key and permitted to publish blocks and delegate master authority.
- **Read-only replica:** anyone may join, verify, retain, exchange, and materialize blocks without permission or a signing key. A replica may relay a block to a master even when their respective originating masters cannot directly communicate.
- **Entity GUIDs:** every table, column, and row has its own immutable GUID, independent of SQL-visible names or declared keys. Names may change without changing identity. A deleted GUID cannot be reused; recreating a same-name entity uses a new GUID.
- **Genesis master:** initial sole signing authority established by the special genesis block; other masters are added with ledger events.

**Grant:** any currently authorized master may issue a `GRANT_MASTER` event identifying a new public key. The grantee's authority is effective only at or after the grant's canonical position, not retroactively. A block timestamped before its grant remains unauthorized even if received after the grant.

**Revocation:** `REVOKE_MASTER` requires approval from **the master being revoked and one other currently authorized master**. **CONFIRMED:** One party signs the ordinary outer block; the **other party’s Ed25519 signature is contained inside the event payload**. The effective canonical revocation point controls authorization: previously valid blocks remain historically valid; later blocks from a revoked authority cease to be authorized, even if previously applied. Reprojection removes unauthorized effects while retaining historical bytes. Invalidating an authorization link also invalidates authority and dependent effects derived exclusively from that link. The intended policy is that at least one authorized master must remain; the precise machine-checkable constraint is **OPEN**. The older idea of signing only the fixed ASCII phrase `I AGREE` is **NOT YET APPROVED for an interoperable revocation format**: because that phrase is reusable, the second signature should bind to the specific proposed revocation. See Section 6.

**CONFIRMED latest authority clarification:** Every authorized master may grant a public key. A key can be granted and revoked multiple times. Evaluate blocks in canonical order and accept a signer only when currently authorized at that position. An authorization and grants dependent on it must be re-evaluated when previously unknown earlier events change the granting chain. A key lost to disk failure does not magically make the ledger recoverable; if no signing private keys survive, recovery may require independently creating a new ledger. **OPEN:** Multiple simultaneous grants for one key, whether a revocation targets a single grant or all grants for that key, grantor-cascade timing, and the exact mechanical rule for preserving at least one authorized master.

**OPEN:** Precisely define validity of authorization when an earlier granting event is invalidated, the signed revocation intent and encoding, tie semantics within one millisecond, and enforcement of the one-master-minimum invariant. There is no first-version special remedy for unauthorized-key compromise beyond evaluating standard signature and authorization rules.

## 3. Genesis and database initialization — CONFIRMED intent, OPEN trust details

On creation, each **empty** database must choose one of two paths:

- **Create a new ledger:** generate a new ledger GUID and Ed25519 keypair, become the first authorized master, and publish a valid self-signed genesis block.
- **Join an existing ledger:** select an **existing local ledger** identified by its ledger GUID. Adopt its already-established genesis authority and reconstruct the database solely by replaying authenticated ledger events. A populated unrelated database cannot join.

**CONFIRMED:** A genesis block uses the same fixed binary envelope as other blocks but has an **empty payload** (`payload_size=0`, compression `0x00`, SHA-512 of the empty bytes). Its outer Ed25519 signature is verified against the public key in the header, which becomes the initial authorized master key. Ordinary non-genesis blocks may not claim genesis status merely by being self-signed.

**CONFIRMED trust safeguard:** When joining, pin both `ledger_guid` and the **SHA-512 fingerprint of its unique genesis block**, rather than trusting any self-signed block that claims the same GUID. If conflicting genesis blocks claim an identical ledger GUID, do not silently choose one. A ledger GUID identifies the intended chain, but alone does not cryptographically prove that a particular genesis is the user's intended one.

**OPEN:** The exact user interface for selecting a ledger and the handling of conflicting same-GUID genesis records; protocol-version semantics for the special empty-payload genesis. The double identifier (ledger GUID and genesis SHA-512 fingerprint) is approved.

## 4. Canonical chronological order and clocks — CONFIRMED

Accepted blocks are ordered by the tuple `(timestamp_utc_ms, signer_public_key, block_guid)` ascending. Timestamps are signed 64-bit integers representing **milliseconds since 1970-01-01T00:00:00Z**. Compare public keys using their **raw 32-byte Ed25519 encoding** as unsigned lexicographic bytes, not textual Base64. Compare block GUIDs using their canonical 16-byte binary representation. Masters should synchronize clocks frequently against an agreed trustworthy UTC reference, preferably atomic-clock-backed. A signer should not intentionally issue more than one block in a millisecond; if needed, advance to the next millisecond. The block GUID serves as a tertiary tie-break if collisions occur anyway.

No block is rejected solely for its timestamp appearing far in the future or past. A block with an incorrect clock is processed according to its declared signed timestamp, **subject to authorization at that canonical position**. Arrival order never defines canonical order. There is no general guarantee that a participant knows all earlier blocks; late earlier blocks may revise derived state.

**OPEN:** Leap-second representation and operational time-sync procedures; timestamp ties involving authority operations require the precise canonical authority semantics above. No clock-skew quarantine is required.

## 5. Custom binary block wire format — CONFIRMED

Blocks are raw binary, not JSON, Base64, CBOR, or serialized SQL. The layout is packed, with no alignment padding. Multibyte integers use **big-endian** byte order and GUIDs use canonical RFC 9562 byte ordering. Fields are laid out sequentially:

| Field | Encoding | Bytes | Zero-based offset |
|---|---|---:|---|
| `version` | unsigned 16-bit | 2 | 0–1 |
| `ledger_guid` | UUID binary | 16 | 2–17 |
| `block_guid` | UUID binary | 16 | 18–33 |
| `timestamp_utc_ms` | signed 64-bit epoch milliseconds | 8 | 34–41 |
| `signer_public_key` | Ed25519 raw public key | 32 | 42–73 |
| `payload_sha512` | raw SHA-512 digest | 64 | 74–137 |
| `payload_size` | unsigned 64-bit byte count | 8 | 138–145 |
| `payload_compression_type` | unsigned 8-bit | 1 | 146 |
| `payload` | transmitted raw payload bytes | N | 147 through 146+N |
| `signature` | Ed25519 signature | 64 | 147+N through 210+N |

The header length is **147 bytes**; the signature is **64 bytes**; each entire block is **211 + N bytes**, with `N = payload_size`. `version` uses 2 bytes, and `payload_size` uses 8 bytes, permitting values beyond 4 GiB subject to practical resource restrictions. The payload field is not Base64 encoded. 

**Hash:** `payload_sha512 = SHA-512(exact transmitted payload bytes)`; this includes any internal compression properties within those bytes. **Signature:** Ed25519 signs the complete exact serialized block bytes from `version` through the last byte of `payload`, excluding the trailing 64-byte signature. The verified signature authenticates header, timestamp, signer's public key, compression method, payload length, hash, and payload. Ed25519 is a signature scheme, **not symmetric encryption**; the data are public. AES-256-CBC is not used to authenticate blocks.

**Compression type:** `0x00` = uncompressed raw payload; `0x01` = raw LZMA2 compressed stream, not a `.7z` archive. For `0x01`, the first byte **inside the variable payload** carries the LZMA2 dictionary decoder property, followed by the raw LZMA2 data stream, and is counted in `payload_size`. Compression IDs 2–255 are reserved for future specifications. Compression is optional, particularly for very small payloads.

**OPEN:** Exact raw LZMA2 framing and property validation, uncompressed-size and expansion safety limits, unknown versions/compression handling, and the independent identity/hash used for peer inventory. The raw payload's **event body format** is still to be specified; the fixed-width outer format alone does not define interoperable event encoding.

## 6. Binary event payloads — CONFIRMED semantics, OPEN exact encoding

Each non-genesis block normally describes one independently meaningful **typed ledger event**, not arbitrary source SQL text. The master evaluates SQL using its currently known local database state and captures the resulting change. For row DML, the smallest atomic event is **one changed row per block**. A statement modifying 100 rows therefore emits up to 100 independent blocks. Zero rows affected means **zero change blocks**. Multi-row and multi-statement operations are not an all-or-nothing ledger unit.

Row events need to represent `INSERT_ROW`, `UPDATE_ROW`, and `DELETE_ROW`, using the **row GUID and table GUID** as their identity. **UPDATE_ROW records only the columns actually changed**, each identified by **column GUID**, together with its resulting value; it does not replace unrelated fields of the row. SQL-visible table and column names are not used as persistent identity or as the normal lookup keys in ledger row changes. Insert events must provide enough values/identities to build the row. An update that refers to a column renamed elsewhere still operates on its GUID.

DDL/administrative events need to express, at minimum, `CREATE_TABLE`, `ALTER_TABLE`/column creation or modification, `DROP_TABLE`, `CREATE_COLUMN`, `DROP_COLUMN`, `RENAME_TABLE`, `RENAME_COLUMN`, column-type changes, `GRANT_MASTER`, and `REVOKE_MASTER`. The ledger must reconstruct both schema and data. The originator prefers one smallest practical atomic change per block; row changes must be one-row atomic.

### 6.1 Compact payload grammar — approved shape; remaining details OPEN

**CONFIRMED:** A block’s uncompressed event body is binary, not SQL; row UPDATE events identify only the columns actually changed by GUID. **CONFIRMED:** Put a `uint8 event_kind` first in every non-genesis uncompressed event body. The chosen outer compression method compresses this *entire event body*; it does not change the body schema.

| Event code | Proposed event | Uncompressed body after the one-byte code |
|---|---|---|
| `0x01` | `INSERT_ROW` | `table_guid[16]`, `row_guid[16]`, `column_count[varuint]`, repeated `(column_guid[16], typed_value)` |
| `0x02` | `UPDATE_ROW` | Same identifiers and repeated **changed columns only**; no implicit changes to other columns |
| `0x03` | `DELETE_ROW` | `table_guid[16]`, `row_guid[16]` |
| `0x10` | `CREATE_TABLE` | `table_guid[16]`, name, **the complete initial column list**, each with its immutable GUID and full definition (exact attribute encoding OPEN) |
| `0x11` | `DROP_TABLE` | `table_guid[16]` |
| `0x12` | `RENAME_TABLE` | `table_guid[16]`, new name |
| `0x13` | `ADD_COLUMN` | `table_guid[16]`, `column_guid[16]`, name and type/metadata definition |
| `0x14` | `DROP_COLUMN` | `table_guid[16]`, `column_guid[16]` |
| `0x15` | `RENAME_COLUMN` | `table_guid[16]`, `column_guid[16]`, new name |
| `0x16` | `ALTER_COLUMN` | `table_guid[16]`, `column_guid[16]`, changed schema attributes |
| `0x20` | `GRANT_MASTER` | `grantee_public_key[32]` (grant-lineage identifier, if needed, OPEN) |
| `0x21` | `REVOKE_MASTER` | revoked key, second signer public key, second signature bound to this revocation |

`varuint` is **minimal-length unsigned base-128 variable-length integer** (ULEB128; no redundant leading encodings). Length-prefixed text is `varuint byte_length || UTF-8 bytes` (no zero terminator). Length-prefixed BLOB is analogous. Raw GUIDs are 16 bytes in canonical UUID ordering. To prevent multiple valid byte encodings of the same logical row mutation, sort its changed-column entries by ascending raw `column_guid` and reject repeated column GUIDs within an event. This payload encoding does not automatically standardize SQLite/other-engine expression evaluation.

### 6.2 Binary value types and NULL semantics — CONFIRMED Version 1 encoding

**CONFIRMED:** Version 1 supports all nine value types below. The outer block uses raw binary; values appear within the uncompressed typed event body (before any optional LZMA2 compression). All row changes use table, row and column GUIDs as identity. The Version 1 encodings of `DECIMAL`, `DATETIME`, and column nullability flags below are **CONFIRMED** by the originator (2026-10-21).

| Tag | Value type | Approved value bytes following tag |
|---|---|---|
| `0x00` | `NULL` | **Zero bytes.** Tag alone encodes explicit NULL. |
| `0x01` | `INTEGER` | Signed 64-bit logical range, ZigZag-encoded minimal ULEB128 (1–10 bytes) |
| `0x02` | `FLOAT` | IEEE-754 binary64, 8-byte big-endian |
| `0x03` | `TEXT` | Minimal ULEB128 byte length followed by UTF-8 bytes |
| `0x04` | `BLOB` | Minimal ULEB128 byte length followed by raw bytes |
| `0x05` | `DECIMAL` | ZigZag ULEB128 signed **scale**, then ZigZag ULEB128 arbitrary-precision signed **coefficient** |
| `0x06` | `DATETIME` | Signed 64-bit big-endian UTC Unix **epoch milliseconds** (8 bytes) |
| `0x07` | `BOOLEAN` | 1 byte: `0x00` false; `0x01` true |
| `0x08` | `GUID` / UUID | 16 bytes, RFC 9562 network byte order |

#### 6.2.1 Explicit NULL versus omitted column — CONFIRMED

Each `INSERT_ROW` or `UPDATE_ROW` encodes a minimal unsigned `column_count` of **zero, one or more**, followed by exactly that many entries sorted by raw `column_guid`: `column_guid[16] || value_type[1] || value_bytes[0..N]`. All identity references use GUIDs, not SQL-visible names. A duplicate column GUID inside one event is malformed.

- **UPDATE with a column omitted:** leave that column unchanged. Omission is NOT a NULL assignment.
- **UPDATE with a column present and type `0x00`:** explicitly assign NULL, with no additional value bytes. Example: `UPDATE Customers SET MiddleName = NULL WHERE CustomerID = 123` becomes an `UPDATE_ROW` event targeting the row GUID and the `MiddleName` column GUID with value type `0x00`.
- **INSERT with a column omitted:** its value is implicitly NULL under the current base rule, subject to separately defined schema defaults or nonnullable-column correction policy. INSERT events **omit NULL-valued columns**, including explicitly supplied SQL NULLs.
- An INSERT with `column_count = 0` is valid and creates the row identity with no supplied non-NULL columns. An UPDATE with `column_count = 0` is a legal no-effect event shape; whether to emit one for an unchanged SQL operation remains OPEN.

**Concrete uncompressed explicit-NULL update example (51 bytes):** `event_kind 0x02` (1 byte) || `table_guid` (16) || `row_guid` (16) || `column_count 0x01` (1) || `column_guid` (16) || `value_type 0x00` (1). The signed outer block size for this uncompressed example is 211 + 51 = **262 bytes**. No bytes follow the NULL tag.

#### 6.2.2 Column nullability metadata — approved compact flag encoding

**CONFIRMED:** A column definition must preserve its declared nullable or NOT NULL characteristic, independently from the actual NULL state of individual row values. **CONFIRMED:** Each column definition within `CREATE_TABLE` and relevant `ADD_COLUMN`/`ALTER_COLUMN` events has at minimum `column_guid[16]`, a length-prefixed UTF-8 name, `declared_type_tag[1]`, and `column_flags[1]`; additional type parameters follow when required. In `column_flags`, bit 0 is `1` for nullable and `0` for NOT NULL. The remaining bits are reserved until other attributes are defined. NULL (`0x00`) is a value type tag, **not** a column declaration type. Exact complete column-schema field grammar and literal/default-expression support remain OPEN.

**CONFIRMED existing policy:** Version 1 does not reject an otherwise authorized ledger row block merely because its values conflict with application-level NOT NULL metadata. A master detecting a prohibited NULL or an incompatible conversion publishes a **separate corrective signed UPDATE_ROW block first** (using the established type-specific default when necessary); all databases, including the originating master, consume that block normally. Read-only replicas cannot author corrective blocks. There is no direct unlogged materialized-database edit and no automatic echo of ordinary replay. Duplicate corrections from independent masters remain permissible. Until a correction block is present, any unresolved NULL remains a recorded/visible inconsistency or pending projection condition rather than an invented historical block. Exact intermediate SELECT behavior and handling of declared SQL defaults remain OPEN.

#### 6.2.3 Canonical DECIMAL — approved binary encoding

A decimal represents the exact number `coefficient × 10^(−scale)`, with signed arbitrary-precision integer **coefficient** and signed integer **scale**, stored in that order as `signed_scale[ZigZag-ULEB128] || signed_coefficient[ZigZag-ULEB128]`. For each signed integer `n`, ZigZag maps nonnegative `n` to `2n`, negative `n` to `−2n−1`, and the result uses minimal, little-endian-base-128 ULEB128 bytes. No floating-point conversion occurs.

Canonicalize numeric values: if coefficient is zero, encode `(scale=0, coefficient=0)`; otherwise divide out all factors of 10 from the coefficient and decrement scale accordingly. Thus `12.3400` is `(scale=2, coefficient=1234)`, while `1000` can be `(scale=−3, coefficient=1)`. Declared column precision/scale, if any, are separate **schema attributes**, not inferred from the canonical value. This preserves exact numeric meaning, but not redundant textual trailing zeros; SQL formatting can use declared schema scale. Decoder integer-size, decimal precision and scale resource limits and rounding on column-type conversions are OPEN.

#### 6.2.4 Canonical DATETIME — approved binary encoding

Encode a DATETIME **instant** as an 8-byte, signed, big-endian count of **milliseconds since 1970-01-01T00:00:00.000Z** (UTC). Dates before the epoch use negative values. Time-zone-aware input values are normalized to UTC by the originating master **before** emitting the block; other nodes never reinterpret that instant using their local time zones. This preserves precision to milliseconds but does not retain the originally written time-zone offset. If an application requires original offset or local wall-time representation, it can store additional fields (or TEXT). The interpretation of timezone-naive SQL inputs, leap-second normalization, and behavior for submillisecond inputs remain OPEN; one proposed default is to interpret timezone-naive inputs as UTC and reject fractional milliseconds that cannot be represented exactly unless an explicit rounding policy is defined.

#### 6.2.5 Other remaining type details — OPEN

FLOAT non-finite values (NaN/infinity), negative zero, normalization of Unicode, typed constant SQL defaults, column declaration metadata, deterministic datatype conversion, and resource bounds remain to be specified. The specified base value encodings are **approved**; as-yet-unspecified edge-case normalizations remain OPEN.

### 6.3 Second signature on revocation — approved structure; remaining details OPEN

The outer `REVOKE_MASTER` block is signed by one authorized master; the event payload contains the **other consenting master's 32-byte Ed25519 public key and 64-byte signature**, plus the **target key being revoked**. The two signers must be **the target key and one distinct currently authorized master**, in either order; the target's original grantor has no special role. **PROPOSED:** The second signer authenticates a canonical, domain-separated intent containing a stable protocol tag (`DDL-REVOKE-v1`), `ledger_guid`, `block_guid`, effective `timestamp_utc_ms`, outer signer key, target key, and countersigner key. It signs this intent first, then the outer signature covers the entire block including the countersignature. There is **no grant-block reference** in this revocation mechanism: revocation affects the target key's authorization state as a whole.

**CONFIRMED:** A revocation is valid only when its outer signer and countersigner are different and both currently authorized, with the target master providing one of these signatures. This prevents the sole remaining master from revoking itself. **OPEN:** Exact countersignature encoding and canonical signed bytes; behavior under incomplete authorization histories must follow canonical replay.

### 6.4 Remaining event-protocol decisions

**OPEN:** Finalize the complete schema attribute grammar (including approved nullability flags), canonical floating-point representations, SQL-affinity and collation semantics, schema metadata packing, oversized strings/BLOB restrictions, unknown future event handling, and row or schema dependencies. A `CREATE_TABLE` event **MUST** contain its complete initial table schema (all initial column GUIDs and definitions) in one block; later alterations are separate events. The exact schema-attribute wire grammar remains OPEN.


## 7. Deterministic reconciliation and conflict semantics — CONFIRMED

A node retains all authenticated blocks it has received; it validates the signer **against canonical authorization history**, then derives the current schema and rows from applicable events in canonical order. **Later modifications win over earlier modifications to the same affected field** unless a deletion applies; **deletions always win for the same immutable entity GUID**, including after earlier or later updates. The same rule applies to rows, columns, and tables. A deleted row/table/column cannot be recreated with the same GUID.

A delayed update to a GUID for which its earlier INSERT has not yet arrived is retained and logged as temporarily inapplicable; it has no visible effect until the missing earlier INSERT is received and the entity can be reprojected. Operations that cannot yet be interpreted because schema blocks are missing are similarly logged and reconsidered after recovery of dependencies. When a late earlier block arrives, the node **selectively recomputes only affected rows, schema, and dependent state where safe**, using conservative broader rebuild only when necessary. Unrelated rows need not be rolled back. Immutable event history itself never rolls back; only the derived materialized database state can be corrected. A raw block may remain retained even where later validation makes it unauthorized/inapplicable; it must not affect the projection.

**No secondary events from normal replay:** An ordinary block's application does not publish another block. Replay is not a user edit; ordinary blocks never echo into the ledger. Rare automatic corrections are separately initiated by an authorized master by publishing a **new authenticated correction block first** and then having the database consume it. The ledger does not distinguish human-driven and automatic signed changes.

## 8. Schema identity, name collisions, and conversion — CONFIRMED

Every table and every column has an immutable GUID. `RENAME_TABLE` and `RENAME_COLUMN` preserve GUID; concurrent rename events targeting a GUID settle by **latest canonical event wins**, unless the entity is deleted. A dropped column supersedes updates targeting it. A newly added table or column with the same name as an old/deleted one always receives a new GUID.

If two masters create different GUIDs under the same SQL-visible table name or under the same column name within one table, **both entities survive** and must receive distinct SQL-visible names. The default generated name for a conflicting column is `[original-name]-[column-GUID-without-dashes]`; table collisions follow the same GUID-suffix principle. An authorized master that discovers a collision may emit a signed corrective rename block, which is ingested normally. If multiple masters independently emit equivalent or competing corrective blocks, **all valid correction blocks are retained** and processed by canonical order; the latest name change wins, except for deletion. Duplicate correction blocks do not themselves undermine ledger validity or mandate rejecting an otherwise valid event.

When a column data type changes, convert existing values where possible. On incompatible conversion, log an error. If NULL is permitted, use `NULL`. If NULL is not permitted, use a suitable datatype default (`0` for a numeric type, empty string for text, minimum supported date/time for date types, etc.). On encountering an incompatibility that calls for an automatic replacement, a master creates a **new row-value-update block**, rather than silently modifying official state. Automated changes may themselves be followed by later canonical changes; arbitrary circular replay/republishing is not allowed.

**OPEN:** Precise SQL-visible name collision resolution in the presence of independent repair blocks, including avoiding persistent collisions after latest-wins renames; complete type-specific default matrix; intermediate visibility and timing of nonnullable corrective blocks; exact column-declaration grammar; deterministic conversion/rounding for all types. Duplicate correction blocks alone do not guarantee that transient name conflicts can never recur—implementation must ensure eventual naming convergence without rejecting valid events.

## 9. SQL semantics, constraints, and transactions — CONFIRMED

Version 1 **does not enforce ordinary relational application constraints** such as primary-key uniqueness or foreign-key referential integrity when deriving authoritative changes. Two rows with different row GUIDs may contain identical business primary keys. Ledger deletions are not vetoed by referencing rows or application locks. Internal storage integrity and synchronization cannot be disabled merely to bypass these application-level rules. Triggers, stored procedures, user-defined functions with effects, and comparable implicit database execution facilities are **not supported in Version 1**; specific SQLite or other engine internals needed for correctness are a separate implementation concern. Indexes and SELECT/query support may be used for efficient materialization and reads.

SQL clients can use provisional transactions. `BEGIN` starts uncommitted isolated changes; `ROLLBACK` before ledger publication discards those staged changes without emitting blocks. `COMMIT` begins publication of separately authenticated blocks reflecting final staged row changes; publication is **not group-atomic**. If a crash leaves only 60 of 100 intended blocks durably published, those 60 are permanent ledger history; remaining blocks may never exist. The materialized official database is updated only by ledger ingestion, not by the provisional transaction directly. A database MAY detect incomplete commit publication, log or report an error, and optionally attempt resumable publication, but automatic continuation is NOT a fundamental ledger requirement. Applications must handle missing/partial outcomes and any stronger business atomicity at a higher layer; compensating operations create new blocks and do not erase history.

Ordinary offline master writes do not wait for cross-master acknowledgment. A higher-assurance application may implement independent locks/reservations and require all masters' confirmation, recognizing that such applications can block during partitions and still need a true protocol to prevent simultaneous conflicting reservations.

**OPEN:** Exact transient transaction staging architecture, how `COMMIT` signals partial failures, whether progress persists for optional retry, disabled-feature scope, which relational metadata may remain representable without enforcement, and other engine-specific type-affinity behavior. Explicit NULL-versus-omission semantics are now specified in Section 6.2. The initial system makes no promise that ordinary SQL semantics imply global transaction finality.

## 10. Materialization priority, durability, and recovery — CONFIRMED

A received valid block must enter persistent ledger storage before altering the official database. Master-originated blocks follow the same rule. Once received blocks are available, the database **pauses ordinary activity to process the blocks it already knows** and keep its local projection caught up; local precedence over throughput is intentional. This never implies certainty that no missing blocks exist on the wider network. A master can operate while other masters are offline, apart from local synchronization needed to apply known blocks.

Recoverable checkpoints, an application ledger cursor, and idempotent replay must ensure a crash between block durability and materialization can be recovered without permanently omitting or double-applying effects. Master/replica outage, out-of-order delivery, partitions, and late authority changes may all require selective re-projection. The ledger itself is immutable; the projection is replaceable. Acknowledgment of successful ledger append implies local durable storage; a block lost with all copies is unrecoverable.

**OPEN:** Exact crash-safe commit/cursor protocol, staging and snapshot checkpoint formats, projection invalidation/indexes, selective dependency tracking, and observable queuing behavior while the materialization barrier runs.

## 11. Peer-to-peer propagation and offline exchange — CONFIRMED

Every node may receive, verify, store, serve, relay, and export an intact block, regardless of whether it may originate blocks. Block validity is transport-independent. Permit direct peer connections (TCP/QUIC or suitable supported protocols), decentralized peer discovery with configurable bootstraps, BitTorrent-inspired DHT, peer exchange, LAN service discovery, NAT traversal and relaying, HTTP(S), multipeer downloads, and real-time announcements. Nodes normally advertise new validated blocks to peers with resource/bandwidth controls. **No peer discovery or master quorum is necessary for an ordinary local write**.

Combine rapid announcements with periodic cryptographic **inventory reconciliation**, including Merkle-style summaries or equivalent, and comparisons by block ID/hash that do not depend exclusively on timestamps. Export and import must include individual blocks, timestamp ranges, a full ledger, and incremental missing-block batches; large whole-ledger archives can be imported as offline batch jobs, from arbitrary media. The ledger does not require a database to exist; a returning master can catch up through a read-only replica.

**OPEN:** Exact peer/wire protocol, DHT namespace and anti-Sybil limitations, Merkle inventory construction over non-predecessor blocks, block identity for deduplication, bootstrap/anti-entropy schedule, archive format, chunking/backpressure, peer authentication (separate from block authentication), transport encryption, and resource limits.

**Flood resistance / inbound validation — CONFIRMED requirement:** Every node must inspect every received block for framing, protocol version, well-formedness, hash/signature integrity, ledger identity, and signer authorization before it has any database effect. A hostile actor may flood arbitrary invalid blocks. Reject invalid inputs safely and limit untrusted resource consumption (including decoding, disk, CPU, memory and networking) without treating plausible timestamps alone as grounds for rejecting otherwise valid authorized blocks. An out-of-order block whose missing authorization evidence has not arrived must not be materialized before its authority is established. Exact inventory, caching and DoS budgets remain implementation choices.

## 12. Intended guarantees and deliberate non-guarantees

**Intended:** Public cryptographic origin/integrity verification, signed historical authorization, deterministic canonical replay on the same accepted history, append-before-apply, one-row-per-block updates, delete-wins identity semantics, eventual convergence for participants that ultimately receive equivalent relevant events, permissionless block redistribution, complete reconstruction from validated genesis and all ledger history, independent progress during network partitions, and crash recovery for durably accepted events.

**Not promised:** Simultaneous SELECT visibility, knowledge of an objectively complete history at an arbitrary instant, automatic rollback of published blocks, all-or-nothing execution of multi-block SQL transactions, preservation of foreign-key/unique constraints, instant failover without surviving block copies, globally irreversible application business outcomes, and successful application-level strong locking when required masters are unreachable.

## 13. Acceptance scenarios (conceptual, not hidden verification details)

1. A new ledger initializes a genesis signer; an empty replica adopts the ledger and derives an identical state when fed the same history.
2. A grants B; B grants C; a late invalidation of B's grant invalidates C's dependent authority and relevant projected effects.
3. A signer key has no authority at a block position before its own grant; a later revocation invalidates subsequent blocks, not prior ones.
4. A and B publish independently while disconnected; a replica relays blocks to a disconnected master when peer links permit.
5. A block arrives late with an earlier timestamp and changes only its affected row's state/dependencies rather than needlessly rebuilding unrelated rows.
6. Partial-column updates on the same row preserve separately modified columns; deletion of the row, table, or column GUID wins over modifications.
7. Same-name columns/tables with distinct GUIDs both survive; corrective naming blocks are produced by an authorized master, not by direct projection writes.
8. Duplicate primary-key values across distinct row GUIDs survive in Version 1.
9. `BEGIN`+`ROLLBACK` emits no blocks; partial failure during `COMMIT` can preserve only a published prefix of independently durable row blocks.
10. Crashes between ledger persistence and projection recover idempotently; replicas converge once their accepted block sets and authority histories agree.
11. Unsupported or temporarily uninterpretable blocks produce diagnostics, remain available for dependency recovery, and never silently mutate official storage through a bypass.

## 14. Open design decisions to settle before a frozen interoperable instruction

**Protocol-critical:** (a) finalize complete column/table schema attribute grammar and type-conversion edge cases; (b) genesis uniqueness/trust/identity, genesis fingerprint or other binding and protocol-version semantics; (c) grant/revoke two-signature intent binding, multi-grant lineage, and nonzero-authorized-master enforcement; (d) LZMA2 decoder-property framing and resource limits; (e) name-repair convergence rules beyond simply accepting duplicate corrections; (f) deterministic type conversion/default matrix; (g) application-level unsupported SQL/DDL boundaries and transaction-stage behavior; (h) raw block identity and missing-block inventory protocol; (i) materialization persistence and re-projection rules for authorization invalidations; (j) handling maliciously malformed but correctly signed operation bodies under syntactic validation; (k) failure/response policy for partial `COMMIT` publication.

**Engineering freedoms where requirements remain satisfied:** Exact internal thread architecture, indexes/storage layout, transport libraries, peer scheduling, fault-injection architecture for private testing, whether to optimize re-projection or conservatively rebuild affected tables where correctness demands, logging APIs, and choice of an open-source database engine with a compatible license.

**Change-control principle:** Later choices must be incorporated by editing this unified specification, not by appending contradictory overriding sections. Do not treat suggested implementation choices as approved requirements until explicitly decided.


**Decision register — 2026-10-21:** The originator explicitly approved the proposed DECIMAL coefficient/scale encoding (canonical ZigZag-ULEB128), DATETIME signed UTC epoch-millisecond int64, nullability flag convention, and explicit `NULL` versus omitted-column semantics. Implementation must not treat this approval as resolving the separately marked OPEN details such as time-zone-naive input interpretation, decimal rounding limits, SQL defaults, complete DDL grammar, authorization lineage, or peer reconciliation parameters.


## 15. Additional confirmed authority rules and protocol recommendations — 2026-10-21

### 15.1 Confirmed by the originator

- Any currently authorized master may grant signing authority to another public key. Keys may become authorized, revoked, and authorized again at distinct positions in canonical order.
- Authorization is evaluated from the canonical sequence, not delivery order. A signer is valid only while it has an active authorization at the block's canonical position. If a missing earlier block invalidates a grant, dependent authorization and affected database projections are re-evaluated.
- A joining database must pin **both ledger GUID and the SHA-512 fingerprint of the intended genesis block**.
- Revocation needs the signature of the target master and any other currently authorized master; one signs the whole block and the other signs the specific revocation intent included in the payload. The design must not permit removal of the final authorized master; a lost last private key is an operational catastrophe that may require migrating to an entirely new ledger.

### 15.2 Proposed deterministic schema rules — NOT YET APPROVED

**Column definition wire grammar.** Proposed full definition:

`column_guid[16] || name_length[varuint] || name_utf8 || declared_type_tag[1] || column_flags[1] || type_parameters || default_presence[1] || optional_typed_default`.

`column_flags` bit 0 retains the confirmed convention (`1` nullable, `0` NOT NULL); other flag bits remain reserved. `default_presence=0` means no explicit SQL default; `default_presence=1` means a typed constant value follows, including the permitted typed `NULL` sentinel. No dynamic default expressions or triggers are evaluated by ledger replicas. For DECIMAL declarations, include `precision[varuint]` (`0` = unrestricted) and signed `scale[ZigZag varint]`; DATETIME is always UTC epoch milliseconds. A table-creation event includes `column_count[varuint]` plus column definitions in declared SQL column order, with distinct immutable GUIDs. Row INSERT/UPDATE changed-value lists remain sorted by column GUID. `ALTER_COLUMN` is proposed to carry the column's complete resulting definition, so missing field masks cannot produce divergent schemas. These are proposed initial wire choices, not approved semantics.

**Default handling.** Keep explicit SQL `DEFAULT` literals separate from fallback replacements for prohibited NULL/incompatible conversions. An originating master that evaluates a non-NULL default on an INSERT includes that resulting concrete value in its `INSERT_ROW` event; replicas should not re-evaluate dynamic SQL expressions. For exceptional corrective events, propose the following fallback values: INTEGER 0, FLOAT +0.0, DECIMAL exact 0, TEXT empty UTF-8, BLOB zero bytes, DATETIME 0001-01-01T00:00:00.000Z, BOOLEAN false, GUID all-zero 16-byte value. NULL is a value marker, not a declared column type. Automatic corrections must be **new signed ledger blocks**, never private direct writes to materialized storage.

**Conversion determinism.** Propose strict, culture-invariant parsing and explicit rules for overflow, rounding, IEEE-754 NaN/infinities, Unicode normalization and naive datetime inputs. Preserve lossless conversions when possible. If a declared column type changes and conversion is impossible, use NULL when nullable or the fixed fallback when nonnullable and log the error; where correction events are required, an authorized master must publish them before they are applied. Whether lossy numeric conversions should be permitted and exactly which ones remains OPEN.

**Collision repairs — CONFIRMED ordering preference.** For distinct table/column GUIDs claiming the same SQL-visible name, **the entity introduced first in canonical creation-block order retains the original name**; every later-created conflicting entity retains its own GUID and receives the generated suffix name `original-name-entityGUIDwithoutdashes` through normal, signed master-authored corrective rename blocks. Creation-block order governs priority, **not numeric ordering of entity GUIDs**, and network arrival order never determines the winner. All valid original and duplicate repair blocks remain in the ledger. A master publishes any automated correction as a block **before** its own database consumes that block. A read-only participant cannot sign a correction; until a master eventually issues one, the participant may have duplicate/ambiguous names in its logical state. It must retain both distinct GUID-identified entities rather than discarding either one; the precise interim SQL-visible representation is an implementation issue. Latest valid explicit renames still win in canonical order, while deletions always override changes to the deleted GUID.

**OPEN tie-order clarification:** The long-standing total-order comparator is `(timestamp_utc_ms, signer_public_key, block_guid)`. The originator's newer phrase “chronologically and then by GUID” might mean that `block_guid` should replace the signer-public-key secondary key. **Do not change the agreed comparator without explicit clarification.** Whichever comparator is approved determines the first creation event.


### 15.3 Authorization replay and two-party revocation — CONFIRMED intent, detailed wire format OPEN

**CONFIRMED:** Authorization is derived by processing all otherwise valid authorization operations in canonical block order, not from local arrival order or receipt-time guesses. A master's signing key is allowed to author an event only while that key's authorization is active at that event's position in the reconstructed timeline. Keys may be authorized, revoked, and authorized again. A late-arriving earlier event can invalidate later blocks and downstream grants; affected materialized database state must be reconciled. The originator **did not approve** treating separate grants as independently surviving authorization paths that a revocation of one grant leaves untouched. An earlier suggestion to revoke only `grant_block_guid` is **not** established policy.

**CONFIRMED:** Revoking a master requires authorization by the master being revoked and a distinct currently authorized master: one signs the standard outer block, the other provides an Ed25519 countersignature **inside the event payload**. Block acceptance remains based on authentication and canonical authorization, not whether a block looks suspicious. Events from unauthorized keys are not applied. Validly signed-but-unauthorized or malformed events are not allowed to contaminate the materialized state.

**PROPOSED countersignature binding:** Include the ledger GUID, block GUID, timestamp, outer signer public key, revoked public key, and countersigner public key in a domain-separated Ed25519 signed-intent message. The outer signature authenticates the full block including the countersignature. The precise signed bytes and field-ordering are implementation protocol decisions; grantor identity is not an authorization prerequisite.

**CONFIRMED:** GRANT_MASTER sets its target key to authorized when issued by a currently authorized master, including when already authorized; a redundant grant does not create independently surviving authority. REVOKE_MASTER switches the target key to unauthorized if and only if approved by the target key and any distinct currently authorized master. The target's original grantor need not participate. This operates on the key's current authorization state, not on individual grant instances. Late arriving earlier history requires replay, and a later valid grant can re-authorize the same key at its own position. OPEN: exact last-master invariants and cascade validity under late-arriving blocks. Do **not** silently implement the previously proposed independent grant-lineage model.

### 15.4 LZMA2 decoder parameters — APPROVED base settings, safety limits OPEN

The existing outer `payload_compression_type` field remains `0x00` uncompressed or `0x01` compressed. With `0x01`, the first byte inside `payload` is the **raw LZMA2 dictionary-property byte `p`**, followed by the LZMA2 raw compressed stream (no `.7z` archive and no Base64). The LZMA specification defines `0 <= p <= 40` and dictionary size `0xFFFFFFFF` for `p=40`, otherwise `((2 | (p & 1)) << ((p / 2) + 11))` bytes with integer division. CONFIRMED for Version 1: encode using **property `p=20` (4 MiB dictionary)**; other dictionary settings are reserved for future protocol versions. Require an LZMA2 end-of-stream marker and reject trailing garbage in a compressed event. Decode through streaming buffers with configurable decompressed-byte and CPU/memory quotas; avoid silently imposing a small universal row-size limit that would contradict the intended ability to represent large single-row operations. Verification of the Ed25519 signature and SHA-512 payload digest occurs on raw transmitted bytes **before** decompression; malformed compressed data are retained as authenticated bytes but cannot be materialized.

**References:** LZMA SDK / LZMA Specification, https://www.7-zip.org/sdk.html ; RFC 8032 Ed25519, https://www.rfc-editor.org/rfc/rfc8032 ; SQLite datatype conventions, https://www.sqlite.org/datatype3.html .

**Status:** Canonical creation-order name priority, replay-based master authority, the need to validate every inbound block, and LZMA2 method `0x01` with 4 MiB dictionary are **CONFIRMED**. Detailed unsigned/grant overlap cases, compression safety thresholds, and binary wire corner cases remain **OPEN**.


## 16. Revocation signers and repeated grants — CONFIRMED 2026-10-21

- **Ordering:** Maintain the agreed total order `(UTC timestamp milliseconds, signer Ed25519 public key, block GUID)`; no change to this rule.
- **Grant:** Any authorized master can issue `GRANT_MASTER` for a target public key. The event is retained even if that key is currently authorized. When replayed at its canonical position, a valid grant sets the target key to authorized. Such grants do not create independent layers of authorization that survive a later valid revocation of the target key.
- **Revocation:** To revoke C, the block must bear the consent of **C plus any other distinct master authorized at the revocation's canonical position**; the other master need not be C's original grantor. Either consenting party may sign the outer block; the other supplies the Ed25519 countersignature in its binary payload. A key cannot unilaterally revoke itself. A valid revocation makes C unauthorized from that canonical point until any later valid grant restores its authority.
- **Replay and late arrivals:** Every valid grant or revocation event is replayed chronologically, not inferred from arrival time. Blocks authored outside their signer's valid authority interval are not applied to the materialized database. Reordered history can invalidate derived grants and block effects; later valid grants can restore authority prospectively. Historical ledger bytes remain preserved.
- **At least one master:** Two distinct currently authorized participants must consent to a valid revocation, making the final sole master impossible to revoke under this rule. Lost keys or hardware failure can still make a ledger operationally unusable; recovery by starting a new independent ledger is a separate procedure.
- **OPEN:** Exact serialized countersignature intent, validation when missing dependencies arrive, and key/clock adversarial handling. These are implementation details requiring precision, not a return to grantor-specific revocations.
