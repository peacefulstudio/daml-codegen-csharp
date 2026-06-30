# `splice-amulet-name-service` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

Concrete-template fixture, complementing the interface-only
`splice-api-token-holding-v1` snapshot. It is the smallest amulet-family DAR
and exercises the codegen paths the interface-only fixture cannot reach:

- Concrete `template ... where` declarations (`AnsEntry`, `AnsEntryContext`,
  `AnsRules`, `AmuletConversionRateFeed`).
- Non-Unit choices whose typed `<Choice>Result` records carry contract IDs
  (e.g. `AnsRules_RequestEntry`, `AnsEntryContext_CollectInitialEntryPayment`),
  driving the `<Choice>Result` projector, the `FromCreatedContracts` factory,
  and the `<Choice>Async(this ContractId<T>, ...)` extension.
- A `Numeric n` field mapped to `decimal` (`AnsRulesConfig.EntryFee`).
- Cross-family references to the `splice-amulet` package, exercising the
  qualified-reference emission path without emitting the referenced package.

It also pins a pre-existing emitter limitation: a few `<Choice>Result` records
(e.g. `AnsRules_RejectEntryInitialPaymentResult`) reference a generic type
applied across a package boundary (`Splice.Amulet.AmuletCreateSummary<...>`),
for which `FromRecord` currently emits a `default(...)!` placeholder with a
`TODO: Implement deserialization`. That is the emitter's current behaviour, not
specific to this fixture — the larger `splice-amulet` DAR emits the same
pattern. Pinning it here puts the path under drift detection, so the snapshot
flags the day the emitter learns to deserialize it.

Together with the holding snapshot this guards the concrete-template,
typed-choice-result, and decimal-mapping surfaces against formatting,
member-ordering, XML-doc, and `using`-directive drift that the does-it-compile
`EmittedCodeCompilesTests` would let through.

## About `using` directives

Each generated file emits only the namespaces its body actually references,
tracked at codegen time. No generated file emits an unused `using`, so the
generated headers carry no `#pragma warning disable CS8019`.

## Refreshing the `expected/` snapshot

The `expected/` tree is the emitter's output for the vendored
`intermediate.binpb`. To refresh it after an intentional codegen change,
run from the repo root (POSIX shell; on Windows, use WSL or Git Bash):

```bash
scripts/refresh-snapshot.sh splice-amulet-name-service
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-amulet-name-service.dar` is the upstream Splice archive it was derived
from (the `splice-amulet-name-service-current.dar` from the Splice `0.6.9`
`splice-node` bundle), kept alongside for provenance. Do not regenerate either
from a local build without a clear reason. If the upstream Splice package
genuinely needs to advance, replace the files in place, refresh the `expected/`
snapshot per the procedure above, and call out the version bump in the pull
request description.
