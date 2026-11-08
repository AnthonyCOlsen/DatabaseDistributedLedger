# SQLite source provenance

Administrative source record for the permanent UserDimensions SQLite fork.
Original project documentation: MIT; upstream notices retain their own terms.

## Original upstream baseline

- Project: SQLite, https://www.sqlite.org/
- Canonical Fossil repository: https://sqlite.org/src
- Official Git mirror: https://github.com/sqlite/sqlite
- Release: **3.54.1**, **2026-10-16**.
- Release record: https://www.sqlite.org/releaselog/3_54_1.html
- Published download metadata: https://www.sqlite.org/download.html
- Archive: `sqlite-src-3540100.zip`.
- Download URL: https://www.sqlite.org/2026/sqlite-src-3540100.zip
- Published and independently measured size: **14,899,528 bytes**.
- Published and independently calculated **SHA3-256**:
  `877c6f4996ce7adb368686bce21638de9e3d91534832ad3d9d119037d1bc22ad`
- Official Fossil check-in, also verified in `manifest.uuid`:
  `555e07ef33f69a0957d24d8e555b702464a131d6f1607f4c3c1e4551e8e6ca76`
- Official and built `SQLITE_SOURCE_ID`:
  `2026-10-16 14:45:20 555e07ef33f69a0957d24d8e555b702464a131d6f1607f4c3c1e4551e8e6ca76`
- Import date: **2026-11-07**, America/Chicago.
- Pristine import Git commit: `cc0dd4aa2103947dfc019d8e8103c298906ccf36`.
- Annotated baseline tag: `sqlite/upstream-3.54.1`, targeting that exact commit.
- Parent project commit: `c33e302961e563329e1f2533ffed50e120a4678a`.

## Extraction and byte preservation

The contents of the ZIP's single `sqlite-src-3540100/` packaging directory were
mapped directly into `third_party/sqlite/`. The complete import contains
**2,290 files**, totaling **113,150,111 uncompressed bytes**. Source, tests,
extensions, utilities, hidden upstream settings, documentation, manifests,
build materials, AGENTS.md, and license notices are retained unchanged.
No extra packaging directory, downloaded archive, or generated build product
is part of the source commit.

The root `.gitattributes` rule `third_party/sqlite/** -text` disables Git text
normalization for this tree, including when `core.autocrlf=true`. Unrelated
project files and global Git configuration were not changed.

## Verification performed

1. Checked local branch, clean status, author identity, origin and remote main.
   Main matched the expected project design update `c33e302...`; no existing
   baseline tag or import destination conflicted with this assignment.
2. Downloaded the archive directly over HTTPS from the official URL above.
3. Used .NET 10 `System.Security.Cryptography.SHA3_256`, with support checked
   and the empty-input known-answer digest verified before hashing the archive.
   This was SHA3-256, not SHA-256. Git's MSYS OpenSSL could not start in the
   sandbox; no hashing substitution or software installation was performed.
4. Verified actual archive size and SHA3-256 against the published values.
5. Verified `VERSION`, `manifest.uuid`, and required source/build/test entries.
6. Validated archive file paths before extraction: rejected traversal, rooted
   or Windows-unsafe paths, case-insensitive duplicates and symlink entries.
   Every resolved output file was checked to remain inside the import root.
7. Compared each extracted file against its ZIP entry using SHA-256 and checked
   the full file count. This SHA-256 comparison was additional file-integrity
   verification; it did not replace the archive's required SHA3-256 check.
8. Compared every staged Git blob identifier against the unfiltered original
   file bytes; all 2,290 source blobs matched. The import commit includes only
   those files and the root byte-preservation attributes rule.
9. Ran upstream `verify-source` and an unmodified x64 MSVC/NMAKE baseline build
   in temporary space under `.git`, outside the source tree. No source edits,
   custom file signature, omission flags or ledger behavior were introduced.

## Baseline build and smoke results

Environment: Windows; Visual Studio Community 2026 Developer Command Prompt
18.11.3; MSVC toolset directory 14.51.36231; NMAKE 14.51.36260.0; Windows SDK
10.0.26100.0. Used the existing x64 host/x64 target developer environment.
Read the imported Makefile.msc before selecting targets.

From a temporary build directory, with `SQLITE_SRC` set to the absolute imported
source path, ran:

```bat
nmake /f "%SQLITE_SRC%\Makefile.msc" TOP="%SQLITE_SRC%" verify-source
nmake /f "%SQLITE_SRC%\Makefile.msc" TOP="%SQLITE_SRC%" sqlite3.exe sqlite3.dll libsqlite3.lib
```

Both invocations succeeded (exit 0). Captured verifier result:

```text
OK 555e07ef33f69a0957d24d8e5
```

Built the CLI, native DLL and static library; the DLL's import library was
also generated. `dumpbin /headers` identified the DLL as `8664 machine (x64)`;
`dumpbin /exports` listed 300 exports, including `sqlite3_open_v2`,
`sqlite3_libversion`, and `sqlite3_sourceid`.

The built CLI ran this in-memory smoke check:

```sql
SELECT sqlite_version(), sqlite_source_id();
CREATE TABLE smoke(v INTEGER);
BEGIN;
INSERT INTO smoke VALUES(42);
ROLLBACK;
SELECT count(*) FROM smoke;
PRAGMA integrity_check;
```

Captured output, exit 0:

```text
3.54.1|2026-10-16 14:45:20 555e07ef33f69a0957d24d8e555b702464a131d6f1607f4c3c1e4551e8e6ca76
0
ok
```

Baseline artifact SHA-256 values (identification of this build, not a
reproducible-build guarantee):

| Artifact | SHA-256 |
| --- | --- |
| sqlite3.exe | A1C9CBBD299E86D159A68179711A2932E91EFAE88E59AC079831D87E53A40090 |
| sqlite3.dll | 2676272324AF79680D3E08D393658D85EA96180E8C0FD5E9EF25D84A9D04EF9C |
| libsqlite3.lib | 4EDECF728B6096B6734EAAC99F168DC027E3E9FC724BF2EBDDEB07A9DBC6CEBB |

**Not run:** Tcl devtest/releasetest suites; no Tcl development environment was
available on the session PATH or at the previously inspected default location.
No Tcl, dependencies or toolchain components were installed. The smoke check
is not a substitute for the upstream suites. No .NET provider integration or
ledger regression tests were performed. Temporary archive/build files and
logs are excluded from commits and removed after verification; relevant
commands, output and artifact identities are captured here.

## Public domain and third-party notices

Preserve `third_party/sqlite/LICENSE.md` and all original per-file notices.
SQLite core, public tests, extensions and CLI are public domain as described
in that file and https://www.sqlite.org/copyright.html. This import does not
relicense upstream code under the project's MIT license.

Build infrastructure includes non-public-domain exceptions: Autosetup and
its embedded components carry their own notices (including
`autosetup/LICENSE`, a BSD-style license), and legacy Autoconf/TEA materials
include notices such as `autoconf/tea/license.terms`. Preserve copyright,
conditions and disclaimers, including source and binary redistribution
requirements where applicable. All original notice files and embedded
notices are retained. Future reuse or redistribution of these build tools
must follow their actual per-file terms; they must not be assumed public
domain merely because the SQLite engine is public domain.

## Maintenance boundary

The tagged source is the immutable comparison baseline. Subsequent changes
belong in ordinary downstream commits and the modification/upgrade logs.
Do not alter upstream manifests to conceal modifications. Future source
changes must retain applicable notices. Exact upstream bytes can be checked
again against the pinned archive and baseline tag; source authenticity
verification and downstream-fork regression testing are distinct activities.
