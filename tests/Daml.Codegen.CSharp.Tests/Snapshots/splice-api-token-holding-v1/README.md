# `splice-api-token-holding-v1` codegen snapshot

`DriftDetectionTests` regenerates C# from `splice-api-token-holding-v1.dar` and
asserts byte-equal output against the `expected/` tree. When a codegen change
legitimately alters the generated output, refresh the snapshot.

## Why this DAR

Interface-only fixture: one Daml interface (`Holding`) plus a handful of
records (`HoldingView`, `InstrumentId`, `Lock`); no concrete templates, no
contract keys. Small enough to keep snapshot diffs reviewable, no
cross-family cycles. This DAR is **not** a comprehensive feature-coverage
fixture — paths added by recent runtime work (#65 partial-property contract
Key, #88 typed `WitnessParties`, #89 typed `SynchronizerId` reassignment
fields) are exercised by `EmittedCodeCompilesTests` and the per-feature
shape tests, not by this snapshot.

## About `using` directives

Each generated file emits only the namespaces its body actually references,
tracked at codegen time. The `#pragma warning disable CS8019` pragma that
previously appeared in every generated header has been removed — it is no
longer needed because no file emits an unused using (issue #102).

## Refreshing the `expected/` snapshot

Run from the repo root. This script requires a POSIX shell; on Windows, use
WSL or Git Bash rather than plain PowerShell/cmd:

```bash
<run the snapshot refresh script for splice-api-token-holding-v1>
```

## Refreshing the DAR itself

The `.dar` is vendored as the canonical input — do not regenerate it from a
local `daml build` without a clear reason. If the upstream Splice package
genuinely needs to advance, replace the file in place, refresh the
`expected/` snapshot per the procedure above, and call out the version bump
in the PR description.

`SpliceDarCharacterizationTests` (in `Daml.Codegen.DarParser.Tests`) pins two
SHA-256 package-id constants tied to this exact DAR — the main package id
asserted in `main_package_id_is_the_archive_content_hash` and the
`MetadataPackageId` constant for the imported `splice-api-token-metadata-v1`
dependency. Both are derived from the checked-in bytes and must be refreshed
together with the `.dar` whenever the fixture is regenerated.
