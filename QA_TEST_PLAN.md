# UserDimensions Database Distributed Ledger — QA Test Plan & Running Log

> **Document owner:** Database Distributed Ledger — QA-Tester (Lead QA / Test Engineer).
> QA-014 (2026-11-17) expressly authorizes Codex, acting as QA's implementation
> assistant through the Project Owner, to consolidate this QA-owned document.
> Production code, Developer regressions and Architect documents are outside this
> assignment. **QA-016 recommends QA READY for its exact reviewed candidate;
> formal Step 1 acceptance remains an Owner decision.**

| Field | Current value |
| --- | --- |
| Initial edition / previous QA edition | 2026-11-09 / ARCH-010–011 revision 3 |
| Consolidation | QA-014, 2026-11-17; historical receipts are preserved, not rewritten |
| Active authority | Owner-approved ARCH-014 and signed Protocol 1.03; four current root design documents |
| Initial acceptance platform | Windows 11, local qualified storage; other platforms unqualified |
| Phase boundary | Step 1 ledger/identity/authorization; Step 2 interception, Step 3 projection and Step 4 peers remain separate |
| Current closure state | QA-016 on fbb38781faa29eb5cead9de3dcda0e760b63c7a1: 52 candidate PASS, 6 source checks PASS, 76 reference PASS; zero FAIL/BLOCKED/NOT-RUN. Independent QA READY; formal Owner acceptance not declared. See §10. |

Earlier closure/issue descriptions below retain the historical candidate and outcome
they recorded. QA-016 verifies the corrected candidate without rewriting those
results, including QA-015's 47 PASS / 2 FAIL.

This is a QA plan and evidence index, not a normative architecture specification.
[Technical Design](Database_Distributed_Ledger_Technical_Design.md) owns signed bytes,
authorization and canonical history; [Management API](Distributed_Ledger_Management_API_Specification.md)
owns CLI/JSON/errors; [SQLite Plan](SQLite_Architecture_and_Implementation_Plan.md)
owns SQLite/filesystem integration and qualification. [README](README.md) is an overview.
Current Owner-approved requirements control over obsolete examples and this plan.

The complete pre-consolidation document is preserved byte-for-byte as
[QA_TEST_PLAN_PRE_QA014_20261109.md](tests/IndependentQA/QA_TEST_PLAN_PRE_QA014_20261109.md).
Its old protocol, consent-length, direct-to-archive and dispute assumptions are
**historical, not active acceptance criteria**. Original test/evidence files and
past PASS/FAIL/BLOCKED outcomes remain intact.

## 1. Authority, evidence and platform policy

- Owner approves architecture, scope and acceptance exceptions; PM coordinates;
  QA independently reviews evidence and recommends acceptance. Developer owns fixes.
  A test result or committed report does not itself accept Step 1.
- Prefer independent portable reference calculations and deterministic fixtures.
  Protocol, crypto, authorization, canonical ordering and serialization tests should
  be OS-independent. Isolate actual filesystem, locking, process-control, atomic
  replacement and directory-durability qualification behind platform adapters.
- Windows 11 is the initial required environment, not a permanent product restriction.
  Linux/macOS support must be designed for and independently qualified before claiming
  it. Do not delay approved Windows work solely for unrequested certification.
- Run Windows filesystem/locking acceptance in ordinary Windows PowerShell outside
  Codex. Sandbox runs are preflights. Record operator context and antivirus state
  without changing AV or attributing timing failures to it without evidence.
- Use fresh nonproduction instances and public test-only identities. Never mutate
  real ledgers, live keys, shared fixtures or candidate snapshots to simulate faults.
  Keep production-disabled hooks explicit; no additional hooks are authorized here.
- Preserve failed attempts, exact signed bytes, hashes, stdout/stderr, exit codes,
  before/after inventories and barrier receipts. Never weaken assertions to turn
  a failure into PASS. Process termination/byte editing does not prove physical
  power-loss, OS internals, controller-cache or media reliability guarantees.
- Preserve upstream SQLite tests and VFS architecture. The vendor baseline is
  unchanged; no ledger-specific native SQLite certification follows from its tests.

## 2. Active Step 1 invariants and superseded assumptions

| Area | Active acceptance requirement |
| --- | --- |
| Wire / ledger identity | Signed **1.03 (`0x0103`)**, packed 163-byte header plus payload plus 64-byte Ed25519 signature; empty genesis 227 bytes. Ledger ID is the original genesis signer's full 32-byte public key, with an independent full signed-genesis SHA-512 pin. Reject incompatible 1.00/1.01/1.02 without retention; do not migrate/re-sign them silently. |
| Canonical order / time | Ascending signed UTC milliseconds, raw 16-byte block GUID, raw 32-byte signer. Local public authoring generates current UTC internally; imported signed times remain exact. No elaborate accidental-GUID collision campaign is requested. |
| Values / SQL boundary | Five SQLite storage classes: NULL, int64 INTEGER, binary64 REAL, UTF-8 TEXT and byte BLOB. Preserve declared type text; no custom exact DECIMAL/arbitrary-precision or DATETIME/BOOLEAN/GUID value tags. Signed 1.03 rejects NaN, permits infinities and preserves valid raw zero/subnormal bits. SQLite-origin NaN conversion and actual SQL affinity are Step 2–3 tests. |
| Revocation | Two distinct authorized signers and valid target role; `DDL-REVOKE-v3` 165-byte A-consent, signed package 229 bytes, final uncompressed event 137 bytes. Separate signed A/B times differ by 0–900000 ms with safe arithmetic; old 149-byte/v2 consent rules are historical. |
| Identity / storage | Same-basename support root with original key, Version 2 manifest, optional Version 1 config, BLK and later STAT/DELn; **no .DLID or nested GUID directory**. Key JSON Version 1/AAD 88 bytes is independent of protocol version. Public embedded wrap key is approved obfuscation, not secrecy from a file-holder. Never regenerate/rebind an existing identity implicitly. |
| Genesis / recovery | Local creation uses its existing key; join verifies one raw genesis in volatile memory against independent ledger ID/full SHA-512 before writes. Interrupted join recovery verifies genesis signer equals ledger ID, not replica key, and preserves the replica's own identity/trust. Explicit original-identity/key-only recovery retains the approved confirmation/fencing rules. |
| Receive-before-write | Validate framing, payload hash, signature, binding, event and historical authority in bounded memory before new persistence. New unresolved input is rejected with retryable AUTHORITY_UNRESOLVED; new known unauthorized input is rejected with NOT_AUTHORIZED. Neither is queued as XBLK or counted as durable. |
| Historical archives | Only previously accepted history invalidated by later-arriving earlier authority becomes pending/quarantined XBLK. Canonical reauthorization can restore exact bytes to BLK. No direct-to-archive GUID dispute arbitration; basic non-destructive GUID conflict/no-overwrite guards remain required. |
| Counts / phase reporting | Retained includes authenticated durable active and archived history, genesis included; pending/quarantined are disjoint archive subsets. disputed_count and opaque_count are zero in Step 1. Authorized retained does not mean projected; projector and peers report not_installed, applied_count null. No AWAITING_PROJECTOR authority category. |
| Transport / retrieval | Raw block get emits one unchanged active signed block. Every export, including zero/one block, emits DDLB Version 1: magic + BE version/count + repeated BE length/exact signed bytes. Archived bytes are excluded even with exact digest; ARCHIVED_NOT_EXPORTABLE accompanies BLOCK_NOT_FOUND. |
| Batches | Evaluate canonical order independently of transport order; a required grant must be durable before dependent persistence. Results remain in original input order with every required field including retryable. Partial imports truthfully retain preceding independent successes and report counts/results; no false batch atomicity. |
| Publication / retry | Acknowledgment follows the qualified durable publication procedure. Active exact-intent retry returns original metadata/bytes without a new timestamp/signature. Nonmatching retained GUID returns BLOCK_GUID_CONFLICT without mutation. Missing key/binding and basic no-overwrite gates remain enforced. |
| Archived publication retry | **Management API §13.3.1 controls:** matching ordinary or revocation XBLK retry returns BLOCK_ARCHIVED/exit 4/retryable false. Required details separate original historical publication from current archived pending/quarantined classification and path, plus new_publication:false and mutated:false. Do not substitute NOT_AUTHORIZED, CONSENT_EXPIRED, reactivation or fresh signing. |
| Accepted-history corruption | Detect corrupt/demonstrably known-missing accepted history; never authenticate/export/apply damaged bytes. Normal history-dependent commands use RECOVERY_REQUIRED/4 with reason LEDGER_HISTORY_CORRUPT or demonstrably justified LEDGER_HISTORY_INCOMPLETE. Status remains diagnostic, does not fabricate authorization/counts, and distinguishes history faults from signing-key loss. Preserve good bytes, identity, pin and diagnostic evidence. No automatic repair in Step 1. |

The older OPEN archived-retry question still visible in Management API §17 does not
override the explicit Owner-approved §13.3.1 outcome. It is an editorial reconciliation
item, not a reason to invent expectations or defer the required BLOCK_ARCHIVED test.

## 3. Phase coverage and acceptance matrix

These rows index coverage, not blanket phase acceptance. A historical PASS is tied
to its candidate; later interface requirements receive new tests rather than rewriting
that result. QA-014 runs only its affected subset; unrelated completed cases are not
represented as freshly rerun.

| ID | Step | Required coverage / evidence |
| --- | --- | --- |
| L-01 | 1 | Independent 1.03 crypto/framing/value/event fixtures, malformed/oversized inputs, LZMA2 and legacy rejection: QA-010 reference/WIRE/LIMIT/LZMA cases; QA-014 selects affected legacy/join paths. |
| L-02 | 1 | Genesis trust pin, canonical ordering, missing/unauthorized/restored authority: QA-010 JOIN/AUTH/ORDER; selected QA-014 regression. |
| L-03 | 1 | Basic duplicate/no-overwrite/idempotent behavior and non-destructive conflicts, without special accidental-collision arbitration: PUB/EXPORT/BATCH cases; no elaborate new collision campaign. |
| L-04 | 1 | Exact signed-byte publication/get/DDLB export, crash recovery, partial framing/import: QA-010, QA-011 actual batch hooks, QA-012 observable durability. |
| L-05 | 1 | One JSON envelope/final LF, stable exit/error precedence, nulls/fields/status/counters, no private output: every selected CLI assertion; new QA-014 archived/history-fault contracts are separately tested. |
| L-06 | 1 | Independent library/CLI publication, current UTC, no public clock override, authority and revocation retry: QA-010 PUB/REV; QA-014 active/archived/restored retries. |
| L-07 / L-08 | 1 | Calendar/OUTOFRANGE byte-derived filenames and unsupported-version no-retention/opaque=0: historical QA-010; selected legacy import/join in QA-014. |
| K-01–K-07 | 1 | Key/AAD/manifest/config strictness, original identity creation/backup/restore, interrupted genesis/init/join/key-only recovery, duplicate-identity leases: QA-010 lifecycle/identity/config/fault evidence and QA-011 join; QA-014 rechecks missing/corrupt-key gates. |
| A-01 / ST-01 / X-00 | 1 | 1.03 consent roles/signed interval, correct inclusive/disjoint counts, BLK→XBLK→BLK without SQL/STAT/peers: QA-010 AUTH/REV and QA-014 affected history/retry cases. |
| COR-01 | 1 | Corrupted complete orphan temps never promoted/authenticated; eleven deterministic damage patterns, positive publication barrier/kill/restart, healthy receiver: eleven QA-013 PASS. |
| COR-02 | 1 | Previously accepted BLK damaged under original SHA-512 filename; reject bad bytes and preserve identity/unrelated history: eleven QA-013 PASS. Updated Step 1 recovery diagnostic contract is a separate QA-014 check, not retroactively included in those passes. |
| S-01–S-03 | 2 | Native supported SQL interception, replica mutation denial, ephemeral evaluation, rollback/effective events and honest partial publication. NOT IMPLEMENTED/NOT RUN by this QA work. No official projector writes in Step 2. |
| P-01–P-06 / X-01–X-04 | 3 | Qualified native DLDB integration, authenticated projector/progress atomicity, SQL-native values, STAT/DELn manager, delayed projection/read barriers and deterministic rebuild after late/restored history. File-only archive transitions already have Step 1 coverage; SQLite/STAT effects do not. NOT RUN here. |
| N-01–N-03 / COR-03 | 4 | Peer inventory/relay exclusion of XBLK, offline convergence, authenticated discovery/retrieval and safe recovery of missing/damaged history. Automatic network repair is DEFERRED, not a Step 1 FAIL/BLOCKED requirement. NOT RUN here. |

If missing history is later restored, Step 3 may require complete authenticated
database replay; focused rollback is an optional equivalent optimization. Step 4
seeks replacements from peers and revalidates signed bytes, ledger/genesis pin and
historical authority before accepting them. No valid replacement means no claim of
complete authoritative history. Corrupted pinned-genesis and archived-XBLK recovery
need appropriate future coverage; do not invent Step 1 automatic retrieval duties.

## 4. Fault qualification dispositions (historical outcomes preserved)

| Observation | Historical record | Current Owner-approved disposition / evidence |
| --- | --- | --- |
| FI-07 original rename-before-directory-confirmation inside OS | QA-010 and QA-011 BLOCKED; no observable split inside MoveFileExW | **REPLACED / NOT EXECUTED**. Never relabel the original internal point PASS or insert a fake callback inside the OS call. |
| FI-07 equivalent observable software behavior | QA-012 six publication cases | **PASS:** complete authentic temp, before/after-call termination, post-RELEASE acknowledgment, held-source real I/O failure, basic no-overwrite and truthful post-call observer error; exact original bytes survive/recover. |
| FI-15 original replacement-before-directory-confirmation inside OS | QA-010 and QA-011 BLOCKED | **REPLACED / NOT EXECUTED**, same internal-observation limit. |
| FI-15 equivalent observable software behavior | QA-012 five config cases | **PASS:** complete old/new images at before/after-call kill/RELEASE, genuine held-target error and truthful post-call observer error; identity/trust unchanged. |
| FI-09 and FI-09-GRANT actual DDLB boundaries | Missing in QA-010; actual callbacks tested in QA-011 | **PASS for observed storage/result/recovery behavior.** Positive READY/RELEASE, exact authenticated durable grant/prefix, original input index versus canonical order, absent dependent/temp at barrier, external termination, restart and truthful duplicate/new accounting. |
| FI09-LIVE-RESULT-SNAPSHOT | Literal private in-flight result object unavailable in QA-011 callback; that run records BLOCKED | **NOT OBSERVED.** Owner-approved paired evidence combines independently authenticated READY storage/kill/recovery with a separate completed READY/RELEASE trial showing actual returned result flags. This is not a fabricated live snapshot or a new execution. |
| Other initialization/archive/config/lease/acknowledgment faults | QA-010 executed cases and per-case receipts | Retain their actual named outcomes and candidate. No wholesale rerun or physical power-loss claim in QA-014. |

Replacement PASS concerns observable software behavior, not independent certification
of same-volume MoveFileExW/Flush(true), directory entries, filesystems, hardware caches
or device failure. Document the Windows backend/qualification scope and obtain the
required Security/Owner acceptance disposition through the project review process.
QA evidence does not assert a missing or completed external sign-off without its record.

## 5. Candidate identities and historical execution ledger

Repository root `R`:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger`.
Evidence base `E`: `R\.git\dev-003-work`. Every folder below identifies an exact local
evidence path under that base. Full logs/instances remain local; committed summaries
index them and their hashes, not replace the complete forensic record.

Candidate identities:

- QA-010 production: `6328bbc3a289b969b33dc365f6d8639cc005793f`.
- QA-011/012/013 production: `8bc96f0f513faac57c1db23a8189288479d8a992`.
- QA-014 selected repository HEAD: `00b54475aef34181ce9f9290c0080aa9974d45de`;
  last production change remains **8bc96f0f513faac57c1db23a8189288479d8a992**.
  Subsequent commits are QA/documentation/ignore changes, not a rebuilt binary.
- QA-014 selected CLI SHA-256:
  `a7b9c642bdb3939d7a705692778457dab4c12ecc22bb18cb397f487046905cf8`.
- QA-014 Ledger library SHA-256:
  `71b5fadbd96a1dfed90e2290c2f2c9b62484cb017645419456535bcd13d55aa3`.
- Source tree at selected HEAD and 8bc96f0: `aa671a5664071e1ea381ee69af9d62ff86528eb2`.
  Pristine SQLite tree: `9e69364cf105a1d38e1d17ee66290967f640f78d`.

| Assignment / context | Candidate | Reference result | Candidate result | Exact evidence folder / disposition |
| --- | --- | --- | --- | --- |
| QA-010 ordinary Windows, 2026-11-16 | QA-010 production above | 76 PASS, 0 FAIL | 130 PASS, **1 FAIL**, 4 BLOCKED; exit 1 | `E\qa010-20261116-201537-c7555114`; DDLB join input returned UNSUPPORTED_VERSION instead of BAD_FRAME. Preserve failure; 8bc96f0 later fixes it. |
| QA-011 ordinary Windows, 2026-11-17 | 8bc96f0 above | 76 PASS, 0 FAIL | 10 PASS, 0 FAIL, 3 BLOCKED; exit 2 | `E\qa011-20261117-073333-c359b240`; join fix and actual batch hooks. Three historical BLOCKED outcomes retain their original labels; current dispositions are in §4. |
| QA-012 ordinary Windows | 8bc96f0 above | NOT-RUN; QA-011 evidence carried forward without recounting | 11 PASS, 0 FAIL, 0 BLOCKED; exit 0 | `E\qa012-20261117-082002-d2581f93`; six FI-07 and five FI-15 software replacement cases. |
| QA-013 ordinary Windows | 8bc96f0 above | 12 PASS, 0 FAIL | 22 PASS, 0 FAIL, **2 historical SPEC-PENDING checks**; exit 2 | `E\qa013-20261117-101908-0dbd780d`; eleven orphan-temp plus eleven accepted-file corruption cases. Detection PASS is preserved; the later Step 1 diagnostic contract is tested separately in QA-014. |
| QA-013 later launcher attempt | Expected 8bc96f0; actual cae7fe3d09e49ee17162bcf56bfe19113f29e6e6 | NOT-RUN | NOT-RUN; launcher exit 1 | `E\qa013-20261117-102758-5e43bebd`; commit-pin mismatch, not a corruption-test failure. Prior completed evidence is unchanged. |
| QA-014 NewOnly sandbox preflight | QA-014 selected HEAD / production above | 76 PASS | 4 PASS, **4 FAIL**, 0 BLOCKED, 30 NOT-RUN; exit 1 | `E\qa014-20261117-131118-36f5684b`; sandbox evidence only. Three archived-retry and one new history-diagnostic failures; restorations and incompatible-genesis checks pass. |
| QA-014 targeted ordinary Windows | QA-014 selected HEAD / production above, unchanged DLL hashes | 76 PASS, 0 FAIL | **34 PASS, 4 FAIL, 0 BLOCKED, 0 NOT-RUN; exit 1** | `E\qa014-20261117-132749-e11315c1`; Owner-run 2026-11-17 13:27:49–13:28:29 America/Chicago. All 30 reused cases PASS. Three archived retry failures plus one retained-history diagnostic failure; 198 command receipts and three restorations independently verified. |

Counts are per run/context and are not additive unique-test totals. Never add a
reference check, sandbox/native repeat, paired barrier trial or carried-forward
case to historical totals as if newly executed. Native/sandbox result JSON and
original failed attempts remain unchanged. QA-014 and this consolidation do not
retroactively accept Step 1 or convert historical pending/error results into passes.

### Earlier historical records retained

- **2026-11-08, Owner-reported upstream SQLite baseline:** 23 build, 40 fuzz, 5 make,
  8,021 Tcl jobs and 4,164,068 checks; 14 failures isolated to win_unc_locking/walthread
  in Codex. Ordinary-Windows rerun 58/58 PASS, exit 0, integrity/no-leak checks pass.
  Separate fuzz: 46,391 tests and 9,839 invariants. Combined evidence is the qualified
  baseline, not a single all-green matrix or a ledger-fork acceptance. Import cc0dd4a,
  tag sqlite/upstream-3.54.1, provenance b0191db. AV causation was not established.
- **2026-11-09 planning / revisions 1–3:** canonical GUID-before-signer amendment,
  manifest-only identity, five SQLite value classes, publication/recovery/config/
  countersign planning and open ARCH-011 questions. These were planning-only, with
  no ledger QA execution at that time. Exact original text/decisions are in the
  pre-consolidation archive; later execution is indexed above.
- Original Protocol 1.01 DEV-003 integration files, eight original-file hashes and
  historical evidence ZIPs remain under tests/IndependentQA. Do not apply their
  old expectations to 1.03; current QA references are independently generated.

## 6. Open items and Step 1 closure categories

| Category | Current issue / disposition |
| --- | --- |
| Implementation — QA014-D01 | Confirmed in preflight and ordinary Windows: matching ordinary pending/quarantined and revocation XBLK retries return NOT_AUTHORIZED/4 without required BLOCK_ARCHIVED historical/current metadata. No mutation was observed, but the latest response contract is unmet. Preserve evidence; separate Developer correction and focused QA rerun required. |
| Implementation — QA014-D02 | Confirmed in preflight and ordinary Windows: normal operations encountering damaged accepted history return HASH_MISMATCH/2 instead of RECOVERY_REQUIRED/4 with LEDGER_HISTORY_CORRUPT reason. Bad bytes are detected and not exported; identity/unrelated bytes survive. The diagnostic/gating contract still needs separate Developer correction and QA verification. |
| Specification / editorial | Explicit §13.3.1 resolves archived retry; stale OPEN wording in §17 needs Architect cleanup, not an invented alternative outcome. Status may retain healthy signing-key availability during history restriction; exact recovery_codes symbol choices are not fully enumerated. QA asserts the required ordinary error/reason and truthful counts/authorization, not an invented universal state/key-health mapping. |
| Corruption phase decision | Former POLICY-ACCEPTED-STATE now maps to Step 1 detection/restriction and QA-014 conformance. Former POLICY-ACCEPTED-REPAIR (automatic peer repair) is **DEFERRED TO STEP 4 / NOT RUN**, not a new Step 1 blocker or PASS. Explicit optional manual repair/replacement commands are not invented here. |
| Security / acceptance documentation | QA-Tester reviews evidence, Owner-approved paired/replacement dispositions and Windows backend scope; PM verifies required Security/Owner sign-off records. No physical-storage certification is introduced as a new acceptance duty. Formal Step 1 acceptance is not declared by Codex. |
| Portability | Exceptions and Windows-only qualification documented in QA012_PORTABILITY.md. Future durable-store/lease/path backends and Linux/macOS native qualification are separate Developer work; do not claim them complete or impose an unapproved permanent Windows limitation. |
| Later-phase specification items | Supported native API/SQL matrix, projection-delay bounds/clock behavior, STAT/progress/rebuild, peer inventory byte grammar/retry/safe replacement and competing logical-entity semantics remain at their Step 2–4 gates. They are not silently converted to Step 1 requirements. |

Historical O-01/O-02/O-09/O-11–O-19 wire/manifest/consent/config/classification
questions are reconciled by current ARCH-014/1.03 contracts; implementation failures
against those resolved contracts are defects, not still-open architectural decisions.
O-03 is tracked by the qualified Windows software evidence/scope above. O-04–O-08
and O-10 retain the appropriate later-phase work; file-only XBLK transitions are
already Step 1 responsibilities, while STAT/projector/peer effects are later.

## 7. Execution, evidence and maintenance procedure

Use each suite's report/launcher and an explicitly reviewed candidate commit/build.
QA-014 source and DLL guards refuse a moving/unreviewed candidate; document-only
commits do not imply changed binaries. Working QA files/this plan may be dirty while
production, Developer regression and SQLite paths must remain stable. Build only
QA adapters in fresh .git/dev-003-work space, never overwrite Developer output.
Record current design/source/binary hashes and exact commands/exit/stdout/stderr.
Preserve barrier PID receipts and original versus restored signed-block SHA-512s.

For failures, classify implementation, harness/environment or unresolved specification
before any change. Do not alter production or Developer tests under this assignment.
Report severities for QA/PM review without falsely calling an interface mismatch a
proven cryptographic compromise. Keep all failed attempts and their original sources.

Append new evidence and dispositions with exact revision/context; preserve old rows
and linked archives. QA-Tester maintains acceptance recommendations and approves
future expectations. Coordinate Git commits with concurrent Developer/Architect work;
stage only authorized QA-owned changes. Publication is not acceptance. Do not begin
Steps 2–4 from this plan or this final Step 1 regression assignment.

## 8. QA-015 running log — 2026-11-17

This append-only follow-up preserves every QA-010 through QA-014 historical row,
original assertion, and Owner-approved FI-07/FI-15/FI09 disposition above. The
older defect/specification descriptions are historical as of their recorded
candidate, not claims that the corrected candidate still produces those errors.

Reviewed production: `c001fc4a0d50067e2b8c9a2685625bf43aeea464`; repository/document
HEAD and observed GitHub main: `06af60d969fddab1045146ecaed7775320d6c49d`.
CLI SHA-256 `7ad53dd36931831df14bc8ecb82f9a53576d71c2fff28a0d22f085191c9c504b`;
Ledger library SHA-256 `9cbc07e403386ec87ed9a576666fc1c0a3bb3a1a40814e5789ae925477a50941`.
Production source equivalence and clean Developer paths checked. Only QA-014
launcher's repository/production/hash selection values changed; its 38-case
assertion runner remains byte-identical to the committed historical source.

| Execution / evidence | Actual results | Disposition |
| --- | --- | --- |
| QA-014 diagnostic follow-up, ordinary Windows, `E\qa014-integrity-20261117-161439-2ce90d05` | 7 candidate PASS, 0 FAIL/BLOCKED/NOT-RUN; 4 reference PASS; exit 0; 52 receipts independently verified | Supplied-input errors remain distinct from accepted-history corruption; healthy-key diagnostics and unchanged identity/good bytes verified. Separate source-order finding remains open. |
| QA-015 Codex preflight, `E\qa015-20261117-170505-ab13ec54` | 47 candidate PASS, 2 FAIL, 0 BLOCKED, 0 NOT-RUN; 76 reference PASS; exit 1. Original QA-014 38/38 PASS; three archived-duplicate cases PASS; six representative input/history precedence cases PASS. Both missing-genesis cases FAIL. 313 receipts independently verified; both QA adapter builds zero warnings/errors. | **NOT READY. QA015-D01:** missing pinned genesis (file or whole directory) lacks `LEDGER_HISTORY_INCOMPLETE` reason and misreports intact key as unavailable. Restrictions and preservation work. Native qualification is not claimed from this run. |
| QA-015 ordinary Windows, `E\qa015-20261117-170913-72b92553` | **47 candidate PASS, 2 FAIL, 0 BLOCKED, 0 NOT-RUN; 76 reference PASS; exit 1.** Original QA-014 38/38 PASS, all three archived-duplicate cases PASS, all six representative precedence cases PASS. Same two missing-genesis failures. Owner transcript and 313 command receipts independently verified; both QA adapters build with zero warnings/errors. | **NOT READY: QA015-D01 confirmed.** Native run 2026-11-17 17:09:13–17:10:24 America/Chicago, exact production/binary identities above. No production changes or assertion changes. Preflight and confirmation are separate runs of the same coverage, not additional distinct test cases. |

The current authoritative design removed the old OPEN archived-retry wording;
§13.3.1 is the tested contract. The corrected candidate passes all four formerly
failing QA-014 assertions in preflight and ordinary Windows, without changing their expectations.
Missing known pinned genesis is Step 1 diagnostic/restriction scope; automatic
peer repair stays Step 4. No later-phase repair acceptance criterion is added.

The separate fresh-publication signing-before-integrity-gate source finding is
retained in `tests/IndependentQA/QA014_INTEGRITY_CONFORMANCE.md`. It is not counted
as another newly executed failing QA-015 case. Developer disposition remains open.
See `tests/IndependentQA/QA015_REPORT.md` and `QA015_D01_DEVELOPER_HANDOFF.md` for
exact observed JSON, source locations, commands, hashes and evidence.

No production, Developer-regression, Architect, SQLite or shared fixture changes;
no antivirus/install changes. Formal Step 1 acceptance is not declared.

## 9. QA-015 evidence publication — 2026-11-17

Repository maintenance only: publish the completed QA-015 reports, original
execution/validation scripts, historical candidate-selection metadata and the
running log above. No candidate tests are newly executed by this publication.
QA-015's **47 PASS / 2 FAIL** on production
`c001fc4a0d50067e2b8c9a2685625bf43aeea464` remains unchanged. The three historical
integrity/QA-015 reports are preserved byte for byte.

The subsequent published Developer production revision is
`fbb38781faa29eb5cead9de3dcda0e760b63c7a1`, following the pre-signing correction
in `3e33f5e0bafb1e092aaf4ffcea06dc4c13879368`. Those corrections are not independent
QA retest results. Earlier statements that the defects remain open describe their
recorded historical candidate; final verification of the corrections is pending.
**Step 1 remains NOT READY pending independent retest; no acceptance is declared.**

Full command receipts and generated instances/keys remain in their existing local
`.git/dev-003-work/` evidence directories, outside Git publication. See
`tests/IndependentQA/README.md` for historical replay guards and fresh-clone limits.
QA-016's separately authored optional source-order checker/instructions are not
present in this checkout and are not integrated or counted by this commit. Preserve
all prior results and Owner-approved FI-07/FI-15/FI09 dispositions above.

## 10. QA-016 independent retest running log — 2026-11-17

Candidate: `fbb38781faa29eb5cead9de3dcda0e760b63c7a1`; execution HEAD:
`5475d8521c9d751f1393261dbfb8cd5558d8d251`. Exact source equivalence and reviewed
CLI/library hashes are checked. Only QA-owned selection constants change in the
QA-015 launcher/validator; all original assertions and historical results remain.

| Evidence / actual run | Results | Qualification |
| --- | --- | --- |
| `E\qa016-20261117-203347-335c1a87`, Codex preflight | 52 candidate PASS, 0 FAIL/BLOCKED/NOT-RUN; 76 reference PASS; original source checker 5 PASS/1 BLOCKED; exit 2 | All original 49 cases, including both missing-genesis failures, now pass; three new no-GUID runtime cases pass. Source blocker is a QA lexical limitation, not a production defect. Original receipts preserved. |
| `E\qa016-source-review-20261117-203711-f0345428`, source-only follow-up | 6 source checks PASS, 0 FAIL/BLOCKED/NOT-RUN | Corrected handling of C# verbatim path/character literals after independent manual call-graph review. Same frozen production source; no repeated candidate tests or weakened expectations. Combined evidence validator verified 331 candidate command receipts. |
| `E\qa016-20261117-204215-069dc0e9`, ordinary Windows confirmation | **52 candidate PASS, 0 FAIL/BLOCKED/NOT-RUN; 6 source checks PASS; 76 reference PASS; exit 0.** Original 49 assertions and three fresh no-GUID checks pass. | **Independent QA READY for fbb38781faa29eb5cead9de3dcda0e760b63c7a1.** Owner transcript matches completed receipts; 331 command receipts and source/design/binary snapshots independently validated. No runtime observation of in-memory signing is claimed. |

Candidate, source and reference totals are separate; preflight/confirmation are not
counted as distinct additional coverage. Runtime checks do not claim direct
in-memory signing observation. QA016_REPORT.md identifies the actual call graph,
new healthy/impaired no-GUID checks, evidence and current independent disposition.

QA-015's 47 PASS / 2 FAIL against c001fc4 and all historical reports remain intact.
Maintain FI-07/FI-15 11 replacement PASS with literal checkpoints REPLACED/NOT
EXECUTED, FI09-LIVE-RESULT-SNAPSHOT NOT OBSERVED — OWNER-APPROVED EQUIVALENT EVIDENCE,
and automatic repair deferred to Step 4. No Developer/Architect/SQLite changes,
new protocol requirements, Step 2 work or formal Owner acceptance are introduced.

QA-016 final disposition: **QA READY for the exact reviewed production candidate**.
Both QA015-D01 scenarios and the pre-signing finding are independently verified
corrected within this scope; no additional defect reproduced. Historical QA-015
47 PASS / 2 FAIL and the initial QA-016 source-checker blocker remain preserved.
Final recommendation is returned to Developer, Security and PM through the Owner;
formal Step 1 acceptance is not declared by this execution report.
