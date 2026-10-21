# Database Distributed Ledger — Technical Design

**Copyright © 2026 Anthony C. Olsen.** This original design document is released under the [MIT License](LICENSE).

**Public repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger

Design specification — working draft (updated October 2026)

**Status:** Conceptual architecture capturing the originator's stated requirements. This is a self-contained handoff document, **not** a finalized implementable protocol. Items marked **OPEN** require decisions before coding. Recommendations are explicitly distinguished from requirements.


## 1. Executive summary

Build a public-data, multi-master relational database whose authoritative source is a widely replicated, append-only, cryptographically authenticated log of database changes. Any currently authorized master may independently issue changes while disconnected; read-only replicas may validate, store, replay, and relay blocks, including between masters unable to communicate directly. Every participant deterministically sorts all known valid blocks by UTC creation timestamp at millisecond precision, breaking equal-timestamp ties by the signer's public key, and derives its local database from that history. Late-arriving earlier operations may require **selective** rollback/replay of affected rows and dependencies. Convergence is eventual, not instantaneous; applications are responsible for choosing and enforcing stronger business transaction guarantees when required.


## 2. Vocabulary and trust model

**Ledger / blockchain:** Publicly readable, immutable, append-only set of authenticated change blocks, with references to predecessor blocks. It has a **single canonical logical ordering**, even if physical predecessor references reflect incomplete knowledge at creation time.

**Master:** Database participant with a currently authorized signing key; may create signed change blocks and delegate authority.

**Replica:** Any participant that validates, stores, relays, and applies blocks but cannot create authoritative blocks without an authorization grant.

**Genesis master:** Initial sole authorized signer.

**Block signer:** Public/private keypair holder. The private key signs blocks; everyone uses the public key to verify signatures. This is **digital signing, not public-key encryption**. The original description used 'encrypt with private key / decrypt with public key'; signing implements its intended public verification property. Data is public, so confidentiality encryption is not required.

**Record GUID:** Stable globally unique row identity, independent of any application-defined primary key, potentially hidden in the database but present or resolvable in row mutations.

**Canonical order:** Ascending UTC millisecond timestamp, then deterministic ordering of signer public keys. See OPEN tie cases.


## 3. Non-negotiable design requirements (as stated)

1. The ledger, not any individual database, is the authoritative record of changes; all changes must be durably added to the ledger **before** being applied to the issuing database or downstream databases.

2. Genesis has one master; a signed ledger authorization operation can grant another database/public key master status. **Any currently authorized master may grant further master status.**

3. Revocation of an existing authority requires authorization by **both the grantor and grantee public keys** as specified by the originator. Already-issued blocks from a later-revoked key remain historically valid.

4. Masters may create and publish changes independently and concurrently without coordinating first. This continues during partitions and outages.

5. Each new block refers to the latest block its creator knows about. The originator explicitly rejects the idea of accepting competing alternative chains and choosing one winner: **all valid blocks are retained**; the system may simply lack some latest blocks. A physical history with concurrent predecessor references can nevertheless be a DAG. This terminology must not be interpreted as permission to discard blocks or to elect a winning branch.

6. The system imposes one deterministic **logical** chronological order, not an arrival order: UTC timestamps with millisecond precision, with the **signer's public key itself** as the secondary tie-break key when timestamps are equal.

7. Masters must regularly synchronize clocks with an agreed trusted master time reference, preferably atomic-clock-backed. Clock agreement is an operational assumption; it does not by itself prove total-order finality or eliminate timestamp manipulation.

8. Participants apply changes in canonical order, including INSERT, UPDATE, DELETE, CREATE TABLE, ALTER TABLE, DROP TABLE, and other representable database/schema changes.

9. Late-arriving blocks are accepted and inserted into their proper logical position. A replica intelligently rolls back/recomputes **only affected rows and dependent state**, not necessarily the entire database.

10. Conflicts follow pre-agreed deterministic semantics. Specifically **deletion wins over modification of the same logical record**, including updates made during a period when another participant's earlier deletion was unknown. A delayed deletion overrides those modifications.

11. Every row has an underlying immutable globally unique GUID, even when its table has a different declared primary key. The GUID may be hidden or exposed and should be associated with mutations.

12. Every table has an underlying immutable globally unique GUID. The GUID may be hidden or exposed and should be associated with mutations.

13. Any participant can read, validate, retain, and redistribute blocks. A read-only replica can transmit blocks to a master, even when masters cannot connect directly. Replicas need not have private signing keys.

14. Ledger data is intended to be **public**; private/confidential application data is not stored in this database.

15. The database promises **eventual convergence**, not immediate agreement among replicas. Applications—not the base database—decide when stronger transactional assurance, locks, or confirmation are necessary.

16. A participant cannot always know it has every earlier block. It acts on what it has; a higher-assurance application may query masters about recent block timestamp/signing-key lists and/or run a confirmation protocol, recognizing that constantly active masters may never have identical latest-block lists.


## 4. Block envelope and authentication


### 4.1 Intended visible envelope

The original proposal places in plaintext at the beginning of each block: (a) a unique **blockchain identifier GUID** (precise distinction from per-block ID is OPEN), (b) UTC block creation timestamp, and (c) public verification key of the signer. A signed body repeats these header values, allowing mismatch detection.

**Recommended representation (not yet approved):** canonical-encoded header containing ledger_guid, unique block_guid, utc_timestamp_ms, signer_public_key, protocol_version, payload_hash, encrypted_payload_hash (which you can decrypt using the signer_public_key), the payload (which contains typed database operations, GRANT_MASTER, or REVOKE_MASTER (additional details below)  – usually just one operation per payload).

Signature algorithm: Ed25519 (confirmed 2026-10-21; supersedes AES-256-CBC)

hash algorithm: SHA-512

Canonical serialization: Everything is in JSON the payload being 7-zip compressed and then Base64Encoded:

ledger_guid

unique block_guid

utc_timestamp_ms,

signer_public_key,

protocol_version,

payload_hash (of everything, in CSV form, minus the payload_hash and encrypted_payload_hash),

encrypted_payload_hash,

payload

size limits: None – except that each block should normally contain a single atomic database operation, so the size of the block depends (indirectly) on the size of the largest operation’s corresponding data (like a table row being inserted). The operations don’t have timestamps – they are understood to have occurred at the time of the block’s time-stamp. Multiple operations with different time-stamps should come over in different blocks.


### 4.2 Validity versus applicability

**Cryptographic validity:** Signature and hash links check out.

**Authorization validity:** Signer was entitled to issue the block according to the authorization history and its deterministic interpretation.

**Syntactic validity:** Operation and schema are well formed.

**Applicability:** The operation has a defined effect under deterministic conflict and schema rules.

A correctly signed block can still be inapplicable to the current derived database state. The system must retain its immutable historical record while deterministically recording its result (applied, superseded, rejected, etc.).

4.2.1 Precise Validity Rules:

A deleted row trumps updates to that same row

A deleted table trumps all other updates to that table

If a database (master or replica) receives the block granting authorization to public key, and it’s received long after other blocks from that same public key, it will initially reject all of those blocks. Then after it receives the public key authorization, then the next time it receives those blocks it will accept them.


## 5. Authority management

Genesis defines the ledger identity and initial master key.

A currently authorized master issues a signed **GRANT_MASTER** operation naming the new public key. The grantee then has authority under the ledger's deterministic authorization rules.

Any master may delegate again.

**REVOKE_MASTER** must contain verifiable approval by the grantor and grantee (both keys). One of the two (grantor and grantee) must be the encryptor (signer) of this block and the other is the (encryptor) signer of an ASCII-7 string “I AGREE” at the end of the deprecation block. Historical blocks issued before the effective revocation remain valid, even if those blocks are received out of order and arrive after the REVOKE_MASTER block.


## 6. Ordering, publication, and replication


### 6.1 Block issuance

1. A master receives a proposed schema/data operation.

2. It identifies row GUIDs, table GUIDs, validates locally as appropriate, obtains its synchronized UTC timestamp, and creates a signed block.

3. It durably commits the signed block to its local ledger storage ONLY, but triggers the event code to notify other databases that there was a recent change, triggering synchronization processing on a separate thread. The code that reads from the ledger is triggered that a new block has been receives and applies the database change to the materialized database.

4. It applies or derives the operation according to its currently known canonical history.

5. It gossips/serves the block to other masters and read-only replicas. No cross-master acknowledgment is required for ordinary issuance.

The ledger append and database projection must have crash-recoverable bookkeeping so a crash between steps 3 and 4 is replayable without double application.


### 6.2 Ordering rule

All known valid blocks are logically sorted by `(UTC_timestamp_ms, signer_public_key)` ascending (direction of public-key byte-ordering ascending based on the binary value of the base64 public key represents). This captures the originator's exact tie-break decision. The **same signer** may not create two blocks in the same millisecond – if it already used the millisecond it must move the new block to the next millisecond. If this is encountered anyway, the block GUID will be used as the 3rd sorting. The timestamp is creation time, not an assurance that all preceding events have been received.


### 6.3 Anti-entropy and relay

Participants may request missing blocks by hashes/IDs, ranges, known heads, or other inventory protocols (**OPEN**).

Any replica can relay an intact signed block to any other node, including a master; the relay is not treated as its author.

Out-of-order arrival is normal. There is no requirement to wait for every master to be online before making ordinary changes.

A read-only node may catch up from any peers and independently reconstruct the database from the genesis block.


### 6.4 What 'no branches' means

The design **does not** perform longest-chain selection, fork-choice voting, or dropping one of two concurrently signed valid blocks.


## 7. Materialization and selective reconciliation

Every participant keeps a local materialized relational database and enough ledger/index/projection state to recompute affected portions when an older block arrives.


### 7.1 Basic example

Chronological history: `A=tree`, `B=apple`, `B=pear`, `A=bush`. If `A=tree` is delivered only after `A=bush`, only Row A's history needs reconsideration; Row B remains `pear`. If an operation for A dated between `tree` and `bush` arrives, replay A's operations in canonical order; the final value remains `bush` if these are unconditional assignments.


### 7.2 Conflict example: deletion wins

If Master A deletes row GUID R and Master B independently updates R while unaware of the deletion, the deletion is authoritative for that logical row and the update does not resurrect it. This remains true when the update arrived/applied before the deletion was learned. A new row reusing the same business primary key must have a **new GUID** and is not the same row.

Delete wins forever against all subsequent updates to the same GUID. Foreign keys and unique key cannot be guaranteed and the software layer above the database has to take that into account. A deleted row or table cannot be recreated with the same GUID because the delete trumps everything for that GUID.


### 7.3 Dependency-aware replay

Selective replay is trivial for independent unconditional row assignments but can require broader recomputation for: multi-row transactions, conditional updates, triggers, foreign keys, uniqueness constraints, generated columns, aggregate/materialized views, stored procedures, schema changes, and operations dependent on SELECT/query results. The system must track read/write dependencies or use conservative recomputation to guarantee identical results. **OPEN:** allowed operation language, determinism requirements, dependency tracking, and whether DDL forces global or table-level re-projection.


## 8. Application transaction semantics

The database's ordinary local acceptance is **not** a guarantee that a business outcome is irreversible across partitions. The originator explicitly delegates business correctness and stronger confirmation requirements to applications.


### 8.1 Example: two withdrawals

Two masters can independently accept withdrawals against the same apparent balance and later reveal an overdraft. An application may instead store each monetary movement as an individual transaction and calculate balances as rollups rather than mutating a single balance row. For an operation requiring stronger guarantees, it may first establish a **tentative lock/reservation**, collect confirmations from **all master databases** that the lock is compatible with each master's known total, and proceed only after the confirmations; failed lock means cancellation.

**Important engineering qualification:** An informal 'everyone checked and said yes' handshake does **not** by itself prevent two simultaneous reservations from both succeeding. A real protocol must define reservation identity, exclusive conflict handling, fencing/epochs or equivalent, durable votes, expiration/cancellation, membership snapshot, retry/idempotency, and crash recovery. During an unreachable-master partition, requiring *all* masters necessarily blocks this stronger operation. These are application/protocol requirements, not features the base database has already solved.


### 8.2 Optional high-assurance completeness check

An application may ask every master for timestamps and signing keys of the last X blocks, and proceed only if all match, but high throughput may prevent matching snapshots. Also, agreement on the last X identifiers does not prove that there are no unknown older blocks or undisclosed histories. An explicit stable-cut/barrier or quorum protocol would be needed for stronger proof. The originator did **not** require ordinary database operations to wait for such checks.


## 9. Relational features and schema changes

The ledger must express inserts, updates, deletes, table creation, table alterations, table deletion, and potentially any other normal observable database changes. The database must deterministically reconstruct both schema and data. Each row has a stable GUID distinct from application keys.

**OPEN:** DDL versus DML collisions; table identity GUIDs; column identity and renaming; schema-version binding for each operation; types, collations, precision, NULL semantics, constraints, triggers, indexes, permissions, functions, non-deterministic SQL, transaction boundaries, and whether operations are logical SQL statements or fully evaluated row-level changes. For deterministic cross-platform replay, **recommended** preference is canonical logical row-change events rather than re-executing arbitrary non-deterministic SQL.


## 10. Time and correctness model

All timestamps are UTC (UTC+00:00, colloquially 'UTC-0'); milliseconds are part of ordering.

Masters frequently compare/synchronize clocks against agreed authoritative time sources, ideally atomic-clock-backed.

Clock synchronization reduces drift but does not guarantee a causal or globally agreed final order. Delayed earlier timestamps may still arrive, requiring reconciliation.

**OPEN:** permitted clock drift, clock rollback handling, monotonic timestamps per signer, spoofed future timestamps, trusted time attestation, leap seconds, and policy for blocks far outside permitted skew.


## 11. Failure and recovery expectations

**Master offline:** Other authorized masters continue issuing blocks. Returning master obtains missing blocks from any reachable master or replica and reconciles.

**Network partition:** Both sides may progress independently; neither discards otherwise valid blocks when reconnected.

**Replica offline:** It catches up from peers, verifies signatures/authorization, and rebuilds its local projection.

**Out-of-order delivery:** Accept/store and selectively recompute relevant state.

**Crash between ledger append and materialization:** Replay pending ledger blocks exactly once in effect.

**Corrupt or forged block:** Reject/quarantine based on signature/hash/authorization checks, without accepting the relay's assertions.

**Authority change during partition:** Not fully specified; see Section 5.


## 12. Explicit guarantees and non-guarantees


### Intended guarantees, subject to completing the protocol

Public verification of the provenance and integrity of blocks.

No unauthorized block creation under the defined key-authority rules.

All valid accepted history retained; no fork-choice discard.

Deterministic replay from an identical set of valid blocks, assuming fully deterministic semantics and ordering.

Eventual convergence among participants that ultimately receive the same valid history.

Replication/relay independent of write authority.

Historical validity of blocks from keys subsequently revoked, under precisely defined effective-time rules.


### Not guaranteed

Immediate consistency across masters.

Availability of a global snapshot during network partition.

Permanent finality of an ordinary locally observed business result.

Prevention of overdrafts, double spending, or other application-level conflicts without stronger application protocols.

Completeness of a participant's known history at any arbitrary moment.

Security against malicious clock manipulation or compromised keys without additional rules.


## 13. Implementation work still required (do not assume decisions)

**P0 — essential to a working interoperable protocol:**

1. Canonical binary encoding, cryptographic signature and hash specification, ledger/block identity, exact predecessor-link rules.

2. True total-order tie-break for blocks from the **same key within one millisecond**, and unambiguous public-key byte ordering.

3. Deterministic authority validation under late-arriving grants/revocations, multi-level delegation, partitions, and compromised keys.

4. Deterministic DML/DDL semantics, delete-wins rule, constraints, transaction atomicity, and rejected/superseded operations.

5. Dependency-aware selective rollback/replay, or safe conservative fallback.

6. Durable append-before-apply protocol, checkpoints, projection recovery, and exactly-once effect.

7. Network anti-entropy, missing-block discovery, data integrity verification, and eventual delivery assumptions.

**P1 — security, operations, and scalability:**

8. Trusted time synchronization policy and adversarial timestamp handling.

9. Master key custody/rotation and emergency revocation.

10. Ledger storage growth, snapshots, pruning versus permanent verification, indexing, and historical query support.

11. Denial-of-service controls, block size/rate quotas, replay protection, malformed payload handling.

12. Membership-aware optional application locking/reservation protocol, timeout/failure behavior, and durable decisions.

13. Recovery and convergence testing under network partitions, reordered delivery, conflicting DDL/DML, and crashes.


## 14. Acceptance scenarios for future development

1. Genesis authorizes B; B authorizes C; all replicas verify the authority lineage.

2. A and B publish concurrently with the same predecessor; all valid blocks are retained, never subject to winner-takes-all chain selection.

3. Read-only R relays a signed block from A to B when A and B cannot communicate directly.

4. A node receives a timestamp-earlier block late; its canonical order changes and only impacted projections are recomputed where dependencies permit.

5. A row update conflicts with a row deletion; deletion wins regardless of network arrival order.

6. Two rows have independent updates; late arrival for A does not needlessly roll back B.

7. Two blocks have the same timestamp but different signer keys; key ordering produces the same sequence everywhere.

8. Two blocks from the same signer have the same millisecond timestamp; protocol must define deterministic resolution before this test can pass.

9. A formerly authorized key's valid historical blocks remain verifiable after its effective revocation.

10. A master crashes after durable append but before projection; recovery applies the change without duplication.

11. A partitioned master rejoins and catches up exclusively via read-only replicas.

12. An application requiring all-master reservation confirmation cannot complete while a required master is unavailable; ordinary writes can continue.

13. Rebuilding from genesis yields the same schema and row state as incrementally reconciled replicas, once histories match.


## 15. Independent implementation and future development

This document describes a database-engine-independent architectural proposal for a public, cryptographically authenticated, multi-master distributed ledger and relational materialization system. It is a conceptual design, not a released implementation or finalized interoperable protocol. The author may evaluate different open-source database engines as implementation foundations. Outstanding decisions remain explicitly marked OPEN.


## 16. Design updates — 2026-10-18 (originator clarifications)

This section records subsequent decisions and proposals. It supersedes conflicting statements in earlier sections; the rest of the document remains a work in progress and is not silently finalized.


### 16.1 Independent blocks — confirmed

Each block does NOT reference a previous block. Remove predecessor references, DAG/branch terminology, predecessor-link requirements, and predecessor-based acceptance tests from earlier sections when consolidating the specification. Blocks are independently authenticated and logically ordered by the agreed timestamp and signer-key rules. No valid block is discarded in a fork-choice process.


### 16.2 Complete engine transformation — confirmed

The proposed implementation fundamentally transforms an existing open-source relational database engine rather than adding only a replication plugin. Preserve existing SQL clients/editors and read/query capabilities. Intercept all user-originated mutations before they reach materialized storage; issue and durably record ledger blocks first. The database materialization write path consumes ledger operations exclusively, including locally originated operations.


### 16.3 Client visibility and durability — confirmed

A successful SQL write response may mean that a ledger block has been durably accepted, not that the local database view has already been updated. Subsequent SELECT queries may temporarily return older state. The editor need not be changed; eventual visibility is an accepted user-facing behavior. The system is intended to survive catastrophes if its authenticated blocks remain durably available on surviving nodes; a block existing only on destroyed storage cannot be recovered.


### 16.4 Constraint precedence and SQL feature scope — partly open

Authoritative ledger deletions must prevail over row/table locks and application-level referential restrictions, including foreign keys and primary-key-related constraints on other rows. Internal storage-engine synchronization and crash-safety must still be preserved. Whether to disable stored procedures, functions, triggers, and related implicit mutation mechanisms remains OPEN; no final decision has been made. Determine how to prevent any permitted feature from bypassing the ledger-only write path.


### 16.5 Development and verification — OPEN

Potential implementations should be evaluated through independent correctness and recovery testing, including network partitions, reordered delivery, crash recovery, and deterministic convergence. The underlying engine and implementation methodology remain open.


### 16.6 Remaining open decisions and legacy text

Earlier sections retain obsolete cryptographic wording (superseded by Section 18.22), plus unresolved choices on canonical byte serialization and hash boundaries, authorization ordering, clock trust, schema changes, selective replay dependencies, and trigger/function/procedure support. Earlier predecessor-reference statements are obsolete per 16.1. Review and reconcile these before treating the specification as an executable protocol.


## 17. Schema conflict decisions — 2026-10-20

This section records the originator’s subsequent decisions about DDL/DML collision semantics. It supersedes conflicting earlier text; the remaining OPEN items have not been resolved.


### 17.1 Column deletion — confirmed

Deleting a column takes precedence over updates to that column, including updates issued concurrently or received late. As with deleted rows and tables, deletion permanently tombstones the deleted column GUID. Recreating a same-named column never restores the deleted GUID.


### 17.2 Column identity — confirmed

Every column has an immutable globally unique GUID independent of its column name. Dropping and later adding a column with the same name creates a different logical column with a new GUID.


### 17.3 Column renaming — confirmed

Renaming a column retains its GUID. Mutations referring to an older column name must still target that same GUID when the mapping can be resolved. If different masters concurrently rename the same column GUID, the rename latest in canonical block order determines the surviving name.


### 17.4 Column data type changes — confirmed

When a column changes type, preserve existing values and convert them to the new type where possible. If a value cannot be converted, write NULL for that value and record an error in database logs. The error is diagnostic; it must not prevent deterministic replay or contradict ledger-first materialization.


### 17.5 Concurrent same-name column additions — confirmed in principle

If independent masters add columns with the same name to the same table and distinct column GUIDs, BOTH columns survive. The engine automatically disambiguates their SQL-visible names. OPEN: Specify a deterministic name-disambiguation algorithm, including collisions with existing names and effects on SQL queries and schema introspection.


### 17.6 Remaining schema work — OPEN

Still to decide: conversion syntax/precision/collation details; handling of NOT NULL or other constraints when failed conversion produces NULL; schema-version interpretation of old-name and old-type writes; SQL naming of concurrent additions; whether to support triggers, functions, or stored procedures; transaction boundaries; and the remaining OPEN items in Section 9. No decisions are inferred from this section beyond those explicitly stated above.


## 18. Block distribution and additional schema decisions — 2026-10-20

This section records newly confirmed requirements and supersedes contrary or incomplete language in earlier sections (including OPEN items in 9 and 17). The specification remains under review; issues explicitly marked OPEN are not resolved by implication.


### 18.1 Transport-independent block authenticity — confirmed

A block is self-authenticating using the ledger’s specified cryptographic validation and signer-authorization history. Acceptance and deterministic replay must not depend on the transport, relay identity, or arrival route. Transport receipt alone does not prove signer authorization; a valid block may remain pending until missing authorization or schema information is received.


### 18.2 Supported transport and discovery mechanisms — confirmed scope

Provide an extensible block-exchange architecture supporting direct peer links (including TCP/QUIC where practical), peer discovery and exchange, configurable bootstrap peers, decentralized BitTorrent-inspired DHT/tracker-style discovery, local-network discovery, NAT traversal/relay where available, HTTP(S) retrieval, concurrent transfer from multiple peers, and periodic anti-entropy reconciliation. Interfaces and mechanisms must degrade gracefully when unavailable. Exact wire protocols, identity namespace, privacy/abuse controls, and which capabilities are built into the initial implementation remain OPEN.


### 18.3 Permissionless read-only replication — confirmed

Anyone may create and operate a read-only replica and join the exchange network without registration or prior approval. Replicas can verify, store, replay, advertise, serve, and relay blocks to any other participant, including masters. Only authorized master signing keys may create authoritative new ledger blocks; possession of a block or ability to relay it conveys no signing authority.


### 18.4 Export, import, and database-independent ledger — confirmed

Support export/import of a single block, blocks selected by timestamp range, all blocks of a specified ledger, a complete ledger archive usable to reconstruct from genesis, and incremental exports of blocks missing at the destination. Allow batch import/export of the entire ledger. The ledger can exist and circulate without any database engine; any compatible, empty database instance may choose to adopt a ledger and then reconstruct its schema and contents by consuming blocks. A nonempty pre-existing database may NOT adopt a ledger. Exact empty-database preflight and ledger-adoption validation remain OPEN.


### 18.5 Automatic block advertisement and partition tolerance — confirmed

After a participant obtains and accepts a valid block, it automatically advertises availability to peers, subject to configurable bandwidth, storage, and resource limits. Independent ordinary writes at a master never require peer discovery, network connectivity, or acknowledgments from other masters. Synchronization is asynchronous and may require later catch-up; missing blocks must be discovered eventually under stated delivery assumptions.


### 18.6 Duplicate names across columns and tables — confirmed rule; convergence detail OPEN

When distinct GUIDs for columns in one table, or distinct table GUIDs in one namespace, create the same SQL-visible name, retain both objects. A master discovering the collision emits a separate authenticated rename block rather than making an unaudited local rename. The default generated column name is [original-name]-[column-GUID-without-dashes]. Apply the analogous GUID suffix to conflicting table names. If multiple masters independently generate correction/rename blocks for the same object, all valid blocks are retained, and the change latest in canonical order takes precedence; deletion of the corresponding GUID always overrides renames and other modifications. OPEN: deterministic choice of the object to rename, duplicate correction suppression and idempotency, collision of generated names, and avoiding repeated rename cycles under delayed block arrival. Do not assume the first master to encounter the collision is globally unique.


### 18.7 Failed conversions and NOT NULL constraints — confirmed rule; details OPEN

For a failed type conversion, the general rule remains to set the value to NULL and log an error. When a column is NOT NULL, replace the incompatible value with the default appropriate to its data type (e.g. numeric zero, empty string, or a minimum representable date/time, such as DateTime.MinValue where that type exists) instead of leaving NULL. The first master to encounter the case emits an additional authorized value-update block documenting the repair. The exact type mapping (SQLite does not have a native DateTime type), distinction between column-declared defaults and type defaults, and conflict-free/idempotent creation of repair blocks remain OPEN.


### 18.8 Operations observed under an outdated schema — confirmed behavior

If an operation cannot be interpreted because earlier schema blocks are missing, report an error and retain the signed block for retry/reconciliation after those missing blocks arrive. Do not permanently discard an otherwise valid operation merely because its dependencies are not yet known. Precise missing-dependency detection and retry scheduling remain OPEN.


### 18.9 SQL feature scope for initial implementation — confirmed intent

For the initial engine transformation, stored procedures, triggers, user-defined functions, and related features that introduce implicit or non-deterministic mutations are not supported and may require disabling applicable code paths. This does not permit disabling the underlying engine’s essential storage integrity, synchronization, or recovery mechanisms. The exact supported SQL subset remains OPEN.


### 18.10 Operational distinction: local writes versus synchronized materialization — confirmed

An issuing master accepts a local SQL mutation by durably writing an authenticated block first; it does not synchronously update the relational projection. Separately, the ledger consumer applies known valid blocks in canonical order. Peers can be offline or unreachable without stopping normal local block issuance. SQL reads may temporarily observe older projected state.


### 18.11 Remaining questions flagged for next design review — OPEN

Priority decisions: (a) idempotent repair-block generation and convergence without repeated correction loops, including NOT NULL value repairs; (b) what happens when the default generated name itself conflicts; (c) what constitutes an empty database suitable for ledger adoption; (d) how schema changes are represented and applied; (e) synchronization protocol details.


### 18.12 Concurrent correction blocks: latest-wins with delete precedence — confirmed 2026-10-20

If two or more authorized masters independently observe the same schema collision and emit separate valid correction blocks, accept and retain all such blocks in the public ledger. Interpret changes to the same logical object in canonical chronological order: the most recent change takes precedence, except that any valid deletion of that object’s immutable GUID is permanent and overrides all changes, regardless of arrival order. This same latest-wins / delete-wins principle applies generally to conflicting modifications, subject to more specific rules explicitly approved elsewhere in this document. Equal-timestamp ordering follows the already established signer-key and block-GUID tie-breaks. Arrival order does not determine precedence.

CLARIFICATION: Ordinary block application does not generate or publish another block. The later canonical block simply changes the materialized state; prior valid blocks remain retained. New ledger blocks can result from an existing block only under specifically approved, rare automatic repair rules, such as SQL-visible table/column name conflicts and NOT NULL conversion-default repairs. The system must avoid self-triggering repairs or reissuing the same correction merely because it is replayed. OPEN: exact detection/deduplication and termination rules for independently authored repair events, particularly where new information arrives late; these are correctness requirements, not a proposal to elect a single master or discard valid blocks.


### 18.13 One-way block materialization; exceptional correction issuance — confirmed 2026-10-20

All mutations submitted through an authorized master’s existing SQL/client write interface become ledger blocks before any change to materialized database state. Only ledger consumers may modify the materialized database. Consuming an ordinary block does not publish another block: e.g., if Master A writes “apple” and Master B writes “banana” to the same GUID, both signed blocks remain in the ledger and all replicas apply both in canonical order, ending at “banana” if it is later. No compensating or extra block is needed, even if one or both blocks arrive late.

The only intended exception is an explicitly defined rare system-generated repair, where observing a ledger block exposes a condition needing its own auditable authoritative change: e.g., resolving a duplicate table/column SQL-visible name or substituting a type default for a NOT NULL value after a failed conversion. An authorized master emits a new signed correction block, which the ledger consumer later applies through the same normal path. Merely applying or replaying any block—including a repair block—must not automatically re-submit that same change as a new block. Repair emission must be condition-driven, finite, and idempotent under repeated delivery/replay; independent masters may issue distinct valid corrections and latest-canonical-wins applies unless the target GUID has been deleted. The objective is no circular repair cascade; exact mechanisms remain to be specified.


### 18.14 SQL processing and smallest practical ledger operations — confirmed 2026-10-20

A master should, wherever feasible, evaluate a submitted SQL mutation against its currently known local relational state and emit blocks containing the resulting concrete changes, rather than publishing the original SQL text for re-execution by replicas. Prefer each ledger block to represent the smallest practical atomic change. The block is durably accepted before any associated materialized database mutation; the ledger consumer is still the only path that updates stored tables, including on the originating master. Evaluating the SQL for block generation must not itself directly commit user-requested changes to materialized tables.

This design explicitly does not guarantee that the relational database projection is updated when SQL statement execution or block submission returns. Subsequent SQL statements and queries may observe the old local projection and produce outcomes different from ordinary synchronous SQL expectations. Application developers are responsible for managing read-after-write assumptions and higher-level correctness when using this eventually consistent database. The existing SQL editor/client interface does not require redesign for this behavior.

RESOLVED IN SECTION 18.20: A multi-row SQL statement produces one independent block per affected row. Multi-row and multi-statement all-or-nothing ledger transactions are not supported in the initial version. Remaining OPEN details include conditional SQL evaluation, statement errors, and special operations that cannot be represented as a simple row mutation.


### 18.15 Independent operations from multi-statement SQL transactions — confirmed 2026-10-20

Even when a client groups multiple SQL statements in one traditional SQL transaction, each resulting database mutation is recorded as its own independent ledger block. Do not assume one multi-statement transaction maps to one all-or-nothing ledger block. Ledger-first acceptance and eventual visibility apply independently; applications/developers are responsible for designing workflows that tolerate delayed materialization and non-atomic cross-block outcomes. Earlier OPEN language about preserving transaction-wide atomicity is superseded by this decision. OPEN: detailed client-side semantics of BEGIN/COMMIT/ROLLBACK when writes have already been durably issued as separate blocks, and how compound row changes within one SQLite operation are split without violating the specified block granularity.


### 18.16 Two-party master revocation and retroactive reconciliation — confirmed 2026-10-20

Revoking a master requires the existing jointly signed revocation protocol: both grantor and grantee must authorize the revocation, as described in Section 5. A unilateral block issued by Master A does NOT revoke Master B. Once a valid joint revocation block is known, its effective position in canonical history governs authorization, not the date it was received. Any blocks created/signed by the revoked master after the revocation’s effective ordering point are rejected as unauthorized, even if some replicas previously accepted and materialized them while the revocation block was missing. Those replicas must selectively reverse or recompute all affected derived database state (including dependencies) to converge. Blocks signed before effective revocation retain their historical validity. OPEN: explicit rule for authorization-block ordering at an exact timestamp tie, and semantics of a grantor that has been independently reauthorized or revoked.


### 18.17 Same-name tables and immutable GUID identity — confirmed 2026-10-20

Every table has its own immutable GUID independent of SQL-visible name or schema contents. If masters create distinct tables with identical names, both survive as distinct objects, even if their definitions are identical. Use the same collision-detection and signed rename-correction mechanism specified for columns, applied to table names. Changes remain targeted by GUID, not current display name. Later valid changes prevail over earlier changes to the same object, except delete-wins. OPEN: exact default generated name for tables (column rule already specified); handling of concurrent rename repairs and name collisions across repairs under already stated latest-wins policy.


### 18.18 Behavior when an authorized master acts maliciously — confirmed initial version 2026-10-20

For version one, do not attempt to distinguish malicious from benign writes solely by content or conflict. A block from a master that remains authorized at its canonical position is not rejected merely because it conflicts with other authorized blocks. All otherwise valid authorized blocks are retained and interpreted under canonical ordering, latest-wins, and delete-wins. Normal cryptographic, protocol, syntactic, and authorization checks still apply. The initial version has NO automatic governance or recovery from a hostile takeover of valid signing authority. The stated manual recovery concept is to freeze an affected ledger locally at a chosen point, start an entirely new ledger under a new genesis master, and replay/reissue selected prior changes through the new ledger. This is a reset/migration concept, not an in-place rollback or a fully specified version-one feature. OPEN: precise mechanics and controls for a future reset/migration, if ever implemented.


### 18.19 Consolidation reminder — OPEN legacy contradictions

When the specification is fully edited, reconcile earlier sections that still mention predecessor block links, DAG branches, atomic multi-statement SQL transaction assumptions, or unilateral revocation scenarios. Section 16.1 confirms blocks have NO predecessor reference. The newer decisions in Sections 18.15–18.18 supersede those legacy statements. Other outstanding questions (canonical encoding, exact signed-byte boundaries, crash-safe append, repaired-value blocks, precisely supported SQL feature set, and timing policy) remain OPEN unless explicitly resolved elsewhere.


### 18.20 One row per independent ledger block — confirmed 2026-10-20

For version one, the smallest atomic unit for row-level DML is a single row mutation. If one SQL statement modifies 100 rows, the authorized master emits 100 independent authenticated ledger blocks, one per affected row, reflecting the concrete resulting changes evaluated against its currently known local state. These blocks need not arrive or materialize together; each is individually ordered, verified, and reconciled. There is no all-or-nothing ledger guarantee across those rows, even when the originating SQL statement or client transaction groups them. Developers must account for intermediate visibility and partial progress. This is the explicit initial-version requirement; larger atomic groups may be considered in a future version.

OPEN: Define the exact treatment of SQL statements that modify schema rather than rows, zero-row statements, failed statements, constraints, and database-internal operations whose effects cannot be reduced safely to a single row change.


### 18.21 Constraint, replay, and block-authentication clarifications — 2026-10-20


#### 18.21.1 Relational constraints disabled — confirmed, supersedes earlier primary-key proposal

Version 1 disables enforcement of application-level relational constraints, including uniqueness/primary-key and foreign-key restrictions, across independent ledger row events. Two independently inserted rows with distinct immutable row GUIDs may therefore share the same declared SQL primary-key value; neither is rejected merely because of that collision. This decision supersedes the earlier proposed rejection of a second duplicate primary key and any previous wording implying such rejection was final. Deletes remain authoritative. Physical SQLite integrity, ledger authentication, and crash safety remain mandatory and are not disabled. OPEN: Exact treatment of NOT NULL, CHECK, generated values, and the previously approved NULL-to-type-default repair-block behavior under broadly disabled constraints.


#### 18.21.2 Delayed UPDATE before INSERT — confirmed

A signed UPDATE event referring to a row GUID not yet materialized is retained in the ledger and logged as temporarily inapplicable; the database does not create a phantom row. When missing earlier INSERT history later arrives, the affected row is reprojected in canonical order, including its UPDATE(s) unless overridden by a delete. The engine does not require the network to deliver the INSERT before the UPDATE. This is a dependency/availability error, not necessarily evidence of a malformed block.


#### 18.21.3 SQL statements matching zero rows — confirmed

An ordinary SQL UPDATE/DELETE evaluated by a master against its currently materialized database and affecting zero rows produces zero ledger row-change blocks. The SQL text is not normally stored or replayed on other nodes. Applications needing an effect once missing remote history arrives must explicitly retry their SQL or implement a higher-assurance synchronization strategy. The base database does not re-run such a statement after later replication.


#### 18.21.4 Block proof mechanism — Ed25519 approved in Section 18.22

The required cryptographic property is public verification of each master-authored block: only the holder of an authorized master private key can construct a valid authenticator, while anyone with the corresponding public key can verify that authenticator against the exact authenticated block bytes/hash. The originator described this as decrypting an encrypted key/hash field and comparing the hash. IMPORTANT TECHNICAL DISTINCTION: AES-256-CBC is a symmetric encryption mode and cannot implement private-key authorship with public-key verification. The approved implementation uses Ed25519 (RFC 8032) for signature creation and verification, without treating signature verification as literal public-key decryption. Retain SHA-512 as the independent block hash. Signer-key encoding, SHA-512 hash placement, exact signed/canonical bytes, and algorithm versioning remain OPEN; do not implement an insecure invented private-key encryption or hash-only comparison.



### 18.22 Cryptographic standard — CONFIRMED 2026-10-21

Version 1 MUST use Ed25519 (RFC 8032) for asymmetric digital signatures on master-authored blocks. Authorized masters retain private signing keys; public verification keys allow unrestricted independent authentication. Use SHA-512 as the separately specified block hashing algorithm. The legacy AES-256-CBC "signature algorithm" wording is superseded and MUST NOT be implemented as block authentication; AES-256-CBC is symmetric encryption and has no public verification key. Ed25519 is a signature mechanism, not payload encryption, and this ledger's contents are public. Still OPEN: canonical wire encoding, precise signed bytes, whether the separately calculated SHA-512 digest is signed or embedded, hash-field cycle avoidance, version negotiation/algorithm upgrades, public-key encoding, and signature representation. This decision does not automatically resolve those separate protocol issues.


### 18.23 Authorization grant effective time — CONFIRMED 2026-10-21

Master authority is determined at the block’s position in the canonical timestamp-and-key order, not at block receipt time. A block signed by a newly granted master is unauthorized and must be rejected if its canonical position precedes the effective GRANT_MASTER authorization, even if the key is authorized by the time the block is received. Example: If Master A grants Master B authority at 10:00, a B-signed block at 09:59 is unauthorized. A previously materialized block found to be unauthorized after later receipt of relevant authority history must be removed from the derived database projection and its dependencies selectively reconciled; retained raw bytes may be kept for audit/quarantine rather than accepted as an authorized change. OPEN: exact tie-order semantics for grant and grantee blocks with identical timestamps, and safeguards against timestamp manipulation. This rule does not modify the existing two-party revocation requirement.

### 18.24 SQL transactions and partial commit publication — CONFIRMED 2026-10-21

`BEGIN` opens an isolated, provisional SQL transaction. Statements may evaluate and stage changes,
but MUST NOT mutate the official materialized database or publish ledger blocks before `COMMIT`.
`ROLLBACK` discards provisional changes and emits **no** blocks.

At `COMMIT`, staged resulting row changes are emitted as separate authenticated ledger blocks,
**one affected row per independent block**. Blocks are durably published individually, without
all-or-nothing group guarantees. If a crash interrupts publication after 60 of 100 blocks, the 60
published blocks remain permanent authoritative ledger history and the other 40 may never appear.
`ROLLBACK` cannot remove or undo a block once durably recorded. Independently receiving and
applying these blocks may happen later.

Applications that require all-or-nothing outcomes must implement their own coordination and/or
verify and issue missing work; compensation requires new ledger blocks and does not erase history.
Atomic publication of independent blocks is a possible **future** capability, not a Version 1 rule.

**OPEN:** Exact `COMMIT` client response upon partial failure, idempotent retries, recovery policy for
unpublished staged operations, isolation/staging implementation, and timeout/error behavior.
Do not infer atomic commit of a multi-block group.


### 18.25 Ledger versus database responsibility for incomplete SQL commits — CONFIRMED 2026-10-21

Architectural separation: The ledger is the foundational, authoritative, immutable record of individually issued blocks. A database engine and all applications are above it. The ledger neither interprets a multi-row SQL transaction as an all-or-nothing unit nor guarantees completion of a particular database-level SQL COMMIT. A durable block remains authoritative regardless of why other intended blocks were not created.

If a database crashes after issuing 60 of 100 intended row-change blocks, the ledger authoritatively contains those 60 blocks. The database layer should report or log that the intended COMMIT was not fully completed, where such failure can be detected. The crashed process cannot throw a live error after abrupt termination; a durable commit-progress record could enable the recovered database to log/report the partial failure and identify outstanding work.

Optional database-layer resilience: Upon restart, the database MAY use its durable staged-transaction or commit-progress metadata to resume an interrupted COMMIT and issue the remaining 40 independent blocks. Such continuation must avoid duplicate creation and handle retries, but it is an implementation choice for the database layer, NOT a requirement or guarantee of the underlying ledger. Likewise, the application may detect and repair incomplete work.

OPEN for the database implementation: Precise persistence of staged changes and publish-progress bookkeeping, deduplication/operation identity, whether and when to auto-resume, how and when SQL clients see failures, and semantics of an interrupted request. These are not ledger requirements. No previously created block may be erased or reversed by an ordinary SQL ROLLBACK.


### 18.26 Cascading authorization and historical re-evaluation — CONFIRMED 2026-10-21

Authorization is transitive and evaluated using the canonical ledger order. If subsequently received earlier history establishes that a master lacked valid authority at the time it granted authority to another master, the downstream grant is invalid, as are all blocks relying solely on that downstream authorization. This applies recursively through the grant chain. Previously materialized consequences must be selectively reconciled against the newly determined authorized set; signed historical bytes remain in the append-only ledger record.

Version 1 does not add a special governance or recovery mechanism for compromised authorized masters. Detailed adversarial governance scenarios are outside the public design scope.


### 18.27 Signed blocks with inaccurate clocks — CONFIRMED 2026-10-21

A block is not rejected or quarantined solely because its signed timestamp appears far in the past or future compared with the receiver's clock. Its declared UTC millisecond timestamp participates in canonical sorting; eligibility continues to depend on cryptographic authenticity, authorized signer at that canonical position, and syntactic validity. The operational expectation remains that masters synchronize their clocks regularly.

OPEN: Distinguish the treatment of temporarily missing authorization history from permanently invalid/unauthorized blocks. No automatic rejection solely on timestamp deviation is allowed.


### 18.28 Block-reception priority and materialization barrier — CONFIRMED 2026-10-21

When new blocks arrive, the local database pauses ordinary database activities until the received blocks have been authenticated as applicable and their effects, including any necessary reconciliation, have been reflected in the local materialized state. This is a local correctness barrier: it does not require confirmation from remote masters, completion of network-wide synchronization, or knowledge of blocks that have not yet been received. Correctness is more important than throughput or latency.

OPEN: The exact queueing policy for arrivals during processing, fairness/forward-progress protection under a nonstop incoming stream, reader concurrency, and receipt acknowledgments are implementation details. This rule does not supersede offline master availability for writes when there are no newly received blocks to process.


### 18.29 Block envelope and compression-byte proposal — PROPOSED, awaiting approval 2026-10-21

Proposed outer wire format: a JSON object with version, ledger_guid, block_guid, timestamp_utc_ms, signer_public_key, payload_sha512, payload (Base64 text), and signature (Base64 text). These field names and encodings are proposals only; the protocol is not frozen.

CONFIRMED: After Base64 decoding, the first octet of the binary payload identifies its compression method. The remaining bytes are the encoded operation payload. 0x00 means no compression; 0x01 means 7-Zip-based compression using the project/library the originator identified; values 0x02–0xFF are reserved. When uncompressed, the operation is represented as UTF-8 JSON. The precise 0x01 compression stream/7z archive subformat has not yet been chosen.

PROPOSED: Canonicalize the logical operation as UTF-8 JSON using RFC 8785 (JCS), then optionally compress it. Let payload_binary = compression_octet || encoded_operation. Calculate payload_sha512 over these exact binary bytes, before Base64 encoding. Generate Ed25519 signature over the RFC 8785 canonical UTF-8 bytes of the entire outer object excluding only the signature field. Public verification reconstructs those same bytes and checks the signature; hash verification compares SHA-512 of payload_binary with payload_sha512. This avoids circular signatures and hashes. Precise digest encoding (hex vs Base64) and timestamp decimal-string representation are OPEN.

OPEN: Decide whether compression ID 0x01 means a genuine single-file .7z archive or the LZMA/LZMA2 compressed stream supplied by the 7-Zip SDK. Those are different formats, and the wire protocol must select precisely one for interoperability. Define deterministic decoding, compression options, limits on decompressed size and work, canonical data-type encoding, and whether optional compressed bytes should be preserved exactly from the received original block.


### 18.30 Missing-block discovery and transport-independent synchronization — COMBINED APPROACH APPROVED 2026-10-21

CONFIRMED DESIGN CHOICE: Use a combination of complementary block discovery and anti-entropy techniques rather than depending on one transport, one peer, predecessor links, or timestamp-range checks alone. All nodes, including permissionless read-only replicas, may discover, request, verify, store, and relay blocks. An ordinary master write never waits for peer discovery or remote synchronization.

PROPOSED REAL-TIME PATH: On durable receipt of a new block, announce its ledger identifier, block GUID, and content hash to connected peers. Peers request blocks they do not possess and may fetch different missing blocks concurrently from multiple peers. Use peer exchange, bootstrap nodes, decentralized discovery, local-network discovery, relays, HTTPS, and manual file import/export as available. Relays never acquire write authority merely by transporting a block.

PROPOSED ANTI-ENTROPY PATH: Periodically and on reconnection, compare per-ledger cryptographic set summaries. Build a deterministic Merkle tree (or equivalent hierarchical digest structure) over immutable block identities and content hashes, partitioned by stable hash-prefix buckets rather than only by timestamps. Peers whose summaries differ recursively compare subranges, exchange inventories of differing block IDs/hashes, and transfer only missing blocks. Periodically check the full ledger, not just the newest time window, because late-arriving blocks can have arbitrarily old or future timestamps. Timestamp windows may supplement discovery as an optimization but cannot be the sole correctness mechanism.

PROPOSED VALIDATION: Check exact block bytes, ledger identity, block GUID, SHA-512 digest, Ed25519 signature, and the applicable authorization history independently of transport. Deduplicate retransmissions. Retain blocks whose signatures verify but whose authority/dependency status cannot yet be determined, for reevaluation as missing history arrives; do not let an untrusted peer decide validity. Any discrepancy between identical block GUIDs with different content hashes requires a defined conflict/error rule.

PROPOSED BULK PATH: Support import/export of a single block, time range, arbitrary ID set, full ledger, and incremental missing-block sets. Full archives must contain all underlying blocks needed for independent verification and database reconstruction, without requiring predecessor references or any particular master to be online.

LIMITATION: No synchronization protocol can prove or obtain a block that all reachable sources withhold or have lost. Eventual convergence assumes that previously issued blocks remain available from some eventually reachable source and that anti-entropy continues fairly.

OPEN IMPLEMENTATION CHOICES: Exact Merkle/set-summary encoding, hash-bucket fanout, gossip cadence, anti-entropy scheduling, connection limits, rate control, bootstrap defaults, resume offsets for partial downloads, conflicting block-ID handling, and version negotiation. The proposed mechanisms are implementation guidance, not newly approved protocol constants.


### 18.31 Custom binary block format — confirmed direction; sizes proposed 2026-10-21

**CONFIRMED:** Use a compact custom binary envelope instead of JSON/Base64: a fixed-width header,
raw binary payload, and an Ed25519 signature after the payload. For compression, use a raw
LZMA/LZMA2 stream, **not** a full `.7z` archive. The hybrid four-mechanism distribution and
anti-entropy approach is retained. The exact widths, offsets, and stream subformat below are
**proposed, pending approval**.

| Field | Proposed encoding | Bytes | Zero-based offsets |
|---|---|---:|---|
| `version` | unsigned 16-bit integer | 2 | 0–1 |
| `ledger_guid` | RFC 9562 UUID bytes | 16 | 2–17 |
| `block_guid` | RFC 9562 UUID bytes | 16 | 18–33 |
| `timestamp_utc_ms` | signed 64-bit milliseconds since Unix epoch, UTC | 8 | 34–41 |
| `signer_public_key` | raw Ed25519 public key | 32 | 42–73 |
| `payload_sha512` | raw SHA-512 digest | 64 | 74–137 |
| `payload_size` | unsigned 64-bit length in bytes | 8 | 138–145 |
| `payload_compression_type` | byte: 0x00 none; 0x01 LZMA2 | 1 | 146 |
| `payload` | raw binary payload | N | 147 through 146+N |
| `signature` | raw Ed25519 signature | 64 | 147+N through 210+N |

No alignment/padding bytes. Multibyte integer fields use big-endian network byte order;
GUIDs use RFC 9562 byte order. Fixed prefix 147 bytes; fixed suffix 64 bytes; total
**211 + N bytes per block**. `uint32` version would make this 213 + N bytes.

**PROPOSED authentication:** `payload_sha512 = SHA-512(raw payload bytes)`;
Ed25519 signature covers the exact byte sequence starting at `version` and ending
at the last payload byte, excluding only the final 64-byte signature. This signs
all headers, payload size, selector, digest, and payload without self-reference.
Block-content hash for transport/inventories remains a separate specified choice.

**PROPOSED 0x01 format:** Raw LZMA2 stream rather than `.7z` container. The decoder
needs an agreed dictionary-size property. Candidate design: first byte *within*
`payload` stores the raw LZMA2 dictionary property; the remaining bytes are the
LZMA2 stream (all included in `payload_size`). Alternative: fix the dictionary
size for protocol version 1. Exact LZMA2 framing, decoder property, memory and
expansion limits are OPEN. `0x00` carries raw, uncompressed bytes.

**OPEN:** Canonical binary encoding of the actual row/DDL/authority events, typed values,
byte/record limits, compression dictionary properties, and signed-message framing.
This section supersedes the older JSON/Base64 envelope proposals and the earlier idea
of locating the compression selector as the first byte of a decoded Base64 payload.
