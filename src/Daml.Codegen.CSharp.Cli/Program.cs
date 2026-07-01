// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.CSharp.Versioning;
using Daml.Codegen.Intermediate;

namespace Daml.Codegen.CSharp.Cli;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Generate C# code from an IntermediateDar proto");

        var intermediateOption = new Option<FileInfo>("--intermediate")
        {
            Description = "Path to an IntermediateDar proto file produced by the JVM helper.",
            Required = true
        };
        intermediateOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<FileInfo?>();
            if (value is not null && !value.Exists)
            {
                result.AddError($"IntermediateDar proto not found: {value.FullName}");
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

        var verbosityOption = new Option<int>("-v")
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
        targetFrameworkOption.Validators.Add(result =>
        {
            var value = result.GetValue(targetFrameworkOption);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--target-framework must be a non-empty target framework moniker (e.g. net10.0, net9.0).");
            }
        });

        var runtimeVersionOption = new Option<string?>("--runtime-version")
        {
            Description = "Version of Daml.Runtime package to reference"
        };
        runtimeVersionOption.Validators.Add(result =>
        {
            var value = result.GetValue(runtimeVersionOption);
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--runtime-version must be a non-empty version string when specified (e.g. 1.2.3).");
            }
        });

        var contractIdentifiersOption = new Option<bool>("--contract-identifiers")
        {
            Description = "Generate a ContractIdentifiers helper class for PQS queries",
            DefaultValueFactory = _ => true
        };

        var emitterCounterOption = new Option<int>("--emitter-counter")
        {
            Description = "4th segment of the generated NuGet version (Major.Minor.Patch.Revision). Defaults to 0; set a monotonic counter to distinguish republished builds of the same source.",
            DefaultValueFactory = _ => 0
        };
        emitterCounterOption.Validators.Add(result =>
        {
            if (result.GetValue(emitterCounterOption) < 0)
            {
                result.AddError("--emitter-counter must be a non-negative integer (the 4th version segment is a monotonic counter).");
            }
        });

        var releaseCountersOption = new Option<FileInfo?>("--release-counters")
        {
            Description = "Path to a JSON release-counter store, shared across all packages produced from one source (e.g. Splice or Daml.Finance). Requires --intermediate. When set, the CLI resolves this codegen build's shared generation ordinal from the store — the same ordinal for every package emitted while this codegen version is current, advancing only when the codegen tool version changes — and uses it as the 4th NuGet version segment, overriding --emitter-counter. The store is created on first use and atomically updated on each run."
        };

        var versionSuffixOption = new Option<string?>("--version-suffix")
        {
            Description = "SemVer prerelease suffix appended to generated package versions, e.g. 'preview.2'. Mirrors the emitter prerelease tag. No leading dash."
        };
        versionSuffixOption.Validators.Add(result =>
        {
            var value = result.GetValue(versionSuffixOption);
            if (value is not null && !FourPartPackageVersion.IsValidPrereleaseSuffix(value))
            {
                result.AddError($"--version-suffix '{value}' is not a valid SemVer prerelease suffix: it must be a non-empty dot-separated sequence of [0-9A-Za-z-] identifiers (e.g. preview.2), with no leading dash.");
            }
        });

        var packageLicenseOption = new Option<string>("--package-license")
        {
            Description = "SPDX license expression emitted in the generated .csproj's <PackageLicenseExpression>. Defaults to Apache-2.0.",
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

        var repositoryUrlOption = new Option<string?>("--repository-url")
        {
            Description = "Repository URL emitted in the generated .csproj's <PackageProjectUrl>/<RepositoryUrl>/<RepositoryType>. When omitted, those elements are not emitted."
        };
        repositoryUrlOption.Validators.Add(result =>
        {
            var value = result.GetValue(repositoryUrlOption);
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--repository-url must be a non-empty URL when specified (e.g. https://github.com/acme/widgets).");
            }
        });

        rootCommand.Options.Add(intermediateOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(namespaceOption);
        rootCommand.Options.Add(verbosityOption);
        rootCommand.Options.Add(rootOption);
        rootCommand.Options.Add(nullableOption);
        rootCommand.Options.Add(generateProjectOption);
        rootCommand.Options.Add(includeDepsOption);
        rootCommand.Options.Add(targetFrameworkOption);
        rootCommand.Options.Add(runtimeVersionOption);
        rootCommand.Options.Add(contractIdentifiersOption);
        rootCommand.Options.Add(emitterCounterOption);
        rootCommand.Options.Add(releaseCountersOption);
        rootCommand.Options.Add(packageLicenseOption);
        rootCommand.Options.Add(versionSuffixOption);
        rootCommand.Options.Add(repositoryUrlOption);

        Func<ParseResult, CancellationToken, Task<int>> action = (parseResult, cancellationToken) =>
            RunCodegen(
                new CodegenArgs(
                    parseResult.GetValue(intermediateOption)!,
                    parseResult.GetValue(outputOption)!,
                    parseResult.GetValue(namespaceOption),
                    parseResult.GetValue(verbosityOption),
                    parseResult.GetValue(rootOption),
                    parseResult.GetValue(nullableOption),
                    parseResult.GetValue(generateProjectOption),
                    parseResult.GetValue(includeDepsOption),
                    parseResult.GetValue(targetFrameworkOption)!,
                    parseResult.GetValue(runtimeVersionOption),
                    parseResult.GetValue(contractIdentifiersOption),
                    parseResult.GetValue(emitterCounterOption),
                    parseResult.GetValue(releaseCountersOption),
                    parseResult.GetValue(packageLicenseOption)!,
                    parseResult.GetValue(versionSuffixOption),
                    parseResult.GetValue(repositoryUrlOption)),
                cancellationToken);
        rootCommand.SetAction(action);

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    internal static async Task<int> RunCodegen(CodegenArgs args, CancellationToken cancellationToken)
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

            await GenerateFromIntermediate(args.IntermediateFile, args, logger, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.Error(
                "Code generation was canceled. " +
                $"Partially written files may remain in '{args.OutputDirectory.FullName}'.");
            return 130;
        }
        catch (Exception ex)
        {
            logger.Error($"Code generation failed: {ex.Message}");
            var rootCauseMessage = ex.GetBaseException().Message;
            if (rootCauseMessage != ex.Message)
            {
                logger.Error($"Root cause: {rootCauseMessage}");
            }
            if (args.Verbosity >= 3)
            {
                logger.Error(ex.ToString());
            }
            return 1;
        }
    }

    private static async Task GenerateFromIntermediate(FileInfo file, CodegenArgs args, ConsoleLogger logger, CancellationToken cancellationToken)
    {
        logger.Info($"Reading IntermediateDar: {file.Name}");
        IntermediateDar proto;
        await using (var stream = file.OpenRead())
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var dar = IntermediateDarReader.Read(proto);
        logger.Info($"  Package: {dar.MainPackage.Name} v{dar.MainPackage.Version}");
        logger.Info($"  Modules: {dar.MainPackage.Modules.Count}");
        logger.Debug($"  Dependencies: {dar.Dependencies.Count}");

        var effectiveCounter = args.ReleaseCountersFile is not null
            ? ResolveReleaseCounter(args.ReleaseCountersFile, proto, dar.MainPackage.Name, dar.MainPackage.Version, logger)
            : args.EmitterCounter;

        var generator = new CSharpCodeGenerator(BuildOptions(args, effectiveCounter), logger);
        var generatedFiles = generator.Generate(dar);
        await WriteGeneratedFiles(generatedFiles, args, logger, cancellationToken);
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
        var version = NuGetVersionResolver.Compute(packageVersion, EmitterVersion.Current, store);

        var truncated = hash[..Math.Min(12, hash.Length)];
        logger.Info($"  Release counter: {packageName}@{packageVersion.Major}.{packageVersion.Minor}.{Math.Max(0, packageVersion.Build)} content_hash={truncated}… codegen_version={EmitterVersion.Current} version={version}");

        return version.Revision;
    }

    private static CodeGenOptions BuildOptions(CodegenArgs args, int emitterCounter) =>
        new()
        {
            RootNamespace = args.RootNamespace,
            RootFilter = args.RootFilter,
            EnableNullableReferenceTypes = args.EnableNullable,
            GenerateProjectFile = args.GenerateProjectFile,
            IncludeDependencies = args.IncludeDependencies,
            TargetFramework = args.TargetFramework,
            RuntimePackageVersion = args.RuntimePackageVersion,
            GenerateContractIdentifiers = args.GenerateContractIdentifiers,
            EmitterCounter = emitterCounter,
            PackageLicenseExpression = args.PackageLicenseExpression,
            VersionSuffix = args.VersionSuffix,
            RepositoryUrl = args.RepositoryUrl,
        };

    private static async Task<int> WriteGeneratedFiles(IReadOnlyList<GeneratedFile> generatedFiles, CodegenArgs args, ConsoleLogger logger, CancellationToken cancellationToken)
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

            if (file.IsBinary)
            {
                await File.WriteAllBytesAsync(filePath, file.BinaryContent!, cancellationToken);
            }
            else
            {
                await File.WriteAllTextAsync(filePath, file.Content, cancellationToken);
            }
            logger.Debug($"  Generated: {file.RelativePath}");
            written++;
        }
        return written;
    }
}

internal sealed record CodegenArgs(
    FileInfo IntermediateFile,
    DirectoryInfo OutputDirectory,
    string? RootNamespace,
    int Verbosity,
    string? RootFilter,
    bool EnableNullable,
    bool GenerateProjectFile,
    bool IncludeDependencies,
    string TargetFramework,
    string? RuntimePackageVersion,
    bool GenerateContractIdentifiers,
    int EmitterCounter,
    FileInfo? ReleaseCountersFile,
    string PackageLicenseExpression,
    string? VersionSuffix,
    string? RepositoryUrl);
