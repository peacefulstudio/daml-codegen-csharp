// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.CommandLine;
using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.CSharp.Versioning;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate;
using Spectre.Console;

namespace Daml.Codegen.CSharp.Cli;

/// <summary>
/// Entry point for the Daml C# code generator CLI. Bundled as a
/// self-contained single-file binary inside the <c>dpm codegen-cs</c>
/// OCI artifact (#136). Accepts either an <c>IntermediateDar</c> proto
/// file (the post-#147 primary path, produced by the JVM helper) via
/// <c>--intermediate</c>, or one or more <c>.dar</c> archives via the
/// positional <c>dar-files</c> argument (legacy direct path retained for
/// local development).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Generate C# code from Daml Archive (.dar) files or an IntermediateDar proto");

        var intermediateOption = new Option<FileInfo?>("--intermediate")
        {
            Description = "Path to an IntermediateDar proto file produced by the JVM helper. When set, the positional dar-files argument is ignored."
        };
        intermediateOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<FileInfo?>();
            if (value is not null && !value.Exists)
            {
                result.AddError($"IntermediateDar proto not found: {value.FullName}");
            }
        });

        var darFilesArg = new Argument<FileInfo[]>("dar-files")
        {
            Description = "DAR files to generate C# bindings for (legacy direct path; ignored when --intermediate is given)",
            Arity = ArgumentArity.ZeroOrMore
        };
        darFilesArg.Validators.Add(result =>
        {
            foreach (var file in result.GetValueOrDefault<FileInfo[]>() ?? [])
            {
                if (!file.Exists)
                {
                    result.AddError($"DAR file not found: {file.FullName}");
                    return;
                }
                if (!file.Extension.Equals(".dar", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError($"File must have .dar extension: {file.FullName}");
                    return;
                }
            }
        });

        var outputOption = new Option<DirectoryInfo>("-o")
        {
            Description = "Output directory for generated sources",
            DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory())
        };
        outputOption.Aliases.Add("--output-directory");

        var namespaceOption = new Option<string?>("-n")
        {
            Description = "Root namespace for generated code (default: derived from package name)"
        };
        namespaceOption.Aliases.Add("--namespace");

        var verbosityOption = new Option<int>("-V")
        {
            Description = "Verbosity level: 0=errors only, 1=warnings, 2=info, 3=debug",
            DefaultValueFactory = _ => 1
        };
        verbosityOption.Aliases.Add("--verbosity");

        var rootOption = new Option<string?>("-r")
        {
            Description = "Regular expression to filter which templates to generate (default: .*)"
        };
        rootOption.Aliases.Add("--root");

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Generate JSON serialization support",
            DefaultValueFactory = _ => true
        };

        var nullableOption = new Option<bool>("--nullable")
        {
            Description = "Enable nullable reference types in generated code",
            DefaultValueFactory = _ => true
        };

        var generateProjectOption = new Option<bool>("--generate-project")
        {
            Description = "Generate a .csproj file for the generated code",
            DefaultValueFactory = _ => false
        };

        var includeDepsOption = new Option<bool>("--include-dependencies")
        {
            Description = "Generate code for dependency packages as well",
            DefaultValueFactory = _ => false
        };

        var targetFrameworkOption = new Option<string>("--target-framework")
        {
            Description = "Target framework for the generated project (e.g., net10.0)",
            DefaultValueFactory = _ => "net10.0"
        };

        var runtimeVersionOption = new Option<string?>("--runtime-version")
        {
            Description = "Version of Daml.Runtime package to reference"
        };

        var contractIdentifiersOption = new Option<bool>("--contract-identifiers")
        {
            Description = "Generate a ContractIdentifiers helper class for PQS queries",
            DefaultValueFactory = _ => true
        };

        var emitterCounterOption = new Option<int>("--emitter-counter")
        {
            Description = "4th segment of the generated NuGet version (M.m.p.r per ADR 0002). Defaults to 0; the Splice publish CI passes a monotonic counter from the emitter-version mapping table.",
            DefaultValueFactory = _ => 0
        };
        emitterCounterOption.Validators.Add(result =>
        {
            if (result.GetValue(emitterCounterOption) < 0)
            {
                result.AddError("--emitter-counter must be a non-negative integer (ADR 0002 segment 4 is a monotonic counter).");
            }
        });

        var releaseCountersOption = new Option<FileInfo?>("--release-counters")
        {
            Description = "Path to a JSON release-counter store (JsonReleaseCounterStore). REQUIRES --intermediate (the content hash that keys the store is computed from the IntermediateDar proto bytes). When set, the 4th NuGet version segment is resolved from this store via SpliceNuGetVersion.Compute — overriding --emitter-counter. The store is created on first use and atomically updated on each run; per ADR 0004 the Splice publish CI loads / saves it as a GitHub Actions repo variable across runs."
        };

        var packageLicenseOption = new Option<string>("--package-license")
        {
            Description = "SPDX license expression emitted in the generated .csproj's <PackageLicenseExpression>. Defaults to Apache-2.0 (correct for the Splice publish path).",
            DefaultValueFactory = _ => "Apache-2.0"
        };
        packageLicenseOption.Validators.Add(result =>
        {
            var value = result.GetValue(packageLicenseOption);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--package-license must be a non-empty SPDX license expression (e.g. Apache-2.0, MIT, BSD-3-Clause).");
            }
        });

        rootCommand.Arguments.Add(darFilesArg);
        rootCommand.Options.Add(intermediateOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(namespaceOption);
        rootCommand.Options.Add(verbosityOption);
        rootCommand.Options.Add(rootOption);
        rootCommand.Options.Add(jsonOption);
        rootCommand.Options.Add(nullableOption);
        rootCommand.Options.Add(generateProjectOption);
        rootCommand.Options.Add(includeDepsOption);
        rootCommand.Options.Add(targetFrameworkOption);
        rootCommand.Options.Add(runtimeVersionOption);
        rootCommand.Options.Add(contractIdentifiersOption);
        rootCommand.Options.Add(emitterCounterOption);
        rootCommand.Options.Add(releaseCountersOption);
        rootCommand.Options.Add(packageLicenseOption);

        Func<ParseResult, CancellationToken, Task<int>> action = (parseResult, _) =>
            RunCodegen(new CodegenArgs(
                parseResult.GetValue(intermediateOption),
                parseResult.GetValue(darFilesArg) ?? [],
                parseResult.GetValue(outputOption)!,
                parseResult.GetValue(namespaceOption),
                parseResult.GetValue(verbosityOption),
                parseResult.GetValue(rootOption),
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(nullableOption),
                parseResult.GetValue(generateProjectOption),
                parseResult.GetValue(includeDepsOption),
                parseResult.GetValue(targetFrameworkOption)!,
                parseResult.GetValue(runtimeVersionOption),
                parseResult.GetValue(contractIdentifiersOption),
                parseResult.GetValue(emitterCounterOption),
                parseResult.GetValue(releaseCountersOption),
                parseResult.GetValue(packageLicenseOption)!));
        rootCommand.SetAction(action);

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static async Task<int> RunCodegen(CodegenArgs args)
    {
        var logger = new ConsoleLogger(args.Verbosity);

        try
        {
            logger.Info($"Daml C# Codegen v{typeof(Program).Assembly.GetName().Version}");
            logger.Info($"Output directory: {args.OutputDirectory.FullName}");

            if (!args.OutputDirectory.Exists)
            {
                args.OutputDirectory.Create();
                logger.Debug($"Created output directory: {args.OutputDirectory.FullName}");
            }

            if (args.ReleaseCountersFile is not null && args.IntermediateFile is null)
            {
                logger.Error("--release-counters requires --intermediate. The 4th NuGet version segment is keyed off the IntermediateDar content hash (see ADR 0002 / ADR 0004); the DAR-direct path does not expose the deterministic proto bytes.");
                return 1;
            }

            if (args.IntermediateFile is not null)
            {
                await GenerateFromIntermediate(args.IntermediateFile, args, logger);
                return 0;
            }
            if (args.DarFiles.Length > 0)
            {
                await GenerateFromDarFiles(args.DarFiles, BuildOptions(args, args.EmitterCounter), args, logger);
                return 0;
            }

            logger.Error("Either --intermediate <path> or one or more <dar-files> must be provided.");
            return 1;
        }
        catch (Exception ex)
        {
            logger.Error($"Code generation failed: {ex.Message}");
            if (args.Verbosity >= 3)
            {
                logger.Error(ex.ToString());
            }
            return 1;
        }
    }

    private static async Task GenerateFromIntermediate(FileInfo file, CodegenArgs args, ConsoleLogger logger)
    {
        logger.Info($"Reading IntermediateDar: {file.Name}");
        IntermediateDar proto;
        await using (var stream = file.OpenRead())
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }

        var dar = IntermediateDarReader.Read(proto);
        logger.Info($"  Package: {dar.MainPackage.Name} v{dar.MainPackage.Version}");
        logger.Info($"  Modules: {dar.MainPackage.Modules.Count}");
        logger.Debug($"  Dependencies: {dar.Dependencies.Count}");

        var effectiveCounter = args.ReleaseCountersFile is not null
            ? ResolveReleaseCounter(args.ReleaseCountersFile, proto, dar.MainPackage.Name, dar.MainPackage.Version, logger)
            : args.EmitterCounter;

        var generator = new CSharpCodeGenerator(BuildOptions(args, effectiveCounter), logger);
        var generatedFiles = generator.Generate(dar);
        await WriteGeneratedFiles(generatedFiles, args, logger);
    }

    private static int ResolveReleaseCounter(
        FileInfo storeFile,
        IntermediateDar proto,
        string packageName,
        Version packageVersion,
        ConsoleLogger logger)
    {
        var hash = IntermediatePackageContentHash.Compute(proto.Main);
        var store = JsonReleaseCounterStore.OpenOrCreate(storeFile.FullName);
        var version = SpliceNuGetVersion.Compute(packageName, packageVersion, hash, store);

        var truncated = hash[..Math.Min(12, hash.Length)];
        logger.Info($"  Release counter: {packageName}@{packageVersion.Major}.{packageVersion.Minor}.{Math.Max(0, packageVersion.Build)} content_hash={truncated}… version={version}");

        return version.Revision;
    }

    private static CodeGenOptions BuildOptions(CodegenArgs args, int emitterCounter) =>
        new()
        {
            OutputDirectory = args.OutputDirectory.FullName,
            RootNamespace = args.RootNamespace,
            RootFilter = args.RootFilter,
            GenerateJsonSupport = args.GenerateJson,
            EnableNullableReferenceTypes = args.EnableNullable,
            Verbosity = args.Verbosity,
            GenerateProjectFile = args.GenerateProjectFile,
            IncludeDependencies = args.IncludeDependencies,
            TargetFramework = args.TargetFramework,
            RuntimePackageVersion = args.RuntimePackageVersion,
            GenerateContractIdentifiers = args.GenerateContractIdentifiers,
            EmitterCounter = emitterCounter,
            PackageLicenseExpression = args.PackageLicenseExpression,
        };

    private static async Task GenerateFromDarFiles(FileInfo[] darFiles, CodeGenOptions options, CodegenArgs args, ConsoleLogger logger)
    {
        var totalFiles = 0;
        foreach (var darFile in darFiles)
        {
            logger.Info($"Processing: {darFile.Name}");
            await AnsiConsole.Status().StartAsync($"Reading {darFile.Name}...", async ctx =>
            {
                ctx.Status($"Parsing {darFile.Name}...");
                var dar = await DarArchive.ReadAsync(darFile.FullName);

                logger.Info($"  Package: {dar.MainPackage.Name} v{dar.MainPackage.Version}");
                logger.Info($"  Modules: {dar.MainPackage.Modules.Count}");
                logger.Debug($"  Dependencies: {dar.Dependencies.Count}");

                if (options.IncludeDependencies)
                {
                    logger.Info($"  Including {dar.Dependencies.Count} dependencies in code generation");
                }

                if (dar.MainPackage.DependencyReferences.Count > 0)
                {
                    logger.Debug($"  Package references {dar.MainPackage.DependencyReferences.Count} dependencies:");
                    foreach (var depRef in dar.MainPackage.DependencyReferences)
                    {
                        var depInfo = depRef.Name != null
                            ? $"{depRef.Name} v{depRef.Version}"
                            : depRef.PackageId[..Math.Min(16, depRef.PackageId.Length)] + "...";
                        logger.Debug($"    - {depInfo}");
                    }
                }

                ctx.Status("Generating C# code...");
                var generator = new CSharpCodeGenerator(options, logger);
                var generatedFiles = generator.Generate(dar);

                ctx.Status("Writing files...");
                totalFiles += await WriteGeneratedFiles(generatedFiles, args, logger);
            });
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Generated {totalFiles} files");
    }

    private static async Task<int> WriteGeneratedFiles(IReadOnlyList<GeneratedFile> generatedFiles, CodegenArgs args, ConsoleLogger logger)
    {
        var written = 0;
        foreach (var file in generatedFiles)
        {
            var filePath = Path.Combine(args.OutputDirectory.FullName, file.RelativePath);
            var fileDir = Path.GetDirectoryName(filePath);
            if (fileDir is not null && !Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            await File.WriteAllTextAsync(filePath, file.Content);
            logger.Debug($"  Generated: {file.RelativePath}");
            written++;
        }
        return written;
    }
}

internal sealed record CodegenArgs(
    FileInfo? IntermediateFile,
    FileInfo[] DarFiles,
    DirectoryInfo OutputDirectory,
    string? RootNamespace,
    int Verbosity,
    string? RootFilter,
    bool GenerateJson,
    bool EnableNullable,
    bool GenerateProjectFile,
    bool IncludeDependencies,
    string TargetFramework,
    string? RuntimePackageVersion,
    bool GenerateContractIdentifiers,
    int EmitterCounter,
    FileInfo? ReleaseCountersFile,
    string PackageLicenseExpression);
