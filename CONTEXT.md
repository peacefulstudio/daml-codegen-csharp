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
executed against the host JDK (a dpm install precondition).
_Avoid_: "Scala helper", "decoder service", "ast extractor"

**AstToIntermediate translator**:
The single function inside the JVM helper that maps
`Dar[(PackageId, Ast.PackageSignature)]` to `IntermediateDar`. The only place in the
project that depends on DA-internal Scala case-class shapes; everything else depends on
the Intermediate AST.
_Avoid_: "AST converter", "Scala-to-proto mapper"

**C# emitter**:
The .NET binary that consumes an Intermediate DAR and writes `.cs` files. Replaces today's
`CSharpCodeGenerator` walking `DamlPackage`; the new walk is over `IntermediatePackage`.
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
with no `sdk-version` key (the two are mutually exclusive by upstream design; see
ADR-0001). At M1 the supply chain is the OCI registry plus stock dpm — the codegen
toolchain will not be distributed as a `dotnet tool`, a Docker image, or a NuGet
package. (Today's shape, pre-M1, ships `Daml.Codegen.CSharp` as a `dotnet tool` /
NuGet package; that distribution is retired at the F6 dpm cutover. See the project guide's
"Packages" target-vs-current note.)
_Avoid_: "the cli", "codegen-cs tool", "codegen-cs plugin", "the container"

**Dpm mode**:
The `Daml.Codegen.CSharp.MSBuild` adapter that runs codegen via `dpm codegen-cs` (the OCI
bundle: JVM helper + emitter). Needs `dpm` + a JDK at build time. The default `DamlCodegenMode`.
_Avoid_: "dpm plugin", "online mode"

**Standalone mode**:
The JVM-free MSBuild adapter that bundles the DAR-direct CLI (and thus `Daml.Codegen.DarParser`) —
no `dpm`, no JDK. Internal-only until `Daml.Codegen.DarParser` is public.
_Avoid_: "offline mode", "DarDirect package", "embedded mode"

**`DamlCodegenMode`**:
The MSBuild seam (a property) that selects which `$(DamlCodegenTool)` adapter runs — `Dpm` or
`Standalone`.
_Avoid_: "codegen backend", "toolchain flag"

## Example dialogue

> **Dev**: Where does the JVM dependency go? I thought consumers shouldn't need a JDK.
>
> **Domain expert**: They don't. The JVM helper only runs at codegen time — when you
> regenerate against a new DAR. The generated `.cs` is plain .NET; once it lands in your
> repo (or a Canton.Splice NuGet), the JVM is gone from the picture.
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
