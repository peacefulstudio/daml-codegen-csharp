# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog tracks the four packages published from this repo together,
because they are versioned in lockstep:

- `Daml.Codegen.CSharp` — dotnet tool CLI
- `Daml.Runtime` — runtime types referenced by generated code
- `Daml.Ledger.Abstractions` — transport-agnostic `ILedgerClient` interface
- `Daml.Codegen.CSharp.MSBuild` — MSBuild integration

## [Unreleased]

### Changed — BREAKING

- **Contract-key `Key` property is now a `partial` declaration** instead of a stub that throws `NotImplementedException` at runtime ([#65](https://github.com/peacefulstudio/daml-codegen-csharp/pull/65)). The codegen still detects keys and emits `: IHasKey<TKey>`, but the property body is now supplied by a hand-rolled `partial` in the consuming project until the full DALF key-expression analysis (mapping the template's `key` Daml expression back to template fields) lands. This shifts the failure mode from runtime (throwing on first `Key` access) to compile time (Roslyn `CS9248` on the consumer build until the implementing partial is supplied) — impossible to ship to production unnoticed. Consumers must add an implementing partial alongside the generated template, **inside whatever namespace the generated `Foo.cs` declares**. By default that namespace is derived from the Daml package name (e.g. `My.Daml.Package`); if you override it with `--namespace` (CLI) or `CodeGenOptions.RootNamespace` (library), match the override exactly. Open the generated `Foo.cs` to confirm the namespace before writing your partial:
  ```csharp
  // In your project, alongside the generated Foo.cs.
  // Namespace MUST match whatever the generated file declares. Default is
  // package-derived, but `--namespace` / `RootNamespace` overrides it —
  // open the generated Foo.cs and copy the namespace from there.
  namespace My.Daml.Package;

  public sealed partial record Foo
  {
      public partial string Key => Owner.Id;  // or whatever your key expression is
  }
  ```
  The implementing partial's type kind must match the generated type kind: if you configure the codegen with `UseRecordTypes=false`, the generated template is a `public sealed partial class` and the implementing partial must also be a `partial class` (not `partial record`). Requires C# 13 on the consumer side, which means **.NET 9 SDK or later on the build machine** even when the consumer's `<TargetFramework>` is `net8.0` — the C# compiler is shipped with the SDK, not the target runtime, so a build host with only the .NET 8 SDK installed cannot parse the generated partial-property syntax. The codegen-emitted `.csproj` pins `<LangVersion>13</LangVersion>` only for packages that actually contain a key-bearing template, so key-less DARs continue to build with whatever LangVersion the consumer's SDK defaults supply. Unblocks Sample to opt into typed key fetch / exercise wrappers (`Foo.FetchByKeyAsync`, `Foo.<Choice>ByKeyAsync` against `IPqsClient` / `ILedgerClient`) without inheriting a throwing default. Full ByKey wrapper emission is tracked in [#64](https://github.com/peacefulstudio/daml-codegen-csharp/issues/64).

### Added

- **`Daml.Runtime.Data.SynchronizerId`** — `readonly record struct` mirroring
  `Party`'s shape (null/whitespace-guarded constructor, `Id` accessor with
  default-uninitialized throw, implicit `→ string` conversion, explicit
  `string →` cast, JSON converter that round-trips as a plain string).
  Stored as an opaque string per Canton's documented guidance — does not
  decompose into name / fingerprint / protocol-version components, so the
  wrapper is safe across the Canton 3.4 (`name::fingerprint`) → 3.5
  (`name::fingerprint::protocol-version`) wire-format change. Closes #87.
- **Typed `<Choice>Result` records and `FromCreatedContracts` projectors** for
  every Daml choice whose return type carries one or more `ContractId T`
  references. Choice creates a single template → single field; `Optional` →
  nullable field; `[…]` (list) → `IReadOnlyList<ContractId<T>>`; tuples are
  flattened across components. The static `FromCreatedContracts(IEnumerable<CreatedContract>)`
  projector returns `ExerciseOutcome<<Choice>Result>.One` when every required
  slot has the expected count, `.None` when a single-cardinality slot's
  template is missing, and `.Many` when a single- or optional-cardinality
  slot has more than one. Template matching is by `(ModuleName, EntityName)`
  only, so package-id drift from upgrades doesn't break projection. Issue
  #60.
- **`<Choice>Async(...)` extension methods on `ContractId<TemplateName>`** —
  one per create-bearing choice on each template, in a per-template static
  `<TemplateName>Extensions` class. Signature:
  `(ContractId<T> contractId, ILedgerClient client, [<Choice>Arg argument,] Party actAs, string? workflowId = null, CancellationToken cancellationToken = default)`.
  Body: builds a `CommandsSubmission`, calls
  `ILedgerClient.TrySubmitAndWaitForTransactionAsync`, projects success via
  `<Choice>Result.FromCreatedContracts`. `DamlError` and `InfraError`
  outcomes pass through with all fields preserved. Workflow id has no
  default — workflow IDs are correlation keys, and a per-choice constant
  would bucket every submission of the same choice under one id and break
  observability.
- **`Daml.Ledger.Abstractions` `<PackageReference>` in generated csproj** —
  added unconditionally alongside `Daml.Runtime`. The package is
  interface-only and lockstep-versioned with the runtime, so pure-projector
  consumers absorb it at zero transitive weight. Required by the emitted
  `<Choice>Async` extension methods, which take `ILedgerClient`.

### Changed — generated code shape

- Generated template `.cs` files now declare additional `using` directives
  unconditionally: `Daml.Ledger.Abstractions`, `Daml.Runtime.Outcomes`,
  plus the BCL set (`System`, `System.Collections.Generic`,
  `System.Threading`, `System.Threading.Tasks`) so generated source
  compiles without `<ImplicitUsings>` enabled in the consumer csproj.

### Changed — BREAKING

- **`Daml.Runtime.Streams.ContractStreamEvent<T>.{Created, Archived, Exercised, Assigned, Unassigned}.WitnessParties`**
  changes from `IReadOnlyList<string>` to `IReadOnlyList<Party>` for sibling
  consistency with `Daml.Runtime.Contracts.{CreatedEvent, ArchivedEvent,
  ExercisedEvent}` and the broader project trend toward typed party values
  (`Party` instead of bare `string`). Consumers comparing or pattern-matching
  on these collections need to migrate `string` accesses to `Party.Id` (or
  use the `Party` value directly via `Equals`). Closes #86. **Bridge
  follow-up required**: `Canton.Ledger.Grpc.Client` / `Daml.Runtime.Grpc`
  must update its `ProjectTransaction` / equivalent stream-projection code to
  construct `WitnessParties` as `Party` (currently constructs from proto
  `string` directly) before consuming the new `Daml.Runtime` version.
- **`ContractStreamEvent<T>.Assigned.{Source, Target}`** and
  **`Unassigned.{Source, Target}`** change from `string` to
  `Daml.Runtime.Data.SynchronizerId`. Same migration shape as the
  `WitnessParties` change above. Closes #87. Same canton-bridge follow-up
  required: reassignment-event projection in `Canton.Ledger.Grpc.Client` /
  `Daml.Runtime.Grpc` must construct `Source` / `Target` as `SynchronizerId`
  (currently constructs from proto `string`) before consuming the new
  `Daml.Runtime` version.

### Fixed

- **`WriteChoiceMethod` now skips emission for choices with a fallback
  `<Choice>Arg` argument type.** Previously emitted code referenced
  `arg.ToRecord()` against a stub record with no `ToRecord()` method,
  breaking consumer compilation in those edge cases. Fixes #78.

## [0.1.4] — 2026-05-01

### Added

- **`Daml.Ledger.Abstractions` (new package)** — transport-agnostic
  `ILedgerClient` interface lifted from `canton-ledger-api-csharp`.
  Implementations live in their respective transport packages:
  `Canton.Ledger.Grpc.Client` (gRPC) and a planned HTTP REST client.
  Generated codegen output (projector helpers, `<Choice>Async`
  extensions) will reference this package instead of the canton-specific
  one — projector-only consumers no longer transitively pull in a gRPC
  stack. Versioned in lockstep with `Daml.Runtime` and the codegen tool.
  The throwing-API variants `CreateAsync` and
  `SubmitAndWaitForTransactionAsync` (long `[Obsolete]` on canton's
  interface) are intentionally **not** part of the abstraction; only
  their outcome-based `Try*` replacements are surfaced. Other methods
  on the interface (`SubmitAsync`, `ExerciseAsync`, etc.) keep their
  existing names. Existing callers of the dropped methods migrate to
  `TryCreateAsync` / `TrySubmitAndWaitForTransactionAsync`.
  ([#74](https://github.com/peacefulstudio/daml-codegen-csharp/pull/74))
- **`Daml.Runtime.Streams.ContractStreamEvent<T>`** — transport-agnostic discriminated
  record for typed contract subscription streams. Variants:
  `Created`, `Archived`, `Exercised`, `Assigned`, `Unassigned`, `Checkpoint`,
  `StreamError`. Lives in `Daml.Runtime` so any ledger client (gRPC, JSON,
  in-memory) can yield these without dragging the consumer into a
  transport-specific dep. `StreamError.StatusCode` is `int` (a
  `Grpc.Core.StatusCode` would be cast at the call site) — consumers stay free
  of any transport library. Counterpart in `Canton.Ledger.Grpc.Client` (which
  had the prior owner of this type) is being migrated to consume from here.
- **`Daml.Runtime.Outcomes.ExerciseOutcome<T>`** — transport-agnostic
  discriminated record for exercise/create outcomes. Variants: `One`,
  `None`, `Many`, `DamlError`, `InfraError`. `T` is unconstrained
  (any payload shape). `InfraError.StatusCode` is `int`
  (cast `(int)Grpc.Core.StatusCode` at the gRPC client construction site)
  so this type is dep-free and any ledger client can yield it.
- **`Daml.Runtime.Outcomes.DamlErrorCategory`** — closed enum mirroring the
  Canton 3.5 documented error categories. Pre-existing canton type lifted
  here so it's reachable from generated code without a transport dep.
- **`Daml.Runtime.Contracts.TransactionResult`** and
  **`Daml.Runtime.Contracts.CreatedContract`** — pure data records for
  submitted-transaction results. Lifted from `Canton.Ledger.Grpc.Client`;
  no transport deps, useful from any ledger client.
- **`Daml.Runtime.Contracts.TransactionResultExtensions`** with
  `Single<T>`, `TrySingle<T>`, `All<T>` over `TransactionResult` for
  template-typed projection of `CreatedContracts`. `(module, entity)`
  matching tolerates package-id drift.
- **`Daml.Runtime.Stdlib` namespace** with hand-coded stubs for Daml stdlib
  types that are not generated per package. Currently covers
  `DA.Time.Types.RelTime`. Future stdlib types (`Set`, `Map`, `Tuple2`, ...)
  are tracked in [#57](https://github.com/peacefulstudio/daml-codegen-csharp/issues/57).
- **`Daml.Runtime.Stdlib.GenericStub.NotImplemented<T>(string)`** — runtime stub
  used by generated `ToRecord`/`FromRecord` methods on records with
  type-parameter fields. Generated code compiles; calling the stub at runtime
  throws `NotImplementedException` with a pointer to the workaround. Tracked
  for proper static-abstract dispatch in #57.
- **Interface-placeholder record emission**. Daml-LF emits a same-named empty
  record for every `interface I where ...` declaration; the codegen now detects
  this case (record name matches an interface name in the same module) and
  emits the placeholder as `: ITemplate` with throwing static metadata. Lets
  `ContractId<I>` (which constrains `T : ITemplate`) keep compile-time safety
  while loudly failing if anyone reads `I.TemplateId` directly without first
  coercing to a concrete template type.
- **Cross-DAR type reference resolution**. Generated csprojs now emit a
  `<PackageReference>` for every type referenced from a foreign DAR, with
  fully qualified namespace prefixes in the generated code. Stdlib references
  route to `Daml.Runtime.Stdlib.*` instead of cross-package references.
- **`TextMap` and `GenMap` codec support** in both `ToValue` and `FromValue`
  conversion paths. Previously `IReadOnlyDictionary<,>.FromRecord(...)` was
  emitted, which never compiled; now generates `DamlTextMap`/`DamlGenMap`
  round-trips correctly.
- **Variant `FromRecord` stub**. Variants are emitted with a `FromRecord` that
  throws `NotImplementedException` so parent records that hold a variant field
  still compile. Full variant codec support tracked in #57.
- **`publish-splice.yaml` workflow** (workflow_dispatch only). Downloads a
  `hyperledger-labs/splice` release tarball, generates and packages each Splice
  DAR family in dependency order, pushes to GitHub Packages, and uploads
  per-family logs as artifacts. Inputs are validated against an explicit regex
  before flowing into `curl` URLs or MSBuild properties.

### Changed

- **`ContractId<T>` typeparam doc** clarifies that `T` may be an interface or
  interface placeholder (in addition to a template), and points at the
  throwing-stub pattern. Behavior unchanged: the constraint `T : ITemplate`
  is preserved.
- **Record-field deserialization expressions** simplified — redundant outer
  parens stripped (`((val).As<T>()).Value` → `val.As<T>().Value`). Generated
  code is unchanged in behavior.
- **`DamlList`/`DamlTextMap`/`DamlGenMap` materialization** in generated
  `ToRecord` methods now emits an explicit `(DamlValue)` projection cast
  followed by `.ToList()`/`.ToDictionary(...)` so the result satisfies the
  `IReadOnlyList<DamlValue>` / `IReadOnlyDictionary<string, DamlValue>`
  constructor parameter without relying on covariance.
- **Module-qualified enum dispatch** in the from-value conversion: enum
  type-refs are now keyed by `<module>:<name>` rather than just `<name>`, so
  a record and an enum sharing a simple name across different modules of the
  same package no longer route through the wrong dispatch path. Same fix
  applied to choice `ResultDecoder` emission for return-typed enums.
- **`<PackageReference>` cross-DAR list** in generated csprojs is now derived
  from types actually referenced in generated code (not from raw DAR-level
  dependency metadata, which was empty for splice DARs). Stdlib packages are
  filtered out and routed to `Daml.Runtime.Stdlib.*` instead.

### Fixed

- **`__@lock` invalid-identifier bug** when a Daml record has an `Optional`
  field whose name is a C# keyword (`lock`, `class`, `event`, ...). The
  sanitizer escapes keywords with a leading `@`; the codegen's pattern-match
  variable then concatenated `__@<keyword>` which is not a valid C# identifier.
  Fixed by stripping the `@` prefix from the local-variable name only.
- **`ToDictionary` throwing on duplicate type names** when a Daml package
  defines the same simple type name in different modules (e.g.
  `splice-amulet` defines records and enums named `Amulet` in distinct
  modules). The package-wide lookup is now built defensively as last-wins.
- **`ContractId<T>` for non-template `T`** — `splice-api-token-metadata-v1`
  uses `ContractId AnyContract` where `AnyContract` is an interface, not a
  template. The codegen's interface-placeholder emission (see Added) makes
  these contract ids compile-safe.

### Security

- **Workflow-input validation** in `publish-splice.yaml`: `splice_version`
  must match `^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$` and
  `package_version_suffix` (if non-empty) must match
  `^[A-Za-z0-9.+-]+$` before being interpolated into a release tarball URL or
  passed as an MSBuild `-p:VersionSuffix=...` property. `workflow_dispatch` is
  already gated by repo write access; this hardens the residual injection
  surface.

## [0.1.2] — 2026-04-24

### Changed — BREAKING

- **Package renamed**: `Daml.Codegen.CSharp.Runtime` → `Daml.Runtime` (#43).
  Consumers must update their `PackageReference` and `using` directives.
  Type names are unchanged.
- **Pre-release version scheme** now uses dot separators
  (`0.1.2-<branch>.<run>.<sha>` instead of `0.1.2-<run>-<branch>-<sha>`) so that build
  numbers compare numerically under SemVer 2.0. Dev packages published under
  the old `0.1.1-*` scheme have been removed from the GitHub Packages feed
  (`nuget.pkg.github.com/peacefulstudio`); consumers pinned to them must
  upgrade to `0.1.2-*`.

### Added

- **First-class `Party` value type** (#40). Daml `Party` now maps to a
  dedicated `Party` struct instead of `string`, giving type-safety at the
  boundary between generated code and application code.
- **`FromDamlValue<T>` helper** on `Daml.Runtime` (#44) — converts a
  `DamlValue` into strongly-typed .NET values (generated records, primitives,
  `Party`, `ContractId<T>`, and `DamlValue` subtypes) in one call, removing
  the need for manual `FromRecord` wiring in application code.

### Fixed

- **`Party` JSON serialization** is now a plain JSON string (`"Alice::1220…"`),
  matching the Ledger JSON API and PQS wire format (#45, #46). Previously
  `Party` was serialized as a JSON object, which broke PQS-based consumers.

## [0.1.0] — initial public alpha

Initial release of the three-package suite:

- DAR/DALF parsing via generated protobuf stubs
- C# code generation for records, variants, enums, templates, choices,
  contract keys, interfaces, generic types, and package upgrades
- Runtime library covering all Daml primitives with JSON serialization
- CLI distributed as a `dotnet tool`
- MSBuild integration for build-time code generation

Historical pre-release dev builds (`0.1.0-*`, `0.1.1-*`) were published to
the GitHub Packages NuGet feed
(`nuget.pkg.github.com/peacefulstudio`) during development and have
since been pruned. They are not supported.

[Unreleased]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.4...HEAD
[0.1.4]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.2...v0.1.4
[0.1.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.0-alpha.3...v0.1.2
[0.1.0]: https://github.com/peacefulstudio/daml-codegen-csharp/releases/tag/v0.1.0-alpha.3
