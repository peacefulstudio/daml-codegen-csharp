// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.DarReader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Smoke test that pipes the codegen-emitted source through Roslyn against the
/// real <c>Daml.Runtime</c> + <c>Daml.Ledger.Abstractions</c> assemblies. Pins
/// "the emitted shape compiles" against quiet drift: string-shape tests in
/// <see cref="ChoiceResultStructTests"/> and <see cref="ChoiceAsyncExerciserTests"/>
/// can pass while the surrounding template body introduces Roslyn errors — this
/// test fails on any such error-severity diagnostic. Warnings are not asserted
/// against; consumer projects choose their own warning policy.
/// </summary>
public class EmittedCodeCompilesTests
{
    private static CSharpCodeGenerator CreateGenerator()
    {
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true
        };
        var logger = new ConsoleLogger(0);
        return new CSharpCodeGenerator(options, logger);
    }

    private static DamlType ContractIdOf(string templateName) =>
        new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef("", "Test.Module", templateName)]);

    private static DamlType TupleType(params DamlType[] componentTypes) =>
        new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", $"Tuple{componentTypes.Length}"),
            componentTypes);

    private static DamlType OptionalOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [inner]);

    [Fact]
    public void Emitted_template_with_create_bearing_choice_compiles()
    {
        // Bare ContractId<T> return is the simplest path that exercises:
        //   - the <Choice>Result record (one slot named after the template)
        //   - the FromCreatedContracts projector (cardinality + global:: qualifier)
        //   - the <Choice>Async extension (ILedgerClient + ExerciseOutcome plumbing)
        // Tuple returns hit the unrelated DA.Types:TupleN result-decoder mapping
        // path, which is orthogonal to this PR — covered by string-shape tests
        // upstream.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Agreement"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code should compile against Daml.Runtime + Daml.Ledger.Abstractions, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    private static IReadOnlyList<Diagnostic> CompileEmittedFiles(IReadOnlyList<GeneratedFile> files)
    {
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, path: f.RelativePath))
            .ToArray();

        // The TFM is net10.0 — pull system assemblies via reflection on a known type
        // (object lives in System.Private.CoreLib, GetReferenceAssemblies pattern
        // would require a separate package). We grab everything Daml.Runtime and
        // Daml.Ledger.Abstractions transitively reference, which covers the surface
        // emitted code touches.
        var runtimeAssemblies = new[]
        {
            typeof(object).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(System.Collections.Generic.IEnumerable<>).Assembly,
            typeof(System.Threading.Tasks.Task).Assembly,
            typeof(System.Console).Assembly,
        };

        var runtimeRefs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add explicit refs that may not be loaded yet.
        foreach (var asm in runtimeAssemblies)
        {
            if (!runtimeRefs.Any(r => r is PortableExecutableReference per && per.FilePath == asm.Location))
            {
                runtimeRefs.Add(MetadataReference.CreateFromFile(asm.Location));
            }
        }

        // Daml.Runtime + Daml.Ledger.Abstractions — referenced via project-ref.
        var damlRuntime = typeof(Daml.Runtime.Contracts.ITemplate).Assembly;
        var damlAbstractions = typeof(Daml.Ledger.Abstractions.ILedgerClient).Assembly;
        if (!runtimeRefs.Any(r => r is PortableExecutableReference per && per.FilePath == damlRuntime.Location))
        {
            runtimeRefs.Add(MetadataReference.CreateFromFile(damlRuntime.Location));
        }
        if (!runtimeRefs.Any(r => r is PortableExecutableReference per && per.FilePath == damlAbstractions.Location))
        {
            runtimeRefs.Add(MetadataReference.CreateFromFile(damlAbstractions.Location));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "EmittedCodeCompilesTests-emit",
            syntaxTrees: trees,
            references: runtimeRefs,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics();
    }
}
