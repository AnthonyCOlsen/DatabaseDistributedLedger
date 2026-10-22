# Database Distributed Ledger — Technical Design

**Copyright © 2026 Anthony C. Olsen. Licensed under the [MIT License](LICENSE).**  
**Public repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger  
**Revision:** 2026-10-22 — reconciled draft v3, incorporating owner approvals of design questions 1–8 and the no-age-discard projection rule; read visibility and remaining interoperability details OPEN (DRAFT)

**Status:** Independently developed, database-engine-agnostic architectural design. **CONFIRMED** denotes decisions accepted by the originator. **OPEN** denotes matters needing resolution before a fully interoperable implementation. **PROPOSED** denotes a candidate choice that has not been approved. This 2026-10-22 revision is a working draft pending review; it is not a released engine or frozen interoperable protocol.

## 1. Purpose and layer boundaries — CONFIRMED

Build a public-data, disaster-resilient, cryptographically authenticated distributed event ledger supporting an eventually consistent relational database on top. **The immutable ledger, not a relational database, is the authoritative data history.** The ledger may exist without a database. Only an **empty** database can initially adopt an existing ledger; it derives schema and data solely by consuming ledger events. Existing database SQL interfaces and SELECT/query facilities may be retained or compatibly extended, but there must be no supported direct SQL-to-official-storage mutation path: ordinary INSERT/UPDATE/DELETE or other supported user-managed data/schema changes submitted through any normal client connection must first produce authenticated ledger events; official storage changes only by ingesting ledger events. The implementation may use different internal components, but bypassing this invariant through an alternative SQL client, administrator-accessible supported interface, or unwrapped server endpoint is nonconforming.

An authorized master receives SQL write requests, evaluates them against the relational state it currently knows (or provisional transactional state), and publishes the resulting **concrete atomic changes** to the ledger before they are ever applied to official materialized storage. The same ledger consumer then applies locally produced blocks as it does remotely received blocks. A successful write acknowledgment can precede local SELECT visibility; client developers must accommodate stale reads. The database and application layers may implement stronger transactional protocols, but the underlying ledger neither promises global synchronous agreement nor interprets a client's business transaction as an atomic group.

**Every block is independent. No block references any predecessor, previous block, head, fork, or chain link.** “Blockchain” is a descriptive term for the authenticated historical collection and must not be interpreted as a hash-linked chain. There is no fork-choice election or dropping otherwise valid blocks to choose a preferred branch. A single logical order is calculated over the accepted blocks presently known to a participant.

## 2. Participation, identity, and authority — CONFIRMED

- **Ledger GUID:** immutable globally unique identifier shared by every block belonging to one ledger.
- **Block GUID:** immutable identifier of an individual block, independent of its ledger GUID.
- **Master:** participant holding an authorized Ed25519 private signing key and permitted to publish blocks and delegate master authority.
- **Read-only replica:** anyone may join, verify, retain, exchange, and materialize blocks without permission or a signing key. A replica may relay a block to a master even when their respective originating masters cannot directly communicate.
- **Entity GUIDs:** every table, column, and row has its own immutable GUID, independent of SQL-visible names or declared keys. Names may change without changing identity. A deleted GUID cannot be reused; recreating a same-name entity uses a new GUID.
- **Genesis master:** initial sole signing authority established by the special genesis block; other masters are added with ledger events.

**Grant — CONFIRMED:** Any currently authorized master may issue a `GRANT_MASTER` event identifying a public key. A valid grant sets that key's authorization state to **authorized** at the grant's canonical position. Grants do not stack into separately surviving permissions: a redundant grant to an already authorized key is permitted but does not protect it from a subsequent valid revocation. A later valid grant can re-authorize a previously revoked key; no grant retroactively validates earlier blocks.

**Revocation — CONFIRMED:** A `REVOKE_MASTER` event must be approved by **the master being revoked and one other distinct currently authorized master**, in either order. One provides the ordinary Ed25519 outer block signature and the other provides a countersignature inside the payload. The target's original grantor has no special role. At the event's canonical position, a valid revocation sets the **target public key** to unauthorized as a whole, not merely one grant instance. Subsequent blocks signed by that key remain in retained history but have no materialized effect unless a later valid grant restores authorization prospectively. The two-distinct-authorized-signers condition prevents revoking the sole remaining authorized master. A lost final private key is an operational catastrophe, not an exception to this rule.

**Canonical authorization replay — CONFIRMED:** Process grants, revocations, and other events in the total block order defined in Section 4, irrespective of receipt order. A block is applicable only when its signer is authorized at that canonical position. Late-arriving earlier history can invalidate or revalidate dependent grants and projected effects; re-evaluate those consequences without erasing retained block bytes. A previously accepted block is not guaranteed permanently applicable when missing earlier history later arrives. No separate independent grant-lineage model or grant-block-specific revocation is used.

**CONFIRMED:** The `REVOKE_MASTER` payload field order is fixed in Section 6.3. **OPEN:** Detailed classification/diagnostics for malformed authorization bodies, dependent unknown authority history, duplicate block GUIDs with differing bytes, and adversarial identity cases. The older proposal to sign only the reusable fixed phrase `I AGREE` is **not approved**. There is no additional Version 1 key-compromise recovery mechanism beyond these ordinary signature/authority rules.

## 3. Genesis and database initialization — CONFIRMED identity and version; OPEN administrative interface

On creation, each **empty** database must choose one of two paths:

- **Create a new ledger:** generate a new ledger GUID and Ed25519 keypair, become the first authorized master, and publish a valid self-signed genesis block.
- **Join an existing ledger:** select an **existing local ledger** identified by its ledger GUID. Adopt its already-established genesis authority and reconstruct the database solely by replaying authenticated ledger events. A populated unrelated database cannot join.

**CONFIRMED:** A genesis block uses the same fixed binary envelope as other blocks but has an **empty payload** (`payload_size=0`, compression `0x00`, SHA-512 of the empty bytes). Its outer Ed25519 signature is verified against the public key in the header, which becomes the initial authorized master key. Ordinary non-genesis blocks may not claim genesis status merely by being self-signed.

**CONFIRMED trust safeguard:** When joining, pin both `ledger_guid` and the **SHA-512 fingerprint of its unique genesis block**, rather than trusting any self-signed block that claims the same GUID. If conflicting genesis blocks claim an identical ledger GUID, do not silently choose one. A ledger GUID identifies the intended chain, but alone does not cryptographically prove that a particular genesis is the user's intended one.

**CONFIRMED Version 1:** The block header `version` is the big-endian unsigned 16-bit value `1` (`0x0001`), including for genesis. Genesis uses the exact standard 211-byte envelope with empty payload and its normal self-signature. Its fingerprint is `SHA-512` of the **entire serialized 211-byte genesis block**, including the outer signature. Only one pinned genesis is accepted for a ledger identity. An incoming genesis candidate with the same ledger GUID but a different full-block fingerprint is rejected as belonging to the wrong ledger, not silently merged. An unknown later protocol version may be safely retained as an opaque, unapplied record, but must not change the Version 1 projection. **OPEN:** The concrete user-facing command/parameter schema for selecting and pinning a ledger, diagnostic codes for mismatch, and exact forward-compatibility semantics. These interfaces belong in the separate management API specification.

## 4. Canonical chronological order and clocks — CONFIRMED

Accepted blocks are ordered by the tuple `(timestamp_utc_ms, signer_public_key, block_guid)` ascending. Timestamps are signed 64-bit integers representing **milliseconds since 1970-01-01T00:00:00Z**. Compare public keys using their **raw 32-byte Ed25519 encoding** as unsigned lexicographic bytes, not textual Base64. Compare block GUIDs using their canonical 16-byte binary representation. Masters should synchronize clocks frequently against an agreed trustworthy UTC reference, preferably atomic-clock-backed. A signer should not intentionally issue more than one block in a millisecond; if needed, advance to the next millisecond. The block GUID serves as a tertiary tie-break if collisions occur anyway.

No block is rejected solely for its timestamp appearing far in the future or past. A block with an incorrect clock is processed according to its declared signed timestamp, **subject to authorization at that canonical position**. Arrival order never defines canonical order. There is no general guarantee that a participant knows all earlier blocks; late earlier blocks may revise derived state.

**CONFIRMED tie behavior:** Equal-millisecond timestamps, including authority operations, are resolved by raw signer public key, then raw block GUID as specified above; they are not ordered by arrival or by block GUID before signer public key. **OPEN:** Leap-second representation and operational time-sync procedures. No clock-skew quarantine is required.

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

**Compression type — CONFIRMED:** `0x00` = uncompressed raw event payload; `0x01` = **raw LZMA2** compressed event payload, not a `.7z` archive or Base64. For `0x01`, the first byte **inside `payload`** is the LZMA2 decoder-property byte `p`, followed by the raw compressed stream; the property byte is included in `payload_size` and in the transmitted-payload SHA-512. Version 1 encoding uses **`p=20`, a 4 MiB dictionary**. Other dictionary properties are reserved for future protocol versions. Decoding must require the LZMA2 end-of-stream marker and reject trailing garbage. The signature and SHA-512 digest are checked over transmitted bytes before decompression. Other compression type IDs are reserved. Compression is optional for small events.

**CONFIRMED safety/resource policy:** Implementations must provide **configurable** input-size, decoded-size, CPU/time, memory, and decompression limits. No arbitrary small universal maximum is imposed merely because the outer payload length is 64-bit. Apply bounded incremental processing or reject safely before exhausting resources. Malformed blocks, failed hash/signature checks, unknown event kinds, unknown future versions, and unverifiable authority must never mutate official materialization. An implementation may retain such bytes for diagnostics or later dependency recovery with clear status; this does not authorize their application. **OPEN:** Precise configuration fields and minimum interoperable bounds, block inventory identity/hash, and diagnostic/status codes for these cases.

## 6. Binary event payloads — CONFIRMED base semantics and column format; remaining events OPEN

Each non-genesis block normally describes one independently meaningful **typed ledger event**, not arbitrary source SQL text. The master evaluates SQL using its currently known local database state and captures the resulting change. For row DML, the smallest atomic event is **one changed row per block**. A statement modifying 100 rows therefore emits up to 100 independent blocks. Zero rows affected means **zero change blocks**. Multi-row and multi-statement operations are not an all-or-nothing ledger unit.

Row events need to represent `INSERT_ROW`, `UPDATE_ROW`, and `DELETE_ROW`, using the **row GUID and table GUID** as their identity. **UPDATE_ROW records only the columns actually changed**, each identified by **column GUID**, together with its resulting value; it does not replace unrelated fields of the row. SQL-visible table and column names are not used as persistent identity or as the normal lookup keys in ledger row changes. Insert events must provide enough values/identities to build the row. An update that refers to a column renamed elsewhere still operates on its GUID.

DDL/administrative events need to express, at minimum, `CREATE_TABLE`, `ALTER_TABLE`/column creation or modification, `DROP_TABLE`, `CREATE_COLUMN`, `DROP_COLUMN`, `RENAME_TABLE`, `RENAME_COLUMN`, column-type changes, `GRANT_MASTER`, and `REVOKE_MASTER`. The ledger must reconstruct both schema and data. The originator prefers one smallest practical atomic change per block; row changes must be one-row atomic.

### 6.1 Compact payload grammar — CONFIRMED Version 1 base fields; remaining events OPEN

**CONFIRMED:** A block’s uncompressed event body is binary, not SQL; row UPDATE events identify only the columns actually changed by GUID. **CONFIRMED:** Put a `uint8 event_kind` first in every non-genesis uncompressed event body. The chosen outer compression method compresses this *entire event body*; it does not change the body schema.

| Event code | Version 1 event | Uncompressed body after the one-byte code |
|---|---|---|
| `0x01` | `INSERT_ROW` | `table_guid[16]`, `row_guid[16]`, `column_count[varuint]`, repeated `(column_guid[16], typed_value)` |
| `0x02` | `UPDATE_ROW` | Same identifiers and repeated **changed columns only**; no implicit changes to other columns |
| `0x03` | `DELETE_ROW` | `table_guid[16]`, `row_guid[16]` |
| `0x10` | `CREATE_TABLE` | `table_guid[16]`, length-prefixed table name, `column_count[varuint]`, then full column definitions (Section 6.2.2) in declared order |
| `0x11` | `DROP_TABLE` | `table_guid[16]` |
| `0x12` | `RENAME_TABLE` | `table_guid[16]`, new name |
| `0x13` | `ADD_COLUMN` | `table_guid[16]`, complete column definition (including `column_guid`) from Section 6.2.2 |
| `0x14` | `DROP_COLUMN` | `table_guid[16]`, `column_guid[16]` |
| `0x15` | `RENAME_COLUMN` | `table_guid[16]`, `column_guid[16]`, new name |
| `0x16` | `ALTER_COLUMN` | `table_guid[16]`, complete **resulting** column definition (including unchanged `column_guid`) from Section 6.2.2 |
| `0x20` | `GRANT_MASTER` | `grantee_public_key[32]`; authorization is a per-key state, not separate grant instances |
| `0x21` | `REVOKE_MASTER` | `revoked_public_key[32] || countersigner_public_key[32] || countersignature[64]` (Section 6.3) |

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

#### 6.2.2 Column definition grammar — CONFIRMED Version 1

**CONFIRMED by the originator on 2026-10-22:** Each complete Version 1 column definition uses the following ordered binary fields, with no separators or padding:

`column_guid[16] || name_length[varuint] || name_utf8 || declared_type_tag[1] || column_flags[1] || type_parameters || default_presence[1] || optional_typed_default`.

- `column_guid` is the immutable 16-byte identity; `name_length` is minimal ULEB128 counting UTF-8 bytes.
- `declared_type_tag` is one of the non-NULL value type tags `0x01` through `0x08` (INTEGER, FLOAT, TEXT, BLOB, DECIMAL, DATETIME, BOOLEAN, GUID). `NULL` (`0x00`) is a value marker, not a declared column type.
- `column_flags` bit 0 equals `1` for nullable, `0` for NOT NULL. Other bits are reserved and MUST be zero for Version 1.
- For DECIMAL (`0x05`), `type_parameters` are `precision[varuint] || scale[ZigZag-ULEB128]`, with `precision=0` meaning unrestricted. Other eight-type-list parameters are absent for the Version 1 base declarations, unless an explicitly revised wire version later defines them.
- `default_presence` is the single byte `0x00` for no declared default or `0x01` for a typed constant default (including an explicitly typed NULL); with `0x01`, `optional_typed_default` is `value_type[1] || value_bytes` using Section 6.2 values. Unknown presence/flag bytes are malformed for Version 1.
- `CREATE_TABLE` contains `column_count[varuint]` then all complete column definitions **in declared order**. `ADD_COLUMN` transmits one full definition. `ALTER_COLUMN` transmits the complete resulting definition with the existing column GUID; it does not use ambiguous partial attribute masks. Existing row event column-value entries remain ordered by raw column GUID.

**OPEN refinements:** Exactly which SQL default expressions are representable as typed constants versus unsupported; the effect of a declared default on an omitted inserted column (the base INSERT omission rule remains Section 6.2.1); detailed SQL datatype conversions/declared precision enforcement; collation/type-affinity metadata beyond the eight supported non-NULL type categories; and malformed/default-type mismatch diagnostics. Do not silently encode unapproved computed defaults.

**CONFIRMED existing policy:** Version 1 does not reject an otherwise authorized ledger row block merely because its values conflict with application-level NOT NULL metadata. A master detecting a prohibited NULL or an incompatible conversion publishes a **separate corrective signed UPDATE_ROW block first** (using the established type-specific default when necessary); all databases, including the originating master, consume that block normally. Read-only replicas cannot author corrective blocks. There is no direct unlogged materialized-database edit and no automatic echo of ordinary replay. Duplicate corrections from independent masters remain permissible. Until a correction block is present, any unresolved NULL remains a recorded/visible inconsistency or pending projection condition rather than an invented historical block. Exact intermediate SELECT behavior and handling of declared SQL defaults remain OPEN.

#### 6.2.3 Canonical DECIMAL — approved binary encoding

A decimal represents the exact number `coefficient × 10^(−scale)`, with signed arbitrary-precision integer **coefficient** and signed integer **scale**, stored in that order as `signed_scale[ZigZag-ULEB128] || signed_coefficient[ZigZag-ULEB128]`. For each signed integer `n`, ZigZag maps nonnegative `n` to `2n`, negative `n` to `−2n−1`, and the result uses minimal, little-endian-base-128 ULEB128 bytes. No floating-point conversion occurs.

Canonicalize numeric values: if coefficient is zero, encode `(scale=0, coefficient=0)`; otherwise divide out all factors of 10 from the coefficient and decrement scale accordingly. Thus `12.3400` is `(scale=2, coefficient=1234)`, while `1000` can be `(scale=−3, coefficient=1)`. Declared column precision/scale, if any, are separate **schema attributes**, not inferred from the canonical value. This preserves exact numeric meaning, but not redundant textual trailing zeros; SQL formatting can use declared schema scale. Decoder integer-size, decimal precision and scale resource limits and rounding on column-type conversions are OPEN.

#### 6.2.4 Canonical DATETIME — approved binary encoding

Encode a DATETIME **instant** as an 8-byte, signed, big-endian count of **milliseconds since 1970-01-01T00:00:00.000Z** (UTC). Dates before the epoch use negative values. Time-zone-aware input values are normalized to UTC by the originating master **before** emitting the block; other nodes never reinterpret that instant using their local time zones. This preserves precision to milliseconds but does not retain the originally written time-zone offset. If an application requires original offset or local wall-time representation, it can store additional fields (or TEXT). **CONFIRMED 2026-10-22:** Interpret timezone-naive SQL DATETIME inputs as UTC and reject inputs with unrepresentable submillisecond precision rather than silently rounding. **OPEN:** Exact leap-second input policy, SQL timezone-type mapping and error codes.

#### 6.2.5 Type edge-case policy — CONFIRMED principles; remaining conversion matrix OPEN

**CONFIRMED 2026-10-22:** Reject `NaN`, positive infinity and negative infinity in FLOAT input; normalize IEEE-754 negative zero (`-0.0`) to positive zero (`+0.0`) in the canonical FLOAT encoding. Preserve valid UTF-8 text exactly; do **not** perform implicit Unicode normalization (NFC/NFD/etc.). Do not silently apply lossy type conversions; reject a conversion unless it is explicitly requested under a supported and documented conversion rule. DECIMAL remains exact with no implicit FLOAT intermediary, and DATETIME follows Section 6.2.4. **OPEN:** Full allowed SQL conversion matrix, precision/scale range checking and errors, handling imported noncanonical FLOAT bytes, leap seconds, supported typed SQL defaults, and locale/collation behavior.

### 6.3 Second signature on revocation — CONFIRMED Version 1 signed intent and payload

The outer `REVOKE_MASTER` block is signed by one currently authorized master. Its payload contains the **revoked target's 32-byte Ed25519 public key**, the **other consenting master's 32-byte Ed25519 public key**, and that countersigner's **64-byte Ed25519 signature**. The two signers must be distinct and currently authorized at the event's canonical position. **The target must be one of the two signers**, in either role. The target's original grantor has no special role. Revocation affects the target's authorization state as a whole, not an individual grant. There is no grant-block reference.

**CONFIRMED by the originator on 2026-10-22:** The countersigner signs the following exact **149-byte** message, formed by concatenating the fields without separators, length prefixes, or padding:

| Signed field | Length and encoding |
|---|---|
| ASCII literal `DDL-REVOKE-v1` | 13 bytes, exactly those case-sensitive ASCII characters, without a trailing NUL |
| `ledger_guid` | 16 raw bytes in canonical RFC 9562 UUID wire order |
| `block_guid` | 16 raw bytes in canonical RFC 9562 UUID wire order |
| `timestamp_utc_ms` | 8 bytes signed two's-complement integer, big-endian |
| `outer_signer_public_key` | 32 raw Ed25519 public-key bytes |
| `revoked_public_key` | 32 raw Ed25519 public-key bytes |
| `countersigner_public_key` | 32 raw Ed25519 public-key bytes |

The countersigner's signature is the 64-byte Ed25519 signature over these **exact 149 bytes**. It is computed first. The outer signer then signs the ordinary complete serialized block (from `version` through the transmitted payload), thereby authenticating the payload including the countersigner's public key and signature. A verifier independently verifies both signatures and the authorization state of both distinct participants in canonical order. A revocation whose target is not one of the two signers is invalid. This rule prevents the sole remaining authorized master from revoking itself. Late earlier authorization history requires re-evaluation rather than permanent rejection by receipt order.

**CONFIRMED by the originator on 2026-10-22:** The `REVOKE_MASTER` uncompressed event body is exactly `event_kind 0x21 || revoked_public_key[32] || countersigner_public_key[32] || countersignature[64]`. The complete uncompressed body is therefore **129 bytes**. The field order is fixed; the 149-byte signed-intent message above is unchanged. A compressed version, if used, compresses the entire 129-byte body using the ordinary outer compression convention.

### 6.4 Remaining event-protocol decisions

**CONFIRMED:** Complete Version 1 base column-attribute grammar is in Section 6.2.2; FLOAT, DATETIME and text policies are in Sections 6.2.4–6.2.5. A `CREATE_TABLE` event contains its complete initial table schema in one block; later alterations are separate events. **OPEN:** The larger SQL schema-feature contract (indexes, collations, constraints as metadata, type-specific conversions/default expressions), exact handling of hostile but correctly signed malformed event bodies, dependencies for unrecognized schema events, and interoperable error/status categories. Configurable resource limits are mandatory (Section 5), but their concrete configuration interface is not frozen.


## 7. Deterministic reconciliation and conflict semantics — CONFIRMED

A node retains all authenticated blocks it has received; it validates the signer **against canonical authorization history**, then derives the current schema and rows from applicable events in canonical order. **Later modifications win over earlier modifications to the same affected field** unless a deletion applies; **deletions always win for the same immutable entity GUID**, including after earlier or later updates. The same rule applies to rows, columns, and tables. A deleted row/table/column cannot be recreated with the same GUID.

A delayed update to a GUID for which its earlier INSERT has not yet arrived is retained and logged as temporarily inapplicable; it has no visible effect until the missing earlier INSERT is received and the entity can be reprojected. Operations that cannot yet be interpreted because schema blocks are missing are similarly logged and reconsidered after recovery of dependencies. **A valid block is never ignored or permanently excluded solely because it arrives late or has an earlier timestamp.** When an earlier block changes the canonical interpretation of materialized history, the node **must correct the official projection**: it may roll back/recompute affected rows, schema and dependencies, or rebuild its materialized database **from genesis and the retained relevant full ledger history**. Rebuilding from scratch is conforming, even if slower. Any partial/recovery representation must not pretend the late valid block never existed. A block with missing authority/schema dependencies can remain pending until its prerequisites are known; that is not permission to discard it because of age. Unrelated rows need not be rolled back when selective replay is proven safe. Immutable event history itself never rolls back; only the derived materialized database state can be corrected. A raw block may remain retained even where later validation makes it unauthorized/inapplicable; it must not affect the projection.

**No secondary events from normal replay:** An ordinary block's application does not publish another block. Replay is not a user edit; ordinary blocks never echo into the ledger. Rare automatic corrections are separately initiated by an authorized master by publishing a **new authenticated correction block first** and then having the database consume it. The ledger does not distinguish human-driven and automatic signed changes.

## 8. Schema identity, name collisions, and conversion — CONFIRMED

Every table and every column has an immutable GUID. `RENAME_TABLE` and `RENAME_COLUMN` preserve GUID; concurrent rename events targeting a GUID settle by **latest canonical event wins**, unless the entity is deleted. A dropped column supersedes updates targeting it. A newly added table or column with the same name as an old/deleted one always receives a new GUID.

If two masters create distinct GUIDs under the same SQL-visible table name or the same column name within one table, **both entities survive**. **CONFIRMED priority:** The entity introduced **first in canonical creation-block order** retains the original name; later-created conflicting entities are renamed to `original-name-entityGUIDwithoutdashes` by **new signed corrective RENAME blocks authored by an authorized master**. GUID numerical order and network arrival order do not determine which entity keeps the name. The authorized master publishes a correction block before its own database applies that correction; a read-only replica cannot originate corrections. Until a corrective block arrives, a replica retains both GUID-distinct entities, possibly with ambiguous SQL-visible names. All valid independent repairs remain in ledger history; latest valid explicit rename events still win canonically unless deletion applies.

When a column data type changes, convert existing values where possible. On incompatible conversion, log an error. If NULL is permitted, use `NULL`. If NULL is not permitted, use a suitable datatype default (`0` for a numeric type, empty string for text, minimum supported date/time for date types, etc.). On encountering an incompatibility that calls for an automatic replacement, a master creates a **new row-value-update block**, rather than silently modifying official state. Automated changes may themselves be followed by later canonical changes; arbitrary circular replay/republishing is not allowed.

**CONFIRMED 2026-10-22:** Distinct GUID entities with conflicting names must survive and an authorized master must publish deterministic GUID-suffixed renames as authenticated corrective blocks. Until correction, SQL name resolution must not silently bind an ambiguous name to an arbitrary entity or discard either entity; the visible state and any ambiguity/error must be deterministic for a given applied history. The same append-before-apply correction principle applies to invalid NULL/type values: an authorized master emits a separate signed corrective row block rather than unlogged storage mutation. **OPEN:** Full deterministic fallback-value table, concurrent automatic repairs that might collide again, interaction with later user renames, exactly when corrections are emitted, and transient read behavior while reconciliation is underway. A read-only replica never fabricates a corrective signed block.

## 9. SQL semantics, constraints, and transactions — CONFIRMED

Version 1 **does not enforce ordinary relational application constraints** such as primary-key uniqueness or foreign-key referential integrity when deriving authoritative changes. Two rows with different row GUIDs may contain identical business primary keys. Ledger deletions are not vetoed by referencing rows or application locks. Internal storage integrity and synchronization cannot be disabled merely to bypass these application-level rules. User-defined triggers, stored procedures, user-defined functions with effects, and comparable user-defined implicit write facilities are **not supported in Version 1**. This excludes their use as alternate mutation paths; it does **not** require removing SQL built-ins or internal database functions necessary for parsing, expression evaluation, data conversion, indexing, recovery, or supported SELECT/DML. The base Version 1 supported and excluded SQL subset is confirmed below; detailed per-command corner cases remain **OPEN**. Indexes and SELECT/query support may be used for efficient materialization and reads.

**CONFIRMED 2026-10-22 SQL scope:** Version 1 supports ordinary `SELECT`, `CREATE TABLE`, `ALTER TABLE`, `DROP TABLE`, `INSERT`, `UPDATE`, and `DELETE`, with `BEGIN`, `COMMIT`, `ROLLBACK`, and savepoints for provisional local staging. Unsupported user-defined triggers, stored procedures, user-defined functions **with side effects**, and other unapproved mutation paths must be rejected or made unavailable rather than allowed to bypass ledger-authoritative changes. PostgreSQL internal built-ins necessary for supported SQL remain available. This is a baseline feature subset, not a promise to preserve unsupported SQL extensions or direct writes. **OPEN:** The detailed supported expressions, joins, RETURNING/COPY/MERGE/CTE behavior, permissions/admin DDL, indices and types beyond those explicitly listed.

SQL clients can use provisional transactions. `BEGIN` starts uncommitted isolated changes; `ROLLBACK` before ledger publication discards those staged changes without emitting blocks. `COMMIT` begins publication of separately authenticated blocks reflecting final staged row changes; publication is **not group-atomic**. If a crash leaves only 60 of 100 intended blocks durably published, those 60 are permanent ledger history; remaining blocks may never exist. The materialized official database is updated only by ledger ingestion, not by the provisional transaction directly. **CONFIRMED 2026-10-22:** If the origin can determine that a COMMIT stopped after publishing only some intended blocks, it must communicate failure or explicit partial completion accurately, never claim atomic rollback of already durable blocks. Network disconnects may leave the client uncertain; they do not annul independently durable blocks. Resumable publication is optional, not a fundamental ledger requirement. Applications must handle missing/partial outcomes and any stronger business atomicity at a higher layer; compensating operations create new blocks and do not erase history.

Ordinary offline master writes do not wait for cross-master acknowledgment. A higher-assurance application may implement independent locks/reservations and require all masters' confirmation, recognizing that such applications can block during partitions and still need a true protocol to prevent simultaneous conflicting reservations.

**OPEN:** Precise SQLSTATE/message or progress response for partial `COMMIT`, ambiguous lost-response cases, retry identity and deduplication across network interruptions, optional staged-progress retention, row-count/RETURNING behavior, zero-change UPDATE emission, per-command unsupported-feature matrix, non-enforced metadata representation, and engine-specific type-affinity behavior. Explicit NULL-versus-omission semantics are now specified in Section 6.2. The initial system makes no promise that ordinary SQL semantics imply global transaction finality.

## 10. Materialization priority, durability, and recovery — CONFIRMED

A received valid block must enter persistent ledger storage before altering the official database. Master-originated blocks follow the same rule. Once received blocks are available, the database **pauses ordinary activity to process the blocks it already knows** and keep its local projection caught up; local precedence over throughput is intentional. This never implies certainty that no missing blocks exist on the wider network. A master can operate while other masters are offline, apart from local synchronization needed to apply known blocks.

Crash-safe progress tracking and idempotent replay or reconstruction must ensure a crash between block durability and materialization can be recovered without permanently omitting or double-applying effects. **CONFIRMED 2026-10-22:** Late valid historical blocks and later-revealed earlier authority history must cause the projection to be corrected through safe rollback/replay of affected materialization **or a complete rebuild from authenticated ledger history**; no timestamp-age cutoff may permanently suppress those corrections. Master/replica outage, out-of-order delivery, partitions, and late authority changes may all require selective re-projection. The ledger itself is immutable; the projection is replaceable. Acknowledgment of successful ledger append implies local durable storage; a block lost with all copies is unrecoverable.

**OPEN (Question 9 was not approved):** Whether ordinary SELECT during catch-up/reprojection sees (a) the most recent fully consistent older projection, (b) blocks until an atomic new projection is ready, or (c) another explicitly defined consistent/stale read state; also precise crash-safe cursor/checkpoint formats and applied-progress observability. The ordinary-read policy during in-progress rebuild has not yet been approved; the correctness requirement is that the final reconstructed projection reflect canonical ledger history. Internal rollback/replay versus full rebuild is an implementation choice, **not** an unresolved permission to ignore older blocks.

## 11. Peer-to-peer propagation and offline exchange — CONFIRMED behavior; management API extension required

Every node may receive, verify, store, serve, relay, and export an intact block, regardless of whether it may originate blocks. Block validity is transport-independent. Permit direct peer connections (TCP/QUIC or suitable supported protocols), decentralized peer discovery with configurable bootstraps, BitTorrent-inspired DHT, peer exchange, LAN service discovery, NAT traversal and relaying, HTTP(S), multipeer downloads, and real-time announcements. Nodes normally advertise new validated blocks to peers with resource/bandwidth controls. **No peer discovery or master quorum is necessary for an ordinary local write**.

Combine rapid announcements with periodic cryptographic **inventory reconciliation**, including Merkle-style summaries or equivalent, and comparisons by block ID/hash that do not depend exclusively on timestamps. Export and import must include individual blocks, timestamp ranges, a full ledger, and incremental missing-block batches; large whole-ledger archives can be imported as offline batch jobs, from arbitrary media. The ledger does not require a database to exist; a returning master can catch up through a read-only replica.

**CONFIRMED 2026-10-22:** Preserve compatible existing database SQL/client communications and extend them with a minimal **documented management interface** for new-ledger initialization, joining/pinning a ledger, signed raw-block publication/import, retrieval/export of individual blocks and batches, peer configuration/connectivity, and inspection of synchronization/applied status. The implementation may use SQL administration functions, a CLI, or an additional API for these operations; the **externally callable choices, object/message schemas, errors, and launch/configuration contract must be frozen before independent interoperability tests**. Detailed peer discovery, gossip, retries, transport library/protocol and transport scheduling are implementation freedoms, provided configurable peers can demonstrably exchange all relevant blocks under offline/local-network testing. **OPEN:** Exact management API command and response schema, bulk-file framing, inventory/dedup identity, bootstrap/DHT namespace, transport security and anti-Sybil policy; implementation-configurable budgets apply under Section 5.

**Flood resistance / inbound validation — CONFIRMED requirement:** Every node must inspect every received block for framing, protocol version, well-formedness, hash/signature integrity, ledger identity, and signer authorization before it has any database effect. A hostile actor may flood arbitrary invalid blocks. Reject invalid inputs safely and limit untrusted resource consumption (including decoding, disk, CPU, memory and networking) without treating plausible timestamps alone as grounds for rejecting otherwise valid authorized blocks. An out-of-order block whose missing authorization evidence has not arrived must not be materialized before its authority is established. Exact inventory, caching and DoS budgets remain implementation choices.

## 12. Intended guarantees and deliberate non-guarantees

**Intended:** Public cryptographic origin/integrity verification, signed historical authorization, deterministic canonical replay on the same accepted history, append-before-apply, one-row-per-block updates, delete-wins identity semantics, eventual convergence for participants that ultimately receive equivalent relevant events, permissionless block redistribution, complete reconstruction from validated genesis and all ledger history, independent progress during network partitions, and crash recovery for durably accepted events.

**Not promised:** Simultaneous SELECT visibility, knowledge of an objectively complete history at an arbitrary instant, automatic rollback of **ledger blocks**, all-or-nothing execution of multi-block SQL transactions, preservation of foreign-key/unique constraints, instant failover without surviving block copies, globally irreversible application business outcomes, and successful application-level strong locking when required masters are unreachable. **Required despite these non-guarantees:** correcting/rebuilding the derived database projection when an earlier valid block changes canonical materialization; retaining history and never suppressing an applicable block simply because it arrived late.

## 13. Acceptance scenarios (conceptual, not hidden verification details)

1. A new ledger initializes a genesis signer; an empty replica adopts the ledger and derives an identical state when fed the same history.
2. A grants B; B grants C; a late invalidation of B's grant invalidates C's dependent authority and relevant projected effects.
3. A signer key has no authority at a block position before its own grant; a later revocation invalidates subsequent blocks, not prior ones.
4. A and B publish independently while disconnected; a replica relays blocks to a disconnected master when peer links permit.
5. A block arrives late with an earlier timestamp; the node corrects affected materialization by selective rollback/replay **or complete rebuild from the validated ledger**, without treating the block as too old to apply.
6. Partial-column updates on the same row preserve separately modified columns; deletion of the row, table, or column GUID wins over modifications.
7. Same-name columns/tables with distinct GUIDs both survive; corrective naming blocks are produced by an authorized master, not by direct projection writes.
8. Duplicate primary-key values across distinct row GUIDs survive in Version 1.
9. `BEGIN`+`ROLLBACK` emits no blocks; partial failure during `COMMIT` can preserve only a published prefix of independently durable row blocks.
10. Crashes between ledger persistence and projection recover idempotently; replicas converge once their accepted block sets and authority histories agree.
11. Unsupported or temporarily uninterpretable blocks produce diagnostics, remain available for dependency recovery, and never silently mutate official storage through a bypass.

## 14. Owner approvals and remaining design issues — 2026-10-22

### 14.1 Confirmed in this revision

The owner approved design questions **1–8** on 2026-10-22: the complete base column-definition field order and declared-type parameters; revocation payload field order; Version 1 genesis/header identity policy; conservative FLOAT/TEXT/DATETIME/explicit-conversion edge rules; deterministic signed name/value repair principles; the ordinary supported SQL subset and honest partial-COMMIT outcomes; configurable resource and decoding limits; and the requirement for a minimal documented ledger-management API. These choices are incorporated in their primary sections above. The previously approved exact 149-byte revocation countersigned-intent message remains unchanged.

For question **9**, the owner confirmed that **every valid late-arriving block must be considered in the canonical projection**, even if this requires selective rollback/replay or a complete rebuild from the full relevant ledger. The owner **did not approve** the proposed ordinary-SELECT visibility policy during reconciliation; this remains open.

### 14.2 Protocol/interoperability details that still need exact specification

1. **SQL default/conversion semantics:** Which literal defaults are supported; insertion with omitted columns and defaults; full declared DECIMAL precision/scale enforcement and allowed conversions; nonnullable fallback values; timezone leap seconds; imported noncanonical float encoding; deterministic SQL error outcomes.
2. **DDL and SQL command boundaries:** Comprehensive allowed/disallowed syntax/features, table indexes/collations/constraints-as-metadata, schema renames and collision-repair corner cases, `COPY`/`MERGE`/`RETURNING`/writable CTEs and built-in volatile expressions; exact partial-COMMIT SQLSTATE or response representation.
3. **Genesis, malicious inputs, and forward compatibility:** Handling multiple competing self-signed genesis candidates, genesis-join API, unknown future versions/events, duplicate GUID with conflicting bytes, missing authority evidence, malformed but signed body classification, and error/status categories. Core Version 1 fingerprint and signed envelope are already confirmed.
4. **Management API and block-exchange interoperability:** Precise operation names/arguments/outputs, raw-block and batch file framing, node startup/join configuration, import/export and synchronization-status semantics, deterministic verification setup. Peer-discovery/transport algorithms themselves remain engineering freedoms.
5. **Resource limit configuration:** Specific externally documented knobs and required behavior on exceeding size, decompress, CPU/memory/time budgets. The requirement for configurable limits is already approved; no hidden fixed threshold is implied.
6. **Read behavior during re-projection — Question 9:** Define whether ordinary SELECT blocks during reconstruction or reads an independently consistent previous snapshot, and when each state becomes visible; define observable applied versus durable retained-block status. All valid earlier blocks must ultimately be materialized according to full canonical history. No permanently ignored age cutoff is permitted.
7. **Automatic repair liveness:** Deterministic resolution of repeated/conflicting name repairs and errors that require corrective value blocks; repair timing and safeguards against unlimited repair cycles. Corrections themselves must be signed ledger events authored by authorized masters.

### 14.3 Implementation freedom and change control

This specification defines externally required ledger behavior. Implementers may choose internal memory/WAL/index layout, SQL interception mechanism, rollback or from-scratch projection rebuild, crash recovery strategy, peer transport/discovery, and administrative-interface implementation **within the separately frozen callable contract**. An implementation cannot satisfy the requirements merely by providing an optional wrapper that leaves a supported direct SQL-to-storage mutation bypass. No particular PostgreSQL internal routine or code change is prescribed in this database-engine-independent design.

**Change control:** Incorporate future owner approvals into their primary sections, removing contradicted OPEN statements rather than appending overriding requirements. Do not equate approval of a principle with approval of an otherwise unspecified byte grammar or hidden-verifier convention.
