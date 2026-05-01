# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog tracks the three packages published from this repo together,
because they are versioned in lockstep:

- `Daml.Codegen.CSharp` — dotnet tool CLI
- `Daml.Runtime` — runtime types referenced by generated code
- `Daml.Codegen.CSharp.MSBuild` — MSBuild integration

## [Unreleased]

### Added

- **`Daml.Runtime.Streams.ContractStreamEvent<T>`** — transport-agnostic discriminated
  record for typed contract subscription streams. Variants:
  `Created`, `Archived`, `Exercised`, `Assigned`, `Unassigned`, `Checkpoint`,
  `StreamError`. Lives in `Daml.Runtime` so any ledger client (gRPC, JSON,
  in-memory) can yield these without dragging the consumer into a
  transport-specific dep. `StreamError.StatusCode` is `int` (a
  `Grpc.Core.StatusCode` would be cast at the call site) — consumers stay free
  of any transport library. Counterpart in `Canton.Ledger.Grpc.Client` (which
  had the prior owner of this type) is being migrated to consume from here.
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

[Unreleased]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.0-alpha.3...v0.1.2
[0.1.0]: https://github.com/peacefulstudio/daml-codegen-csharp/releases/tag/v0.1.0-alpha.3
