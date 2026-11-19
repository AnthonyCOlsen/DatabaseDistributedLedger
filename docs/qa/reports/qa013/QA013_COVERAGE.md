# QA-013 corruption coverage

Candidate: `8bc96f0f513faac57c1db23a8189288479d8a992` (Protocol 1.03 / ARCH-014).
This supplements QA-010 through QA-012 without rerunning or changing their assertions.
Execution results live in `QA013_REPORT.md` and each immutable run's receipts.

## Existing coverage and additions

| Area | Existing evidence | New QA-013 coverage |
| --- | --- | --- |
| Malformed incoming wire, hash/signature/framing, unknown versions | QA-010 independent vectors and actual `WIRE-NEG-*` imports | Reuse the oracle; new deterministic damage is applied to actual on-disk candidate-created signed blocks, with a separate healthy receiver to distinguish parser rejection from local-history failure. |
| Fully flushed temporary block and explicit retry fencing | QA-010 FI-06 and QA-012 before-publication termination | New `CORRUPT-TEMP-*`: positively observe existing pre-rename callback, authenticate original, externally terminate, damage surviving temp, restart/probe/retry/import. No repeated full QA-012 qualification campaign. |
| Accepted `.BLK` bytes, idempotent export/get | QA-010 EXPORT and QA-012 exact-byte recovery | New `CORRUPT-ACCEPTED-*`: confirm durable publication, preserve independent original reference, mutate bytes while retaining original SHA-512 filename, restart and probe real CLI/library paths. |
| Identity/configuration corruption | QA-010 IDENTITY/CONFIG cases | Not repeated; original key, manifest, binding and unrelated valid block hashes are preserved in every new corruption case. |
| Recovery policy for damaged accepted history | Not fully specified | `POLICY-ACCEPTED-STATE` and `POLICY-ACCEPTED-REPAIR` are BLOCKED/SPEC-PENDING. Actual codes/state/gating are observations, not newly invented contracts. |

## Deterministic mutations

All offsets are zero-based. Each original has a 163-byte header, a 17-byte DROP_TABLE
payload and a 64-byte Ed25519 signature, totaling 244 bytes. Locally authored timestamps
remain candidate-generated current UTC; no historical-clock override or re-signing
is used. Explicit test GUIDs are deterministic UUIDv5 identifiers.

| Pattern | Damage | Independent integrity boundary |
| --- | --- | --- |
| truncate-1 | Remove last 1 byte | Exact total length |
| truncate-7 | Remove last 7 bytes | Exact total length |
| truncate-64 | Remove entire 64-byte signature | Minimum/framed length |
| truncate-into-header | Leave exactly 162 bytes | Complete 163-byte header/minimum signed frame |
| zero-length | Replace with empty bytes | Minimum frame |
| flip-header | XOR 1 at offset 50 in signed timestamp | Ed25519 authentication |
| flip-payload | XOR 4 at offset 171 in transmitted payload | Payload SHA-512/authentication |
| flip-hash | XOR 128 at offset 90 in stored payload digest | Payload SHA-512 |
| flip-signature | XOR 32 at offset `original_length - 17` | Ed25519 authentication |
| append-1 | Append `00` | Exact framing/trailing rejection |
| append-8 | Append `51 41 30 31 33 00 ff 7f` | Exact framing/trailing rejection |

`QA013_FIXTURES.json` stores portable deterministic original/damaged bytes, full
SHA-512s, exact mutation offsets/lengths and independent reference errors.
`QA013_MUTATIONS.py` validates the original plus eleven negatives: **12 independent
reference checks**, distinct from candidate execution. It uses the existing public
NONPRODUCTION QA identity seeds, never real signing keys.

Each of eleven patterns is applied separately to a surviving temp and a previously
accepted file: **22 candidate cases**. The focused first run selects truncate-1,
flip-payload and append-8 for both locations: six cases; the other sixteen are NOT-RUN.

## Candidate observations and assertions

- Confirm and independently authenticate original bytes, signing key, block GUID,
  original full signed SHA-512 and publication location before damaging them.
- For temps, confirm positive READY PID/pre-rename callback and absent final path,
  then externally terminate the specific worker before modifying its remnant.
- Candidate `BlockCodec.Parse` is exercised through a separate QA-owned portable
  .NET probe, against a frozen library; Python supplies the expected rejection.
- Each restart/probe is a fresh process. Exercise `status`, `instance status`, get by
  ID and ID+original digest, full/selected DDLB export, unrelated valid get, library
  `Snapshot`, pinned `recover-genesis`, ordinary exact-GUID retry and damaged import.
  There is no standalone production `block verify` command; no nonexistent command
  is passed off as verification coverage.
- A successful get/export must contain independently authenticated previously valid
  original signed bytes. A failed output must not publish a final output file.
- Damaged bytes must be rejected by the candidate parser and on a fresh healthy
  receiver, with no receiver filesystem mutation. Errors/codes are recorded rather
  than pinned to an invented accepted-file recovery diagnostic.
- Preserve the corrupted diagnostic target, unrelated valid history, original
  identity/trust, separate original byte fixture and signed-file names. Never use
  a SHA-512 suffix as an integrity oracle. No corruption repair/deletion/re-signing
  is attempted by QA.
- Record exact before/after inventories and every command's stdout/stderr/exit.

## Scope limits

Python mutations/fixtures/oracle and the new C# parse probe use portable facilities.
The current production storage backend requires Windows fixed NTFS, so candidate
execution is presently Windows qualification. Process termination and publication
coordination remain in the existing QA adapter; no new production hooks are needed.
Linux/macOS candidate execution is NOT-RUN/unqualified. Portable source is not a
support claim. No OS internals, physical storage, power cuts, random media faults,
privileged attack or later Step 2–4 behavior is tested or certified.

Accepted-genesis corruption, `.XBLK` corruption, missing/renamed files, concurrent
external mutation during reads, adversarial re-signing and exhaustive fuzzing are
not covered by this focused assignment. Ordinary signed-block truncation/empty/
bit-flip/append invariants are covered; no formal Step 1 acceptance is declared.
