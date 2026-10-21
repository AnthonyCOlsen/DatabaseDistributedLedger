# Database Distributed Ledger

An open-source database server farm synced through a cryptographically authenticated, multi-master distributed database ledger.

**Status:** Working technical proposal. This is not a working database engine. Open questions and earlier statements superseded by later design updates are explicitly preserved in the current draft for further review.

**Repository:** https://github.com/AnthonyCOlsen/DatabaseDistributedLedger

## Design documentation

- [Technical design (Markdown)](Database_Distributed_Ledger_Technical_Design.md)
- [Technical design (Word)](Database_Distributed_Ledger_Technical_Design.docx)

The proposal is **not committed to SQLite or any other underlying database implementation**. It considers ledger-first writes, authorized masters, public read-only relay, out-of-order block delivery, and deterministic conflict resolution.

## License

Copyright © 2026 Anthony C. Olsen. Original documentation is released under the [MIT License](LICENSE). Any future imported third-party code remains subject to its own applicable license terms.

*This repository contains an independent public design document, not an implementation.*
