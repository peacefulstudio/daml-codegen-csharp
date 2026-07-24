# `splice-token-standard-utils` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree.

This package emits no C# types, so its snapshot is pinned by the
`emits-no-types` marker file in this directory: the drift test asserts that
codegen produces zero `.cs` output (only the `.daml-langversion` state file the
codegen always writes). The day the package starts emitting a type, the drift
test fails, so the change is caught rather than shipped unnoticed.

## Why this DAR

The Token Standard V2 helper library — a non-`vN` package (version 2.0.0). It
is a library of Daml functions and re-exports of types that are owned by the V2
API packages, so it defines no serializable data types of its own and the C#
codegen emits nothing for it. Pinning it as an `emits-no-types` snapshot puts
that fact under drift detection: if a future Splice release adds a
codegen-visible type to the library, or an emitter change starts emitting one,
the drift test flags it instead of letting the new surface ship unnoticed.

## The `emits-no-types` marker

The `emits-no-types` file in this directory marks the snapshot as expecting
zero generated `.cs`. When a package legitimately gains a codegen-visible type,
remove the marker and refresh the `expected/` snapshot so it pins the emitted
types instead.

## Refreshing the `expected/` snapshot

The `expected/` tree is the emitter's output for the vendored
`intermediate.binpb`. To refresh it after an intentional codegen change,
run from the repo root (POSIX shell; on Windows, use WSL or Git Bash):

```bash
scripts/refresh-snapshot.sh splice-token-standard-utils
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-token-standard-utils.dar` is the upstream Splice archive it was
derived from (`splice-token-standard-utils-2.0.0.dar` from the Splice
`0.6.13` `splice-node` release tarball), kept alongside for provenance. Do not
regenerate either from a local build without a clear reason. If the upstream
Splice package genuinely needs to advance, replace the files in place, refresh
the `expected/` snapshot per the procedure above, and call out the version bump
in the pull request description.
