# Independent QA

Independent QA ownership and expectations remain separate from Developer
regressions; both can be run through the shared `scripts/test.ps1` command.

| Folder | Contents |
| --- | --- |
| runners/ | Historical PowerShell launchers with exact candidate/hash guards |
| suites/ | Python suite bodies, independent reference calculations and receipt validators |
| fixtures/ | Public nonproduction fixture data |
| adapters/ | C# fault driver, byte verifier and independent equivalence verifier |
| suites/legacy-dev003/ | Eight byte-preserved original files and their manifest; keep the acceptance ZIP beside its verifier |

[QA documentation](../../docs/qa/README.md) contains the plan, active Step 2
planning documents, reports and compact published evidence. Historical outcomes
and qualifications remain unchanged. [Repository layout](../../docs/development/REPOSITORY_LAYOUT.md)
explains current commands and local storage.

New current regressions use `scripts/test.ps1` from the repository root. Historical
launchers are not automatically retargeted to a new implementation. Ordinary
PowerShell outside Codex remains required for Windows filesystem acceptance.
