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

**ARCH-003 approved operational layout:** A same-basename `.DLID` descriptor sits beside `.DLDB` and points to a persistent same-basename sibling directory (for example, `ABC/` beside `ABC.DLDB`) containing `instance.identity.json`, `signing-key.v1.json`, `BLK/`, and `STAT/`. `instance.identity.json` pins the ledger GUID and entire-genesis SHA-512 outside rebuildable `.DLDB`. A single STAT manager rotates `STAT` to `DEL1`/`DEL2` etc. before full replay; a blocking external lock pauses ordinary processing until cleared, with diagnostics. Original upstream SQLite tests remain tracked; each proposed exclusion requires review/Owner approval. The existing Version 1 risk of backdated blocks from a compromised master key is explicitly accepted. No phase may expose a supported direct mutation path bypassing the authenticated ledger. The Project Manager coordinates implementation and test assignments; the Developer owns production code, QA owns tests under project permissions, and the Architect owns design documentation.

**Note:** The ledger GUID and first master's Ed25519 public key are distinct identifiers cryptographically associated by the genesis block. Multiple `.DLDB` files may share a parent directory while retaining independent `.DLID` descriptors and support directories.

**Under review (not approved features):** Exact local JSON/AAD identity format is an Architect candidate requiring Owner/Security/QA sign-off. Selected-history recovery into a new ledger is future planning only; hierarchical SHA-256 block inventory (daily leaf inputs use alphabetically sorted `.BLK` filename stems with binary record framing), best-effort peer push and periodic pull are under evaluation, not approved protocols.

**Repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger

## November 9 Owner approvals and compatibility note

- A new, demonstrably empty `.DLDB` without prior instance files offers **new-ledger creation** or **join an existing ledger**; a previously initialized/orphaned database requires original-identity recovery instead of new identity generation.
- A join starts with the ledger GUID but must discover/import and validate genesis, then **display and explicitly confirm the full genesis SHA-512** against a trusted expected fingerprint before binding.
- Version 1 signing-key JSON/AES-256-GCM **88-byte AAD** format is approved in the SQLite Architecture Plan §8.3; no mandatory OS keystore or interactive unlock.
- **Canonical ordering changed by Owner approval:** `(timestamp_utc_ms, block_guid, signer_public_key)` ascending. The earlier signer-before-GUID ordering is obsolete. Binary signed-block format is unchanged, but equal-timestamp historical replay semantics are not backward-compatible without assessment.
- Proposed binary hash-tree inventories remain **unapproved for implementation** and are distinct from the authoritative canonical block order.
