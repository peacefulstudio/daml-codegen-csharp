# Daml C# Code Generator

[![CI](https://github.com/peacefulstudio/daml-codegen-csharp/actions/workflows/ci.yaml/badge.svg)](https://github.com/peacefulstudio/daml-codegen-csharp/actions/workflows/ci.yaml)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/daml-codegen-csharp/badges/coverage.json)](https://github.com/peacefulstudio/daml-codegen-csharp/actions/workflows/ci.yaml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-white.svg)](https://dotnet.microsoft.com/)

Generates strongly-typed C# from Daml `.dar` archives so .NET applications can
talk to a Canton/Daml ledger with full type safety.

## Status

Active. Curated public releases land here; ongoing development happens
in a private development repository. Issues, discussions, and pull
requests are welcome on this repo.

This project is pre-1.0: under SemVer 0.x, any release may change the
public API without a major-version bump (see the versioning note at the
top of the [CHANGELOG](CHANGELOG.md)). The first release published to
NuGet.org is `0.1.8-preview.1`.

## Features

- **Strongly-typed contracts**: Generated record types for all Daml templates and data types
- **Choice support**: Fluent API for exercising choices with compile-time type safety
- **JSON serialization**: Built-in support for the JSON Ledger API
- **Modern C#**: Uses C# 12+ features (records, primary constructors, file-scoped namespaces); key-bearing DARs require C# 13 (`partial` property support — .NET 9 SDK or later on the build machine)
- **Nullable reference types**: Full nullable annotation support
- **Cross-platform**: Works on Windows, macOS, and Linux
- **NuGet pipeline**: Generate complete `.csproj` files for publishing DAR dependencies as NuGet packages

## Quick Start

### Installation

Add to your `daml.yaml` to pull the codegen component via dpm (set `DPM_AUTO_INSTALL=true` so dpm fetches it automatically on first use):

```yaml
components:
  - oci://ghcr.io/peacefulstudio/dpm-codegen-cs:<version>
```

Add the runtime package to your C# project:

```bash
dotnet add package Daml.Runtime
```

Until the first NuGet.org release is live, build the packages from
source instead — see [Building from Source](#building-from-source) and
`dotnet pack` below.

### Generate Code

```bash
# Generate C# from a DAR file
dpm codegen-cs --dar ./my-project.dar --out ./generated -n MyCompany.Contracts

# With verbose output
dpm codegen-cs --dar ./my-project.dar --out ./generated -V 2
```

> **Architecture note.** `dpm codegen-cs` accepts a `.dar` because the OCI
> bundle pairs a DAR → IntermediateDar decoder (a JVM helper) with the C#
> emitter from this repo. The emitter CLI in this repo consumes only the
> decoded IntermediateDar proto (`--intermediate`); it cannot read a `.dar`
> directly. See [CLI Reference](#cli-reference).

### Use Generated Code

```csharp
using MyCompany.Contracts.Main;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;

// Create a contract
var iou = new Iou(
    Issuer: new Party("Alice"),
    Owner: new Party("Bob"),
    Currency: "USD",
    Amount: 1000.00m
);

// Build a create command
var createCmd = CreateCommand.For(iou);

// Exercise a choice
var contractId = new Iou.ContractId("00abc123...");
var transferCmd = ExerciseCommand.For(
    contractId,
    Iou.ChoiceTransfer.Name,
    new Iou.Transfer(NewOwner: new Party("Charlie")).ToRecord());

// Submit commands
var submission = CommandsSubmission.Single(createCmd)
    .WithActAs(new Party("Alice"))
    .WithWorkflowId(new WorkflowId("iou-workflow"));
```

## NuGet Packages

The following packages are published to NuGet.org, starting with
`0.1.8-preview.1` (earlier versions in the CHANGELOG were internal
milestones and never reached a public feed):

| Package | Role |
|---|---|
| `Daml.Codegen.CSharp` | C# emitter library — consumes the intermediate package, writes `.cs` |
| `Daml.Runtime` | Types referenced by generated code |
| `Daml.Ledger.Abstractions` | Transport-agnostic `ILedgerClient` interface |
| `Daml.Codegen.Testing.Conformance` | Conformance corpus + harness for the emitter |

`Daml.Codegen.CSharp.Cli` — the proto-path emitter CLI that the
`dpm codegen-cs` OCI bundle runs — ships in this repo as source only; it
is not published to NuGet.

## Project Structure

```
daml-codegen-csharp/
├── src/
│   ├── Daml.Codegen.CSharp/              # C# emitter library (NuGet package)
│   │   ├── CodeGen/                      # C# code generation logic
│   │   ├── Model/                        # Intermediate AST
│   │   └── IntermediateDarReader.cs      # proto-to-model adapter
│   ├── Daml.Codegen.CSharp.Cli/          # proto-path emitter CLI (run by the dpm codegen-cs OCI bundle; source-only)
│   ├── Daml.Codegen.Testing.Conformance/ # conformance corpus + harness (NuGet package)
│   ├── Daml.Runtime/                     # Runtime library (NuGet package)
│   │   ├── Commands/                     # Ledger command types
│   │   ├── Contracts/                    # Contract and template base types
│   │   ├── Data/                         # Daml primitive types
│   │   └── Serialization/                # JSON serialization
│   └── Daml.Ledger.Abstractions/         # Transport-agnostic ILedgerClient (NuGet package)
├── tests/
│   ├── Daml.Codegen.CSharp.Tests/
│   ├── Daml.Codegen.Testing.Conformance.Tests/
│   ├── Daml.Ledger.Abstractions.Tests/
│   └── Daml.Runtime.Tests/
├── conformance/                          # Daml conformance corpus (source of richtypes.dar)
├── proto/                                # intermediate DAR proto schema
├── samples/
│   └── QuickstartExample/                # Working example
└── CONTEXT.md                            # domain model and architecture overview
```

## CLI Reference

Two layers share one flag surface:

- **`dpm codegen-cs`** — the OCI bundle. Takes a `.dar`, decodes it to an
  IntermediateDar proto with its bundled JVM helper, then runs the emitter
  CLI below on the proto. This is the only place the DAR → IntermediateDar
  decode ships.
- **`Daml.Codegen.CSharp.Cli`** — the emitter CLI in this repo. Takes the
  IntermediateDar proto via `--intermediate`; it does not read `.dar` files.

### Bundle usage

```
dpm codegen-cs --dar <path-to-dar> --out <output-dir> [emitter-options...]
```

Options other than `--dar`/`--out` are forwarded to the emitter CLI.

### Emitter CLI

Output of `dotnet run --project src/Daml.Codegen.CSharp.Cli -- --help`
(the `--output-directory` default is the invoking directory):

```
Description:
  Generate C# code from an IntermediateDar proto

Usage:
  Daml.Codegen.CSharp.Cli [options]

Options:
  --intermediate <intermediate>          Path to an IntermediateDar proto file produced by the JVM helper.
  -o, --output-directory <o>             Output directory for generated sources [default: the invoking directory]
  -n, --namespace <n>                    Root namespace for generated code (default: derived from package name)
  -V, --verbosity <V>                    Verbosity level: 0=errors only, 1=warnings, 2=info, 3=debug [default: 1]
  -r, --root <r>                         Regular expression to filter which templates to generate (default: .*)
  --json                                 Generate JSON serialization support
  --nullable                             Enable nullable reference types in generated code
  --generate-project                     Generate a .csproj file for the generated code
  --include-dependencies                 Generate code for dependency packages as well
  --target-framework <target-framework>  Target framework for the generated project (e.g., net10.0) [default: net10.0]
  --runtime-version <runtime-version>    Version of Daml.Runtime package to reference
  --contract-identifiers                 Generate a ContractIdentifiers helper class for PQS queries
  --emitter-counter <emitter-counter>    4th segment of the generated NuGet version (Major.Minor.Patch.Revision). Defaults to 0; set a monotonic counter to distinguish republished builds of the same source. [default: 0]
  --release-counters <release-counters>  Path to a JSON release-counter store. Requires --intermediate (the content hash that keys the store is computed from the IntermediateDar proto bytes). When set, the 4th NuGet version segment is resolved from this store, overriding --emitter-counter. The store is created on first use and atomically updated on each run.
  --package-license <package-license>    SPDX license expression emitted in the generated .csproj's <PackageLicenseExpression>. Defaults to Apache-2.0. [default: Apache-2.0]
  -?, -h, --help                         Show help and usage information
  --version                              Show version information
```

## DAR to NuGet Pipeline

The code generator supports creating NuGet packages from DAR files, including proper handling of dependencies. This enables you to publish your Daml contracts as private NuGet packages that can be consumed by C# applications.

### Basic NuGet Package Generation

Generate a complete NuGet-ready project from a DAR file:

```bash
# Generate C# code with a .csproj file
dpm codegen-cs --dar ./my-contracts.dar \
    --out ./generated \
    -n MyCompany.Contracts \
    --generate-project \
    --runtime-version 0.1.8-preview.1 \
    --target-framework net10.0

# Build and pack
cd generated
dotnet pack -c Release
```

This creates:
```
generated/
├── my.contracts.csproj      # Ready for NuGet packaging
├── Main/
│   └── Iou.cs               # Generated template code
└── ... other modules
```

### Generated Project File

The `--generate-project` flag creates a `.csproj` file with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>my.contracts</PackageId>
    <Version>1.0.0</Version>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Daml.Runtime" Version="1.0.0" />
  </ItemGroup>
</Project>
```

### Including Dependencies

When your DAR depends on other packages, use `--include-dependencies` to generate code for all dependencies:

```bash
# Generate code for main package AND all dependencies
dpm codegen-cs --dar ./my-app.dar \
    --out ./generated \
    --generate-project \
    --include-dependencies \
    --runtime-version 0.1.8-preview.1
```

This generates separate directories and project files for each package:
```
generated/
├── my.app/
│   ├── my.app.csproj
│   └── ... (main package code)
├── daml.finance/
│   ├── daml.finance.csproj
│   └── ... (dependency code)
└── some.library/
    ├── some.library.csproj
    └── ... (dependency code)
```

The main package's `.csproj` automatically references its dependencies:

```xml
<ItemGroup>
  <PackageReference Include="Daml.Runtime" Version="1.0.0" />
  <PackageReference Include="daml.finance" Version="2.0.0" />
  <PackageReference Include="some.library" Version="1.5.0" />
</ItemGroup>
```

### Complete Workflow Example

Here's a complete example of converting a Daml project to NuGet packages:

```bash
# 1. Build your Daml project
cd my-daml-project
dpm build -o my-project.dar

# 2. Generate C# code with dependencies
dpm codegen-cs --dar ./my-project.dar \
    --out ./csharp-bindings \
    -n MyCompany.Daml \
    --generate-project \
    --include-dependencies \
    --runtime-version 0.1.8-preview.1 \
    --target-framework net10.0 \
    -V 2

# 3. Build all generated projects
cd csharp-bindings
for dir in */; do
    echo "Building $dir..."
    dotnet build "$dir" -c Release
done

# 4. Pack all as NuGet packages
for dir in */; do
    echo "Packing $dir..."
    dotnet pack "$dir" -c Release -o ../nuget-packages
done

# 5. Publish to your private NuGet feed
dotnet nuget push ./nuget-packages/*.nupkg \
    --source https://nuget.mycompany.com/v3/index.json \
    --api-key $NUGET_API_KEY
```

### Using Generated Packages

Once published, consume the packages in your C# application:

```bash
# Add the generated package
dotnet add package MyCompany.Daml.MyProject --version 1.0.0
```

```csharp
using MyCompany.Daml.MyProject.Main;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;

// Use strongly-typed contracts
var contract = new MyTemplate(
    Owner: new Party("Alice"),
    Data: "Some data"
);

var createCmd = CreateCommand.For(contract);
```

## Type Mappings

| Daml Type | C# Type | Runtime Type |
|-----------|---------|--------------|
| `Int` | `long` | `DamlInt64` |
| `Numeric` | `decimal` | `DamlNumeric` |
| `Text` | `string` | `DamlText` |
| `Bool` | `bool` | `DamlBool` |
| `Party` | `Party` (readonly record struct) | `DamlParty` |
| `Date` | `DateOnly` | `DamlDate` |
| `Time` | `DateTimeOffset` | `DamlTimestamp` |
| `ContractId T` | `ContractId<T>` | `DamlContractId` |
| `Optional a` | `T?` | `DamlOptional` |
| `List a` | `IReadOnlyList<T>` | `DamlList` |
| `TextMap a` | `IReadOnlyDictionary<string, T>` | `DamlTextMap` |
| Record | `record` class | `DamlRecord` |
| Variant | Abstract record + derived | `DamlVariant` |
| Enum | `enum` | `DamlEnum` |

`Party` serializes as a plain JSON string (not an object) so payloads
round-trip against PQS and the JSON Ledger API; conversions to and from
`string` are explicit so a party can never be silently mistaken for an
arbitrary string.

## Generated Code Example

Given this Daml template (the model vendored at
[`samples/QuickstartExample/daml/Iou.daml`](samples/QuickstartExample/daml/Iou.daml)):

```daml
template Iou
  with
    issuer : Party
    owner : Party
    currency : Text
    amount : Decimal
  where
    signatory issuer
    observer owner

    choice Transfer : ContractId Iou
      with
        newOwner : Party
      controller owner
      do create this with owner = newOwner
```

the codegen produces (excerpted verbatim from the checked-in emitter output at
[`samples/QuickstartExample/Generated/Quickstart/Iou.cs`](samples/QuickstartExample/Generated/Quickstart/Iou.cs);
elisions marked `…`):

```csharp
namespace Quickstart;

/// <summary>
/// Generated from Daml template Iou:Iou
/// </summary>
public sealed partial record Iou(Party Issuer, Party Owner, string Currency, decimal Amount) : ITemplate
{
    /// <summary>Gets the template identifier.</summary>
    public static Identifier TemplateId { get; } = new("c6ae1a03c6a0e5c146dba48c5c577583e4e2bc12ef1dad7fa72429f733367aba", "Iou", "Iou");

    // … package id/name/version properties and Archive choice metadata elided …

    /// <summary>Converts this value to a DamlRecord.</summary>
    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("issuer", Issuer.ToDamlValue()),
        DamlField.Create("owner", Owner.ToDamlValue()),
        DamlField.Create("currency", new DamlText(Currency)),
        DamlField.Create("amount", new DamlNumeric(Amount))
    );

    /// <summary>Creates an instance from a DamlRecord.</summary>
    public static Iou FromRecord(DamlRecord record) => new Iou(
        Issuer: Party.FromDamlValue(record.GetRequiredField("issuer").As<DamlParty>()),
        Owner: Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
        Currency: record.GetRequiredField("currency").As<DamlText>().Value,
        Amount: record.GetRequiredField("amount").As<DamlNumeric>().Value
    );

    /// <summary>
    /// Exercise the Transfer choice.
    /// This choice is consuming and will archive the contract.
    /// </summary>
    public static Choice<Iou, Transfer, ContractId<Iou>> ChoiceTransfer { get; } = new()
    {
        Name = new ChoiceName("Transfer"),
        Consuming = true,
        ArgumentEncoder = arg => arg.ToRecord(),
        ResultDecoder = val => new ContractId<Iou>(val.As<DamlContractId>().Value)
    };

    /// <summary>Contract ID for Iou.</summary>
    public sealed record ContractId(string Value) : ContractId<Iou>(Value), IExercises<Iou>
    {
        ContractId<Iou> IExercises<Iou>.ContractId => this;
    }

    /// <summary>Active contract for Iou.</summary>
    public sealed record Contract(ContractId Id, Iou Data) : IContract<ContractId, Iou>
    {
        /// <summary>Creates a Contract from a CreatedEvent.</summary>
        public static Contract FromCreatedEvent(CreatedEvent @event) =>
            new(new ContractId(@event.ContractId), Iou.FromRecord(@event.CreateArguments));
    }
}
```

The same file also emits the typed `TransferResult` projection plus
`IouExtensions.TransferAsync` and `IouSubmissionExtensions.CreateAsync`
extension methods that submit through an `ILedgerClient`; the choice-argument
record `Iou.Transfer` is emitted alongside in
[`Iou.Transfer.cs`](samples/QuickstartExample/Generated/Quickstart/Iou.Transfer.cs).
See [`samples/QuickstartExample`](samples/QuickstartExample/Program.cs) for a
complete, runnable rendition of this shape.

## Building from Source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — the exact pinned version is in [`global.json`](global.json)
- (Optional) [Daml SDK via dpm](https://docs.daml.com/) for testing with real DAR files

### Build

```bash
# Clone the repository
git clone https://github.com/peacefulstudio/daml-codegen-csharp.git
cd daml-codegen-csharp

# Build
dotnet build

# Run tests
dotnet test

# Run the sample
dotnet run --project samples/QuickstartExample
```

### Create NuGet Packages

```bash
dotnet pack -c Release
```

## Integration with Canton

The generated code is designed to work with the Canton Ledger API. The
highest-level path is the generated extension methods
(`IouSubmissionExtensions.CreateAsync`, `IouExtensions.TransferAsync`, …)
submitting through a `Daml.Ledger.Abstractions.ILedgerClient`
implementation. Below that, the runtime types map directly onto the wire
formats:

### JSON Ledger API (v2)

The generated `TemplateId` and `DamlJsonSerializer` output plug straight
into the JSON Ledger API v2 command endpoints — here
`POST /v2/commands/submit-and-wait` (the port is the participant's JSON
Ledger API port; `7575` is the conventional default):

```csharp
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Quickstart;

var http = new HttpClient { BaseAddress = new Uri("http://localhost:7575") };

var alice = new Party("Alice::1220deadbeef");
var iou = new Iou(Issuer: alice, Owner: new Party("Bob::1220deadbeef"), Currency: "USD", Amount: 100m);

var request = new
{
    commands = new object[]
    {
        new
        {
            CreateCommand = new
            {
                templateId = Iou.TemplateId.ToString(),
                createArguments = JsonNode.Parse(DamlJsonSerializer.Serialize(iou.ToRecord())),
            },
        },
    },
    commandId = Guid.NewGuid().ToString(),
    userId = "ledger-api-user",
    actAs = new[] { alice.Id },
};

var response = await http.PostAsJsonAsync("/v2/commands/submit-and-wait", request);
response.EnsureSuccessStatusCode();
```

`Iou.TemplateId.ToString()` renders the package-id reference format
(`<package-id>:<module>:<entity>`); the API also accepts the
package-name format (`#<package-name>:<module>:<entity>`), which can be
built from the generated `PackageName` property.

### gRPC Ledger API

The runtime types can be converted to/from the gRPC protobuf types. See the [Canton documentation](https://docs.canton.network/) for gRPC integration details.

## Contributing

Contributions are welcome from anyone in the Daml and C# community. See
[CONTRIBUTING.md](CONTRIBUTING.md) for the dev setup, the red-green TDD
requirement, and the branch model. The per-PR checklist itself lives in
the PR template and is filled in when you open a PR. By participating
you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

For security-sensitive bugs, please follow [SECURITY.md](SECURITY.md)
instead of opening a public issue.

## Project stewardship

`daml-codegen-csharp` is currently developed and maintained by **Peaceful
Studio OÜ** (Estonia, VAT EE102232996). The project is licensed under
Apache-2.0 with the explicit intent of community ownership: if and
when adoption warrants neutral governance, Peaceful Studio commits to
transferring this repository to a community-led organisation under the
same license terms. Contributions welcome from anywhere in the
Daml and C# ecosystem; no CLA required.

## Roadmap

### Completed

- [x] IntermediateDar proto reader covering the full Daml-LF type surface
- [x] Interface support (`IDamlInterface`, `IHasView<TView>`, `IImplements<TInterface>`)
- [x] Contract key support (`IHasKey<TKey>`)
- [x] Package upgrade support (`IUpgradeable` marker interface)
- [x] Generic types (type parameters on records and variants)
- [x] DAR dependencies and NuGet pipeline

### Planned

- [ ] Source generator (compile-time Roslyn codegen)
- [ ] gRPC client integration
- [ ] End-to-end Canton integration tests

## License

Apache-2.0. © 2026 Peaceful Studio OÜ. Licensed under the
[Apache License 2.0](LICENSE). See [LICENSE](LICENSE) for the full text
and [NOTICE](NOTICE) for attribution requirements.

## Related Projects

- [Daml SDK](https://github.com/digital-asset/daml) - The Daml smart contract language
- [Canton](https://www.canton.network/) - Privacy-enabled blockchain infrastructure
- [Java Codegen](https://docs.daml.com/app-dev/bindings-java/codegen.html) - Official Java code generator
- [TypeScript Codegen](https://docs.daml.com/app-dev/bindings-ts/daml2js.html) - Official TypeScript code generator
