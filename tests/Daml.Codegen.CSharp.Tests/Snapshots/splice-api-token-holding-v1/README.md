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

POSIX shell (run from the repo root). Windows maintainers should use WSL or
adapt the `rm -rf` step to PowerShell `Remove-Item -Recurse -Force`.

```bash
# 1. Build the codegen tool from the current source tree.
dotnet build src/Daml.Codegen.CSharp -c Release

# 2. Wipe the previous expected output so removed files don't linger.
rm -rf tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-api-token-holding-v1/expected

# 3. Regenerate. `dotnet run --project ...` is preferred over invoking the
#    DLL directly so the path is target-framework-agnostic.
dotnet run --project src/Daml.Codegen.CSharp -c Release -- \
  tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-api-token-holding-v1/splice-api-token-holding-v1.dar \
  -o tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-api-token-holding-v1/expected \
  --target-framework net10.0 --verbosity 1

# 4. Verify the test passes against the new snapshot.
dotnet test --filter FullyQualifiedName~DriftDetectionTests

# 5. Stage the new expected/ tree (newly-emitted files are untracked) and
#    commit alongside the codegen change so the diff is reviewable in one PR.
git add tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-api-token-holding-v1/expected
```

## Refreshing the DAR itself

The `.dar` is vendored as the canonical input — do not regenerate it from a
local `daml build` without a clear reason. If the upstream Splice package
genuinely needs to advance, replace the file in place, refresh the
`expected/` snapshot per the procedure above, and call out the version bump
in the PR description.
