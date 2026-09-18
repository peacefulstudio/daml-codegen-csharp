# Conformance corpus

DAML models that define the type shapes codegen claims to support. Three corpora are
shipped (compiled + embedded) in `Daml.Codegen.Testing.Conformance`: `richtypes` below,
plus the contract-key and default-target corpora described further down.

The committed `.dar` is the source of truth for the package build. To grow the
corpus (new type shapes), edit `RichTypes.daml`, then rebuild and commit the DAR:

    cd conformance/richtypes && dpm build && cp .daml/dist/richtypes-*.dar ./richtypes.dar

After rebuilding the DAR, refresh the generated tree the package ships
(`scripts/refresh-conformance.sh`) and the pinned SHAs the determinism gate
compares against (`scripts/codegen-determinism.sh --update`); both read this DAR.

`RichRecord` covers the primitive, collection and nominal shapes; `TypeCorners`
covers the harder corners — parameterized records and variants (`Box`, `Slot`)
instantiated in a template payload, `GenMap` keyed by `Party` and by `Int`,
`Either`, `Tuple2`/`Tuple3`, the recursive record `Branch`, an `Optional` nested
directly inside an `Optional` (`maybeMaybeNote`) and one separated from its outer
`Optional` by an intervening record (`nestedNote`), an `Optional` over a record's
own type variable (`Crate`) and one carried by an `Either` arm (`noteOrRank`) —
positions C# nullable syntax cannot spell, so they emit the `Optional<T>` wrapper
instead — and the `Numeric` scale extremes 0 and 37. The `Holding` interface carries choices (`Describe`, `Reissue`, `Split`) as well as a
view, so the interface-choice emitter path is exercised through a real DAR rather
than a synthetic package; `Split` is nonconsuming and returns `[ContractId Holding]`,
so an interface choice whose result is itself a generic family is covered too.
`GenericResults` covers the same ground for a template's own choices, one choice per
top-level generic family: `ReturnContractIds` returns `[ContractId GenericResults]`;
`ReturnOptionalText` returns `Optional Text` driven by its own `wantSome` argument;
`ReturnTextMap` returns `TextMap Int`; `ReturnGenMap` returns `Map Text Int`;
`ReturnTuple` returns `(Text, Int)`; `ReturnEither` returns `Either Text Int` driven
by its own `wantRight` argument; `ReturnSet` returns `Set Int`; `ReturnNonEmpty`
returns `NonEmpty Int`; and `ReturnNestedOptional` returns `Optional (Optional Text)`
driven by its own `outer`/`inner` arguments. `ReturnGenMap`'s result is written with
`DA.Map`'s `Map` rather than the bare `GenMap` primitive on purpose: `Map` is a type
synonym for `GenMap`, not a distinct Daml-LF shape — `TypeCorners.quotaByParty` and
`.labelByRank` above already pin that erasure for `Map`-typed fields, and
`RichTypesCorpusDarCharacterizationTests` pins it again for this choice's result — so
one choice proves both the `GenMap` and the `Map` family from this corpus's
top-level-generic-family choice-return conformance requirement; a second choice under a
different Daml spelling would characterize the same `DamlTypeApp(GenMap, ...)` shape a
second time, not a different one. Every one of these choices renders as a C# constructed
generic type (`Either<long, string>`, `IReadOnlyDictionary<string, long>`, `Set<long>`,
`NonEmpty<long>`, `Optional<Optional<string>>`, ...); before it was fixed, that tripped a
defect in `ChoiceEmitter.NonContractExercisers.cs`'s
`WriteSingleNonContractChoiceAsyncExerciser`, which interpolated the raw return-type name
into an XML `<c>...</c>` doc-comment tag without escaping `<`/`>`/`,`, turning
`GenerateDocumentationFile` + `TreatWarningsAsErrors` (both repo-wide via
`Directory.Build.props`) into a `CS1570` build break for every one of them. Escaping the
interpolated type name fixed it, which is what unblocked this template-choice half of the
corpus; see the typed exercise-path choice-descriptor work (choice descriptors as witnesses,
`ArgumentDecoder` + interface-choice descriptors) for the wider follow-up.

`richtypes/daml.yaml` carries `-Wno-upgrade-interfaces` in its `build-options`:
at `--target=2.1`, a smart-contract-upgrade-eligible target, `damlc` refuses a
module that mixes interface definitions with the templates implementing them
(as `RichTypes.daml` does — `Holding` plus its implementers) unless that
warning is explicitly silenced. `contractkeys/daml.yaml` carries the same flag
for the same reason: `ContractKeys.daml` mixes the `Stewardship` interface
with its implementing template (`Steward`) too.

Contract keys are deliberately absent: they need a Daml-LF version above the
`--target` this corpus pins, and retargeting it would remove the 2.1 emit-path and
main-package read coverage *this corpus* pins — the vendored Splice snapshots under
`tests/Daml.Codegen.CSharp.Tests/Snapshots/` keep their own 2.1 main packages either way. The
2.1 dependency read floor stays regardless: at the SDK version pinned here, it is held by the
twenty-seven per-module `daml-prim`/`daml-stdlib` component packages a DAR bundles whatever
its target, not by this corpus. So keys live in their own package below.
`RichTypesCorpusDarCharacterizationTests` asserts both the pin and the absence of
keyed templates, so moving the pin or adding a keyed template here has to be a
deliberate act.

The `contractkeys` package carries the contract-key shapes, and pins
`--target=2.3` because neither 2.1 nor 2.2 can express a contract key at all. It
is compiled and shipped by `Daml.Codegen.Testing.Conformance` alongside
`richtypes`, and its DAR is reachable from
`ConformanceCorpus.OpenDar(ConformancePackage.ContractKeys)`. The six key shapes
it covers are the ones real key-bearing Daml packages use: a record built from
several payload fields (`Account`), a record whose field comes from a projection
nested inside a payload record (`Holiday`), a record built by a function declared
in another module, sharing no field name with the payload (`Schedule`), a
bare `Party` key whose maintainer clause names the key binder itself
(`Steward`), a tuple key `(Party, Text)` (`Membership`), and a tuple key with an
Optional-family component, `(Party, Optional Text)` (`Enrollment`) — both derive
their maintainer from the key's first component (`key._1`). `Steward` also
implements the `Stewardship` interface, which declares both a `viewtype`
(`StewardshipView`) and a non-`Unit` nonconsuming choice (`DescribeCharter`),
so a keyed template backing an interface with a real choice is covered too.
`ContractKeysCorpusDarCharacterizationTests` reads the DAR and
asserts each of them, so a fixture edit that flattens a shape fails rather than
quietly narrowing the evidence. Rebuild its DAR the same way:

    cd conformance/contractkeys && dpm build && \
      cp .daml/dist/contractkeys-*.dar ./contractkeys.dar

`contractkeys` is a shipped package, so rebuilding its DAR also requires
refreshing the generated tree (`scripts/refresh-conformance.sh`) — without that
the `Generated/ContractKeys/` sources the package compiles go stale silently.
The determinism gate reads only `richtypes`, so `codegen-determinism.sh` does
not need re-running for a contract-key-only change.

The `crossmodulecollision` package is a codegen-drift regression
fixture rather than a type-shape corpus: two modules (`CollisionA`,
`CollisionB`) each declare a same-named `Retag` choice-argument record with a
different field list, guarding against a cross-module simple-name collision in
the emitter. Its generated output is pinned by the `cross-module-collision`
drift snapshot under `tests/Daml.Codegen.CSharp.Tests/Snapshots`; rebuild its
DAR the same way:

    cd conformance/crossmodulecollision && dpm build && \
      cp .daml/dist/crossmodulecollision-*.dar ./crossmodulecollision.dar

That snapshot regenerates from its own vendored copy of this DAR, not from the
one here, so a rebuild is only half the job: copy the result over
`tests/Daml.Codegen.CSharp.Tests/Snapshots/cross-module-collision/cross-module-collision.dar`
and refresh the snapshot (`scripts/refresh-snapshot.sh cross-module-collision`).
Nothing enforces that the two copies agree — skip the copy and the snapshot goes
on regenerating cleanly from the stale one.

The `defaulttarget` package is a single boring template whose only job is
to be compiled with **no `build-options:` block at all**. `damlc` picks a
Daml-LF target when a project does not request one, and it announces that
choice nowhere, so a scaffolded project hands the toolchain a version the rest
of this corpus never exercises. The omission is load-bearing: adding
`--target=` here — to any version, including the one currently emitted — turns
the fixture into a restatement of the pin and stops it tracking the default.
`DefaultTargetDarCharacterizationTests` reads this DAR off disk and asserts the
version it actually carries, so a moved default surfaces as a failing
assertion rather than as silence. It is a shipped package too: the emitter runs
over it and `Daml.Codegen.Testing.Conformance` compiles and ships the result, so
the default target is covered on the emit path and not only on the read one.
Rebuild its DAR the same way:

    cd conformance/defaulttarget && dpm build && \
      cp .daml/dist/defaulttarget-*.dar ./defaulttarget.dar

Like `contractkeys`, rebuilding this DAR also requires refreshing the generated
tree (`scripts/refresh-conformance.sh`). The determinism gate reads only
`richtypes`, so `codegen-determinism.sh` does not need re-running for a
default-target-only change.

The decision record for the conformance package and its live-ledger gate is
kept in the project's internal ADR collection.
