// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;

namespace Daml.Codegen.CSharp.Tests;

public partial class ProjectFileGeneratorTests
{
    private static CodeGenOptions CreateOptions(
        string targetFramework = "net10.0",
        string? runtimeVersion = null,
        bool enableNullable = true,
        string? repositoryUrl = null,
        string? versionSuffix = null)
    {
        return new CodeGenOptions
        {
            TargetFramework = targetFramework,
            RuntimePackageVersion = runtimeVersion,
            EnableNullableReferenceTypes = enableNullable,
            GenerateProjectFile = true,
            RepositoryUrl = repositoryUrl,
            VersionSuffix = versionSuffix
        };
    }
}
