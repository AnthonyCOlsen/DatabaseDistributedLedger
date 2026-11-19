# DEV-003 Step 1 Independent QA Execution Status — 2026-11-10

**FROM:** Lead QA / Test Engineer  
**TO:** Project Manager  
**VIA:** Project Owner  
**Candidate:** commit `5c585cefa59509f9fad8cba55000e374768685b7` (Developer-reported; not source-verified)

## Evidence completed this turn

- Retrieved and reviewed Developer handoff document `DEV-003.txt` from the user's Library. It identifies detailed Step 1 scope, source/build inventory, dependencies (BouncyCastle.Cryptography 2.7.0, SharpCompress 1.0.0), Windows NTFS write-through/flush/rename procedure, execution instructions, and limitations.
- Re-ran the separate independent offline fixture verification: **PASS**. 26 nonproduction signed-block vectors, SHA-512, Ed25519, 149-byte revocation consent, 88-byte AES-GCM AAD, `DDLB`, raw LZMA2, and expected negative semantics. These fixtures do not test the candidate implementation.
- Previously provided Developer acceptance ZIP: verified 57 reported PASS lines, 0 reported failures, successful release build and process exit 0. This is **Developer-supplied supporting evidence**, not independent execution.
- Prepared an independent native Windows PowerShell CLI runner that imports QA-owned signed vectors into a fresh nonproduction instance, tests genesis join, exact raw get and one-block `DDLB` export, late revoke/regrant, identity dispute, duplicate handling, invalid imports, configuration persistence/corruption, and missing-original-key restrictions. This runner is **prepared, not executed**; syntactic/runtime qualification must occur on Windows before treating its results as evidence.

## Execution blockers

1. This session runs Linux and has no `dotnet`/Windows PowerShell/NTFS execution capability, and checkout of the current GitHub commit failed due unavailable DNS/network access. The public GitHub page could not be fetched through the alternate browsing channel. No candidate source or compiled binary has been independently examined in this turn.
2. Developer handoff report is dated before final publication and states no push had yet occurred at handoff. That statement may be stale; the Developer subsequently reported availability, but QA could not independently verify repository synchronization or compiled artifact SHA.
3. Deterministic FI-01–FI-20 process-interruption checkpoints require coordinated production-disabled test hooks and native Windows 11 execution outside the Codex sandbox. The Developer report describes internal injected callbacks, but does not supply externally armable checkpoint documentation. No fault checkpoint or physical power-loss test was independently executed.
4. The reported earlier Avast `aswSnx.sys` crash remains Developer-attributed, not independently examined from dump/Event Viewer evidence.

## Required independent acceptance work

- Run and log the Windows QA-owned CLI runner on an isolated local NTFS location, using the exact candidate DLL and independent fixture JSON; record output evidence paths, assembly hashes, Git SHA, `dotnet --info` and Windows event evidence for any driver recurrence.
- Execute fault matrix FI-01–FI-20 after agreed reproducible hook handoff, preserving pre/post exact-byte hashes, durability acknowledgments and recovery-state JSON.
- Resolve or annotate ARCH-011 O-20 (schema versus authority pending), O-21 (backdated publishing), O-22 (config-corruption precedence), and O-23 (pending versus unauthorized when authority history incomplete) before asserting unambiguous oracles.
- Distinguish any runner expectation mismatch from a confirmed product defect until reproducible implementation-specific evidence is collected.

## Formal recommendation

**DEV-003 Step 1: NOT ACCEPTED — independent candidate execution and Windows filesystem qualification incomplete.** This is **not** a software FAIL finding. The verified independent offline fixtures and Developer logs provide readiness evidence, not independent product acceptance. Do not advance to Steps 2–4 on this basis.
