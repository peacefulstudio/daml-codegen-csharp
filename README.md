# Daml C# Code Generator

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-white.svg)](https://dotnet.microsoft.com/)

Generates strongly-typed C# from Daml `.dar` archives so .NET applications can
talk to a Canton/Daml ledger with full type safety.

## Status

Active. Curated public releases land here; ongoing development happens
on a separate working tree. Issues, discussions, and pull requests are
welcome on this repo.

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

### Generate Code

```bash
# Generate C# from a DAR file
dpm codegen-cs --dar ./my-project.dar --out ./generated -n MyCompany.Contracts

# With verbose output
dpm codegen-cs --dar ./my-project.dar --out ./generated -V 2
```

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
var contractId = new Iou.IouContractId("00abc123...");
var transferCmd = contractId.ExerciseTransfer(newOwner: new Party("Charlie"));

// Submit commands
var submission = CommandsSubmission.Single(createCmd)
    .WithActAs(new Party("Alice"))
    .WithWorkflowId(new WorkflowId("iou-workflow"));
```

## NuGet Packages

The following packages are published to NuGet:

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
├── proto/                                # intermediate DAR proto schema
├── docs/
│   └── public/                           # public architecture decision records
└── samples/
    └── QuickstartExample/                # Working example
```

## CLI Reference

```
dpm codegen-cs --dar <dar-file> --out <output-dir> [emitter-options]

Bundle flags (parsed by dpm-codegen-cs entrypoint):
  --dar <path>           DAR file to generate C# bindings for (required)
  --out <dir>            Output directory for generated sources (required)

Emitter options (forwarded to the emitter):
  -n, --namespace        Root namespace for generated code
  -V, --verbosity        0=errors, 1=warnings, 2=info, 3=debug [default: 1]
  -r, --root             Regex filter for template names [default: .*]
  --json                 Generate JSON serialization support [default: true]
  --nullable             Enable nullable reference types [default: true]
  --generate-project     Generate a .csproj file for NuGet packaging [default: false]
  --include-dependencies Generate code for dependency packages as well [default: false]
  --target-framework     Target framework for the generated project [default: net10.0]
  --runtime-version      Version of the runtime package to reference
  --help                 Show help
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
    --runtime-version 1.0.0 \
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
    --runtime-version 1.0.0
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
    --runtime-version 1.0.0 \
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

Given this Daml template:

```daml
template Iou
  with
    issuer: Party
    owner: Party
    currency: Text
    amount: Decimal
  where
    signatory issuer

    choice Transfer: ContractId Iou
      with
        newOwner: Party
      controller owner
      do create this with owner = newOwner
```

The codegen produces:

```csharp
public sealed record Iou(
    Party Issuer,
    Party Owner,
    string Currency,
    decimal Amount) : ITemplate
{
    public static Identifier TemplateId { get; } = new("pkg-id", "Main", "Iou");

    public DamlRecord ToRecord() { ... }
    public static Iou FromRecord(DamlRecord record) { ... }

    public sealed record TransferArgument(Party NewOwner);

    public sealed record IouContractId(string Value) : ContractId<Iou>(Value), IExercises<Iou>
    {
        public ExerciseCommand ExerciseTransfer(Party newOwner) => ...;
    }

    public static class IouContract
    {
        public static Contract<Iou> FromCreatedEvent(CreatedEvent @event) => ...;
    }
}
```

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

The generated code is designed to work with the Canton Ledger API:

### JSON Ledger API

```csharp
using System.Net.Http.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:7575") };

// Create a contract via JSON API
var iou = new Iou(new Party("Alice"), new Party("Bob"), "USD", 100m);
var payload = new {
    templateId = Iou.TemplateId.ToString(),
    payload = DamlJsonSerializer.Serialize(iou.ToRecord())
};

await httpClient.PostAsJsonAsync("/v1/create", payload);
```

### gRPC Ledger API

The runtime types can be converted to/from the gRPC protobuf types. See the [Canton documentation](https://docs.daml.com/) for gRPC integration details.

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

- [x] Full Daml-LF protobuf parser
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
