# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog tracks the packages published from this repo together,
because they are versioned in lockstep:

- `Daml.Codegen.CSharp` — C# emitter library (NuGet package)
- `Daml.Runtime` — runtime types referenced by generated code
- `Daml.Ledger.Abstractions` — transport-agnostic `ILedgerClient` interface
- `Daml.Codegen.Testing.Conformance` — compiled conformance corpus types + embedded DAR

> **Versioning and stability.** This project is pre-1.0: under SemVer 0.x, any
> release may change the public API without a major-version bump. The first
> release published to NuGet.org is `0.1.8-preview.1`; the `0.1.0`–`0.1.7`
> sections below record internal development milestones whose packages were
> never published to a public feed.

## [Unreleased]

### Added

- `NOTICE` file added at repo root (Apache-2.0 open-source prep).
- Add `DamlValueExtensions.AsOptional(this DamlValue)` — normalizes a value into a `DamlOptional` (an existing optional passes through, a bare value wraps as Some), recovering Optional fields from ledger JSON where Some is flattened to the inner value.
- `ILedgerClient` now implements `IAsyncDisposable`, with a default implementation that bridges to `Dispose()` — `await using var client = …` works against every implementation with no source change on the implementation side.
- `Daml.Codegen.CSharp` now ships XML documentation and a NuGet package README, so IntelliSense and the NuGet.org gallery page document the emitter API.
- XML documentation is completed across the shipped packages — `CS1591` (missing XML comment on a publicly visible member) is no longer suppressed for them.

### Changed — BREAKING

- **`IDamlValue` is now a bare marker interface; its `DamlRecord ToRecord()` member moved to a new `IDamlRecord : IDamlValue`, alongside a new `IDamlVariant : IDamlValue` carrying `DamlVariant ToVariant()`.** Generated record types and template choice-argument types now implement `IDamlRecord` instead of `IDamlValue`, and `ITemplate` / `IDamlInterface` now extend `IDamlRecord` (so every template and interface still exposes `ToRecord()`). Code that holds a value as `IDamlValue` and calls `.ToRecord()` must now hold it as `IDamlRecord` (or `ITemplate`); generic constraints `where T : IDamlValue` that relied on `ToRecord()` should be widened to `IDamlRecord`. Wire format and `ToRecord()` output are unchanged. Variant emission is unchanged in this slice (the design decision is kept in the project's internal ADR collection; variant round-trip lands in a follow-up).
- **Removed the unused public interfaces `ITemplateCompanion<T>` and `ICreateAnd<T>` from `Daml.Runtime`.** Neither was ever implemented by generated code or referenced anywhere, so no consumer can be relying on them; they are deleted rather than frozen into the current 0.x surface.
- **`Contract<T>`, `TransactionResult`, and `CreatedContract` (in `Daml.Runtime.Contracts`) are now `sealed`.** These public result records can no longer be subclassed. Nothing was expected to derive from them, so this is a technically-breaking change only for consumers who had created their own subtypes; switch to wrapping or composing these records instead.
- **`ILedgerClient` submitter API is now strongly typed: the single-party convenience overloads of `TryExerciseAsync<TResult>`, `TryCreateAsync<TTemplate>`, `TryExerciseForCreatedAsync<TTemplate>`, `SubscribeAsync<T>`, and `SubscribeActiveAsync<T>` (and `LedgerClientExtensions.ExerciseAsync`) take `Party actAs` instead of `string actAs`.** The `SubmitterInfo` overloads remain the abstract primitives implementations override; the `Party actAs` overloads are convenience default-interface-methods that forward to them with that single `ActAs` party and empty `ReadAs`. Replace `client.ExerciseAsync(cmd, "alice")` with `client.ExerciseAsync(cmd, new Party("alice"))`.
- **Removed the implicit `string` → `SubmitterInfo` conversion.** A bare `string` no longer binds to a submitter parameter — construct the party explicitly (`new Party("alice")`), or build a `SubmitterInfo`. The implicit `Party` → `SubmitterInfo` conversion is retained, so single-party submission stays a one-liner once you hold a `Party`. This closes a transposition footgun where `actAs` and the adjacent `string? workflowId` could be swapped silently.
- **`Party` → `string` is now an explicit conversion** (was implicit). Use `party.Id` or `(string)party` where a raw string is genuinely wanted; this prevents a `Party` from silently flowing into an arbitrary `string` parameter such as `workflowId`. There is still no implicit `string` → `Party` conversion — construct with `new Party(...)`.
- The default submission path carries `ReadAs` parties and multiple `ActAs` parties straight through to the implementation instead of throwing `NotSupportedException` — a single-`actAs`-plus-`readAs` submission no longer fails at runtime.
- **`ContractId<T>` → `string` is now an explicit conversion** (was implicit). Use `.Value` or `(string)cid` to get the raw contract id; this stops a typed contract id from silently degrading into an arbitrary `string` parameter. The explicit `string` → `ContractId<T>` conversion is unchanged.
- **`SynchronizerId` → `string` is now an explicit conversion** (was implicit). Use `.Id`, `ToString()`, or `(string)sid` where a raw string is genuinely wanted, so a synchronizer id can no longer flow silently into an arbitrary `string` parameter. JSON serialization is unchanged — `SynchronizerId` still round-trips as a plain JSON string. The explicit `string` → `SynchronizerId` conversion is unchanged.
- **`CommandsSubmission.WorkflowId` and `CommandsSubmission.CommandId` are now the value types `WorkflowId?` / `CommandId?`** (was `string?`). Construct with `new WorkflowId("…")` / `new CommandId("…")` (or the explicit `(WorkflowId)"…"` cast), or pass `null` to omit them; read the raw string back via `.Value` (a nullable submission field unwraps as `submission.WorkflowId?.Value`). `WithWorkflowId(...)` / `WithCommandId(...)` now take the value types, so transposing a workflow id and a command id at a call site is a compile error rather than a silent swap. `WorkflowId` rejects only `null` — an empty or whitespace value is accepted, matching the Ledger API which documents `workflow_id` as optional with no non-empty constraint (`CommandId` still rejects empty/whitespace since `command_id` is required). Generated `<Choice>Async` convenience methods keep their `string? workflowId` parameters and stay 100% compatible with the previous string path: a `null` or empty `workflowId` omits the field, any other value (including whitespace) is forwarded verbatim, so only direct `CommandsSubmission` construction is affected. The values still project onto the Ledger API `workflow_id` / `command_id` string fields unchanged.
- **The `Choice` member on `ExerciseCommand`, `ExerciseByKeyCommand`, and `CreateAndExerciseCommand` — and the `choice` parameter of the `ExerciseCommand.For`, `ExerciseCommand.ForInterface`, and `CreateAndExerciseCommand.For` factories — are now the value type `ChoiceName`** (was `string`). Migrate by wrapping the choice name, e.g. `ExerciseCommand.For(cid, "Relabel", arg)` becomes `ExerciseCommand.For(cid, new ChoiceName("Relabel"), arg)`; read the raw string back via `.Value`. Because `Choice` and the adjacent `ContractId` no longer share the `string` type, transposing them when constructing an `ExerciseCommand` is now a compile error rather than a silent swap. The value still projects onto the Ledger API `choice` string field unchanged.
- **`ExerciseCommand.ContractId` is now the abstract `ContractId` base type** (was `string`). A new non-generic `abstract record ContractId` in `Daml.Runtime.Contracts` is the base of `ContractId<T>`, so the typed contract id flows onto the command with no unwrap/rewrap; the bare-`string` positional construction path is gone — build commands via `ExerciseCommand.For<T>` / `ForInterface<T>` (which now also reject a `null` contract id), and read the raw string back via `.Value`. The value still projects onto the Ledger API `contract_id` string field unchanged.
- **`ContractId<T>` now validates its value on construction** — `new ContractId<T>(value)` throws `ArgumentException` when `value` is null, empty, or whitespace (previously it silently accepted them). All real ledger contract ids are non-empty, so legitimate callers are unaffected; a malformed/empty id now fails loud instead of carrying `""`.
- **`ContractId<T>` is no longer a positional record, so its compiler-generated `Deconstruct(out string)` is removed.** Consumers using positional deconstruction (`var (value) = contractId;`) must read `.Value` instead.
- **`Choice<TTemplate, TArg, TResult>.Name` is now the value type `ChoiceName`** (was `string`). Generated choice metadata now constructs it explicitly — `Name = "Archive"` becomes `Name = new ChoiceName("Archive")`; read the raw string back via `.Value`. This extends the project-wide "no bare `string` choice names" pass (a decision kept in the project's internal ADR collection) to the choice-metadata record.
- **Generated variants now round-trip through `DamlVariant` instead of `DamlRecord`.** A generated variant's abstract base now implements `IDamlVariant` (was `IDamlValue`) and exposes `DamlVariant ToVariant()` plus a static `FromVariant(DamlVariant)` that dispatches on the constructor tag; each case overrides `ToVariant()` to produce `DamlVariant.Create("<Tag>", <payload>)` (a no-argument case uses `DamlUnit.Instance` as its value). The previous lossy `ToRecord()` / throwing `FromRecord(...)` stubs are gone, so a record or choice-result field whose type is a variant now serializes and deserializes correctly instead of throwing `NotImplementedException` at runtime. Hand-written code that called `.ToRecord()` / `.FromRecord(...)` on a generated variant must switch to `.ToVariant()` / `.FromVariant(...)`. Wire format is unchanged — the runtime `DamlVariant` JSON shape is the same (per a decision kept in the project's internal ADR collection).
- **Generated enum serialization extensions are renamed `ToRecord`/`FromRecord` → `ToDamlEnum`/`FromDamlEnum`** so the method name matches its `DamlEnum` return/parameter type (an enum's `ToRecord()` never returned a `DamlRecord`). For a generated `enum Status`, replace `status.ToRecord()` with `status.ToDamlEnum()` and `StatusExtensions.FromRecord(value)` with `StatusExtensions.FromDamlEnum(value)`. Generated `ToValue`/`FromValue` round-tripping is updated automatically; only hand-written code that called the enum extensions directly needs migrating. Record/variant/template `ToRecord`/`FromRecord` are unchanged.
- **Removed `CodeGenOptions.GenerateJsonSupport`, `CodeGenOptions.OutputDirectory`, `CodeGenOptions.Verbosity`, and the `--json` CLI flag.** All four were documented no-ops — setting them never changed emitter behavior or output. Delete any assignments and drop the flag; generated output is unchanged.
- **Removed the dead interface member `IDarSource.ResolveAllDependencyReferences()`.** No code path ever called it; implementations simply delete their override.
- **`DamlPackageReference.Name` and `DamlPackageReference.Version` are now init-only**, completing the `IDarSource` cleanup: package references are immutable inputs to the emitter, and dependency resolution is no longer part of the public surface.
- **The CI versioning cluster is now internal**: `JsonReleaseCounterStore`, `NuGetVersionResolver` (née `SpliceNuGetVersion`), `ReleaseCounterEntry`, `IntermediatePackageContentHash`, and `FourPartPackageVersion` no longer appear in the public API. They exist to compute the 4th version segment for CI-driven publishing and were never a consumer integration point.
- **Model-side `DamlField` is renamed `DamlFieldDefinition`.** Only consumers of the codegen model API are affected; the runtime `DamlField` type referenced by emitted code is unchanged.
- **The conformance corpus types moved from the bare root namespace `Richtypes` to `Daml.Codegen.Testing.Conformance.Richtypes`.** Replace `using Richtypes;` with `using Daml.Codegen.Testing.Conformance.Richtypes;`.
- **CLI: `--intermediate` is now required, and the short flag `-V` is renamed `-v`.** Cancellation (Ctrl+C) is now honored and exits with code 130.

### Changed

- **`DamlNumeric` now serializes to JSON in canonical unpadded decimal form.** Trailing zeros are stripped and at least one fractional digit is always emitted, so the wire shape no longer depends on how the `decimal` was constructed: `1.5m` and `1.50m` both serialize to `"1.5"`, and an integer value such as `42m` serializes to `"42.0"`. Scientific notation is never emitted (e.g. `0.0000000001m` → `"0.0000000001"`). Previously the output preserved the `decimal`'s internal scale (`1.50m` → `"1.50"`, `42m` → `"42"`). This is the commitment-grade wire shape for the current 0.x surface, per a decision kept in the project's internal ADR collection; PQS-style scale-padded reading remains out of scope (deferred to a future release).
- Licensing: every source file now carries the two-line SPDX Apache-2.0 header (`Copyright (c) 2026 Peaceful Studio OÜ` + `SPDX-License-Identifier: Apache-2.0`); the central `<Copyright>` tag in `Directory.Build.props` is retained for assembly metadata. (open-source prep)
- `DamlJsonSerializer` failures now always surface as `JsonException`: a number outside `decimal` range, a non-object top level passed to `DeserializeRecord`, an unsupported `DamlValue` subtype, and value nesting beyond 64 levels all throw `JsonException` instead of leaking `FormatException`/`InvalidOperationException`/`NotSupportedException`.
- **`DamlRecord`, `DamlList`, `DamlTextMap`, and `DamlGenMap` now implement structural equality** — two values with equal contents compare equal, so two `ToRecord()` results of the same payload now satisfy `Equals`. `DamlNumeric` equality compares the numeric value only, ignoring `Scale`, so a deserialized Numeric (always reconstructed at the default scale) compares equal to the value it round-tripped from. `DamlGenMap.Create` now rejects structurally-equal duplicate keys, matching `DamlTextMap`.
- **`DamlJsonSerializer` hardening**: duplicate JSON property names are rejected; offset-less timestamp strings now parse as UTC instead of the machine's local time zone; the value-nesting bound is raised from 64 to 128 levels (above Daml-LF's own 100-level limit, so any ledger-valid value fits); and serializing a `DamlRecord` carrying duplicate field labels now throws instead of silently keeping the last value.
- **The throwing `ExerciseAsync` convenience wrappers now throw `LedgerOperationException`** (derives from `InvalidOperationException`, so existing catch blocks keep working), carrying the structured `DamlError`/`InfraError` detail — error category, error id, metadata, and status code — instead of a flattened message.
- Generated variant `Tag`/`ToVariant` overrides now carry `/// <inheritdoc />` doc comments, so consumer builds with `GenerateDocumentationFile` enabled no longer emit CS1591 for emitted code.

### Fixed

- **`DamlJsonSerializer` untyped deserialization no longer type-confuses Text values that merely look like dates or timestamps.** Inference is restricted to the exact canonical shapes the serializer emits (`yyyy-MM-dd` dates, ISO-8601 `T`-separated timestamps), so `"12:30"` or `"12/25/2023"` stay `DamlText` instead of becoming a nondeterministic `DamlTimestamp`/`DamlDate`.
- **`DamlNumeric` now round-trips through JSON.** Strings matching the canonical numeric shape the serializer emits (`-?digits.digits`, e.g. `"1.5"`, `"42.0"`) deserialize back to `DamlNumeric`; previously `DamlNumeric(1.5m)` came back as `DamlText("1.5")`.
- **`DamlJsonSerializer` no longer misreads records as variants.** Only an object with exactly the two keys `tag` (a JSON string) and `value` becomes a `DamlVariant`; objects with extra properties or a non-string `tag` deserialize as `DamlRecord` instead of silently dropping fields or throwing.
- **Optional record fields now survive deserialization from ledger JSON.** An explicit JSON `null` field deserializes to `DamlOptional.None` instead of being dropped (which made generated `FromRecord` throw on every Optional-bearing template), and generated `FromRecord`/choice-result decoders read Optional fields via `AsOptional`, accepting both the wrapped `DamlOptional` shape and the JSON-flattened Some shape.
- Generated `<summary>` doc comments now use the correct indefinite article for type names beginning with a vowel — e.g. a variant's `FromVariant` summary reads `Reconstructs an Outcome`, not `Reconstructs a Outcome`.
- **A record (or template/choice-argument) field whose type is an `enum` defined in a *dependency* package now round-trips correctly.** Previously the codegen only recognized enums declared in the package being generated, so a field referencing an enum from another package fell through to the record serialization path and the emitted code failed to compile — the TO side called `.ToRecord()` and the FROM side called `FromRecord(...)` on a bare C# `enum`, neither of which exists. Such fields now serialize and deserialize through the foreign enum's generated `…Extensions.ToDamlEnum` / `…Extensions.FromDamlEnum` helpers, exactly like a same-package enum field.
- **Generated C# for real-world Daml-LF 1.x and parametric DARs now compiles.** Archive choices on placeholder-named (pre-LF-1.8, no `PackageMetadata`) packages no longer crash or leak a `No.Package.Metadata.Archive` reference, leading-hyphen dependency names no longer produce a leading-dot package id, and stdlib-known types (`Tuple2`, `Either`, `Set`, `NonEmpty`, `Map`, `RelTime`) reached through such packages now resolve to `Daml.Runtime.Stdlib.*`. Parametric records/variants now emit a valid `using Daml.Runtime.Stdlib;` for their `GenericStub` placeholders, and a record carrying a `Numeric` field no longer emits a spurious `using Daml.Runtime.Stdlib;` for its erased scale (which renders as `decimal`).
- **`DamlJsonSerializer` now throws `JsonException` when a canonical numeric string's magnitude exceeds the `System.Decimal` range** instead of silently deserializing a ledger-valid `Numeric` as `DamlText`. The `decimal` bound — 28–29 significant digits vs Daml `Numeric`'s 38 — is now documented on `DamlNumeric`; fractional precision beyond it rounds.
- **`GetTemplateId<T>` now reads the `ITemplate` static-abstract members directly and throws on an empty `PackageName`** instead of silently falling back to the package-id hash format, which produced PQS queries that matched nothing. Its format selector is now the `TemplateIdFormat` enum (`PackageName` for read-path filters, `PackageHash` for command submission) instead of a bare boolean.
- **The default `Daml.Runtime` / `Daml.Ledger.Abstractions` package references in generated projects now pin the emitter's lockstep version** instead of `*`, which never resolves a prerelease version and failed `dotnet restore` at launch.
- **Nested choice-argument record signatures are now correctly indented** — an emitter parameter-joiner bug produced garbled signatures such as `Relabel(string NewLabel    )`.

### Security

- **`IntermediateDarReader` now rejects identifiers outside the Daml-LF name grammar**, closing the path by which a hand-crafted `IntermediateDar` proto could inject arbitrary text into generated C# source.

## [0.1.7] — 2026-06-01

### Added

- **New package `Daml.Codegen.Testing.Conformance`** (first available on NuGet.org with `0.1.8-preview.1`): compiled C# generated from the `richtypes` conformance corpus (types under namespace `Richtypes`) plus the corpus DAR embedded as a resource. Consumers call `ConformanceCorpus.OpenDar()` to obtain the DAR stream for upload to a Canton participant before running live-ledger round-trip tests. Not for production use.

- **All three `dpm-codegen-cs` entrypoints now support `--publish-nuget --nuget-config <path> --nuget-source <name>`**. When `--publish-nuget` is set, the entrypoint injects `--generate-project` into the emitter call, runs `dotnet pack`, discovers the produced `.nupkg`, and pushes it via `dotnet nuget push --skip-duplicate`. All flags are validated before any work begins; `dotnet` on PATH is also checked. Warns to stderr if `--runtime-version` is not supplied (the generated `.csproj` will reference `Daml.Runtime` with a wildcard version). Covered across `dpm-codegen-cs` (POSIX bundle entrypoint), `dpm-codegen-cs.cmd` (Windows bundle entrypoint), and `scripts/codegen-pipeline.sh` — all three ship inside the `ghcr.io/peacefulstudio/dpm-codegen-cs` OCI artifact, not in this repository.

### Changed — BREAKING

- **`ILedgerClient`: `ExerciseAsync<TResult>` and void `ExerciseAsync` overloads are removed from the interface** and replaced by `TryExerciseAsync<TResult>` returning `ExerciseOutcome<TResult>` for structured error handling (callers `switch` on the outcome instead of catching exceptions). Throwing `ExerciseAsync` convenience overloads remain available at every call site via the new `LedgerClientExtensions` static class — existing callers compile unchanged. Implementations must now override `TryExerciseAsync<TResult>` instead of `ExerciseAsync<TResult>`.

## [0.1.6] — 2026-06-01

### Added

- **`Daml.Runtime.Stdlib.Either<TL, TR>` runtime type, and codegen now maps `DA.Types.Either a b` onto it.** Previously a Daml field or choice type of `Either a b` emitted a bare `Either<TL, TR>` with no definition or `using`, so any DAR using `Either` (e.g. `canton-ping`) failed to compile (`CS0246`). `Either` is now a parametric stdlib type: `Either<TL, TR>` is an abstract record with `Left`/`Right` cases, round-tripping through `DamlVariant` via `ToValue`/`FromValue`. Generated code references it as `Daml.Runtime.Stdlib.Either<…>`.
- **`ghcr.io/peacefulstudio/dpm-codegen-cs` OCI bundle contract is now codified in ADR 0005**. Anyone integrating with the artifact directly — not via `dpm` — gets a versioned contract for the bundle layout (top-level `component.yaml`, `bin/<exe>`, `bin/<jar>`), the per-layer OCI media type (`application/vnd.component.file`), the required `network.canton.dpm.file-{mode,modtime,name}` annotations (`file-name` is the relative path inside the bundle, not the basename), and the consumer `daml.yaml` shape (`components: ["oci://…"]`, no `sdk-version:` alongside, never the dead-code `override-components: <name>: image-tag:`). Stock `dpm ≥ 1.0.12` required on the consumer side; `dpm 1.0.16` is what our workflows pin. Public-package consumers must NOT `docker login ghcr.io` with `${{ secrets.GITHUB_TOKEN }}` — anonymous pull is the supported path.
- **`dpm codegen-cs` is now distributable as a multi-arch OCI artifact at `ghcr.io/peacefulstudio/dpm-codegen-cs`**. A self-contained single-file C# emitter binary is built for `linux/amd64`, `linux/arm64`, `darwin/arm64`, and `windows/amd64`, each pushed as its own OCI artifact and composed into a multi-arch index under `ghcr.io/peacefulstudio/dpm-codegen-cs:<version>`. Stock `dpm` fetches the right RID lazily on first invocation using its `<os>/<arch>=<path>` asset-selection syntax — no host .NET runtime required on the consumer side; a host JDK is the only runtime precondition.
- **4-part `M.m.p.r` NuGet versioning** per ADR 0002, exposed as the new `Daml.Codegen.CSharp.Versioning` namespace. Segments 1–3 of a generated package's NuGet version are the DAR-intrinsic `Major.Minor.Patch`; segment 4 (`r`) is a monotonic emitter counter that disambiguates content-identical re-emissions of the same DAR-intrinsic version under different emitter versions. New consumer-facing API: `SpliceNuGetVersion.Compute(packageName, intrinsicVersion, contentHash, counterStore)` returns the canonical 4-part `FourPartPackageVersion`. The counter is persisted in a JSON file (`JsonReleaseCounterStore.OpenOrCreate(path)`) keyed by `{packageName}@{M.m.p}`; first emission of a (package, intrinsic-version) pair returns `r=0`, identical re-emissions hold the revision steady, and any content change bumps it. `IntermediatePackageContentHash.Compute(IntermediatePackage)` returns the stable SHA-256 over the deterministic protobuf encoding for use as the content-hash input. The NuGet packing step consumes this API; consumers see the new 4-segment versions on the wire.
- **Codegen now emits a buildable `.csproj` and packs a NuGet package per Daml package**. Generated projects carry the `M.m.p.r` 4-part version from ADR 0002 (Daml package version supplies segments 1–3; the 4th segment is the emitter counter, defaulting to `0` for the first emission), declare `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` (configurable via the new `--package-license <SPDX>` CLI flag / `CodeGenOptions.PackageLicenseExpression` for non-Apache DARs), and reference `Daml.Runtime` and `Daml.Ledger.Abstractions` so consumers can run `dotnet add package <Pkg>` + `dotnet build` against an unmodified output tree. A new `--emitter-counter <int>` CLI flag on `daml-codegen-csharp` (validated at the boundary to reject negatives) exposes the 4th-segment override; local-dev invocations leave the default in place.
- `Daml.Codegen.CSharp.IntermediateDarReader.Read(IntermediateDar)` — proto-to-model adapter; the new public API surface for emitter consumers. Throws `InvalidDataException` fail-fast on malformed input (missing data-type shape, missing choice `argument_type` / `return_type`, unknown proto sort, `BUILTIN_TYPE_UNSPECIFIED`) and `NotSupportedException` on intentionally-deferred builtins; no silent fallback to `Unit` or empty record.
- `Daml.Codegen.CSharp.Model.DarModel` and `Daml.Codegen.CSharp.Model.IDarSource` — the emitter input contract. `CSharpCodeGenerator.Generate` now takes `IDarSource`, satisfied by `DarModel` (proto-direct).
- `Daml.Codegen.CSharp.ICodegenLogger` — minimal logging contract that `CSharpCodeGenerator` now depends on. `ConsoleLogger` implements it; tests and host applications can supply alternative implementations without taking a console dependency.
- `scripts/codegen-pipeline.sh` — orchestration shim that runs the codegen pipeline end-to-end. Stands in for the `dpm codegen-cs` OCI bundle entry point.

### Changed — BREAKING

- **`Daml.Codegen.CSharp` is now a pure emitter library.** It consumes an `IntermediateDar` proto and emits `.cs`. The legacy `dotnet tool` CLI surface (`PackAsTool`, `daml-codegen-csharp` command) is removed from `Daml.Codegen.CSharp`. A new thin CLI project `Daml.Codegen.CSharp.Cli` produces the binary `daml-codegen-csharp`; it accepts `--intermediate <proto-path>`. The CLI publishes as a self-contained single-file native binary per RID — `dotnet publish src/Daml.Codegen.CSharp.Cli -c Release -r <rid> --self-contained true -p:PublishSingleFile=true`.
- **The typed-`actAs` codegen path is now enabled by default**. Previously party analysis was `Dynamic` on every template and choice, so `CreateAsync` and `<Choice>Async` wrappers always required a `SubmitterInfo` parameter. Static party-expression analysis now runs over `signatories` / `observers` / `controllers` / `choiceObservers`, and generated `*SubmissionExtensions.cs` emits the `Signatories(payload)` and `Observers(payload)` helpers and derives `actAs` from the payload for templates whose signatories are payload-field projections — the dominant idiom across Splice's Amulet, Token Standard, and Synfini templates.

### Changed

- **`IntermediateDar` proto schema gained `Template.signatories`, `Template.observers`, `Choice.controllers`, `Choice.observers`, and a new `PartyAnalysis { Static | Dynamic }` message**. All four new fields are forward-compatible (absent on older proto bytes is read as `Dynamic`). Proto consumers other than `IntermediateDarReader` that interpret the wire format should add a case for `PartyAnalysis`.
- **Generated code now emits the runtime `Party` type unqualified (bare `Party`) on packages whose namespaces don't shadow it**, matching every other runtime type's qualification policy. Previously `Party` was hard-coded as `global::Daml.Runtime.Data.Party` at every emission site, so it always carried the `global::` prefix even when nothing shadowed it — the lone runtime type that bypassed the collision-aware qualifier. It now routes through that qualifier like `ContractId`, `IDamlValue`, `Choice`, and the rest: bare on non-colliding packages, still `global::`-qualified when a generated namespace segment shadows it (e.g. a package deriving a namespace ending in `.Party`). Output for shadowing packages is unchanged; non-shadowing packages see `global::Daml.Runtime.Data.Party` become `Party` in field types, record/choice parameters, `IHasKey<>` keys, and value-decoder expressions.
- **Generated code now emits the `Daml.Runtime.Stdlib.*` types (`Either`, `Tuple2`/`Tuple3`, `Set`, `NonEmpty`, `Map`, `RelTime`, `Unit`, `GenericStub`) as bare type names under a new `using Daml.Runtime.Stdlib;` import**, qualifying with `global::` only when a generated namespace segment shadows the name. Previously these stdlib types were written fully qualified (`Daml.Runtime.Stdlib.RelTime`, `Daml.Runtime.Stdlib.Tuple2<…>`, …) at every emission site — the lone remaining family that bypassed the collision-aware qualifier. They now route through it like every other imported type: bare on non-colliding packages (e.g. `splice-api-token-holding-v1`'s `Lock.ExpiresAfter` becomes `RelTime?` under `using Daml.Runtime.Stdlib;`), still `global::`-qualified when a generated namespace segment shadows the name — fixing the `CS0118` namespace-shadowing bug class for stdlib types and completing the central-qualifier work. The using requirement is also package-gated: a user package that defines its own `DA.Types:Tuple2` (or another stdlib-named type) is rendered under its own namespace and no longer emits an unused `using Daml.Runtime.Stdlib;`, so it is not surfaced as `CS8019` under consumer `<TreatWarningsAsErrors>`.
- Generated files now emit only the `using` directives their body actually references, tracked per-file at codegen time — each namespace is required at its actual emit site (e.g. `System.Collections.Generic` only when a list/map field appears, `Daml.Runtime.Contracts` only when a template, interface, or contract-ID type is emitted, `System` only when `Version`, `DateTimeOffset`, etc. appear). The `#pragma warning disable CS8019` header that previously suppressed unused-using warnings in every file has been removed; no generated file emits an unused `using`. Consumers with `<TreatWarningsAsErrors>` no longer need a workaround, and IDEs get accurate import lists.

### Fixed

- **Generated code no longer fails to compile with `CS0118` when a Daml package or module name derives a C# namespace segment that collides with an imported runtime or BCL type name.** Previously a namespace ending in (e.g.) `Party` bound the bare `Party` identifier to the namespace rather than the runtime struct, failing the consumer build with `CS0118: 'Party' is a namespace but is used like a type` (`canton-party-replication-alpha` is the motivating real-world case). The emitter now `global::`-qualifies an imported simple type name only when a generated namespace would actually shadow it, so output for non-colliding packages (e.g. `splice-api-token-holding-v1`) is byte-identical — no churn. Coverage spans the runtime value/type family (`Party`, `ContractId`, `ITemplate`, `IHasKey`, `IDamlValue`, `Choice`, `SubmitterInfo`, `Identifier`, `ExerciseOutcome`, `TransactionResult`, `ILedgerClient`, the `Daml*` value types, …) and BCL types (`IReadOnlyList`, `IReadOnlyDictionary`, `HashSet`), across both type positions and expression/value positions (e.g. `new Identifier(...)`, `.As<DamlParty>()`, `DamlRecord.Create(...)`) as well as XML-doc crefs.
- **The key-bearing template's XML-doc `<see cref>` for `IHasKey<>` is now well-formed under `<GenerateDocumentationFile>`**, closing a `CS1584`/`CS1658` doc-build warning. The generated `Key` property doc previously embedded the rendered key type inside the cref braces (`<see cref="...IHasKey{IReadOnlyList{string}}"/>`, or `IHasKey{global::Daml.Runtime.Data.Party}` in a `Party`-shadowing namespace) — a constructed type in cref braces, which Roslyn rejects as a syntactically incorrect cref. The cref now targets the open generic by its declared type-parameter name (`<see cref="global::Daml.Runtime.Contracts.IHasKey{TKey}"/>`) and the concrete key type is rendered as prose (`Gets the contract key of type <c>…</c>`).
- **`DamlJsonSerializer.Serialize(DamlUnit.Instance)` no longer throws `ArgumentException`** and now returns `"{}"` per the Daml-LF JSON encoding for Unit. The serializer's `ValueToJsonNode` branch for `DamlUnit` wrapped a `JsonObject` in `JsonValue.Create(...)`, which only accepts primitive values and rejects any `JsonNode` — every attempt to serialize a `DamlUnit` (standalone or via the `DamlValueJsonConverter` path used by the top-level `Serialize(DamlValue)` entry point) threw at runtime.
- **Generated files now emit `using Daml.Runtime.Contracts;` for every `ContractId<T>` reference**, not only top-level record fields. Two emit sites previously skipped the required-using pass and produced files that referenced `ContractId<T>` without importing its namespace — a variant constructor whose argument type contained a `ContractId` (e.g. `Splice/Api/Token/Metadata/V1/AnyValue.cs` from `splice-api-token-metadata-v1`), and any record field whose type wrapped a `ContractId` inside a parametric stdlib or user-defined generic type (e.g. `Set (ContractId T)`, `Tuple2 (ContractId T) Int`). Both shapes appeared in 7 of 22 Splice 0.6.5 DAR families and surfaced as `CS0246: The type or namespace name 'ContractId<>' could not be found` at consumer build time.
- **`DamlJsonSerializer.Deserialize` now parses date and timestamp strings under `CultureInfo.InvariantCulture`**, closing a round-trip asymmetry with the serialize side, which already pinned `InvariantCulture` and emits ISO-8601 (`yyyy-MM-dd` for `DamlDate`, `"O"` for `DamlTimestamp`). Previously `InferStringValue` called `DateOnly.TryParse(s, out _)` and `DateTimeOffset.TryParse(s, out _)` without an explicit culture, falling back to `CurrentCulture`. Under cultures whose default calendar is non-Gregorian (`th-TH`, `fa-IR`, `ar-SA`, …) the ISO date string was reinterpreted in the host calendar — e.g. `"2026-05-26"` round-tripped to `1483-05-26` under `th-TH` — silently corrupting `DamlDate` values across the wire. `DamlTimestamp` parsing was unaffected in practice (the `"O"` shape parses universally) but is pinned for symmetry.
- **`DamlJsonSerializer.Serialize` now handles `DamlGenMap`** instead of throwing `NotSupportedException`. `DamlGenMap` is the wire-level backing for Daml `GenMap k v` and underpins the `Daml.Runtime.Stdlib.Map<K, V>` and `Set<T>` stdlib wrappers, both of which appear pervasively in the Splice Amulet and Wallet DARs (e.g. `Map Party Int` beneficiary lists, `Set Party` membership). The serialized shape is a JSON array of two-element `[key, value]` arrays, matching the Daml-LF JSON encoding for `GenMap`.
- **`DamlJsonSerializer.DeserializeRecord` and the top-level `DamlJsonSerializer.Deserialize` now both reconstruct `DamlGenMap`** from the same `[[key, value], ...]` wire shape the serializer emits, closing the round-trip asymmetry whereby `Deserialize(Serialize(genMap))` previously collapsed to a `DamlList` of two-element `DamlList`s. The `DamlValueJsonConverter` used by the top-level `Deserialize` / `Serialize` entry points now delegates to the same canonical mappers as `DeserializeRecord` / `Serialize(DamlRecord)`, removing a duplicated and divergent traversal that also disagreed on string→date inference, Variant null handling, and infinite-recursed on `Serialize(DamlValue)`. The heuristic is documented on the public `Deserialize` XML doc and is necessarily lossy for three untyped-JSON edge cases — a `List (List a)` whose inner lists all happen to be length 2 is reinterpreted as a `DamlGenMap`; an empty `[]` always resolves to an empty `DamlList` (never an empty `DamlGenMap`); and a pair with a `null` first element falls back to the list path and surfaces the original "Null array elements not supported" error rather than a misleading GenMap-key error. Callers needing exact round-trips for those shapes must deserialize against a type schema.
- **`DamlJsonSerializer` now formats `Numeric`, `Date`, and `Timestamp` values under `CultureInfo.InvariantCulture`**, so wire-format output is identical regardless of the host's `CurrentCulture`. Previously `DamlNumeric` rendered with the current-culture decimal separator (e.g. `"123,456789"` under `fr-FR`), and `DamlDate` / `DamlTimestamp` could pick up calendar-specific formatting under cultures whose default calendar is not the Gregorian one. This is required for round-tripping through PQS and the JSON Ledger API, both of which expect invariant-formatted scalars.
- **`IsArchiveChoice` filter now gates on the stdlib package id**, not just the choice name and module path. Previously a user-defined choice named `Archive` whose argument type referenced `DA.Internal.Template:Archive` would be falsely suppressed by the non-CID wrapper emitter, so the generated code was missing an `ArchiveAsync` extension on the template's contract id. The filter now mirrors the `IsParametricStdlibTypeRef` pattern: the argument type's `PackageId` must resolve through the current archive to a Daml stdlib package (`daml-prim` / `daml-stdlib` / `ghc-stdlib`); otherwise the choice flows through and a typed wrapper is emitted.
- **Choice-argument types are now emitted fully qualified when referenced by sibling records or variant constructors**. Choice-arg types (e.g. `MergeDelegation_Merge`, `DsoRules_AddSv`) are nested inside their parent template class in the generated output; any reference from outside that template — a sibling record field, a variant constructor parameter, or a cross-package variant — was previously emitted as a bare or namespace-only name that the C# compiler could not resolve (CS0246 / CS0234). The codegen now qualifies such references as `TemplateName.ChoiceArgTypeName` (same-package) or `ForeignNamespace.TemplateName.ChoiceArgTypeName` (cross-package). No consumer action required beyond re-running the codegen; the fix resolves the Splice `MergeDelegationCall` and `DsoRules_ActionRequiringConfirmation` compilation failures.
- **Interface and template-extension XML-doc `<see cref>` tags are now `global::`-qualified**, closing a `CS1574` doc-build warning under `<GenerateDocumentationFile>` for packages whose namespace is rooted at `Daml.*`. Five `<see cref="Daml.Runtime.*"/>` / `<see cref="Daml.Ledger.*"/>` strings were emitted bare into generated interface-extension and template-extension class docs; Roslyn resolves `Daml.Runtime.*` relative to the enclosing `Daml.*` namespace and fails with `CS1574: XML comment has cref attribute that could not be resolved` on a package such as `daml` (namespace `Daml.*`). All five crefs now carry `global::`.
- **`FromRecord` for `TextMap`/`GenMap`-of-`List` fields no longer emits a non-compilable `Dictionary<K, List<V>>`** — the value projection lambda is now cast to `IReadOnlyList<V>` so `ToDictionary` infers `Dictionary<K, IReadOnlyList<V>>`, which does implement `IReadOnlyDictionary<K, IReadOnlyList<V>>`. Without the cast, C# generic invariance caused CS1503 in consumer builds whenever a generated record had a field of Daml type `TextMap (List a)` or `GenMap k (List v)` (surfaces in, for example, `WalletUserProxy_BatchTransferResult.SenderChangeMap`). The same cast is also emitted for top-level `List` fields and `Choice` result decoders, ensuring consistency across all deserialization paths.

## [0.1.5] — 2026-05-03

### Changed — BREAKING

- **Contract-key `Key` property is now a `partial` declaration** instead of a stub that throws `NotImplementedException` at runtime. The codegen still detects keys and emits `: IHasKey<TKey>`, but the property body is now supplied by a hand-rolled `partial` in the consuming project until the full DALF key-expression analysis (mapping the template's `key` Daml expression back to template fields) lands. This shifts the failure mode from runtime (throwing on first `Key` access) to compile time (Roslyn `CS9248` on the consumer build until the implementing partial is supplied) — impossible to ship to production unnoticed. Consumers must add an implementing partial alongside the generated template, **inside whatever namespace the generated `Foo.cs` declares**. By default that namespace is derived from the Daml package name (e.g. `My.Daml.Package`); if you override it with `--namespace` (CLI) or `CodeGenOptions.RootNamespace` (library), match the override exactly. Open the generated `Foo.cs` to confirm the namespace before writing your partial:
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
  The implementing partial's type kind must match the generated type kind: if you configure the codegen with `UseRecordTypes=false`, the generated template is a `public sealed partial class` and the implementing partial must also be a `partial class` (not `partial record`). Requires C# 13 on the consumer side, which means **.NET 9 SDK or later on the build machine** even when the consumer's `<TargetFramework>` is `net8.0` — the C# compiler is shipped with the SDK, not the target runtime, so a build host with only the .NET 8 SDK installed cannot parse the generated partial-property syntax. The codegen-emitted `.csproj` pins `<LangVersion>13</LangVersion>` only for packages that actually contain a key-bearing template, so key-less DARs continue to build with whatever LangVersion the consumer's SDK defaults supply. Unblocks consumers to opt into typed key fetch / exercise wrappers (`Foo.FetchByKeyAsync`, `Foo.<Choice>ByKeyAsync` against `IPqsClient` / `ILedgerClient`) without inheriting a throwing default. Full ByKey wrapper emission is tracked separately.
- **Unresolvable cross-package type references now throw at codegen time
  instead of warning and emitting unqualified names.** `ResolveTypeRefName`
  used to log a warning and return the bare sanitised name when the
  referenced foreign package was missing from the DAR (or no archive
  context was available); the consumer's `dotnet build` then surfaced a
  generic CS0246 with no pointer back to the cause. Codegen now throws
  `InvalidOperationException` naming the offending module / package id
  and suggesting a remediation (rebuild the DAR with the missing package
  included, or pass a multi-DAR input that resolves it). **Migration:**
  consumers who previously got a successful codegen run with warnings,
  then a downstream CS0246 build failure, will now get a codegen-time
  exception instead. The fix is the same — bundle the missing foreign
  package — only the failure point moves earlier. The unmapped-stdlib
  fallback (`MapStdlibType` returns null for an unknown stdlib type)
  still warns and returns unqualified pending full stdlib coverage.

### Added

- **Per-template `<TemplateName>SubmissionExtensions`** static class emitted
  alongside every generated template. Provides a typed `CreateAsync` extension
  that lifts the static-analyzer's signatory analysis into the C# call site.
  When every Daml signatory is a payload-field reference (the canonical
  `signatory platform, initiator, counterparty` shape against same-named
  `Party` fields), the generated `CreateAsync` takes only the payload and an
  `ILedgerClient` — the wrapper builds a `SubmitterInfo` from the payload's
  `Party` properties so the caller never restates a party that's already in
  the record. When the analyzer can't statically resolve the signatory
  expression, the wrapper takes an explicit `SubmitterInfo submitter`
  parameter (which implicitly converts from `string` / `Party`). Templates
  whose `observer` expression is statically resolvable also expose an
  `Observers(payload)` helper returning the derived observer party set from
  the payload.
- **`Daml.Runtime.Data.SynchronizerId`** — `readonly record struct` mirroring
  `Party`'s shape (null/whitespace-guarded constructor, `Id` accessor with
  default-uninitialized throw, implicit `→ string` conversion, explicit
  `string →` cast, JSON converter that round-trips as a plain string).
  Stored as an opaque string per Canton's documented guidance — does not
  decompose into name / fingerprint / protocol-version components, so the
  wrapper is safe across the Canton 3.4 (`name::fingerprint`) → 3.5
  (`name::fingerprint::protocol-version`) wire-format change.
- **`Daml.Runtime.Stdlib` stubs for `DA.Types.Tuple2` / `Tuple3`,
  `DA.Set.Types.Set`, `DA.NonEmpty.Types.NonEmpty`, and
  `DA.Map.Types.Map` / `DA.Internal.Map.Map`.** Each stub is a generic
  `record` with delegate-based `ToRecord` / `FromRecord` so the codegen
  can round-trip arbitrary CLR generic arguments (e.g. `Tuple2<Party, long>`)
  through the Daml-LF wire shape. The codegen now routes references to
  these types in `daml-prim` / `daml-stdlib` packages to the
  `Daml.Runtime.Stdlib.*` types and emits inline conversion lambdas at
  the call site. Unblocks `splice-token-test-trading-app` end-to-end and
  removes the `default! /* TODO */` decoder fallbacks for these types
  in `splice-amulet`, `splice-dso-governance`, `splice-wallet`,
  `splice-wallet-payments`, and `splice-util-featured-app-proxies`.
- **`Daml.Runtime.Contracts.ExercisedEvent`** — pure-data record describing a
  choice-exercise event observed in a transaction. Captures the subset of
  the Ledger API `ExercisedEvent` proto that the C# codegen needs to
  project typed choice results: wire-level `ChoiceArgument` and
  `ExerciseResult` (as `DamlValue`) plus `ContractId`, `TemplateId`,
  `InterfaceId?`, `ChoiceName`, `Consuming`, `ActingParties`, and
  `WitnessParties`. Other wire fields (event/node identifiers, package
  name, descendant tracking, implemented-interface lists) are intentionally
  omitted — they can be added later if a use case appears. Replaces an
  earlier internal change.
- **`TransactionResult.ExercisedEvents`** — new
  `IReadOnlyList<ExercisedEvent>` init-only property, defaults to an empty
  list. Lets codegen-emitted choice wrappers walk
  `ExercisedEvent.ExerciseResult` through a typed deserializer to project a
  typed `ExerciseOutcome<TResult>` for choices whose return type is not a
  contract id (e.g. `choice GetTrailingTwap : Decimal`). Additive only —
  existing 4-arg construction continues to compile and the property
  defaults to empty until a ledger-client transport implementation
  populates it.
- **`Daml.Runtime.IDamlType` marker interface** — common base for Daml-derived
  C# types. `Daml.Runtime.Contracts.ITemplate` and
  `Daml.Runtime.Contracts.IDamlInterface` both extend it. Lets generic helpers
  that don't dispatch on template-specific static metadata (`T.TemplateId`)
  constrain on the broader marker and accept either a concrete template or an
  interface marker. Additive only — existing `where T : ITemplate` constraints
  continue to compile unchanged. Replaces an earlier internal change that
  became stale-by-relocation when these types lifted to `Daml.Runtime`.
  Unblocks the in-flight interface-markers work.
- **Daml interface markers, first-class** — `ContractId<T>`'s constraint
  is relaxed from `where T : ITemplate` to `where T : IDamlType` (see above)
  so codegen-emitted interface markers (e.g. `IHolding` from the Splice
  token standard) flow through the typed contract id without the placeholder
  hack. `ContractId<T>.ToDamlValue()` resolves the embedded identifier per
  closed generic — `TemplateId` for templates, `InterfaceId` for interface
  markers — via reflection on the static virtual member.
  `ContractIdInterfaceCoercion.ToInterfaceContractId<TConcrete, TInterface>`
  extension method mirrors Daml's `toInterfaceContractId @I cid` at the
  C# type level, gated by `IImplements<TInterface>` on the source template
  so a coercion to an interface the template doesn't implement does not
  compile. `ExerciseCommand.ForInterface<TInterface>(cid, choice, arg)`
  builds an interface-typed exercise command — the wire-level `template_id`
  slot carries the interface id per Canton's `commands.proto` semantics.
- **Codegen-emitted interface choice exercisers** — for every Daml interface
  with one or more choices, the generated `IFoo.cs` file now also contains a
  sibling static `IFooExtensions` class with one `<Choice>Async`-style helper
  per choice. Callers can `cid.TransferAsync(arg)` on a `ContractId<IHolding>`
  without naming the concrete implementing template. Built via the new
  `ExerciseCommand.ForInterface<I>` runtime helper.
- **Typed `<Choice>Result` records and `FromCreatedContracts` projectors** for
  every Daml choice whose return type carries one or more `ContractId T`
  references. Choice creates a single template → single field; `Optional` →
  nullable field; `[…]` (list) → `IReadOnlyList<ContractId<T>>`; tuples are
  flattened across components. The static `FromCreatedContracts(IEnumerable<CreatedContract>)`
  projector returns `ExerciseOutcome<<Choice>Result>.One` when every required
  slot has the expected count, `.None` when a single-cardinality slot's
  template is missing, and `.Many` when a single- or optional-cardinality
  slot has more than one. Template matching is by `(ModuleName, EntityName)`
  only, so package-id drift from upgrades doesn't break projection.
- **`<Choice>Async(...)` extension methods on `ContractId<TemplateName>`** —
  one per create-bearing choice on each template, in a per-template static
  `<TemplateName>Extensions` class. The static-analyzer drives the parameter
  shape: when every controller is a payload-field reference, one named
  `Party` parameter per controller (declaration order) appears on the method,
  and the wrapper unions them into a `SubmitterInfo.actAs` set; when the
  template's `observer` clause (and/or the choice's `observer`) is also
  statically resolvable, those parties are added to `SubmitterInfo.readAs`
  so the submission carries the correct read-as set. When the controllers
  aren't statically resolvable, the wrapper falls back to a single
  `SubmitterInfo submitter` parameter. Body builds a `CommandsSubmission`,
  calls `ILedgerClient.TrySubmitAndWaitForTransactionAsync`, projects success
  via `<Choice>Result.FromCreatedContracts`. `DamlError` and `InfraError`
  outcomes pass through with all fields preserved. Workflow id has no
  default — workflow IDs are correlation keys, and a per-choice constant
  would bucket every submission of the same choice under one id and break
  observability.
- **`Daml.Ledger.Abstractions` `<PackageReference>` in generated csproj** —
  added unconditionally alongside `Daml.Runtime`. The package is
  interface-only and lockstep-versioned with the runtime, so pure-projector
  consumers absorb it at zero transitive weight. Required by the emitted
  `<Choice>Async` extension methods, which take `ILedgerClient`.
- **`PartyExpressionAnalyzer`** in `DarReader` — walks a Daml-LF expression
  rooted at a `List Party`-typed value (the shape carried by template
  `signatories` / `observers` and choice `controllers` / `observers`) and
  resolves it to an ordered list of payload-field references. Falls back to
  a single `Dynamic` marker on any unsupported shape (function calls,
  variable references, key projections), which surfaces as an explicit
  `SubmitterInfo` parameter in the generated wrapper. Recognizes `Cons`
  chains of `RecProj(template_param, fieldName)` and dereferences
  interned-expression nodes (LF 2.dev+). Distinguishes a static empty list
  (`[]`) from a `Dynamic` verdict so codegen can skip emission of helpers
  whose result would always be empty.
- **`DamlPartyAnalysis` / `DamlPartyReference`** model types on
  `DamlTemplate.Signatories`, `DamlTemplate.Observers`,
  `DamlChoice.Controllers`, and `DamlChoice.Observers`. Public so consumers
  (and tests) can inspect the analyzer's verdict before codegen runs.
  `DamlPartyPayloadField(string FieldName)` is the only resolved shape
  today; future shapes (constants, key projection) live behind their own
  records.
- **`PackageContext.GetInternedExpr(int)`** — resolves an interned-expression
  index against the package's `InternedExprs` table. Used by the static
  analyzer to dereference nodes in LF 2.dev+ packages.
- **Typed exerciser wrappers for non-contract-id choice returns**. For
  every choice whose declared return type carries no `ContractId T` slot at the
  top level (`Decimal`, `()`, records *via type-ref*, lists/optionals/tuples
  of primitives, etc.), codegen now emits a
  `<Choice>Async(this ContractId<TemplateName>, ILedgerClient, <args>, Party actAs, ...)`
  extension method on a `<TemplateName>NonContractExtensions` static class. The
  method calls `ILedgerClient.TrySubmitAndWaitForTransactionAsync`, walks the
  resulting `tx.ExercisedEvents` for
  the matching choice, runs the already-emitted
  `Choice<Choice>.ResultDecoder` over its `DamlValue` exercise result, and
  returns `Task<ExerciseOutcome<TReturn>>`. `DamlError` and `InfraError`
  outcomes pass through unchanged. Returns that expose at least one
  `ContractId T` slot at the top level — bare `ContractId T`,
  `Optional (ContractId T)`, `[ContractId T]`, and tuples with `ContractId`
  components — continue to flow through the `<TemplateName>Extensions` class
  and the slot-based projector. Records (referenced by name) whose fields
  happen to contain `ContractId`s also stay on the new wrapper path because
  the slot extractor intentionally does not unfold record types.
- **`Daml.Runtime.Stdlib.Unit`** — single-inhabitant marker (`Unit.Value`) that
  codegen surfaces at the call site for `()`-returning choices, mirroring
  `System.ValueTuple` semantics. Distinct from the wire-level
  `Daml.Runtime.Data.DamlUnit`: `Unit` is the typed return; `DamlUnit` is its
  wire encoding.
- **`Daml.Runtime.Commands.SubmitterInfo`** value type carrying the `actAs`
  (authorizing) and `readAs` (read-only visibility) party sets that propagate
  to `Commands.act_as` / `Commands.read_as` on the wire. `ActAs` is validated
  non-empty (throws `ArgumentException`); each caller-supplied party set is
  snapshotted into an immutable `FrozenSet<Party>` at construction (so caller
  mutations after the fact don't bleed in, and a consumer who casts the
  exposed `IReadOnlySet<Party>` back to a concrete type still can't mutate it),
  and any default-`Party` entry is rejected at construction time so the
  invariant fails loud rather than at later serialization.
  `Equals`/`GetHashCode` are overridden to compare by set contents
  (order-independent) rather than the record-struct-synthesized reference
  comparison on the backing fields. Implicit conversions from `string` and
  `Party` preserve the single-party ergonomic at every call site.
  Canonical home for the type: `Daml.Runtime` already owns `Party`, so command
  submitters belong here too. Foundation for the upcoming `SubmitterInfo`
  overloads on `Daml.Ledger.Abstractions.ILedgerClient` and the named-signatory
  codegen surface.
- **`CommandsSubmission.WithSubmitter(SubmitterInfo)`** helper — sets both
  `ActAs` and `ReadAs` from a typed submitter in one call. The preferred
  projection point for code-generated and library callers; mirrors the wire
  shape exactly.
- **`SubmitterInfo` overloads on `Daml.Ledger.Abstractions.ILedgerClient`**
  for `ExerciseAsync` (both result and void), `TryCreateAsync`,
  `TryExerciseForCreatedAsync`, `SubscribeAsync`, and `SubscribeActiveAsync`.
  Multi-party submitters (`ActAs.Count > 1`) and submitters carrying any
  `ReadAs` parties become expressible at the abstraction surface alongside
  the existing single-party `string actAs` overloads. Default-interface-method
  implementations preserve source compatibility with existing implementers:
  single-party submissions delegate to the legacy `string actAs` overload,
  multi-party submissions throw `NotSupportedException` until the implementation
  overrides the SubmitterInfo overload (replaces an earlier internal change;
  foundation for named-signatories codegen).

### Changed — generated code shape

- Generated template `.cs` files now declare additional `using` directives
  unconditionally: `Daml.Ledger.Abstractions`, `Daml.Runtime.Outcomes`,
  plus the BCL set (`System`, `System.Collections.Generic`,
  `System.Threading`, `System.Threading.Tasks`) so generated source compiles
  without `<ImplicitUsings>` enabled in the consumer csproj. References to
  the new `Daml.Runtime.Stdlib` types (e.g. `Stdlib.Unit`,
  `Stdlib.Tuple2<…>`) are written fully qualified at the use site so no
  `using Daml.Runtime.Stdlib;` is emitted, avoiding spurious IDE0005 / CS8019
  failures in consumer projects with `TreatWarningsAsErrors`.

### Changed — BREAKING

- **`Daml.Runtime.Streams.ContractStreamEvent<T>.{Created, Archived, Exercised, Assigned, Unassigned}.WitnessParties`**
  changes from `IReadOnlyList<string>` to `IReadOnlyList<Party>` for sibling
  consistency with `Daml.Runtime.Contracts.{CreatedEvent, ArchivedEvent,
  ExercisedEvent}` and the broader project trend toward typed party values
  (`Party` instead of bare `string`). Consumers comparing or pattern-matching
  on these collections need to migrate `string` accesses to `Party.Id` (or
  use the `Party` value directly via `Equals`). **Implementor
  obligation**: a ledger-client transport implementation must construct
  `WitnessParties` as `Party` in its stream-projection code (rather than
  from proto `string` directly) before consuming the new `Daml.Runtime`
  version.
- **`ContractStreamEvent<T>.Assigned.{Source, Target}`** and
  **`Unassigned.{Source, Target}`** change from `string` to
  `Daml.Runtime.Data.SynchronizerId`. Same migration shape as the
  `WitnessParties` change above. Same implementor obligation:
  a ledger-client transport implementation must construct `Source` /
  `Target` as `SynchronizerId` in its reassignment-event projection
  (rather than from proto `string`) before consuming the new
  `Daml.Runtime` version.

### Fixed

- **`WriteChoiceMethod` now skips emission for choices with a fallback
  `<Choice>Arg` argument type.** Previously emitted code referenced
  `arg.ToRecord()` against a stub record with no `ToRecord()` method,
  breaking consumer compilation in those edge cases.
- **Cross-package choice argument types now resolve to their fully-qualified
  C# name instead of being silently dropped to `DamlUnit`.** Previously a choice
  whose argument was a `DamlTypeRef` pointing into a neighbouring package
  (e.g. a Splice DAR's choice taking a record imported from another splice
  package) ran through `GetChoiceArgumentInfo`'s "Other external references —
  fallback to DamlUnit as safe default" branch — the wrapper compiled, but
  the encoded payload was an empty unit and the user's record was lost on
  the wire. The defensive filters added by the non-CID exerciser and the
  interface choice extension skipped emission entirely for those choices, so
  callers got a missing wrapper instead of a wrong one — better, but still
  wrong. `GetChoiceArgumentInfo` is now instance-level and routes
  non-Archive `DamlTypeRef` arguments through the same `ResolveTypeRefName`
  pipeline already used for record fields and return types. Wrappers across
  all five emit sites (`WriteSingleChoiceAsyncExerciser`,
  `WriteSingleNonContractChoiceAsyncExerciser`,
  `WriteInterfaceChoiceExtensionMethod`, `WriteChoiceArgumentType`,
  `WriteChoiceMethod`) now emit `{ResolvedNs}.{Record} argument` and
  `argument.ToRecord()` for cross-package shapes, and the defensive filters
  are gone. Consumers must run codegen on every package referenced by a
  choice argument so the resolved C# name is available at compile time —
  the standard multi-DAR codegen flow already does this. The
  companion behaviour change — `ResolveTypeRefName` now throws on
  unresolvable cross-package refs instead of warning and silently
  emitting unqualified names — is captured under `Changed — BREAKING`
  above.
- **Nested `()` in non-CID choice returns now surfaces as
  `Daml.Runtime.Stdlib.Unit` end-to-end.** Previously, a choice declared as
  `choice Foo : Optional ()`, `choice Foo : [()]`, or `choice Foo : TextMap ()`
  produced an async wrapper signed as `ExerciseOutcome<DamlUnit?>` /
  `ExerciseOutcome<IReadOnlyList<DamlUnit>>` / etc. — leaking the wire-level
  `DamlUnit` into the public API. The codegen now recurses through Optional,
  List, TextMap, and GenMap nesting and substitutes
  `Daml.Runtime.Stdlib.Unit` at every Unit slot. The projector emits a
  parallel inline decoder so the wire-typed `Choice<T,A,R>.ResultDecoder`
  doesn't type-mismatch against the public-surface signature. Limitation:
  parametric stdlib types (`Tuple2 a ()`, etc.) and user-defined parametric
  records with `()` components are not rewritten — those decode through
  `FromRecord`, which isn't pluggable per type-arg. Their public-surface
  type still names `DamlUnit` in the type-args, so consumers who pattern-
  match against `Daml.Runtime.Stdlib.Unit` for those positions will see a
  compile-time type mismatch at the call site. Very rare in practice;
  documented in `MapNonContractReturnType`'s doc-comment.
- **Generated `.cs` files no longer trip CS8019 in consumers with
  `<TreatWarningsAsErrors>`.** `WriteUsings` emits a fixed BCL set
  unconditionally so generated code compiles against consumers with
  `<ImplicitUsings>` disabled — but record-only files don't reference every
  using, and Roslyn doesn't suppress CS8019 ("unnecessary using directive")
  on `<auto-generated>` sources, so warnings-as-errors builds failed on the
  generator's own output. The file header now declares
  `#pragma warning disable CS8019` to mute the warning at source.
  Per-file conditional using emission (so the pragma can eventually be
  dropped) is tracked separately — non-urgent.

## [0.1.4] — 2026-05-01

### Added

- **`Daml.Ledger.Abstractions` (new package)** — transport-agnostic
  `ILedgerClient` interface lifted from an internal gRPC ledger bridge.
  Implementations live in their respective transport packages: a
  ledger-client transport implementation (gRPC) and a planned HTTP REST
  client.
  Generated codegen output (projector helpers, `<Choice>Async`
  extensions) will reference this package instead of the transport-specific
  one — projector-only consumers no longer transitively pull in a gRPC
  stack. Versioned in lockstep with `Daml.Runtime` and the codegen tool.
  The throwing-API variants `CreateAsync` and
  `SubmitAndWaitForTransactionAsync` (long `[Obsolete]` on the bridge's
  interface) are intentionally **not** part of the abstraction; only
  their outcome-based `Try*` replacements are surfaced. Other methods
  on the interface (`SubmitAsync`, `ExerciseAsync`, etc.) keep their
  existing names. Existing callers of the dropped methods migrate to
  `TryCreateAsync` / `TrySubmitAndWaitForTransactionAsync`.
- **`Daml.Runtime.Streams.ContractStreamEvent<T>`** — transport-agnostic discriminated
  record for typed contract subscription streams. Variants:
  `Created`, `Archived`, `Exercised`, `Assigned`, `Unassigned`, `Checkpoint`,
  `StreamError`. Lives in `Daml.Runtime` so any ledger client (gRPC, JSON,
  in-memory) can yield these without dragging the consumer into a
  transport-specific dep. `StreamError.StatusCode` is `int` (a
  `Grpc.Core.StatusCode` would be cast at the call site) — consumers stay free
  of any transport library. The counterpart in a ledger-client transport
  implementation (the prior owner of this type) is being migrated to
  consume from here.
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
  submitted-transaction results. Lifted from an internal gRPC ledger
  bridge; no transport deps, useful from any ledger client.
- **`Daml.Runtime.Contracts.TransactionResultExtensions`** with
  `Single<T>`, `TrySingle<T>`, `All<T>` over `TransactionResult` for
  template-typed projection of `CreatedContracts`. `(module, entity)`
  matching tolerates package-id drift.
- **`Daml.Runtime.Stdlib` namespace** with hand-coded stubs for Daml stdlib
  types that are not generated per package. Currently covers
  `DA.Time.Types.RelTime`. Future stdlib types (`Set`, `Map`, `Tuple2`, ...)
  are tracked separately.
- **`Daml.Runtime.Stdlib.GenericStub.NotImplemented<T>(string)`** — runtime stub
  used by generated `ToRecord`/`FromRecord` methods on records with
  type-parameter fields. Generated code compiles; calling the stub at runtime
  throws `NotImplementedException` with a pointer to the workaround. Tracked
  separately for proper static-abstract dispatch.
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
  still compile. Full variant codec support tracked separately.

### Changed

- **BREAKING (codegen consumers): generated template files now emit a
  `<TemplateName>SubmissionExtensions` static class** with typed `CreateAsync`
  and `<Choice>Async` extensions on `ILedgerClient` / `ContractId<T>`. Method
  signatures and parameter shapes change vs. consumers using their own
  hand-rolled wrappers around the lower-level `Choice<T,A,R>` property:
  payload-derived signatories no longer require an explicit `actAs` argument,
  and per-controller named `Party` parameters appear on choice exercisers.
  Single-controller / single-signatory cases stay one-liners via
  `SubmitterInfo`'s implicit conversion from `string` / `Party`.
  `SubmitterInfo` is sourced from `Daml.Runtime.Commands` — the generated
  files do not import any transport package. See
  .
- **BREAKING:** `ContractId<T>`'s generic constraint relaxed from
  `where T : ITemplate` to `where T : IDamlType`. Source-compatible for all
  template-typed callers (`ITemplate : IDamlType`); enables the new
  interface-marker callers. Same change applied to `DamlContractId.ToTyped<T>`.
- **`ContractId<T>` typeparam doc** clarifies that `T` may be an interface or
  interface placeholder (in addition to a template), and points at the
  throwing-stub pattern.
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

## [0.1.2] — 2026-04-24

### Changed — BREAKING

- **Package renamed**: `Daml.Codegen.CSharp.Runtime` → `Daml.Runtime`.
  Consumers must update their `PackageReference` and `using` directives.
  Type names are unchanged.
- **Pre-release version scheme** now uses dot separators
  (`0.1.2-<branch>.<run>.<sha>` instead of `0.1.2-<run>-<branch>-<sha>`) so that build
  numbers compare numerically under SemVer 2.0. Dev packages published under
  the old `0.1.1-*` scheme have been removed from the GitHub Packages feed
  (`nuget.pkg.github.com/peacefulstudio`); consumers pinned to them must
  upgrade to `0.1.2-*`.

### Added

- **First-class `Party` value type**. Daml `Party` now maps to a
  dedicated `Party` struct instead of `string`, giving type-safety at the
  boundary between generated code and application code.
- **`FromDamlValue<T>` helper** on `Daml.Runtime` — converts a
  `DamlValue` into strongly-typed .NET values (generated records, primitives,
  `Party`, `ContractId<T>`, and `DamlValue` subtypes) in one call, removing
  the need for manual `FromRecord` wiring in application code.

### Fixed

- **`Party` JSON serialization** is now a plain JSON string (`"Alice::1220…"`),
  matching the Ledger JSON API and PQS wire format. Previously
  `Party` was serialized as a JSON object, which broke PQS-based consumers.

## [0.1.0] — initial alpha (internal, never published to NuGet.org)

Initial release of the three-package suite:

- DAR/DALF parsing via generated protobuf stubs
- C# code generation for records, variants, enums, templates, choices,
  contract keys, interfaces, generic types, and package upgrades
- Runtime library covering all Daml primitives with JSON serialization
- CLI distributed as a `dotnet tool`

Historical pre-release dev builds (`0.1.0-*`, `0.1.1-*`) were published to
the GitHub Packages NuGet feed
(`nuget.pkg.github.com/peacefulstudio`) during development and have
since been pruned. They are not supported.

[Unreleased]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.7...HEAD
[0.1.7]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.6...v0.1.7
[0.1.6]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.5...v0.1.6
[0.1.5]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.2...v0.1.4
[0.1.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.0-alpha.3...v0.1.2
[0.1.0]: https://github.com/peacefulstudio/daml-codegen-csharp/releases/tag/v0.1.0-alpha.3
