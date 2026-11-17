# QA-010 coverage and checkpoint map

The current authority is the four root Markdown documents at candidate commit
`6328bbc3a289b969b33dc365f6d8639cc005793f` (ARCH-014 / signed Protocol 1.03).
Their actual SHA-256 hashes are captured per run. Historical 1.01/1.02 contradictions
are superseded. This matrix describes implemented tests, not acceptance or execution
success; each run's `coverage-results.csv` and `cases.jsonl` carry actual results.

| Requirement / authority | Reference tests | Actual candidate cases |
| --- | --- | --- |
| Technical §§4–5: 0x0103, 163-byte header, 64-byte signature, 227-byte genesis, offsets, BE integers, UUID bytes | `REF-RFC8032`, `REF-OFFSETS-DDLB`, every `REF-*` vector | `LIFE-01`, `WIRE-POS-*`, `WIRE-NEG-*` |
| Original full genesis public-key ledger ID and exact signed-genesis SHA-512 pin | `REF-genesis`, `REF-genesis_ledger_mismatch` | `JOIN-01`, `JOIN-SUBSTITUTION`, `JOIN-NEG-*`, `FI-GENESIS-*` |
| Five storage tags, minimal ULEB128/ZigZag, UTF-8 declared types, schema/default grammar, NULL/omission | Positive/negative typed and schema vectors | `WIRE-POS-*`, `WIRE-NEG-*` submitted to actual CLI |
| REAL ±0, ±infinity, signed subnormals, maximum finite, all representative NaN classes | `REF-real_*` | `WIRE-POS-real_*`, `WIRE-NEG-real_*`; signed bytes compared unchanged |
| Raw LZMA2 p=20, exact EOS/trailing rejection, independent interoperability and resource bounds | `REF-compressed_*`, `REF-lzma_*` | Incoming `WIRE-*`, `LZMA2-OUTBOUND`, `LIMIT-DECODED` |
| Historical signed 1.00/1.01/1.02 rejection without persistence | `REF-legacy_*` | `WIRE-NEG-legacy_*` |
| Canonical timestamp/GUID/signer order, independent of transport order | `replay()` uses raw tuple | `ORDER-01` deliberately opposes GUID and signer sort; `BATCH-ORDER-*`, `AUTH-02/03` |
| Explicit initialization persists original Ed25519 key before genesis; no silent replacement | Independent key decryption/public-key derivation | `LIFE-01`, `FI-01/02/03`, identity gates |
| First-time raw-genesis join validates before writes and keeps replica key | Exact independent genesis bytes/pin | `JOIN-01`, `JOIN-NEG-*`, `JOIN-SUBSTITUTION` |
| Interrupted local creation and replica join, immutable identity/trust, idempotent recovery | Surviving genesis independently verified | `FI-GENESIS-init`, `FI-GENESIS-join`, `FI-GENESIS-manifest` |
| Internally current UTC; no ordinary public clock/GUID override for A | Independent header/consent decode | `LIFE-01`, `PUB-01/02`, `REV-LIVE`; reflection of public APIs |
| Explicit-GUID ordinary retry returns original timestamp/hash/path; omitted GUID creates new block | Original bytes/JSON/hash oracle | `PUB-01`; no collision/dispute campaign |
| A signs exact 165-byte v3 consent; package 229; distinct authorized A/B and target A/B | Independent Ed25519 consent verification | `REV-LIVE`, `REV-A-NEG-*`, `REV-NEG-*`, `FI-16` |
| B signs 137-byte event with A GUID and separate current UTC | Independent full signed block decode | `REV-LIVE`, `REV-DELTA-*` |
| Signed delta 0/900000 accept; 900001/negative/overflow reject | Independently signed boundary vectors | `REV-DELTA-*`, `WIRE-NEG-rev_interval_overflow` |
| Durable revocation retry after expiry returns original publication | Old signed fixture, current receipt clock | `REV-EXPIRED-RETRY`, `FI-REVOCATION-ACK` |
| New valid inputs retained exactly; unresolved/known false/invalid rejected before writes | Independent signatures/events/authority | `AUTH-01/02`, `AUTH-NO-WRITE-CALLBACKS`, `WIRE-NEG-*`; hashes/directories/temp files compared |
| Retryable missing history vs nonretryable known unauthorized; no missing-projector gate | Canonical authority oracle | `AUTH-01/02`, `BATCH-MIXED`; schema-less DROP_TABLE is accepted when authorized |
| Only formerly accepted history becomes XBLK; pending/quarantine and reauthorization | Independent `replay()` states | `AUTH-02/03`, `FI-10..13` |
| XBLK never get/export; unchanged signed bytes after restart; inclusive/disjoint counts, disputed=opaque=0 | Full signed hashes/canonical oracle | `AUTH-02/03`, `counts()` assertions, `FI-10..13` |
| DDLB v1, canonical exact raw bytes including zero/one, raw get, no overwrite | `REF-OFFSETS-DDLB`, independent framing parser | `EXPORT-01/02`, `DDLB-*`, `BATCH-*` |
| Inverted transport dependencies; grant must be durable before dependent | Signed grant/dependent fixtures | `BATCH-ORDER-*`, `ORDER-01`, `BATCH-FAILED-GRANT`; exact crash-in-batch checkpoint BLOCKED |
| Mixed successful/duplicate/unauthorized/unresolved/malformed import; truthful partial counts and index/retryable fields | Independent fixtures and expected disposition | `BATCH-MIXED`, `DDLB-truncated/trailing` |
| Bounded import memory and no untrusted disk spill | Declared size/framing bounds | `BATCH-MEMORY`, `DDLB-overflow`, `LIMIT-DECODED` |
| Key JSON v1, AES-GCM AAD88, manifest v2, no silent legacy migration | `REF-AESGCM-AAD88`; independent fixture identities | `LIFE-01`, `IDENTITY-AAD-TAMPER`, `IDENTITY-MANIFEST-STRICT`, `IDENTITY-KEY-ONLY` |
| Missing/corrupt key blocks even reads; original backup/restore preserves trust | Saved identity/binding hashes | `IDENTITY-missing/corrupt` (FI-18); forbidden ordinary commands enumerated |
| Config v1/defaults, types/ranges, corruption/GUID mismatch, persistence | Exact approved field/value expectations | `CONFIG-01` (FI-19), `CONFIG-LIMIT-NEG`, `FI-14`, `FI-15-POST` |
| Same-host instance/root/copied-identity exclusion and termination recovery | Positive lease PID/barrier | `FI-17`, including copied root |
| Signed int64 OUTOFRANGE sign-bit-biased filenames | Independent timestamp/path formatter | `PATH-OUTOFRANGE` minimum genesis and maximum event |
| One JSON envelope/final LF, exit codes, exact field families, no private seed output | CLI schema checks | Every `cli()` invocation; specific field/byte assertions in lifecycle, publication, revocation and batch cases |

## Fault matrix reconciliation

| Historical FI ID | Active 1.03 observation | Status / limitation |
| --- | --- | --- |
| FI-01 | key temporary file flushed, before canonical key publication | Existing `after-flush:signing-key.v1.json`; test preserves original temp and checks recovery gate, not an undocumented automatic temp-key promotion |
| FI-02 | canonical key durable before manifest | Existing `after-publish:signing-key.v1.json`; explicit original key-only v2 recovery |
| FI-03 | recovery manifest temp flushed | Existing `after-flush:instance.identity.json`; repeat original key-only recovery |
| FI-04 | durable genesis before binding | Existing `genesis-durable-before-binding` during **both init and join** |
| FI-05 | bound manifest temp flushed | Existing `after-flush:instance.identity.json` after durable genesis |
| FI-06 | signed block temp flushed | Existing `after-flush:<BLK>`; no final accepted block; uncertain explicit retry cannot re-sign |
| FI-07 | post-rename / pre-directory durability | **BLOCKED:** no distinct callback inside single WRITE_THROUGH system call |
| FI-08 | durable block before acknowledgment | Existing `after-publish:<BLK>`; exact duplicate retry |
| FI-09 | completed DDLB prefix before next item | **BLOCKED:** Management batch has no injectable per-item barrier |
| FI-09-GRANT | actual DDLB grant durable before dependent | **BLOCKED:** public batch does not expose this transition. Sequential adapter imports are not substituted. Functional failed-prerequisite and inverted-order tests still run |
| FI-10/11 | before/after BLK→XBLK rename | Existing before/after rename callbacks, late signed revocation, restart classification |
| FI-12/13 | before/after XBLK→BLK restoration | Existing callbacks, late signed regrant, restart classification |
| FI-14 | config temp flushed before replacement | Existing after-flush callback; old complete config retained |
| FI-15 | config replaced before directory durability | **BLOCKED:** same single-call limitation as FI-07; `FI-15-POST` tests only after successful call return |
| FI-16 | signed consent package temp flushed | Existing output-file callback. ARCH-014 supersedes historical "same intent" retry: a fresh A operation generates fresh GUID/current time |
| FI-17 | competing ordinary opens while identity held | QA lease driver with positive ready/release handshake; same and copied roots; terminate/reopen |
| FI-18 | key missing/corrupt at restart | Functional cases exercise diagnostic and ordinary-operation gates plus original backup restore |
| FI-19 | config corrupt / different instance GUID | Functional config cases; identity remains healthy and no silent defaults |
| FI-20 | existing output / externally held target | `EXPORT-01` no-overwrite and `FI-20-HELD-FILE`; no random collision generation or special GUID arbitration |
| Additional | revocation durable before success acknowledgment | Existing after-publish BLK callback, original publication returned on retry |

## Remaining prerequisites and explicit boundaries

- Ordinary Windows 11/PowerShell 7 execution outside Codex is required for native
  locking/durability acceptance. Sandbox execution is diagnostic evidence only.
- Coordination is needed for the four BLOCKED checkpoint observations above. No
  production hooks were modified and no physical power-loss durability is claimed.
- No hard real-time/preemption guarantee is inferred from processing-limit tests.
  CPU/decompression stress beyond the configured negative vectors remains operational
  qualification; no large uncontrolled allocation or timing race campaign is run.
- Third tuple signer tie-breaking is present in the independent reference ordering.
  Accepting competing same-GUID candidates to exercise it is excluded by the Owner's
  no-special-collision-testing direction. `ORDER-01` tests the GUID-before-signer
  distinction with distinct identifiers instead.
- SQL-origin NaN-to-NULL, SQL interception, SQLite projection/STAT, and peer inventory
  are later-phase requirements and are not represented as Step 1 PASS cases.
- `QA_TEST_PLAN.md` should be reviewed by QA for the 1.03 wire/consent lengths,
  interrupted **join** recovery, receive-before-write fence, and removal of historical
  incoming archive/dispute expectations. This proposal does not edit that plan.
- Formal Step 1 acceptance remains with QA-Tester, PM and Owner.

## Subsequent QA addenda (historical matrix above retained)

- QA-011 records the implemented real DDLB batch callbacks and focused native results
  in `QA011_REPORT.md`; its live-import-result-at-READY limitation is explicit.
- QA-012 records approved observable FI-07/FI-15 software qualification in
  `QA012_REPORT.md`. The original inside-OS checkpoints remain NOT EXECUTED; they
  were not retroactively converted to PASS.
- QA-013 adds on-disk orphan-temp and previously accepted BLK corruption coverage;
  see `QA013_COVERAGE.md` and `QA013_REPORT.md`. Existing malformed-input and
  identity/configuration assertions remain unchanged. Accepted-file recovery policy
  remains SPEC-PENDING, and root QA_TEST_PLAN.md changes are proposed separately.
