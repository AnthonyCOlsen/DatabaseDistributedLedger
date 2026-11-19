# QA documentation and evidence

QA owns this area. Runnable independent programs remain under
[tests/IndependentQA](../../tests/IndependentQA/README.md); Developer regressions
remain a separate suite with shared execution through `scripts/test.ps1`.

- [QA test plan and running log](QA_TEST_PLAN.md)
- [Step 2 design acceptance plan](plans/QA_STEP2_DESIGN_ACCEPTANCE_PLAN.md)
- [Step 2 bypass/fault matrix](plans/QA_STEP2_BYPASS_AND_FAULT_MATRIX.md) — design only
- [QA-016 final Step 1 report](reports/qa016/QA016_REPORT.md) — accepted production `fbb3878`
- [QA-017 maintenance report](reports/qa017/QA017_REPORT.md) — authorized successor `83cbcb5`
- [QA-017 handoff](reports/qa017/QA017_HANDOFF.md)
- [Earlier reports](reports/) — preserve each candidate and its original PASS/FAIL history
- [Compact published evidence](evidence/20261110/) — existing public nonproduction archives
- [Earlier QA plans and integration records](../archive/qa/)

Reports are immutable historical records, including their original commands,
absolute local evidence paths, candidate hashes and initial harness failures.
Their older paths are not a current invocation guide. Full original receipts
remain in their existing excluded locations; future runs go to ignored `local/qa`.
A fresh clone includes the reports/fixtures, not private local run directories.

The standard combined regression command runs both Developer and independent QA
without changing their ownership or assertions. It builds Release, starts only
fresh disposable test instances, exercises corruption/process interruption cases,
and retains logs/original bytes. Use ordinary PowerShell outside Codex for Windows
filesystem qualification. No antivirus setting or real instance is changed.

```powershell
pwsh -NoProfile -File ./scripts/test.ps1 -RunContext OrdinaryWindowsPowerShell -PythonExe 'C:\path\to\python.exe'
```

Existing Python must include cryptography and lzma; launchers install nothing.
Historical `Invoke-QA*.ps1` programs retain their original candidate/hash pins;
use them only with the reviewed historical source/binaries and explicit selection.
The current entry point checks the approved source tree before running old test
bodies on a fresh build. It is reorganization regression, not a new acceptance vote.
