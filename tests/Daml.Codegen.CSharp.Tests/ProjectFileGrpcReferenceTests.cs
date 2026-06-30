// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ProjectFileGrpcReferenceTests
{
    [Fact]
    public void project_file_should_not_reference_grpc_transport_client()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().NotContain(".Grpc.Client");
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Ledger.Abstractions\"");
    }
}
