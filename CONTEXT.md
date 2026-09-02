# Daml C# Codegen

Generates strongly-typed C# from Daml `.dar` archives. The codegen pipeline splits across two
runtimes: a JVM-side helper wraps `daml-lf-archive` to decode a DAR into an intermediate
representation, and a .NET-side emitter consumes that representation and writes idiomatic
C#. End-user applications consuming generated code have no JVM dependency.

## Language

**Intermediate AST** (IR):
The protobuf message exchanged between the JVM helper and the C# emitter. Mirrors the
shape of `Ast.PackageSignature` from `daml-lf-archive` but is owned by this repo so
upstream renames cannot break the wire.
_Avoid_: "intermediate JSON", "AST blob", "IR JSON"

**Intermediate DAR**:
The top-level `IntermediateDar { main, dependencies }` IR message for a single `.dar`. The
on-disk artefact handed from the JVM helper to the C# emitter.
_Avoid_: "decoded DAR", "AST file"

**JVM helper**:
The Scala binary that reads a `.dar` via `daml-lf-archive` and emits an Intermediate DAR.
Runs only at codegen time — never at application runtime. Coupling to `daml-lf-archive`
is confined to this binary. Shipped as a JAR inside the `dpm codegen-cs` bundle and
executed against the host JDK (a dpm install precondition). The helper ships
inside that OCI bundle — its source is not part of the public repository. It is
also packaged standalone as `daml-dar-to-proto`: GitHub releases will attach
the runnable jar and the Intermediate DAR proto schema, so non-C# SDKs can run the
JVM helper directly to turn a `.dar` into an Intermediate DAR.
_Avoid_: "Scala helper", "decoder service", "ast extractor"

**AstToIntermediate translator**:
The single function inside the JVM helper that maps
`Dar[(PackageId, Ast.PackageSignature)]` to `IntermediateDar`. The only place in the
project that depends on DA-internal Scala case-class shapes; everything else depends on
the Intermediate AST.
_Avoid_: "AST converter", "Scala-to-proto mapper"

**C# emitter**:
The .NET binary that consumes an Intermediate DAR and writes `.cs` files. Implemented by
`CSharpCodeGenerator`, which walks the model that `IntermediateDarReader` builds from the
`IntermediatePackage` proto.
Shipped as a self-contained, single-file .NET binary inside the `dpm codegen-cs` bundle —
one per target RID, with the .NET runtime statically bundled — so consumers do not need
a host .NET runtime to run codegen. **DPM does not embed the emitter**: DPM is the
dispatcher (Go binary), the OCI bundle's entrypoint spawns the emitter as a child process
(alongside the JVM helper), and the two communicate via the on-disk Intermediate DAR
proto file. The emitter is not a DLL and is never loaded into DPM's address space.
_Avoid_: "the codegen", "generator" (both ambiguous between JVM helper + C# emitter),
"the emitter DLL", "the plugin DLL", "the in-process emitter"

**Schema-mode decode**:
Using `Decode.decodeArchivePayloadSchema` (returns `Ast.PackageSignature`) rather than
`decodeArchivePayload` (returns full `Ast.Package`). Strips expressions and choice bodies,
and is **patch-version-insensitive** — two patch-different versions of the same package
produce identical Intermediate ASTs.
_Avoid_: "signature decode", "lite parse"

**`dpm codegen-cs`**:
The dpm component that runs the codegen pipeline end-to-end: invokes the bundled JVM
helper on the input DAR, hands the Intermediate DAR to the bundled C# emitter, writes
`.cs` to the configured output directory. Distributed as a multi-platform OCI artifact
(`linux/amd64`, `linux/arm64`, `darwin/arm64`, `windows/amd64`); stock `dpm` fetches the
right RID lazily on first invocation (requires `DPM_AUTO_INSTALL=true`) and dispatches
to its launcher at `dpm codegen-cs` invocation time. Users opt in by listing every
component they need — SDK ones and `codegen-cs` — under `components:` in `daml.yaml`,
with no `sdk-version` key (the two are mutually exclusive by upstream design).
The toolchain's supply chain is the OCI registry plus stock dpm — the codegen
toolchain is not distributed as a `dotnet tool`, a Docker image, or a NuGet
package. (The `Daml.Codegen.CSharp` emitter library is separately published
as a NuGet library for programmatic use.)
_Avoid_: "the cli", "codegen-cs tool", "codegen-cs plugin", "the container"

**`PackageEmitContext`**:
The immutable per-package value the C# emitter threads through its emit methods: the root
namespace, the `TypeReferenceQualifier`, the per-package data-type lookup, and the local
enum / variant / interface / choice-argument name sets. Built once per package by
`PackageEmitContext.ForPackage`; read-only during emission. Replaces the mutable `_current*`
/ `_local*` instance fields the emitter used to clear at the start of each package.
_Avoid_: "codegen state", "the current-package fields", "emit scratch"

**`CrossPackageResolver`**:
The DAR-scoped module (`ICrossPackageResolver`) that resolves a `DamlTypeRef` to a C# name.
It owns the archive lookup, the foreign-choice-argument memo, and the set of external package
ids it has discovered — read after emission to emit a `<PackageReference>` per id. Lives for
one `Generate` call. Replaces `ResolveTypeRefName` plus the `_currentArchive` /
`_foreignChoiceArgCache` / `_externalPackageIds` instance fields. The prod adapter
(`DarCrossPackageResolver`) resolves against an `IDarSource`; tests use a canned stub.
_Avoid_: "type resolver", "package resolver service", "the cross-package cache"

**`PartyAnalysis`**:
The pure module that reasons about a template's parties: classifying controller / signatory /
observer sets as statically-resolvable or `Dynamic`, unioning them, partitioning them into
controller-params and observer-only-params, and validating a `DamlPartyAnalysis` against the
real template fields. Shared dependency of `ChoiceEmitter` and `SubmissionExtensionsEmitter`;
party sets in, partitioned params out, so it is trivially unit-testable.
_Avoid_: "party helper", "the party utils", "controller logic"

**`DamlTypeMapper`**:
The module that turns a `DamlType` into C#: `MapType` (→ a C# type name), `ToValue` and
`FromValue` (→ serialize / deserialize expressions). An instance constructed per package over a
`PackageEmitContext` and an `ICrossPackageResolver`, which it calls into for cross-package names
— it does not own resolution. Pure functions of its inputs: `DamlType` in, C# fragment out, with
a trivially-constructible context, so it is unit-testable without a real DAR. Extracted from the
emitter's `MapDamlTypeToCSharp` / `GetToValueConversion` / `GetFromValueConversion` once
`PackageEmitContext` exists.
_Avoid_: "type converter", "the mapping switch", "serializer"

**`SubmissionExtensionsEmitter`**:
The module that emits the template *create / submission* path — `CreateAsync`, the optional
`Observers(payload)` helper, and the `SubmissionExtensions` class — deriving signatories and
observers from the payload via `PartyAnalysis`. Distinct from `ChoiceEmitter`: creating a
contract is not exercising a choice. Extracted from the `NamedSubmitters` partial.
_Avoid_: "submitter", "the create wrapper", "named-submitter partial"

**`ChoiceEmitter`**:
The module that emits the C# to *exercise* a choice: the `<Choice>Arg` fallback type, the
`Choice<Template, Arg, Result>` descriptor with its result decoder, the typed `<Choice>Async`
exercisers (both the contract-id-returning and the value-returning flavour, kept as private
detail of one home — not pre-split), and the interface-choice extensions. An instance
constructed per package over a `PackageEmitContext`, an `ICrossPackageResolver`, the package's
`DamlTypeMapper`, and the shared `PartyAnalysis`; methods take `(IndentWriter, template/interface)`.
It *calls* the mapper for every type fragment and *reads* — does not own — the resolved
choice-argument metadata. The created-slot extraction (return type → list of `ContractId T`
slots) is pulled out as the pure `ChoiceCreatedSlots.Extract` helper and unit-tested directly.
Distinct from `SubmissionExtensionsEmitter`: creating a contract is not exercising a choice.
_Avoid_: "choice helper", "the exercise writer", "the async wrapper generator", "choice-arg owner"

**Descriptor witness**:
A generated `static` value whose *type* names two or more types that belong together, passed
as an ordinary argument so a call site infers all of them at once. C# performs no partial
type-argument inference — a caller either supplies every type argument or none — so whenever
a surface needs two related types and only one is spellable at the call site, the pair travels
on a descriptor instead. `ViewDescriptor<TInterface, TView>` pairs an interface marker with its
view record, `Choice<TTemplate, TArg, TResult>` pairs a template with a choice's argument and
result, and `KeyDescriptor` pairs a keyed template with its key type. A descriptor may be
empty (a pure type witness, like `ViewDescriptor`) or carry codecs (like `Choice`, and like
`KeyDescriptor`, which carries the key decode). `Choice` and `ViewDescriptor` are plain statics
on the type they describe. `KeyDescriptor` is instead reached through a `static abstract` member
on a facet interface, so generic code finds it without reflection — that is the forward
convention new descriptors follow, not a description of the existing two.
_Avoid_: "marker", "phantom type" (those name the facet interface, not the value), "the
metadata object", "type token"

**Contract key**:
The identifier a keyed template's contracts can be looked up by. It is a property of an
*active contract*, not of a template payload — the ledger sends it on every keyed created
event, so it is read, not computed. A payload that has never been to the ledger has no key.
Keys are not unique: several active contracts may share one, lookups return a first match by
an order the ledger only partly guarantees — contracts created in the current transaction
first, then explicitly disclosed contracts in the order the command listed them, then
contracts known to the participant in no guaranteed order — and enforcing uniqueness is the
consuming application's job.
A generated payload type carries a static `Key` **descriptor witness**. That is not a
counter-example to the above: a static describes the *template*, stating which key type this
template's contracts are looked up by. It never asserts that a payload instance knows its own
key, which is the promise ADR 0013 found unkeepable.
_Avoid_: "the key projection" (that names the Daml-side expression, not the value),
"key accessor", "computing a contract's key"

**Key type**:
The generated C# type of a contract key — a record, a tuple, or a bare primitive — fully
constructible and serializable by a caller who has never seen the contract. Distinct from a
**key value**, which is an inhabitant of it. The key type is what makes by-key commands
possible: the caller builds a key value out of data it already holds. Generated for every
keyed template, independent of whether anything about the key can be analysed.
_Avoid_: "the key", "key record" (a key may be a tuple or a bare `Party`)

**Key expression**:
The Daml-side projection from a template payload to a key value. It exists in the Daml-LF
archive and is deliberately **not** carried in the Intermediate AST: representing it means
representing arbitrary value construction, and a partial representation yields a silently
wrong key in a published package. Its absence is a decision, not a gap. It is never inlined
in the archive — the compiler lifts it to a generated top-level value that the template body
applies — so reading one is evaluation, not field access.
_Avoid_: "key projection" unqualified, "the key function", "a record of field projections"

**Supported input**:
The set of Daml-LF versions a released codegen accepts. Supported means
**proven by a fixture exercised in CI** — not merely decoded by the reader, and not merely
emittable by some compiler. A version the reader accepts but no fixture covers is a gap, and
is named as one.
_Avoid_: "the supported envelope" (jargon; say what it is), "supported SDK version" (the SDK
range and the Daml-LF range no longer coincide and must be stated separately)

**Default Daml-LF version**:
Daml-LF **2.2** — what the compiler emits when a project sets no target, on both the 3.4 and
3.5 lines, silently and with no diagnostic. Project scaffolding does not write a target
either, so this is the version an ordinary consumer produces without deciding to. The
`defaulttarget` conformance fixture pins no target for exactly this reason, so the default is
covered on both the read and the emit path.
_Avoid_: treating 2.1 as "the normal case", "LF 2.x" as a single accepted range (the JVM read
path enumerates versions; the .NET path does not)

**Maintainers**:
The parties responsible for a contract key. Declared in Daml as a function *from the key
type* to a list of parties — never from the payload — so a maintainer analysis is a party
projection whose **projection source** is the key.
_Avoid_: "key owners", "key signatories"

**Projection source**:
The binder a static party projection reads from: the template payload (`signatory`,
`observer`), the contract key (`maintainer`), or a choice argument (choice-level
`controller` / `observer`). Carried on every party-analysis verdict, because a projection is
only meaningful against the binder it was written against. A projection whose source cannot
be determined is `Dynamic`.
_Avoid_: "the binder" alone, "field owner"

## Example dialogue

> **Dev**: Where does the JVM dependency go? I thought consumers shouldn't need a JDK.
>
> **Domain expert**: They don't. The JVM helper only runs at codegen time — when you
> regenerate against a new DAR. The generated `.cs` is plain .NET; once it lands in your
> repo (or a NuGet package generated from a DAR), the JVM is gone from the picture.
>
> **Dev**: So if Splice ships a patch release, do we have to regenerate?
>
> **Domain expert**: No. Schema-mode decode is patch-version-insensitive — the
> Intermediate AST is identical, so the C# emitter produces byte-identical output and
> the NuGet hash doesn't move.
>
> **Dev**: And if DA renames an internal `PackageSignature` case class in a `daml-lf-archive`
> release?
>
> **Domain expert**: Only the AstToIntermediate translator has to change. The
> Intermediate AST stays stable; the C# emitter doesn't notice.
