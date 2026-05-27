// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.CommandLine;
using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
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
                parseResult.GetValue(contractIdentifiersOption)));
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

            var options = new CodeGenOptions
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
                GenerateContractIdentifiers = args.GenerateContractIdentifiers
            };

            if (args.IntermediateFile is not null)
            {
                await GenerateFromIntermediate(args.IntermediateFile, options, args, logger);
                return 0;
            }
            if (args.DarFiles.Length > 0)
            {
                await GenerateFromDarFiles(args.DarFiles, options, args, logger);
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

    private static async Task GenerateFromIntermediate(FileInfo file, CodeGenOptions options, CodegenArgs args, ConsoleLogger logger)
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

        var generator = new CSharpCodeGenerator(options, logger);
        var generatedFiles = generator.Generate(dar);
        await WriteGeneratedFiles(generatedFiles, args, logger);
    }

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
    bool GenerateContractIdentifiers);
