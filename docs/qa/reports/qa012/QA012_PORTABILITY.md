# UDDL portability assessment — QA-012 / Architect directive

Reviewed 2026-11-17 at `8bc96f0f513faac57c1db23a8189288479d8a992`.
This is a QA-owned assessment and proposed Developer work list, not a change to
architecture, production code, signed protocol or approved behavior. Cross-platform
portability is a permanent requirement. Windows 11 is the initial acceptance scope,
not a permanent restriction. Linux/macOS/other platforms remain **UNQUALIFIED**.

## Verified source findings

- The managed projects target `net10.0`, without a Windows-specific TFM, WindowsDesktop
  SDK or hard-coded RID in the reviewed project files. `Protocol.cs`,
  `RevocationConsent.cs` and `Authorization.cs` contain no Win32 calls. Protocol 1.03
  uses explicit byte order, raw key/GUID bytes, strict UTF-8/JSON and ordinal ordering.
  Key JSON/AAD and manifest/config formats do not use DPAPI or an OS keystore.
- BouncyCastle and SharpCompress are referenced through managed project dependencies;
  this project-file observation does not independently qualify their runtime behavior,
  .NET crypto providers or packaging on another OS/CPU.
- Filesystem mutation is concentrated in `Persistence.cs` / `DurableFiles`, but this
  is a sealed concrete class, not a replaceable platform interface. Windows/NTFS
  capability checks also appear in `LedgerInstance.Initialize`, outside that adapter.
- The SQLite fork currently contains a **pristine vendor baseline**:
  upstream-tag and HEAD `third_party/sqlite` trees both equal
  `9e69364cf105a1d38e1d17ee66290967f640f78d`. Git shows no committed or working changes
  in that tree. Windows and Unix VFS source and OS-selection code are intact.
  No future .DLDB header/native interception/projector modifications are present to
  audit yet; their portability must be reviewed when implemented.

The read-only source inventory, exact locations and SHA-256s are recorded in
`portability-source-audit.json` under the QA-012 evidence directory. SQLite's VFS
is its OS portability boundary and should remain the native integration boundary.
[SQLite VFS documentation](https://www.sqlite.org/vfs.html).

## Platform exceptions and proposed remediation

These are review proposals requiring Developer work/coordination, not implemented
corrections or newly approved persistence algorithms.

| ID / affected source | Current exception and why it exists | Windows status / proposed Windows approach | Proposed Linux / macOS approach and limits |
| --- | --- | --- | --- |
| P-01 `LedgerInstance.cs:45–57`, `Persistence.cs:65–91` | Explicit Windows, fixed-drive and NTFS gates reflect the only initially qualified durability backend. They currently prevent normal initialization/publication elsewhere. | Keep fail-closed behavior and qualify the existing backend for Step 1. Move capability selection out of lifecycle/business logic in a separately scoped change. | Add separately qualified backend implementations; do not simply remove the guards or label another filesystem safe. No Linux/macOS support claim until tests and storage semantics are reviewed. |
| P-02 `Persistence.cs`, `DurableFiles` | Atomic no-overwrite, replacement and durable directory publication require OS/filesystem-specific primitives. `kernel32!MoveFileExW`, native errors and extended Windows paths are isolated here, but the sealed adapter cannot be replaced by injection. | Propose a small durable-store/platform interface and retain this Windows implementation behind it; preserve flags and error behavior until qualified changes are approved. | Implement native atomic no-overwrite/replace plus file/directory synchronization appropriate to each supported filesystem. Linux and macOS need separate guarantees/qualification; ordinary `File.Move` alone is not proposed as a durability substitute. |
| P-03 `Persistence.cs`, `IdentityLease:188–204`; `LedgerInstance.cs:42` | Named mutexes protect instance GUID and root; Windows uses `Global\\` while other OSes use a different name. Namespace, permissions, abandonment and ownership behavior require qualification. | Preserve tested same-host/copy-identity exclusion. Define lock-provider lifetime and scope explicitly. | Propose a replaceable identity-lease provider. Qualify same-process, cross-process, copied identity, abnormal exit, permissions and session/user scope per OS. Do not assume identical named-mutex semantics merely because the .NET API exists. |
| P-04 `Paths.Safe`, `Paths.Absolute`, path comparisons in `LedgerInstance.cs` | String-length limits, Windows case folding, extended paths and a blanket symlink/junction prohibition are conservative current policies. Character counts/case rules do not fully express filesystem capabilities. | Preserve current Windows acceptance behavior; add volume-aware path/case/alias test coverage before widening it. | Validate filesystem byte/component/path limits, case-sensitive versus insensitive volumes, Unicode spelling and alias policy separately, particularly on macOS. Preserve identity and no-overwrite guarantees; do not silently normalize signed bytes. |
| P-05 `DurableFiles.Directory/Publish/Move` | Direct adapter use can create temp content before `Move` rejects an unqualified OS; normal `Initialize` currently preflights Windows first. Unsupported platform handling is not wholly centralized. | No change required for this QA assignment's Windows cases. Propose capability preflight before mutation consistently across the future adapter interface. | Unsupported backends should fail predictably before mutation; new qualified backends must preserve complete recoverable remnants on real I/O errors. Do not treat this proposal as current non-Windows support. |
| P-06 `scripts/*.ps1`, QA launchers/adapters | Windows acceptance uses NTFS, Windows sharing/handle tests and machine-specific bundled runtime paths. Some old QA launchers gate even reference-only execution on Windows. These are harness/deployment constraints, not wire requirements. | Keep Windows handle/MoveFileEx qualification in a distinct suite. Explicit runtime paths remain overridable; QA-012 separates reference and candidate modes. | Run the unchanged Python reference/fixtures directly without Windows/.NET/NTFS requirements. PowerShell 7 is an optional portable launcher. End-to-end Ledger and OS-specific fault cases require the corresponding backend first; no blanket reuse of Windows sharing assertions. |

No inspected source creates a necessary permanent Windows-only protocol/crypto
architecture. The immediate portability gap is the unfinished storage/locking
backend abstraction and qualification, not a reason to change signed bytes or
delay approved Windows Step 1 work. An interface or future native integration that
would impose a permanent platform restriction must be escalated to the Owner first.

## OS guarantees relied upon, not proven by software tests

The current Windows path writes a same-volume temporary file with WriteThrough,
calls `FileStream.Flush(true)`, then invokes `MoveFileExW` with WRITE_THROUGH and,
for config/manifest, REPLACE_EXISTING. COPY_ALLOWED is not enabled. Existing callbacks
observe before the call and after successful return; none observes inside it.

Microsoft documents intermediate-buffer flushing for `Flush(true)` and describes
WRITE_THROUGH behavior, with a specific flush guarantee for a move implemented as
copy/delete. That wording and the actual same-volume rename procedure require
Developer/Security review for the intended directory/storage guarantee. Neither
the API flag nor this QA process-termination evidence independently establishes
all filesystem, controller-cache, device-failure or physical power-loss behavior.
[FileStream.Flush](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0),
[MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw).

QA-012's alternative tests qualify observable software behavior. Original FI-07/FI-15
OS-internal checkpoints are NOT EXECUTED. A separate approved platform/storage
qualification or acceptance disposition remains with Developer/Security/QA/Owner.

## Portable testing and phased work recommendation

The shared `QA010_REFERENCE.py` / `QA010_FIXTURES_103.json` use Python standard
`struct`, `hashlib`, JSON, UUID, `lzma` and `cryptography`, with no Windows API or
production-code oracle. Existing independent reference results are retained rather
than rerun solely for this audit. On a future test machine with existing prerequisites:

```text
python3 -B QA010_REFERENCE.py --fixtures QA010_FIXTURES_103.json --results <fresh-output-json>
```

QA-012 also allows reference-only PowerShell execution without the Windows/NTFS
candidate gate or requiring .NET. That endpoint remains untested on Linux/macOS;
portable source is not certification. Windows candidates stay explicitly separated.

1. Complete/review the approved Windows observable qualification now. Do not postpone
   Step 1 solely for unrequested Linux/macOS certification.
2. Schedule a bounded Developer platform-interface/capability task; preserve codecs,
   signatures, storage bytes and current Windows behavior.
3. Add Linux and macOS backend/locking implementations with explicit supported filesystem
   matrices and independent native qualification. Run shared reference fixtures on each.
4. When SQLite integration begins, retain VFS/OS boundaries and review native library
   loading and build packaging per target. The pristine upstream baseline must remain
   reproducible. No native implementation/refactor is authorized by this assessment.

Current audit execution platform: Windows only. Linux, macOS, other operating systems,
additional CPU architectures and future SQLite-ledger modifications were NOT TESTED.
