# Database Distributed Ledger

An MIT-licensed open-source distributed signed ledger that derives relational state from immutable authenticated history. **The ledger is authoritative; SQLite `.DLDB` is a rebuildable projection.** Independent masters publish offline, and replicas may verify/relay blocks.

**Status (2026-11-10; ARCH-012):** Architecture/design, not a released distributed-ledger implementation. Baseline: internally maintained **SQLite 3.54.1** fork with official upstream source provenance in the SQLite architecture plan; C#/.NET 10 application components. PostgreSQL/AI-task adapter obligations are historical. **Owner-approved signed-block protocol: 1.02 (`0x0102`)** with the 32-byte genesis-key ledger ID, exact 165-byte `DDL-REVOKE-v2` consent, and Version 2 identity manifest. Historical 1.01 (`0x0101`) and 1.00 (`0x0001`) are incompatible; no released historical ledger migration is required.

## Authoritative documents

- [Technical design](Database_Distributed_Ledger_Technical_Design.md) — Owner-approved 1.02 signed envelope and five-class value/event schema, genesis/authorization, canonical order, replay, conflicts.
- [SQLite architecture and implementation plan](SQLite_Architecture_and_Implementation_Plan.md) — native SQL interception, identity, files, replay, projection and phases.
- [Management API specification](Distributed_Ledger_Management_API_Specification.md) — Phase 1 management commands, JSON/error conventions, recovery and durability.
- [MIT license](LICENSE) — original project license; preserve SQLite upstream public-domain provenance and all third-party notices.

Existing `.docx` files are **historical and not synchronized** with current Markdown documents.

## Identity and storage

```text
ABC.DLDB                              # SQLite derived projection; absent in ledger-only Phase 1
ABC/                                  # support folder derived from database basename
    instance.identity.json            # approved Version 2; stable instance GUID, 32-byte ledger ID + exact genesis SHA-512 pin
    signing-key.v1.json               # Version 1 AES-256-GCM-obfuscated Ed25519 seed
    instance.config.json              # optional Version 1 persisted resource limits
    BLK/YYYY/YYYYMM/YYYYMMDD/         # validated raw signed blocks, exact bytes
    BLK/OUTOFRANGE/                   # timestamp outside UTC calendar years 0001–9999
    STAT/                             # disposable local statuses; mirrors BLK hierarchy
    DEL1/                             # temporary retired STAT tree during replay cleanup
```

**No `.DLID` file.** Identity/ledger binding survives `.DLDB` deletion. **Owner-approved:** ledger ID is the exact 32-byte original genesis Ed25519 public key; genesis must carry the same signer key, verify its self-signature and match the locally pinned SHA-512 of its entire signed bytes. No second hash or independently generated ledger UUID is used. Every explicitly initialized master/replica has a persistent signing key. SEC-002 uses a publicly embedded AES wrapping key for **obfuscation, not confidentiality**; no password, hardware, platform keystore or human unlock required in normal operation. Missing original key pauses all ordinary SQL and ledger processing until original-identity recovery; status/diagnostics/recovery remain available.

The approved signed block version is two bytes `01 02` for **1.02** with a full 32-byte genesis signer public key as ledger ID (163-byte header; 227-byte empty genesis). Owner-approved REAL behavior: native SQLite NaN originates as SQL NULL; signed REAL NaN encodings are invalid, infinities are valid, and signed negative-zero bits are retained in block bytes (even if projection normalizes them). Only five SQLite storage value classes are transmitted: NULL, INTEGER, REAL, TEXT and BLOB; **no special DECIMAL, DATETIME, BOOLEAN or GUID value tags**. SQLite declared column type text and affinity are preserved within the supported SQL/schema subset. Immutable internal GUID identities remain. Canonical history order is `(timestamp_utc_ms, block_guid, signer_public_key)`; `DDLB` transport-container version 1 remains unchanged. Ed25519 signing remains unchanged; the revocation-consent message is now the approved exact 165-byte `DDL-REVOKE-v2` grammar.

## Approved Phase 1 CLI and lifecycle

`ledgerctl --instance-root ABS COMMAND ...` exposes `instance init/status/recover`, `ledger init/join/recover-genesis`, `key backup/restore`, `block publish/import/get/export`, **`master countersign-revocation`**, `config get/set`, and consolidated `status`. Key-only original-instance recovery requires explicit `--confirm-unbound-initialization`; a missing config uses defaults and present corrupted config fails closed. One JSON response per finite command; exit groups 0 success, 2 invalid input, 3 partial import, 4 state/authority/recovery, 5 resources/time, 6 I/O/internal. New identity creation is explicit, initialization crash recovery uses `ledger recover-genesis`, and genesis binding requires a ledger ID and trusted entire-genesis SHA-512. The **Owner-approved local Version 2 `instance.identity.json` schema is normative** (Version 1 is historical). `block publish` can sign non-genesis events before any SQLite projector exists; ambiguous multi-ledger retrieval/export requires an explicit ledger selection. Signed timestamps beyond years 0001–9999 use the deterministic `OUTOFRANGE/T<sign-biased-16-hex>-<block-guid>-<sha512>.BLK` format.

## Delivery order

1. Independent ledger, identity, approved Version 1.02 codec, durable `.BLK`, import/export and management.
2. Native SQLite SQL interception: master mutations publish signed blocks or fail without touching official `.DLDB`.
3. Trusted ledger-to-SQLite projector, read consistency, `.STAT`, recovery, full replay.
4. Best-effort push and periodic `TREE.HASH`-assisted peer reconciliation.

Normal `.BLK` inventory is independent of local-only historical `.XBLK` archives; archived blocks never propagate until later reauthorized and restored. Configurable projection delay defaults to 0 seconds. Exact `TREE.HASH` byte grammar and peer protocol remain for Phase 4. All SQLite upstream tests are preserved; Windows 11 is initial QA acceptance platform. Project Manager coordinates work; Developer owns code, QA tests, Architect design docs and Security reviews.

**Repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger

**ARCH-011 peer-review handoff:** Phase 1 command argument/output/error details, authorizations, partial-import accounting, genesis crash repair, REAL conformance and deterministic QA acceptance objectives are now specified in [Management API §13](Distributed_Ledger_Management_API_Specification.md). QA/Tester owns independent protocol vectors and crash-injection tests; architectural documentation does not claim that those tests have been run or that Phase 1 implementation is complete. `TREE.HASH` grammar, native SQLite SQL interception and projector durability remain later-phase gates.

**ARCH-011 Owner approvals:** Phase 1 uses `status="retained"` for authorized, durable blocks that have **not** been projected; only unresolved historical authority is `pending`. Locally archived `.XBLK` contains pending, unauthorized/inapplicable and disputed authenticated variants (all excluded from normal get/export and peer inventories). `retained_count` counts all authenticated durable variants by full SHA-512 including genesis and archived `.XBLK`; `pending_count` and `quarantined_count` are disjoint; `disputed_count` is a subset of quarantine; `opaque_count=0` because unsupported signed versions are rejected without retention. Reclassifications preserve bytes and obey no-overwrite rules. `block get` emits a raw signed block; `block export` always emits a DDLB wrapper, even for zero/one block. Phase 1 requires independent ledger signing/archival without a SQLite projector, `.STAT`, or peer networking. The detailed normative contracts and Owner-approved recovery/countersigning/configuration formats are in [Management API §14](Distributed_Ledger_Management_API_Specification.md).


## ARCH-012 change-control notice

**Owner-approved design:** The immutable ledger ID is the full original genesis signer Ed25519 public key (32 raw bytes, 64 lowercase hex in JSON/CLI), not an independently generated UUID. Every accepted genesis must contain this same key in both ledger-ID and signer fields, have a valid self-signature, and match the **already-existing SHA-512 of the exact complete signed genesis block** stored in the local trust pin. A different valid genesis signed by the same key does not replace a trusted genesis. The ledger ID persists even after the original master's authorization is revoked.

**Owner-approved interoperability formats (2026-11-10):** Signed protocol 1.02 (`0x0102`; 163-byte header, 227-byte empty genesis), exact 165-byte `DDL-REVOKE-v2` countersign intent, and `instance.identity.json` Version 2 with `ledger_binding.ledger_id_hex`. These supersede the incompatible 1.01 UUID-ledger wire grammar but do **not** change signing-key JSON Version 1/AAD 88 bytes or DDLB transport-container Version 1. Developer/Codex may implement these approved specifications under the existing project process; QA independently develops and validates the new protocol vectors.
