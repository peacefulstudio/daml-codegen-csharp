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
executed against the host JDK (a dpm install precondition). The helper ships only
inside that OCI bundle — its source is not part of the public repository.
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
enum / variant / interface-placeholder / choice-argument name sets. Built once per package by
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
