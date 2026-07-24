// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using Xunit;
using DamlUnit = Daml.Runtime.Data.DamlUnit;
using Identifier = Daml.Runtime.Data.Identifier;
using Party = Daml.Runtime.Data.Party;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Round-trip test for a <c>Unit</c>-returning non-CID choice exerciser. Generates
/// the wrapper for a <c>DoNothing : ()</c> choice, compiles it through Roslyn into an
/// in-memory assembly, then invokes the emitted <c>DoNothingAsync</c> extension
/// end-to-end through an <see cref="ILedgerWriter"/> substitute and asserts the
/// projected <see cref="ExerciseOutcome{T}"/> over <c>Unit</c>. Exercises the
/// Unit-projector branch (<c>needsStdlibUnitDecoder</c>) at runtime, which the
/// emit-string specs in <c>ChoiceEmitterUnitReturnExerciserTests</c> do not.
/// </summary>
public class NonContractChoiceUnitExerciserRoundTripTests
{
    private const string ModuleName = "Test.Sink";
    private const string EntityName = "Sink";
    private const string ChoiceName = "DoNothing";
    private const string PackageId = "test-package-id";
    private const string GeneratedNamespace = "Test.Package";
    private const string NonContractExtensionsSuffix = "NonContractExtensions";

    private static readonly Identifier SinkTemplateId = new(PackageId, ModuleName, EntityName);

    private static Assembly CompileWrapperAssembly()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = EntityName,
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = ChoiceName,
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = EntityName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = PackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));
        var files = generator.Generate(new DarModel { MainPackage = package, Dependencies = [] });

        return EmitAssembly(files);
    }

    private static Assembly EmitAssembly(IReadOnlyList<GeneratedFile> files)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Parse);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
            .ToArray();

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var damlRuntime = typeof(Daml.Runtime.Contracts.ITemplate).Assembly;
        var damlAbstractions = typeof(Daml.Ledger.Abstractions.ILedgerClient).Assembly;
        foreach (var location in new[] { damlRuntime.Location, damlAbstractions.Location })
        {
            if (!references.Any(r => r is PortableExecutableReference per && per.FilePath == location))
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "NonContractChoiceUnitExerciserRoundTripTests-emit",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(
            "the generated Unit-returning wrapper must compile before it can be exercised, but got: {0}",
            string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage() + " @ " + d.Location)));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }

    private static ExercisedEvent ExercisedDoNothing(string contractId) =>
        new(
            ContractId: contractId,
            TemplateId: SinkTemplateId,
            InterfaceId: null,
            ChoiceName: ChoiceName,
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);

    private static TransactionResult TransactionWith(params ExercisedEvent[] events) =>
        new(
            UpdateId: "update-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: default)
        {
            ExercisedEvents = events,
        };

    private static readonly Assembly WrapperAssembly = CompileWrapperAssembly();

    private static async Task<ExerciseOutcome<Unit>> InvokeDoNothingAsync(ILedgerWriter client, string contractId)
    {
        var sinkType = WrapperAssembly.GetType($"{GeneratedNamespace}.{EntityName}", throwOnError: true)!;
        var extensionsType = WrapperAssembly.GetType(
            $"{GeneratedNamespace}.{EntityName}{NonContractExtensionsSuffix}", throwOnError: true)!;
        var method = extensionsType.GetMethod($"{ChoiceName}Async", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{ChoiceName}Async not found on emitted extensions class");

        var contractIdType = typeof(ContractId<>).MakeGenericType(sinkType);
        var typedContractId = Activator.CreateInstance(contractIdType, contractId);

        var task = (Task<ExerciseOutcome<Unit>>)method.Invoke(
            null,
            [typedContractId, client, new Party("alice"), null, null, null, CancellationToken.None])!;
        return await task;
    }

    [Fact]
    public async Task DoNothingAsync_projects_a_unit_outcome_through_the_ledger_writer()
    {
        var client = Substitute.For<ILedgerWriter>();
        client.TrySubmitAndWaitForTransactionAsync(
                Arg.Any<CommandsSubmission>(), Arg.Any<SubmitterInfo>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.One(TransactionWith(ExercisedDoNothing("contract-1")))));

        var outcome = await InvokeDoNothingAsync(client, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<Unit>.One>().Subject;
        one.Result.Should().Be(Unit.Value);
    }

    /// <summary>
    /// Regression: a Daml package declaring its own <c>enum Unit</c> (as
    /// <c>splice-wallet-payments</c> does) must still emit a <c>global::</c>-qualified
    /// reference to <see cref="Daml.Runtime.Stdlib.Unit"/> for a stdlib-<c>Unit</c>-returning
    /// choice, since the generated namespace is flat and a bare <c>Unit</c> reference would
    /// otherwise bind to the package-local enum instead (CS0117 on <c>.Value</c>).
    /// </summary>
    [Fact]
    public void generated_wrapper_compiles_clean_when_the_package_declares_its_own_unit_enum()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = EntityName,
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = ChoiceName,
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = EntityName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Unit",
                    Definition = new DamlEnumDefinition(["USDUnit", "AmuletUnit", "ExtUnit"]),
                }
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = PackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));
        var files = generator.Generate(new DarModel { MainPackage = package, Dependencies = [] });

        EmitAssembly(files);
    }
}
