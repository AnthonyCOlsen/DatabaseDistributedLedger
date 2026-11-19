# Documentation map

The repository root README is the project overview. Keep one authoritative copy
of each contract; implementation guides and diagrams explain it without redefining it.

| Location | Owner and purpose |
| --- | --- |
| [architecture/](architecture/) | Architect-owned Technical Design, Management API and SQLite Plan; current normative contracts |
| [qa/](qa/README.md) | QA-owned test plan, active design-only acceptance plans, published reports and compact evidence |
| [development/](development/REPOSITORY_LAYOUT.md) | Developer-owned implementation guides and repository/tooling conventions |
| [diagrams/](diagrams/README.md) | Editable diagram sources and their matching presentation exports |
| [upstream/](upstream/) | SQLite provenance, modification and upgrade records |
| [archive/](archive/README.md) | Historical handoffs and earlier plans; not current authority |

The November 2026 reorganization moved files with `git mv` before changing links.
Historical QA reports/results and the original DEV-003 bundle keep their exact
bytes. Old commands and paths inside those records describe the original run;
use the current QA index for new commands. No test outcome was rewritten.
