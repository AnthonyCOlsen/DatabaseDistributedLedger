# QA-010 execution findings — 2026-11-16

Candidate: `6328bbc3a289b969b33dc365f6d8639cc005793f`.
Scope: independent ARCH-014 / Protocol 1.03 Step 1 QA only. **No production fix,
production hook change, Developer regression edit, architecture edit, commit, push,
or formal acceptance was performed.**

## Completed sandbox diagnostics and Owner-run Windows execution

| Suite / run | PASS | FAIL | BLOCKED | Exit | Scope |
| --- | ---: | ---: | ---: | ---: | --- |
| Final independent reference | 76 | 0 | 0 | 0 | Independent Python wire/crypto checks; no candidate executed |
| Final independent candidate | 130 | 1 | 4 | 1 | Actual frozen candidate CLI/public API; Codex sandbox diagnostic only |
| Owner-run Windows reference | 76 | 0 | 0 | 0 | Independent reference, launched in ordinary Windows PowerShell |
| Owner-run Windows candidate | 130 | 1 | 4 | 1 | Actual candidate CLI/public API; Owner-reported ordinary terminal, receipt inspected |

The candidate summary is intentionally FAIL because the error-code discrepancy
below is open. The blocked checkpoints remain separate; they are not counted as
passes. Reference success is not candidate conformance. Process termination is not
physical power-loss testing.

Final diagnostic evidence, relative to the repository:

```text
.git/dev-003-work/qa010-20261116-181514-0b01a91b/
```

Every case ID/result/command index is in `candidate-results.json`, `cases.jsonl` and
`coverage-results.csv`; all 76 reference results are in `reference-results.json`.
Full command stdout/stderr/exit receipts are under `commands/`. The QA adapter build
passed with zero warnings/errors in its isolated run directory. Production output
was copied and hashed, never rebuilt or overwritten by this assignment.

Recorded environment: Windows NT 10.0.26300.0, fixed NTFS, PowerShell 7.6.9,
.NET SDK 10.0.403 / runtime 10.0.14 x64, Python 3.12.14 / cryptography 50.0.2.
Actual antivirus state was not operator-recorded; no AV settings were changed.

## Ordinary Windows execution receipt

The Owner executed the complete suite using the existing PowerShell 7 and an explicit
`-PythonExe` path. The completed receipt was inspected at:

```text
.git/dev-003-work/qa010-20261116-201537-c7555114/
```

Receipt context is `OrdinaryWindowsPowerShell`, started `2026-11-17T02:15:37Z`
and completed `2026-11-17T02:16:56Z` (November 16 local time), exit 1.
Reference: 76 PASS / 0 FAIL. Candidate: 130 PASS / 1 FAIL / 4 BLOCKED.
The sole candidate failure is the same `JOIN-NEG-ddlb` error-code discrepancy;
before/after instance inventories are identical. The blocked IDs remain FI-07,
FI-09, FI-09-GRANT and FI-15. No new native-run-only failures were observed.

All frozen QA source/fixture hashes were verified against that run's manifest;
the executed Python, C# adapter, project, fixture and launcher copies match the
current test implementation. Candidate/library hashes match the sandbox run's
hashes below. The adapter built with zero warnings/errors. Git before/after receipts
show the same commit and only the new uncommitted QA files. Historical QA original
hash checks passed. Production and unrelated tracked paths remain unchanged.

The ordinary-terminal requirement is satisfied for the executed cases based on
the Owner's report and inspected receipt. This does not resolve the four missing
checkpoint observations or constitute formal acceptance or physical power-loss proof.

Actual candidate SHA-256:

| Artifact | SHA-256 |
| --- | --- |
| `ledgerctl.dll` | `93ef155c1eb86b1a160b1e752b65bfb7afba7f5f8b596de97d3cc8abb55a8a9a` |
| `UserDimensions.Ledger.dll` | `7e2122da422d69522db13a08225df7d850106c2f727dbfc6d0557a04f268e44d` |

## QA010-D01 — join input error classification needs review

Case: `JOIN-NEG-ddlb`. Supply a valid Version 1 DDLB wrapper containing a valid
independently signed 227-byte 1.03 genesis to `ledger join --genesis-file`.

- Expected by the test: `BAD_FRAME`, exit 2, using the malformed-frame convention
  in Management API §17 and the explicit raw-genesis-only contract.
- Observed: `UNSUPPORTED_VERSION`, exit 2.
- The input is correctly rejected. The instance's complete before/after file and
  directory inventories are equal; no incoming block/temp file or binding is created.
- This is an **error-code conformance discrepancy / specification clarification**,
  not evidence that an invalid genesis was accepted or persisted. Generic signed
  version rejection explains the current behavior, while join's raw-file contract
  motivates the stricter BAD_FRAME assertion. QA/Architect should confirm the exact
  error precedence before any expectation or production implementation is changed.
- The assertion has **not** been weakened to accept the observed result.

Exact final-run evidence:

```text
commands/0023.process.json
commands/0023.stdout.txt
commands/0023.stderr.txt
JOIN-NEG-ddlb.before.json
JOIN-NEG-ddlb.after.json
JOIN-NEG-ddlb.failure.txt
inputs/ddlb-join.bin
instances/join-ddlb/
```

Reproduce against the frozen candidate and already isolated unbound test instance:

```powershell
$repo = 'C:\AnthonyO\32GB_HOME\Development\UserDimensions.DatabaseDistributedLedger'
$e = "$repo\.git\dev-003-work\qa010-20261116-181514-0b01a91b"
$f = Get-Content "$e\suite-snapshot\QA010_FIXTURES_103.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$g = $f.vectors | Where-Object name -eq 'genesis'
dotnet "$e\candidate-snapshot\ledgerctl.dll" --instance-root "$e\instances\join-ddlb" ledger join --ledger-id $f.ledger_id_hex --genesis-sha512 $g.sha512 --genesis-file "$e\inputs\ddlb-join.bin"
```

Any confirmed correction should be a separately authorized Developer task, such as
recognizing a DDLB wrapper at the raw-genesis join boundary and returning BAD_FRAME
without a write. This QA assignment applies no correction.

## QA010-H01 — corrected evidence collector bug, original failure retained

The first run's `FI-20-HELD-FILE` observer attempted to hash `instance.config.json`
while the QA worker intentionally held it with FileShare.None. That observer read
raised PermissionError before the candidate CLI assertion ran. This was a **harness
bug**, not a production locking failure.

After reporting the finding, the collector was corrected to record the unreadable
locked file with `sha512:null` at the barrier, then compare actual bytes after the
holder is terminated. No candidate assertion or expected exit was weakened.
`FI-20-HELD-FILE` subsequently passed. The final run also records zero observed
DurableFiles write/rename callbacks for rejected invalid/unresolved/unauthorized
imports (`AUTH-NO-WRITE-CALLBACKS`), alongside unchanged filesystem inventories.

All original runs, failing logs and source hashes are retained:

| Run directory under `.git/dev-003-work/` | Candidate result | Notes |
| --- | --- | --- |
| `qa010-20261116-180427-791ef4e0` | 115 PASS / 2 FAIL / 4 BLOCKED | Initial D01 discrepancy plus H01 collector bug; original source snapshot verified against the originally recorded SHA-256 hashes |
| `qa010-20261116-181057-4f3a33f8` | 129 PASS / 1 FAIL / 4 BLOCKED | Collector fixed; expanded independent vectors and coverage |
| `qa010-20261116-181514-0b01a91b` | 130 PASS / 1 FAIL / 4 BLOCKED | Final diagnostic, including callback-observed receive fence; 76 reference checks pass |

No old evidence was deleted, overwritten or relabeled PASS.

## QA010-H02 — ordinary-terminal Python discovery corrected

The Owner's first correctly ordered native-launch attempt stopped before any QA
evidence directory or candidate process was created. The launcher reported:
`Supply -PythonExe with existing Python 3 + cryptography/lzma; this launcher installs nothing.`
Its previous automatic discovery depended on Codex's bundled Git being on PATH,
which the ordinary terminal did not provide. This was a **launcher discovery bug**,
not a missing installed prerequisite or candidate test failure.

The launcher now first checks for an existing Python beside its own PowerShell
installation, using `$PSHOME`. Explicit `-PythonExe` continues to take precedence;
existing fallback discovery remains available. No dependency is installed and no
candidate expectation changes. An isolated reference-only smoke run with an
ordinary-terminal-style PATH verified this correction. At that point, native
candidate execution still awaited the Owner's ordinary-terminal rerun; the complete
subsequent run is recorded above.

Smoke receipt: `.git/dev-003-work/qa010-20261116-185455-a368831e/`.
With PATH restricted to standard Git, .NET and Windows directories, all 76 reference
checks passed, exit 0; the candidate suite was explicitly NOT-RUN. The receipt
records the discovered bundled Python executable and the corrected launcher snapshot.
The subsequent complete Owner-run receipt above uses explicit `-PythonExe`; no
additional Python installation was required.

## Missing checkpoints / remaining acceptance prerequisites

1. `FI-07`: no observable boundary between final rename and directory durability
   inside the existing single MoveFileEx WRITE_THROUGH call.
2. `FI-09`: no injectable Management/DDLB completed-item/before-next-item barrier.
3. `FI-09-GRANT`: no observable **actual batch** grant-durable/before-dependent
   checkpoint. Functional inverted-order and failed-prerequisite tests pass, but
   sequential API imports are not substituted for this crash observation.
4. `FI-15`: config replacement/pre-directory-confirmation is likewise not exposed;
   the passing separately named `FI-15-POST` observes only successful syscall return.

These four cases remain BLOCKED pending Developer/QA/Owner coordination. No new
production hook has been added. FI-01 verifies preservation of a flushed temporary
key and the recovery gate; automatic promotion of a temporary key is not invented.
See `QA010_COVERAGE.md` for all requirement and fault mappings, proposed QA test-plan
updates, and boundaries on stress, SQL and excluded GUID-collision testing.

The ordinary Windows 11/PowerShell 7 run outside Codex is now recorded separately
above. Exact rerun commands are in `QA010_README.md`. No further identical rerun is
needed absent a relevant code/harness change, a newly available checkpoint, or a
QA-approved error-code clarification. The current source Git history is unchanged
and all new QA files remain uncommitted, as instructed.

Formal Step 1 acceptance remains with QA-Tester, PM and Owner.
