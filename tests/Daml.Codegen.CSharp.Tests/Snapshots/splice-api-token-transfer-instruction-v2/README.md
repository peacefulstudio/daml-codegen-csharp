# `splice-api-token-transfer-instruction-v2` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

The Token Standard V2 TransferInstruction / TransferFactory interface package
(CIP-0112).

## About `using` directives

Each generated file emits only the namespaces its body actually references,
tracked at codegen time. No generated file emits an unused `using`, so the
generated headers carry no `#pragma warning disable CS8019`.

## Refreshing the `expected/` snapshot

The `expected/` tree is the emitter's output for the vendored
`intermediate.binpb`. To refresh it after an intentional codegen change,
run from the repo root (POSIX shell; on Windows, use WSL or Git Bash):

```bash
scripts/refresh-snapshot.sh splice-api-token-transfer-instruction-v2
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-api-token-transfer-instruction-v2.dar` is the upstream Splice archive
it was derived from (`splice-api-token-transfer-instruction-v2-1.0.0.dar` from
the Splice `0.6.13` `splice-node` release tarball), kept alongside for
provenance. Do not regenerate either from a local build without a clear
reason. If the upstream Splice package genuinely needs to advance, replace the
files in place, refresh the `expected/` snapshot per the procedure above, and
call out the version bump in the pull request description.
