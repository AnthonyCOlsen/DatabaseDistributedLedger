# Distributed Ledger Management API — v0.4 integrated candidate

**v0.4, 2026-10-23 — OWNER-APPROVED DECISIONS INTEGRATED; EXACT-FILE APPROVAL PENDING.** This document integrates the owner-approved C1–C12 decisions. The resulting files remain unapproved and unfrozen until separate exact-artifact review. The [technical design](Database_Distributed_Ledger_Technical_Design.md) defines ledger semantics; the accompanying execution, SQL, management and fault contracts specify the PostgreSQL adapter. No approval record or signed-block wire version is changed.

## 1. Selection, encoding and output

`ledgerctl --pgdata ABS COMMAND ...` selects one instance. Paths are absolute. No daemon, global port, global key directory or external service is assumed. All finite commands write exactly one UTF-8 JSON object followed by LF on stdout; diagnostics go to stderr. Success is `{ok:true,data:OBJECT,error:null}`; failure is `{ok:false,data:null,error:{code:CODE,message:TEXT,retryable:BOOL,details:OBJECT}}`. Raw signed bytes use files, not JSON strings. Shell exit behavior is in §8. The [response schema](schemas/ledgerctl-v1.schema.json) is JSON Schema Draft 2020-12 with command-specific closed result objects. Error.details is extensible except that its required published fields must be present. Tests match codes/fields, not English messages. Commands do not read stdin.

Output UUIDs are lowercase canonical hyphenated strings; digests and public keys are lowercase hex (SHA-512:128 digits; Ed25519:64). Input UUIDs permit either hex case with the canonical hyphens; hex digests likewise permit either case. CLI integers are base-ten digits with an optional leading minus only where a signed value is allowed; no whitespace, float syntax or overflow. Timestamp filters span signed int64 milliseconds. Configuration support beyond the required range is documented, not silently wrapped. Unknown flags, malformed values and incompatible flag combinations fail INVALID_ARGUMENT without changing state.

`--operation-id TOKEN` on import/publish correlates work with fault notifications. Tokens are nonempty, control-free Unicode strings of at most 128 UTF-8 bytes, not deduplication keys, credentials or transaction identities. JSON character counts alone do not enforce the byte limit. Fault-correlation application_name constraints are separate in the fault contract; normal SQL application names are not restricted by this CLI rule.

## 2. Lifecycle and keys

All result-field lists below are complete and include data.operation. Types/closed objects are defined in the response schema; ABS fields are absolute strings, IDs UUIDs, port 1..65535 and pid positive integer.

| Command following --pgdata ABS | Inputs | Success data fields |
| --- | --- | --- |
| instance init | --database NAME --socket-dir ABS --port N; optional --peer-listen 127.0.0.1:PORT, where 0 requests an available peer port | operation="instance.init", instance_id, pgdata, database, socket_dir, port |
| instance adopt | Same options; existing stopped compatible cluster; **narrow verification adapter**, §7 | operation="instance.adopt", instance_id, pgdata, database, socket_dir, port |
| instance start --foreground | Optional --fault-socket ABS | operation="instance.start", instance_id, pid, database, socket_dir, port, peer_endpoint (URI or null) |
| instance stop | --mode fast\|immediate; default fast | operation="instance.stop", instance_id, stopped (Boolean) |
| init | Optional --signing-key-file ABS, unencrypted PKCS#8 PEM Ed25519; otherwise generate | operation="init", ledger_guid, genesis_sha512, master_public_key, genesis_block_guid |
| join | --ledger-guid UUID --genesis-sha512 HEX; optional --signing-key-file ABS | operation="join", ledger_guid, genesis_sha512, mode (master\|replica) |
| key set | --signing-key-file ABS, same key encoding | operation="key.set", selected_public_key (hex), authorized (Boolean), mode (unbound\|master\|replica) |
| key clear | No options | operation="key.clear", selected_public_key=null, authorized=false, mode (unbound\|replica) |
| status | No options | operation="status", instance_id, pid (or null), database, binding, mode, signing, projection, blocks, peers, peer_endpoint |

Instance init accepts nonexistent/empty PGDATA, creates an empty user database and unbound ledger store, and never overwrites an existing cluster; repeat initialization fails INVALID_ARGUMENT. Built-in catalogs are not unrelated user population. Before initialization, all other commands except adopt fail INVALID_ARGUMENT. Adoption incompatibility also uses INVALID_ARGUMENT with details.reason="INCOMPATIBLE_CLUSTER"; an already running target uses ALREADY_RUNNING. No operation gains root privileges.

Status, config get/set, key set/clear and stop work after initialization, including while stopped. Init/join, block and peer operations require a running instance or return NOT_RUNNING. Starting a running instance returns ALREADY_RUNNING. Stop while stopped succeeds with stopped=true. Fast stop completes within the adapter bound of 30 seconds or returns PROCESSING_TIMEOUT without claiming completion. Immediate stop/crash cannot retract acknowledged durable blocks. Foreground start emits one success readiness envelope after SQL and management access work, remains attached until shutdown, logs later diagnostics to stderr, and exits 0 on requested clean stop. Signal exits may interrupt reporting. Readiness promises connectivity, not complete peer history or finished reconciliation; affected reads obey the execution contract even during recovery. Later status reports the actual projection state.

Ledger init creates exactly one fresh identity/genesis for an empty unbound target. Existing unrelated user data gives NOT_EMPTY. Existing binding gives ALREADY_BOUND. Importing history alone is not adopting a database. Join requires an existing locally imported ledger and matching full-genesis fingerprint; missing genesis gives BLOCK_NOT_FOUND, wrong ledger GUID LEDGER_IDENTITY_MISMATCH, wrong pinned fingerprint GENESIS_MISMATCH. A same-ledger conflicting genesis is never silently substituted. Init/join failures do not manufacture authority, change binding or discard user data. Join starts authenticated reconstruction; its success acknowledges binding, not immediate projection completion.

Key set atomically installs/selects one key for the instance. Invalid PEM/type gives INVALID_ARGUMENT; unreadable file gives IO_ERROR. Either preserves the prior selection. Key clear deselects, without promising forensic deletion of backups. Neither command publishes a block, grants authority or changes ledger binding. Key material stays private to the node; responses/logs never disclose private bytes. Selection persists across restart. Join may select an inactive key; its mode is replica until canonical history authorizes that key. Init's supplied/generated key becomes authority **only through the authenticated genesis and binding**. An already selected key can be reused for init only if supplied explicitly; otherwise init generates a new key and replaces selection on success. Init/join key/binding changes are atomic with respect to failed preconditions. A key's authorization may change on grant, two-signature revoke, regrant or late history; key possession does not override canonical authorization at the position of a proposed new block.

## 3. Status semantics

binding is null or `{ledger_guid,genesis_sha512}`. signing is `{selected_public_key,authorized}`. Unbound implies mode=unbound and authorized=false. For a bound instance, mode=master exactly when a selected private key is authorized in currently known canonical history; otherwise mode=replica. No selected key implies authorized=false. This is a snapshot, not proof of missing-history completeness or permission to sign at every timestamp.

projection is `{state,pending_work,error}`: state ready/reconciling/rebuilding/failed, pending_work nonnegative, error null or the structured error above. Failed requires an error. Ready means no known inconsistent reconciliation work (pending_work=0), not that every retained block is applicable. Diagnosed unresolved repair/dependency conditions may remain pending with affected-query errors even when unrelated projection work is ready. Status remains callable at a fault barrier.

blocks is `{retained_variants,status_counts}`, with one count for each applied/pending/quarantined/opaque/rejected. Counts classify retained exact-byte variants once and sum to retained_variants; identical duplicates add no variant. Rejected input need not be retained. Applied means authenticated and materialized in the current official projection; neither retention nor a proposed correction alone qualifies. While unbound, imported genesis/history cannot claim database application. A bound genesis is applied once its binding/authority has been ingested. During queued projection, valid unapplied work is pending with reason PROJECTION_PENDING; unbound applicable history uses DATABASE_UNBOUND. Neither reason permits deletion of retained authenticated history. Other stable reasons include APPLIED, AUTHORITY_MISSING_OR_INAPPLICABLE, SCHEMA_DEPENDENCY_MISSING, REPAIR_REQUIRED, IDENTITY_CONFLICT and FUTURE_VERSION. A snapshot status can change after replay; exported bytes cannot.

peers is an array of `{endpoint,connected}`. peer_endpoint is the implementation's loopback URI or null. pid is null only while stopped. No applied-position/finality cursor or global history completeness field is defined.

## 4. Block exchange

| Command | Inputs | Success data fields |
| --- | --- | --- |
| block import | --input ABS; raw block or auto-detected DDLB; optional --operation-id TOKEN | operation="block.import", results, durable_count |
| block publish | --input ABS; exactly one already signed raw block; optional --operation-id TOKEN | operation="block.publish", results, durable_count |
| block get | --id UUID --output ABS; optional --sha512 HEX and --ledger-guid UUID | operation="block.get", path, bytes, sha512 |
| block export | --output ABS; optional --id UUID, --ledger-guid UUID, inclusive --from-ms INT64 / --to-ms INT64, --exclude-sha512-list ABS | operation="block.export", path, bytes, sha512, block_count |

Each results item is exactly `{ledger_guid,block_guid,sha512,status,reason,duplicate,durable}`. reason is an uppercase identifier. Item statuses use §3. durable means exact received bytes have been durably retained, including optional diagnostic retention; it does not mean applicable/materialized. Duplicate=true means identical retained bytes already existed. Newly received conflicting authenticated variants are both quarantined; no manual winner exists. A presently missing/inapplicable authorization/schema dependency is retained pending and reconsidered, never discarded solely by receipt time. Safely retained known-envelope future-version input is opaque; unsupported/unparseable future encodings may fail safely instead. Input files are never rewritten.

Results follow input order for processed items whose complete IDs/digest can safely be determined. An unframeable tail is described by structured cause/details without fabricated IDs. durable_count equals the number of results records with durable=true, including duplicates and any diagnostic retention, not the number of unique or applicable blocks. A rejected single input fails even if its bytes are retained diagnostically; retained pending/quarantined/opaque input succeeds with its status. A batch stops at its first failed input. PARTIAL_IMPORT takes precedence only if this invocation first newly durably accepted a non-rejected authenticated variant (duplicate=false, durable=true). Merely encountering old duplicates or retaining rejected diagnostics does not mask a later ordinary error. PARTIAL_IMPORT details requires results, durable_count and cause={code,message,retryable,details}; already durable bytes remain. An ordinary block error also includes processed results/durable_count when available. Exact count equality/order and these stateful conditions require behavioral validation beyond schema.

Get without a digest fails BLOCK_ID_DISPUTED for a disputed identity; selecting its exact digest succeeds if retained. Get may return retained diagnostic raw bytes explicitly, without implying they are valid. Export of --id returns raw exact bytes and block_count=1; disputed single export without a digest-selection facility fails BLOCK_ID_DISPUTED, with block get --sha512 used instead. ID mode cannot combine with range/exclusion flags. Batch export is exact design §11 DDLB, ordered by timestamp, raw signer bytes, raw block GUID and full-block SHA-512 for variants; never reserialized/re-signed. Full/range/missing exports exclude unauthenticated diagnostic bytes; authenticated semantic-invalid retained variants may remain exportable and rejected. Range endpoints are independently optional; from>to is INVALID_ARGUMENT. The exclusion file is a JSON array of full-block digests; duplicates are harmless. Hash/digest output describes the exact output file; byte/block counts describe what was actually written. Resource failures must not report a complete output as success.

**Ledger selection (C9):** A bound instance defaults to its pinned ledger. An unbound store defaults to its only retained ledger, fails BLOCK_NOT_FOUND if none, or LEDGER_SELECTION_REQUIRED with sorted ledger_guids if several. Explicit unknown selector fails BLOCK_NOT_FOUND; a selector different from a bound ledger gives LEDGER_IDENTITY_MISMATCH. Unbound stores may retain multiple ledgers; selecting/exporting one never binds them. After binding, foreign imported blocks fail LEDGER_IDENTITY_MISMATCH and preexisting unrelated stored history never materializes into the bound target.

## 5. Peers

`peer add --endpoint URI`, `peer remove --endpoint URI`, `peer list` return exactly operation=peer.add/peer.remove/peer.list and peers. Absent-peer removal is idempotent. Configured loopback peers of the same build accept the advertised URI and exchange all relevant intact blocks; transport/scheme/discovery/retry implementation is free. Endpoints are bounded/documented and do not confer signing authority. Replicas may relay. No ordinary local master write needs peers or a quorum.

Removing peers disables configured automatic exchange; it is not a security proof that malicious processes cannot coordinate over shared localhost. File/UID isolation, TCP authentication and enforced network partitioning are distinct concerns. Offline file exchange remains usable.

## 6. Configuration (C9)

config get returns operation="config.get", limits. config set accepts any nonempty subset of --max-raw-block-bytes N, --max-decoded-event-bytes N, --max-import-memory-bytes N, --block-timeout-ms N and returns operation="config.set" plus every effective setting. No options to config set is INVALID_ARGUMENT. An invalid tuple changes nothing. Settings persist across restart; an admitted import snapshots its limits, and later changes apply to subsequent operations. Reconfiguring limits cannot erase durable history.

| limits key | Approved default | Required supported range, inclusive |
| --- | --- | --- |
| max_raw_block_bytes | 16777216 | 211..16777216 |
| max_decoded_event_bytes | 67108864 | 1..67108864 |
| max_import_memory_bytes | 134217728 | 8388608..134217728 |
| block_timeout_ms | 60000 | 1..60000 |

All combinations in these ranges are required support, not protocol maxima or eager-allocation promises. Larger documented values may be supported. Reject unsupported or invalid settings with INVALID_ARGUMENT and details.setting; the 8 MiB minimum and full cross-product are owner-approved adapter conventions, not protocol-wide bounds. Schema minima reflect these required support minima; no schema maximum imposes a wire-wide cap. The raw minimum includes the 211-byte genesis envelope; small legal settings need not admit every row block.

Do not infer raw<=decoded<=memory: independently enforce the actual limits. If a valid input cannot fit its current working-import budget, return RESOURCE_LIMIT safely. Count dictionary/decoder/input/validation working memory in the import budget; fixed server/static program memory and independent projection state are excluded. No exact implementation allocation strategy is required. Block timeout uses elapsed monotonic processing time excluding intentionally paused fault-barrier intervals, not a universal end-to-end publication/visibility deadline. Resource/time errors have details.limit/configured and observed or required when knowable. Practical CPU/peer/disk bounds must be documented/enforced safely; exact hidden peer counts or CPU quotas are not invented here. Limits never authorize unauthenticated effects or retract durable bytes.

## 7. Populated-cluster verification adapter (C10)

instance adopt is a narrowly defined test-facing verification adapter, not a general-purpose migration feature. Its fixture is a stopped compatible PostgreSQL 18.6 cluster owned by the node UID, created with the trusted baseline initdb (--username=candidate), ordinary SQL-populated with held-out rows, then cleanly stopped. Adopt attaches lifecycle metadata without dropping, rewriting or ledger-authorizing unrelated user rows/schema. It leaves the instance stopped; start is then explicit. A stopped compatible baseline-format cluster and database NAME are preconditions; arbitrary releases, storage formats and migration compatibility are not required. Empty target adoption can later bind normally.

On a populated adopted target, both init and join return NOT_EMPTY, preserve rows/schema, leave binding null, and generate no genesis/grant/correction. Pre-adoption legacy state is not advertised as a ledger-derived official projection. Candidate-run unbound SQL mutations still fail; the fixture is never created through a candidate bypass. Local read-only inspection is permitted for trusted preservation checks and status remains unbound. No hidden physical-layout mutation is authorized; the fixture does not license candidate-run unbound writes.

## 8. Errors and exit status

| Exit | Codes / meaning |
| --- | --- |
| 0 | ok=true; no error, including retained pending/quarantined/opaque |
| 2 | INVALID_ARGUMENT, BAD_FRAME, HASH_MISMATCH, BAD_SIGNATURE, BAD_EVENT, LEDGER_IDENTITY_MISMATCH, GENESIS_MISMATCH |
| 3 | PARTIAL_IMPORT under §4; cause describes the underlying error |
| 4 | ALREADY_RUNNING, ALREADY_BOUND, BLOCK_ID_DISPUTED, BLOCK_NOT_FOUND, NOT_EMPTY, NOT_RUNNING, NOT_AUTHORIZED, LEDGER_SELECTION_REQUIRED |
| 5 | RESOURCE_LIMIT, PROCESSING_TIMEOUT |
| 6 | IO_ERROR, INTERNAL_ERROR; additional implementation-specific diagnostic codes unless a published extension assigns another exit |

retryable means the same request's condition may clear, not that a repeated SQL write is safe. Input/identity/authority/quarantine failures are false; NOT_RUNNING/PROCESSING_TIMEOUT true; RESOURCE_LIMIT/IO_ERROR/INTERNAL_ERROR derive from actual cause; PARTIAL_IMPORT inherits cause retryability but does not authorize automatic replay. Other state preconditions are false. No automatic request-resumption/retry identity protocol is required. A hung command is diagnosed by the trusted harness, never reinterpreted as successful. The 30-second stop bound and exact lexical/error conventions are adapter requirements; none supplies a universal SQL visibility deadline. Exact-artifact approval remains pending.