# `cross-module-collision` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

Cross-module collision regression fixture. The package has two modules,
`CollisionA` and `CollisionB`, that each declare a choice named `Retag` — so
both generate a choice-argument record with the same simple name `Retag` but a
different field list (`CollisionA` carries `newOperator : Party`; `CollisionB`
carries `label : Text` and `count : Int`). The emitter keys its package-wide
data-type table by module-qualified name; this snapshot pins that the two
`Retag` records keep their own distinct fields. Regressing to a simple-name key
makes one record inherit the other's fields — a silent field-drop that
compiles but sends the wrong argument to the ledger — and this snapshot fails.

## The vendored input

`intermediate.binpb` is the canonical codegen input for the drift test. It is
derived from `conformance/crossmodulecollision/crossmodulecollision.dar`, whose
Daml source lives in `conformance/crossmodulecollision`. To regenerate the
proto after a corpus change, rebuild the DAR (`cd
conformance/crossmodulecollision && dpm build && cp
.daml/dist/crossmodulecollision-*.dar ./crossmodulecollision.dar`), then rerun
the JVM helper (`java -jar
jvm-helper/target/scala-2.13/daml-codegen-jvm-helper.jar --dar
conformance/crossmodulecollision/crossmodulecollision.dar --out
tests/Daml.Codegen.CSharp.Tests/Snapshots/cross-module-collision/intermediate.binpb`),
and refresh the `expected/` snapshot below.

## Refreshing the `expected/` snapshot

The `expected/` tree is the emitter's output for the vendored
`intermediate.binpb`. To refresh it after an intentional codegen change, run
from the repo root (POSIX shell; on Windows, use WSL or Git Bash):

```bash
scripts/refresh-snapshot.sh cross-module-collision
```
