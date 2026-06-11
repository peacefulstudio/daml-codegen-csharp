# Daml.Codegen.CSharp

C# code generator library for Daml smart contracts. Consumes an
`IntermediateDar` protobuf (produced by the JVM helper bundled in
`dpm codegen-cs`) and emits strongly-typed C# bindings for Daml templates,
data types, and interfaces.

## Installation

```bash
dotnet add package Daml.Codegen.CSharp
```

## Usage

Most users run codegen through `dpm codegen-cs` rather than this library
directly. Reference the library when you need programmatic code generation,
for example inside a build tool:

```csharp
using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate;

IntermediateDar proto;
await using (var stream = File.OpenRead("intermediate.binpb"))
{
    proto = IntermediateDar.Parser.ParseFrom(stream);
}

var dar = IntermediateDarReader.Read(proto);
var logger = new ConsoleLogger(verbosity: 1);
var generator = new CSharpCodeGenerator(new CodeGenOptions(), logger);
var files = generator.Generate(dar);

foreach (var file in files)
{
    // file.RelativePath, file.Content — the caller owns writing to disk.
}
```

Generated code targets the lockstep-versioned `Daml.Runtime` and
`Daml.Ledger.Abstractions` packages.

## License

Apache-2.0. See the repository's LICENSE file.
