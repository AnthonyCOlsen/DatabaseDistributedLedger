# Database Distributed Ledger

An MIT-licensed, independently developed distributed ledger that cryptographically authenticates changes and reconstructs relational database state across independently operating masters and replicas.

**Project status (2026-11-07): Architecture and design documentation; not a released implementation.** The Project Owner approved a C#/.NET 10 implementation backed by an internally maintained SQLite 3.54.1 fork. Earlier PostgreSQL-specific integration and AI-verifier contracts are no longer the active implementation target. The **Version 1 signed ledger block protocol** remains unchanged.

## Documents

- **[Technical design](Database_Distributed_Ledger_Technical_Design.md)** — ledger semantics, signed Version 1 binary blocks, identity, authorization, canonical ordering, and convergence. Includes the November 2026 SQLite direction; previously approved wire semantics are preserved.
- **[SQLite architecture and implementation plan](SQLite_Architecture_and_Implementation_Plan.md)** — approved SQLite-specific design decisions, `.DLDB` file identification, `.BLK` storage, SQL interception, precise values, recovery, keys, and phased delivery. Describes implementation details still to be validated.
- **[Management API specification](Distributed_Ledger_Management_API_Specification.md)** — SQLite-oriented proposed management interface. New exact CLI/JSON details are *draft*, not frozen interoperability guarantees.
- **[MIT license](LICENSE)** — licensing for original project content. Third-party components retain their own copyright and license/provenance requirements; SQLite core source is public domain.

The existing `.docx` documents are historical copies of the earlier design; **they are not synchronized with the November 2026 Markdown revisions**. Refer to the Markdown documents for current status until refreshed Word editions are generated.

## Key architectural principles

The independent, immutable ledger is authoritative; the local SQLite relational database is **derived state**. Each authorized master can publish signed blocks without network quorum. Replicas may verify, retain, relay, and apply those blocks without signing authority. Identical relevant history produces deterministic state, including when older blocks arrive late. The public SQL interface remains compatible with ordinary supported SQLite operations, but all changes must flow through signed ledger blocks before official database projection.

The official SQLite database uses extension **`.DLDB`** and a custom 16-byte signature (`UDDL database 1` followed by NUL) to discourage accidental editing by stock SQLite applications. This is **not encryption** or a guarantee against hostile filesystem access. Signed blocks use independent immutable **`.BLK` files**; local **`.STAT` sidecars are advisory only**. Private Ed25519 signing keys live outside both ledger blocks and the rebuildable database.

## Intended delivery order

1. Independent ledger — format, signing, authority, exact `.BLK` storage and import/export.
2. Ledger-driven SQLite projector — `.DLDB`, exact typed values, SQL reads, full rebuild and crash recovery.
3. Native SQLite SQL write interception — preserve standard interfaces, create ledger blocks, never modify official projection directly.
4. Peer exchange and eventual convergence — multiple offline masters and read-only replicas.

No phase may expose a supported direct mutation path bypassing the authenticated ledger. The Project Manager coordinates implementation and test assignments; the Developer owns production code, QA owns tests under project permissions, and the Architect owns design documentation.

**Repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger
