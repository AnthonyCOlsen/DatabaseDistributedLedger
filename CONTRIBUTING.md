# Contributing to UDDL

Created and directed by Anthony Charles Olsen (Tony Olsen), developed with AI
assistance. Contributions are welcome under the project's [MIT license](LICENSE).
Retain others' notices and do not assign their copyright to the Owner. These
contribution practices add no restrictions to the license's permissions to use,
modify or redistribute the software.

## Scope and responsibilities

Follow the current [authoritative architecture documents](docs/architecture/)
and approved assignment scope. Protocol bytes, serialization, authorization,
CLI/JSON errors and durability guarantees are contracts; escalate conflicts
before changing behavior. Do not implement later phases without authorization.

Use the [repository layout guide](docs/development/REPOSITORY_LAYOUT.md):

- `src/`: production implementation; Developer-owned.
- `tests/UserDimensions.Ledger.Tests/`: Developer regressions.
- `tests/IndependentQA/`: QA-owned suites, fixtures, reference tools and adapters.
- `docs/architecture/`: Architect-owned normative designs.
- `docs/qa/`: QA-owned plans, reports and selected public evidence.
- `docs/development/`: current implementation guides; `docs/archive/`: history.
- `artifacts/`: ignored, reproducible output; `local/qa/`: ignored, retained
  receipts and synthetic instances; `local-notes/`: ignored personal notes.

Both Developer and QA may run all suites. Coordinate edits to another role's
contracts or assertions; do not weaken tests to match an implementation defect.
Preserve historical evidence. Ignored receipts require a separate backup plan.

## Coding conventions

Apply [.editorconfig](.editorconfig) incrementally to new or edited code. Keep
changes focused; do not reformat unrelated files or run a blanket formatter.
The configuration provides editor formatting preferences without adding build
analyzer rules or changing existing warning policies. Preserve existing line
endings unless an intentional conversion is part of the reviewed change.
Formatting references: the [EditorConfig specification](https://spec.editorconfig.org/)
and Microsoft's [.NET style configuration guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-style-rule-options).

- **C#/.NET:** use four spaces, existing brace/namespace conventions and normal
  .NET naming: PascalCase for types/public members, camelCase for parameters and
  locals. Match nearby private-field conventions. Use meaningful names, nullable
  annotations and deterministic disposal of owned resources. Use `var` where the
  type is clear; favor explicit types when they aid understanding. Avoid blocking
  asynchronous work or changing execution order as an incidental cleanup.
- **C and headers:** follow surrounding SQLite style, normally two spaces and
  its existing brace, naming and comment conventions. Use supported portable C
  constructs and established SQLite types/helpers. Check allocation, bounds,
  integer conversions, ownership and return codes. Keep declarations and headers
  consistent; modify canonical sources and regenerate derived files through
  documented tooling instead of hand-editing generated amalgamations.
- **Design:** apply SOLID pragmatically: give components clear responsibilities,
  keep methods readable, and separate policy from I/O. Extract a method or class
  when it clarifies a responsibility; avoid arbitrary size limits, unnecessary
  interfaces and speculative frameworks. Preserve observable behavior during
  refactoring and review changes to side effects or failure ordering explicitly.
- **Portability:** keep protocol, cryptography, serialization, authorization,
  replay and database logic platform-independent where practical. Isolate OS
  filesystem, locking and durability behavior behind replaceable adapters.
  Document necessary exceptions, supported/untested platforms and qualification
  limits. Windows qualification does not establish Linux/macOS support.

## Errors, comments and security

Validate untrusted input and resource bounds before persistence. Preserve
fail-closed gates, diagnostic precedence, truthful durability acknowledgments and
the original identity/trust binding. Do not swallow errors or report uncertain
operations as successful. Include useful diagnostic context without exposing
signing secrets, private instance data or sensitive paths unnecessarily.

Explain intent, invariants, ordering and non-obvious tradeoffs in comments.
Keep comments accurate and use API documentation for public contracts; avoid
comments that merely repeat statements. Never commit real keys or generated
instances. Use synthetic fixtures and isolated test roots. Review dependencies,
licenses and security effects, including concurrency, native interop and recovery.

## Testing and review

Use the [documented build/test commands](docs/development/REPOSITORY_LAYOUT.md#build-and-test).
Add focused Developer regressions for changed production behavior. Run applicable
existing suites and independent reference checks; use ordinary Windows execution
when filesystem acceptance requires it. Keep candidate tests distinct from
reference-fixture validation. Do not present process termination as physical
power-loss certification, or unexecuted cases as passing.

For a pull request or reviewed direct commit, describe the problem, scope,
resulting behavior, affected contracts and portability/security implications.
Record exact test commands, results, candidate revision/hashes when relevant and
remaining failures or gaps. Review code and tests before approval. Preserve
existing history, coordinate concurrent work, stage only intended files and use
normal non-force publication. Follow Owner/PM acceptance gates; Developer tests
alone do not constitute independent QA or Owner acceptance.

## Human and AI collaboration

AI-assisted contributions are welcome. OpenAI ChatGPT and Codex have assisted
with architecture, implementation, testing, documentation and review. Humans
remain accountable for the changes they submit and approve, including checking
generated code, dependencies, licensing, security and reported test evidence.
Apply the same quality, security and testing standards to all contributions.

Disclose substantive AI assistance in the commit message or pull request where
appropriate. An optional trailer may name the tool:

```text
Assisted-by: OpenAI Codex
```

Preserve truthful Git author and committer identities. Do not invent human
contributors, attribute human review that did not occur, or present an AI tool
as a legal copyright holder or independent human contributor. Do not rewrite
historical commits to add attribution. AI disclosure is a contribution practice,
not an additional license restriction on downstream use.

## SQLite fork policy

`third_party/sqlite/` is an intentionally maintained, modifiable UDDL fork.
The imported SQLite 3.54.1 baseline is preserved in Git through
`sqlite/upstream-3.54.1` and the [provenance record](docs/upstream/SQLITE_PROVENANCE.md).
Preserving that reference does not freeze the working fork. Approved UDDL work
may modify, reorganize or reformat upstream-derived code when justified; explain
the scope and tradeoff, preserve applicable notices and keep changes reviewable.
Repository formatting preferences are not a prohibition on such changes.

Record significant downstream modifications and validation in
[SQLITE_MODIFICATIONS.md](docs/upstream/SQLITE_MODIFICATIONS.md); follow the
[upstream review/port log](docs/upstream/SQLITE_UPGRADE_LOG.md) for selected fixes.
Preserve upstream portability and tests wherever practical. Do not alter upstream
manifests to conceal changes. SQLite engine public-domain material and bundled
tools with separate licenses retain their provenance and terms; the Owner's MIT
notice covers qualifying original UDDL contributions, not ownership of others'
material. This policy grants no implementation authorization by itself.
