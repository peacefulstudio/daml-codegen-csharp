# Quickstart sample codegen input

`QuickstartSampleDriftTests` regenerates C# from the vendored
`intermediate.binpb` (the `IntermediateDar` proto for the `quickstart`
package) and asserts byte-equal output against the committed showcase sample
at `samples/QuickstartExample/Generated`. This puts the showcase sample under
the same drift detection as the snapshot fixtures, so it fails CI the day it
falls behind emitter output instead of silently rotting.

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift gate;
`quickstart-0.0.1.dar` is the archive it was derived from, built from
`samples/QuickstartExample/daml` (SDK 3.5.2, LF target 2.1), kept alongside
for provenance.

## Refreshing the sample

When an emitter change legitimately alters the generated output, refresh the
committed sample (not this fixture) from the repo root:

```bash
scripts/refresh-quickstart-sample.sh
```

## Rebuilding the proto

The proto only changes when the Quickstart `.daml` source or the JVM helper
changes. To rebuild it, build the DAR and re-run the helper from the repo root:

```bash
(cd samples/QuickstartExample/daml && dpm build)
java -jar jvm-helper/target/scala-2.13/daml-dar-to-proto.jar \
  --dar samples/QuickstartExample/daml/.daml/dist/quickstart-0.0.1.dar \
  --out tests/Daml.Codegen.CSharp.Tests/QuickstartSample/intermediate.binpb
cp samples/QuickstartExample/daml/.daml/dist/quickstart-0.0.1.dar \
  tests/Daml.Codegen.CSharp.Tests/QuickstartSample/quickstart-0.0.1.dar
```

Then refresh the sample with `scripts/refresh-quickstart-sample.sh` and call
out the change in the pull request description.
