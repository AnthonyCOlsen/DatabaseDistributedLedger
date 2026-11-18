# Step 1 maintainability review — 2026-11-18

Behavior-preserving Developer maintenance only. Accepted production remains
`fbb38781faa29eb5cead9de3dcda0e760b63c7a1`. Step 2 remains paused. This review does
not accept or supersede that production baseline.

## Repository and review coverage

Initial HEAD/main: `bf8289ad6ad696a58a61ad9a1c6812dcce3ae09e`; working tree clean.
The intervening commits changed documentation/QA evidence, not production source,
Developer regressions, build scripts or SQLite. Maintenance is isolated on
`maintenance/step1-readability-20261118`; main and accepted history are unchanged.

Reviewed all handwritten Step 1 production C# files: the CLI entry point and
AssemblyInfo, Authorization, LedgerInstance, Management, Persistence, Protocol and
RevocationConsent. Also reviewed the Developer test structure and coverage, build
scripts, projects/locked dependencies, current design/implementation records and
existing QA ownership/portability records. Existing tests are retained unchanged.

SQLite's current tree equals `sqlite/upstream-3.54.1` byte-for-byte at Git tree
`9e69364cf105a1d38e1d17ee66290967f640f78d`. No upstream files were edited or
reformatted. Step 1 has no custom C shim or .NET SQLite P/Invoke binding to clean
up: its solution references the managed crypto/compression packages, while the
SQLite source remains the pristine native baseline for later authorized work.

## Overall assessment and conventions

The implementation has coherent conventions, but no repository-wide enforced
formatting standard (`.editorconfig` was not found). File-scoped namespaces,
four-space indentation, PascalCase public types/methods, camelCase locals/private
fields, records, modern constructors and explicit ordinal/binary ordering are
consistent. Nullable analysis, warnings-as-errors, deterministic builds and locked
dependencies are enabled centrally. JSON snake_case members are wire-contract
names and must not be renamed to satisfy a cosmetic C# preference.

The main readability problem is multiple operations, branches and anonymous DTO
fields compressed onto single lines. Spacing varies across files. This is a
maintenance/debugging cost, not evidence that the accepted behavior is incorrect.
The existing security/recovery/resource checks generally explain real protocol,
identity or durability constraints; they were not removed as presumed defensive
noise. Concise invariant comments are useful; broad comment churn is not.

| Component | Assessment |
| --- | --- |
| CLI entry point / AssemblyInfo | Small delegation boundary; Developer friend access is explicit. No cleanup needed. |
| Protocol / RevocationConsent | Explicit byte order, strict decoding, time bounds and independent crypto/compression boundaries; portable managed core. Dense codecs and broad malformed-stream mapping warrant care, not speculative simplification. |
| Authorization | Compact deterministic replay with separate current/historical authority reasoning. Some redundant-looking state deserves a later review; no authorization expressions changed. |
| LedgerInstance | Largest responsibility set: identity lifecycle, configuration, history verification, import/publication, archive reconciliation, retrieval/export and recovery. Inventory readability benefits from localized expansion; a major split is not justified under this assignment. |
| Management | Finite CLI parsing, exact JSON/error contract and batch orchestration are explicit. Import accounting and recovery status are dense. Status is expanded locally; batch ordering/accounting is left intact. |
| Persistence | DurableFiles contains native publication; strict JSON, identity wrapping and leases share this file. Isolation exists but is not yet a replaceable cross-platform backend. Only an unused import was removed. |
| Developer tests | An intentional small console harness, split by concerns and containing 87 regression cases. Source-order and recovery tests protect the recent corrections. No test-framework migration or test edits were needed. |

## Changes made

1. `src/UserDimensions.Ledger/LedgerInstance.cs`: expand only `Inventory()` into
   readable declarations, verification steps and exception handling. Remove its
   compiler-confirmed unused `System.Buffers.Binary` and
   `System.Security.Cryptography` imports.
2. `src/UserDimensions.Ledger/Management.cs`: expand only `Status()` declarations,
   branches, diagnostic fallback and DTO formatting. Add one comment explaining
   why a retained-history failure must not clear healthy signing-key availability.
3. `src/UserDimensions.Ledger/Persistence.cs`: remove the compiler-confirmed unused
   `System.Buffers.Binary` import.

No condition, expression, declaration type, string literal, JSON property/order,
exception filter, operation order, public API, authorization rule, signing path or
filesystem operation was changed. The formatting was deliberately localized;
other compact methods and upstream code were not reformatted.

## Verification and preservation

- Release build: **0 warnings, 0 errors**. Existing Developer regressions:
  **87 PASS, 0 FAIL**. No new tests or full independent acceptance rerun were added.
- Roslyn semantic inspection: zero compilation errors; three unnecessary imports
  in handwritten source removed. Two unused SDK-generated global usings remain
  untouched; these are not product build warnings.
- Roslyn token comparison: every non-import executable C# token matches the
  pre-maintenance source. Only the three confirmed unused imports and trivia differ.
- Compiled comparison with the preserved accepted fbb3878 artifacts: all **951
  library methods** and **two CLI methods** retain identical IL. Captured type,
  method/field/member-reference signatures, flags and exception regions also match.
- `git diff --check`, protected-file hashes and pristine SQLite tree checks pass
  before commit. Historical QA evidence, assertions, fixtures, design
  documents and Developer tests remain untouched.

The verifier uses the installed SDK's Roslyn and framework metadata reader in
temporary `.git` space; it introduces no project dependency or runtime mechanism.
Its setup retries are retained separately from successful product build/test logs.

Binary bytes are not claimed identical: PDB sequence points/checksums, build
identity/informational metadata and deterministic image identifiers can change.
Changed build outputs and their exact SHA-256 values are recorded in the final
Developer handoff and local `candidate-manifest.json`, including the CLI/library
and Developer-test assemblies. The accepted binaries were preserved before builds.

Evidence directory:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\step1-maintenance-20261118-091704-12ae839c`.
It contains before/after source and IL records, protected-file hashes, Release/test
logs, accepted artifact copies and final build fingerprints. Generated evidence
and the temporary verifier are excluded from the maintenance commit.

The token/IL comparisons and regression results strongly support unchanged runtime
behavior. This is not a new independent QA acceptance, an exhaustive formal proof
of every environment/failure mode, or a new OS/storage qualification. Debugger line
numbers and build fingerprints change as expected. No unresolved behavioral
uncertainty was found in the permitted edits themselves.

## Deferred recommendations, in priority/risk order

1. **High regression risk — batch/recovery responsibilities.** A future authorized
   design may separate Management import validation, dependency-safe persistence
   and result accounting, and split identity recovery from LedgerInstance. Preserve
   error precedence, partial durable receipts, gate placement and exact retry
   behavior. Major class extraction requires Owner approval and focused review;
   it is not part of this cleanup.
2. **High design/qualification risk — portable storage/lease backends.** Protocol,
   cryptography, replay, serialization and formats are portable. Windows-specific
   behavior remains concentrated in DurableFiles (`MoveFileExW`, local NTFS),
   initialization's Windows/NTFS gate, IdentityLease's named-mutex scope and path
   policy. DurableFiles is sealed/concrete rather than a replaceable backend.
   Windows/Linux/macOS backend design and native qualification require separate
   Owner-approved work; do not relax guards or claim Linux/macOS support here.
3. **Medium compatibility risk — apparently redundant/obsolete code.** Review
   AuthorizationReplay's repeated validity/historical-authority state, the internal
   `EvaluateRetained()` helper (no in-repository callers found) and Get's legacy
   dispute guard before removal. Runtime/reflection consumers and error contracts
   must be checked; deleting them would exceed a trivia-only change and weaken the
   current preservation argument. No defect is asserted solely from these findings.
4. **Low functional risk, broad diff risk — explicit style policy.** Agree a small
   `.editorconfig`/contribution convention and adopt it incrementally when files
   are otherwise touched. Preserve protocol DTO names, explicit safety checks,
   QA ownership and upstream SQLite formatting. Avoid a whole-repository rewrite.

Any exception-filter narrowing, changed guard/resource calculation, optimization
of repeated verification or public API adjustment is deferred because it can
change externally observable failures, timing or accepted input. No new feature,
recovery flow, framework or abstraction layer was introduced.
