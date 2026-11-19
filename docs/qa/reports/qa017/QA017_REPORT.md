# QA-017 — Independent Step 1 maintenance verification

2026-11-18. QA-only maintenance qualification; separate Owner approval is required.
This report does not replace the accepted Step 1 baseline or authorize Step 2.

## Revisions and isolation

| Role | Exact revision |
| --- | --- |
| Owner-accepted production baseline | `fbb38781faa29eb5cead9de3dcda0e760b63c7a1` |
| Maintenance candidate tested | `83cbcb5ec7933d1d83cb61a9712353c169ba7788` |
| Protected main | `bf8289ad6ad696a58a61ad9a1c6812dcce3ae09e` |
| Protected maintenance branch | `maintenance/step1-readability-20261118`, unchanged at `83cbcb5` |
| QA worktree branch | `qa/qa017-maintenance-20261118`, based directly on `83cbcb5` |

Evidence root:
`C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa017-20261118-111036-e94e79c9`.

There are separate baseline and candidate worktrees inside this excluded root.
The original checkout remains on its documentation branch; neither implementation
diagrams nor their commit are included in the QA candidate. Baseline, maintenance
and main refs are protected. No merge, push, production edit or historical QA edit
is part of QA-017. New scripts and this report exist only in the QA worktree.

## Independent source assessment — PASS

Reviewed the complete production diff directly from Git, not merely the Developer
report. The only production differences are:

- `LedgerInstance.cs`: expand `Inventory()` declarations, verification statements,
  try/catch formatting and spacing; remove two imports.
- `Management.cs`: expand `Status()` declarations, branches, diagnostic fallback
  and returned anonymous records; add one signing-key/history comment.
- `Persistence.cs`: remove one import.

`Inventory` preserves configuration checking, missing-directory handling, bounded
reads, verification exception filters, ledger filtering, filename verification,
memory accounting and the exact pinned-genesis check in their original order.
`Status` preserves disposal, exception filters, healthy signing-key availability
on typed history failures, diagnostic fallback, recovery-code sorting, null count
semantics and all JSON fields/order. No expression, condition, exception category,
state mutation, filesystem operation or return value change was found.

A newly written QA verifier uses the installed SDK's Roslyn parser to compare
every production C# token after removing using directives and trivia. All eight
source files match. The only removed imports are `System.Buffers.Binary` in two
files and `System.Security.Cryptography` in `LedgerInstance.cs`; no import was
added. Successful independent compilations and binary comparison supplement this
check; token equality alone is not treated as semantic proof.

Protocol/revocation/authorization sources, project files, locked dependencies,
build settings and SQLite are unchanged. SQLite tree at both revisions:
`9e69364cf105a1d38e1d17ee66290967f640f78d`.

## Independent builds and executable equivalence — PASS

Both exact revisions were freshly built in their isolated worktrees with SDK
**10.0.403**, Release, using the existing build script's command options:

```powershell
dotnet build <worktree>\UserDimensions.Ledger.slnx -c Release --nologo -m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false /p:MSBuildEnableWorkloadResolver=false /p:RestoreLockedMode=true
```

The same existing locked package cache and environment were used for both.
Both solution builds: **PASS, 0 warnings, 0 errors**. No Developer regression
result is counted as new independent QA execution.

The independently written `QA017_Equivalence.cs` uses Roslyn and
`System.Reflection.Metadata`/`PEReader`; it does not execute candidate assemblies
or consume Developer-produced IL comparison output. Its source and project are
included beside this report; no production package dependency was introduced.

| Assembly | Method definitions | IL bodies | Changed method records | Changed metadata records |
| --- | ---: | ---: | ---: | ---: |
| UserDimensions.Ledger.dll | 951 | 950 | 0 | 0 of 5,530 |
| ledgerctl.dll | 2 | 2 | 0 | 0 of 57 |

The remaining library definition is the native import, without an IL body. Thus
the reported 951 library definitions are accounted for without claiming 951
managed bodies. All managed IL bytes, locals, stack limits, initialization flags
and exception regions match. Compared metadata includes types, API signatures,
parameters, fields, properties/events, generics, references, interop signatures,
custom attributes and constants. Entire user-string/name heaps, initialized RVA
data and managed resources match. This covers the current compiled representation
of protocol constants, serialization names and persistence wrapping data as well
as public API contracts.

Module MVID, PDB/debug-directory data and assembly informational-version metadata
are build identity, recorded separately rather than asserted byte-identical.
Absolute build locations differ. Native apphost/version-resource byte identity
is not claimed. Fresh QA builds therefore have different whole-file hashes from
the Developer's earlier build and historical accepted artifacts; their exact
source provenance is recorded independently.

| Fresh QA build | ledgerctl.dll SHA-256 | UserDimensions.Ledger.dll SHA-256 |
| --- | --- | --- |
| Baseline `fbb3878` | `44dbd8f51af9dc9a92085d0fb87f83dab55519c16f232245a81ce1641e89a45b` | `68cf295ee84e3f41ff91a8d6da45d49ac260b88c31f9cf85e6103abb53040f8f` |
| Candidate `83cbcb5` | `11fbb87b649b9315515c7487ddddfbb1b1607105512cba2d4f7b3f4fdc02fc59` | `10e3543b8f0727e46057fe4f4fd6fb67d5ea511a482c7534f1171772da5a63ba` |

Evidence includes `independent-build-provenance.json`, per-revision tracked-file
hashes, build stdout/stderr/command receipts, `production-diff.stdout.txt` and
`equivalence/equivalence-results.json` plus complete per-assembly metadata/body
snapshots. `dotnet --info` encountered Codex Service Control Manager access denial;
that original failure is preserved. SDK `--version`, `--list-runtimes` and actual
successful builds establish the tooling used without changing privileges.

## Selected regression coverage

The selected **53 candidate cases** consist of the existing 49 QA-015 assertions,
three unchanged QA-016 fresh no-GUID publication checks, and existing `LIFE-01`.
They exercise the central history/status paths changed in formatting, including:

- Fresh initialization/genesis and persistent original identity; publication and
  reopen/restart behavior through independent CLI processes.
- Both missing pinned-genesis scenarios, history corruption, restricted ordinary
  operations, status/instance-status, healthy key versus unavailable authority,
  null counts, unchanged identity/pin and unaffected signed files.
- New malformed input versus accepted-history frame/hash/signature corruption.
- Active retries, archived ordinary/revocation retries, exact archived duplicates,
  original bytes, no reactivation/unauthorized export, and later restoration.
- Canonical/inverted DDLB ordering, grant dependencies, revocations, rejected
  authority with no writes, partial accounting, join validation and no-overwrite.

There are also **6 unchanged advisory source-order checks** and **76 existing
independent reference checks**, counted separately from candidate execution.
Original assertion bodies and fixtures are copied unchanged into each new run's
snapshot. New QA-017 code handles provenance, selection and result aggregation.
Old QA-010 through QA-016 reports/results and accepted FI dispositions are not
edited, reclassified or counted as new tests. Full unrelated fault campaigns are
not repeated for this formatting-only candidate.

## Actual execution and disposition

**QA READY — independently verified maintenance candidate, eligible for separate
Owner approval.** Native candidate coverage: **53 PASS / 0 FAIL / 0 BLOCKED /
0 NOT-RUN**. The Owner executed the supplied command in ordinary Windows
PowerShell, confirmed its output, and the resulting saved evidence was reviewed.

| Execution | Candidate | Source checks | Reference checks | Exit |
| --- | --- | --- | --- | ---: |
| Corrected sandbox preflight | 53 PASS, 0 FAIL/BLOCKED/NOT-RUN | 6 PASS | 76 PASS | 0 |
| Ordinary Windows PowerShell | 53 PASS, 0 FAIL/BLOCKED/NOT-RUN | 6 PASS | 76 PASS | 0 |

Native evidence: `native-20261118-112445-514a146b` beneath the root above. The run
records Windows NT 10.0 build 26300 and SDK 10.0.403; both QA adapters built with
zero warnings/errors. Candidate DLL hashes match the independent comparison and
build manifest above. The run's validator verified 347 process receipts; a
subsequent read-only review checked all **348 receipts**, including the validator's
own process, without modifying original native evidence. Exact JSON envelopes,
stdout/stderr hashes, exit codes, case identities, contexts, original assertions
and frozen artifact/input hashes pass validation. The additional review is saved
separately as `native-evidence-review.json` at the work root.

Corrected preflight evidence: `preflight-20261118-112021-40ce208e` beneath the root
above. Its validator verified **347 process receipts**, candidate/input hashes,
all 53 distinct case IDs, exact contexts, exit codes and CLI JSON envelope truth.
These are new executions of existing assertions, not recounts of accepted QA-016.

The first sandbox preflight executed all 53 assertion bodies successfully, but the
new QA-017 aggregator then read an incorrect child-results path and exited 1.
This was a QA-017 harness defect, not a production assertion failure. Its entire
run, original launcher error and child receipts remain preserved at
`preflight-20261118-111722-d9ea0920`. The path was corrected without changing any
original assertion; subsequent execution is recorded separately.

No production defect or incomplete selected case remains in this verification.
QA READY means eligibility for separate Owner approval, not replacement of
`fbb3878` or authorization of Step 2. The initial QA-017 aggregation defect was
corrected and its failed run remains visible. Module/debug build identity is not
claimed byte-identical; equivalence is not a formal proof of every environmental
failure mode. Linux and macOS remain unqualified; accepted storage qualifications
are unchanged and no new physical durability claim is made.

Final preservation review confirms all 87 protected original-checkout QA/design
files, every tracked baseline/candidate file and the protected main, maintenance
and diagram-branch refs are unchanged. The original checkout remains clean. New
QA-017 artifacts remain uncommitted in the separate QA worktree; there has been
no merge or publication.

## Execution and reproducibility

`Invoke-QA017.ps1` wraps `QA017_EXECUTE.py` and accepts the real operator context
and an existing Python with cryptography/lzma. It verifies exact source/build
selection, snapshots artifacts and unchanged QA inputs, builds only the two QA
adapters, then creates new disposable instances/evidence. Synthetic corruption
and missing-genesis tests only affect those new instances; original copies and
all command stdout/stderr, JSON envelopes, inventories and exit codes are kept.
No production build, real signing key, antivirus setting or installation changes.

For the current isolated checkout:

```powershell
$qa = 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger\.git\dev-003-work\qa017-20261118-111036-e94e79c9\candidate\tests\IndependentQA'
& 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File "$qa\Invoke-QA017.ps1" -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-611-0_26.1112.611.0_x64__3k8sg7r9htsxt\dependencies\python\python.exe'
```

`QA017_VALIDATE.py <evidence-directory>` independently checks case counts,
contexts, artifact/input hashes, process output hashes and CLI envelope truth.
Complete executed commands are recorded in each evidence directory rather than
inferred from the abbreviated reproduction command above. No commit or push has
been made by this assignment.
