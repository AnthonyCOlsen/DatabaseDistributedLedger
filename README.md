# Database Distributed Ledger

An MIT-licensed, independently developed distributed ledger that cryptographically authenticates changes and reconstructs relational database state across independently operating masters and replicas.

**Project status (2026-11-09): ARCH-003 Owner operational decisions and DESIGN-SEC-002 local-key-format review, with qualified upstream SQLite baseline validation; not a released distributed-ledger implementation.** The Project Owner approved a C#/.NET 10 implementation backed by an internally maintained SQLite 3.54.1 fork. Earlier PostgreSQL-specific integration and AI-verifier contracts are no longer the active implementation target. The **Version 1 signed ledger block **wire format** remains unchanged; the **Owner approved an amendment to canonical ordering on 2026-11-09**.

## Documents

- **[Technical design](Database_Distributed_Ledger_Technical_Design.md)** — ledger semantics, signed Version 1 binary blocks, identity, authorization, canonical ordering, and convergence. Includes the November 2026 SQLite direction; previously approved wire semantics are preserved.
- **[SQLite architecture and implementation plan](SQLite_Architecture_and_Implementation_Plan.md)** — approved SQLite-specific design decisions, `.DLDB` file identification, `.BLK` storage, SQL interception, precise values, recovery, keys, and phased delivery. Describes implementation details still to be validated.
- **[Management API specification](Distributed_Ledger_Management_API_Specification.md)** — SQLite-oriented proposed management interface. New exact CLI/JSON details are *draft*, not frozen interoperability guarantees.
- **[MIT license](LICENSE)** — licensing for original project content. Third-party components retain their own copyright and license/provenance requirements; SQLite core source is public domain.

The existing `.docx` documents are historical copies of the earlier design; **they are not synchronized with the November 2026 Markdown revisions**. Refer to the Markdown documents for current status until refreshed Word editions are generated.

## Key architectural principles

The independent, immutable ledger is authoritative; the local SQLite relational database is **derived state**. Each authorized master can publish signed blocks without network quorum. Replicas may verify, retain, relay, and apply those blocks without signing authority. Identical relevant history produces deterministic state, including when older blocks arrive late. The public SQL interface remains compatible with ordinary supported SQLite operations, but all changes must flow through signed ledger blocks before official database projection.

The official SQLite database uses extension **`.DLDB`** and a custom 16-byte signature (`UDDL database 1` followed by NUL) to discourage accidental editing by stock SQLite applications. This is **not encryption** or a guarantee against hostile filesystem access. Signed blocks use independent immutable **`.BLK` files**; local **`.STAT` sidecars are advisory only**. Every initialized instance, including a replica, has its own durable Ed25519 signing identity outside both ledger blocks and the rebuildable database. **SEC-002 (Owner approved November 8, 2026):** Version 1 obfuscates each private seed with AES-256-GCM under a versioned **publicly embedded** application-wide key. This permits unattended, cross-platform operation but **does not protect the seed from anyone with the encrypted file and public application source**. Cryptographic verification requires no signing key, and normal reconstruction with an **available** identity needs no recovery phrase. **ARCH-003:** If an existing key is missing, corrupted or unavailable, ordinary SQLite and ledger operations (including read-only replay) pause until the original identity is restored; diagnostics/status/recovery remain available. No silent key replacement; public-key authorization still depends on genesis/grant/revocation history.

## Intended delivery order

1. Independent ledger — format, identity/signing, authorization and durable `.BLK` storage.
2. **Native SQLite SQL interception** — preserve standard interfaces; supported master mutations publish authenticated durable blocks or fail without modifying official `.DLDB`. Test-only provisional SQL state is isolated and non-authoritative.
3. **Ledger-driven SQLite projector** — official `.DLDB` writes from authenticated blocks only; `.STAT`, precise types, consistent SELECTs and crash-safe deterministic rebuild.
4. Peer exchange and eventual convergence — offline masters and permissionless replicas.

**ARCH-003 approved operational layout (revised November 9):** Beside `ABC.DLDB` is the **`ABC/`** support directory, which contains its same-basename descriptor **`ABC/ABC.DLID`**, `instance.identity.json`, `signing-key.v1.json`, `BLK/`, and `STAT/`. This replaces the earlier location of `ABC.DLID` next to `.DLDB`. `instance.identity.json` pins the ledger GUID and entire-genesis SHA-512 outside rebuildable `.DLDB`. A single STAT manager rotates `STAT` to `DEL1`/`DEL2` etc. before full replay; a blocking external lock pauses ordinary processing until cleared, with diagnostics. Original upstream SQLite tests remain tracked; each proposed exclusion requires review/Owner approval. The existing Version 1 risk of backdated blocks from a compromised master key is explicitly accepted. No phase may expose a supported direct mutation path bypassing the authenticated ledger. The Project Manager coordinates implementation and test assignments; the Developer owns production code, QA owns tests under project permissions, and the Architect owns design documentation.

**Note:** The ledger GUID and first master's Ed25519 public key are distinct identifiers cryptographically associated by the genesis block. Multiple `.DLDB` files may share a parent directory while retaining independent same-basename support directories with their own in-folder `.DLID` descriptors.

**Under review:** The local Ed25519 signing-key JSON/AAD format is Owner-approved; the separate `.DLID` and identity-manifest byte grammar remains a candidate requiring formal review. Selected-history recovery into a new ledger is future planning only. **Approved synchronization direction:** SHA-256 `TREE.HASH` inventories over `BLK/` day/month/year/root levels, daily leaf inputs from alphabetically sorted full `.BLK` filename stems (including signed-block SHA-512), binary serialization, immediate best-effort pushes to known peers and periodic pull reconciliation. Exact cache/peer bytes and implementation timing still require review.

**Repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger

## November 9 Owner approvals and compatibility note

- A new, demonstrably empty `.DLDB` without prior instance files offers **new-ledger creation** or **join an existing ledger**; a previously initialized/orphaned database requires original-identity recovery instead of new identity generation.
- A join starts with the ledger GUID but must discover/import and validate genesis, then **display and explicitly confirm the full genesis SHA-512** against a trusted expected fingerprint before binding.
- Version 1 signing-key JSON/AES-256-GCM **88-byte AAD** format is approved in the SQLite Architecture Plan §8.3; no mandatory OS keystore or interactive unlock.
- **Canonical ordering changed by Owner approval:** `(timestamp_utc_ms, block_guid, signer_public_key)` ascending. The earlier signer-before-GUID ordering is obsolete. Binary signed-block format is unchanged, but equal-timestamp historical replay semantics are not backward-compatible without assessment.
- Hierarchical SHA-256 inventories and best-effort push/periodic pull are **Owner-approved design goals**; exact binary serialization/peer protocol remains under review. Advertised hashes describe the active `.BLK` set. Historically invalidated `.XBLK` archives remain local-only and do not appear in peer inventories until revalidated, restored to `.BLK`, and reannounced. Inventory order does not replace canonical replay order.

## November 9 Owner proposals awaiting reconciliation

The Owner directs reversible local archival of blocks that become **historically unauthorized** after late authorization history. Their `.STAT` entries are invalidated; unchanged signed bytes are renamed from `.BLK` into compact `.XBLK`, and official projection is rebuilt. They are distinct from corrupt-signature blocks. **Archived `.XBLK` is never propagated or inventoried to peers.** If later historical evidence restores authorization, verify the archived bytes, rename to the canonical full-SHA-512 `.BLK` filename, update active hashes, and allow normal push/pull. The approach intentionally does not promise that active inventory equality implies identical privately retained archives.

The **Owner-approved** nonnegative per-instance projection delay (`projection_delay_seconds`, default **0**) applies only to SQL row/schema materialization, not block receipt/verification, durable retention, or canonical authority checks. It reduces some unnecessary replay but provides no finality or guarantee against late blocks. The `.XBLK` local-only/restore policy is Owner-directed; exact binary peer inventory serialization and crash-safe reclassification mechanics remain engineering review items. See SQLite Architecture Plan §§18–19.

### Compact local block filenames (Owner-approved November 9)

```text
ABC.DLDB
ABC/ABC.DLID
ABC/BLK/YYYY/YYYYMM/YYYYMMDD/YYYYMMDD-HHMMSSssss-<block-guid>-<full-block-sha512>.BLK
ABC/STAT/YYYY/YYYYMM/YYYYMMDD/YYYYMMDD-HHMMSSssss-<block-guid>-<full-block-sha512>.STAT
```

The local invalidation archive filename is `YYYYMMDD-HHMMSSssss-<block-guid>-<signer-public-key>.XBLK` (64 lowercase hex signer-key digits). `.XBLK` is retained and locally reevaluated but **never propagated**. When reauthorized, the original signed bytes return to a standard SHA-512-bearing `.BLK` filename and become eligible for inventory/push/pull. The signed binary block format and no-overwrite retention rules remain unchanged.
