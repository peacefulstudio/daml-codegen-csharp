# `splice-api-token-holding-v1` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

Interface-only fixture: one Daml interface (`Holding`) plus a handful of
records (`HoldingView`, `InstrumentId`, `Lock`); no concrete templates, no
contract keys. Small enough to keep snapshot diffs reviewable, no
cross-family cycles. This DAR is **not** a comprehensive feature-coverage
fixture — paths such as the partial-property contract `Key`, typed
`WitnessParties`, and typed `SynchronizerId` reassignment fields are exercised
by `EmittedCodeCompilesTests` and the per-feature shape tests, not by this
snapshot.

## About `using` directives

Each generated file emits only the namespaces its body actually references,
tracked at codegen time. No generated file emits an unused `using`, so the
generated headers carry no `#pragma warning disable CS8019`.

## Refreshing the `expected/` snapshot

The `expected/` tree is the emitter's output for the vendored
`intermediate.binpb`. To refresh it after an intentional codegen change,
run from the repo root (POSIX shell; on Windows, use WSL or Git Bash):

```bash
scripts/refresh-snapshot.sh splice-api-token-holding-v1
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-api-token-holding-v1.dar` is the upstream Splice archive it was
derived from, kept alongside for provenance. Do not regenerate either from a
local build without a clear reason. If the upstream Splice package genuinely
needs to advance, replace the files in place, refresh the `expected/` snapshot
per the procedure above, and call out the version bump in the pull request
description.
