// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

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
internal static class EmittedCodeCompilesTestHelpers
{
    internal static DamlType ContractIdOf(string templateName) =>
        new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef("", "Test.Module", templateName)]);

    internal static DamlType TupleType(params DamlType[] componentTypes) =>
        new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", $"Tuple{componentTypes.Length}"),
            componentTypes);

    internal static DamlType OptionalOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [inner]);

    internal static IReadOnlyList<GeneratedFile> GenerateKeyBearingTemplate(bool useRecordTypes = true)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AssetWithKey",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AssetWithKey",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = useRecordTypes,
            UsePrimaryConstructors = useRecordTypes,
        };
        return CreateGenerator(options).Generate(dar);
    }

    internal static IReadOnlyList<Diagnostic> CompileEmittedFilesWithDocDiagnostics(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files, DocumentationMode.Diagnose);

    internal static IReadOnlyList<Diagnostic> CompileEmittedFiles(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files, DocumentationMode.Parse);

    internal static IReadOnlyList<Diagnostic> CompileEmittedFiles(
        IReadOnlyList<GeneratedFile> files,
        DocumentationMode documentationMode)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: documentationMode);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
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
