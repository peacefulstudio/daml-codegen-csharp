// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// End-to-end pipeline tests for template emission through the public
/// <see cref="CSharpCodeGenerator.Generate"/> surface: the project-file
/// companions, file placement, and the delegation seam onto
/// <see cref="TemplateEmitter"/>. The template body is asserted directly in
/// <see cref="TemplateEmitterTests"/>.
/// </summary>
public class TemplateCodeGenTests
{
    private static DamlModule SingleTemplateModule(string moduleName, string templateName) =>
        new()
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

    [Fact]
    public void Generate_should_emit_a_README_when_project_file_generation_is_enabled()
    {
        var generator = CreateGenerator(new CodeGenOptions { GenerateProjectFile = true });

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));

        files.Should().ContainSingle(f => f.RelativePath == "README.md",
            "the csproj declares <PackageReadmeFile>README.md</PackageReadmeFile>, so dropping the README would fail dotnet pack with NU5039");
    }

    [Fact]
    public void Generate_should_emit_an_icon_when_project_file_generation_is_enabled()
    {
        var generator = CreateGenerator(new CodeGenOptions { GenerateProjectFile = true });

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));

        files.Should().ContainSingle(f => f.RelativePath == "icon.png",
            "the csproj declares <PackageIcon>icon.png</PackageIcon>, so dropping the icon would fail dotnet pack with NU5046")
            .Which.BinaryContent.Should().NotBeNullOrEmpty("the packed icon must carry its PNG bytes");
    }

    [Fact]
    public void Generate_should_not_emit_an_icon_when_project_file_generation_is_disabled()
    {
        var generator = CreateGenerator(new CodeGenOptions { GenerateProjectFile = false });

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));

        files.Should().NotContain(f => f.RelativePath == "icon.png");
    }

    [Fact]
    public void Generate_should_place_the_template_file_under_the_root_namespace_directory()
    {
        var generator = CreateGenerator();

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Deeply.Nested.Module", "MyTemplate")));
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("MyTemplate.cs", StringComparison.Ordinal));

        templateFile.Should().NotBeNull();
        templateFile!.RelativePath.Should().Contain("Test/Package/");
    }

    [Fact]
    public void Generate_should_delegate_template_emission_to_the_template_emitter()
    {
        var generator = CreateGenerator();

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("SimpleTemplate.cs", StringComparison.Ordinal));

        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("public sealed partial record SimpleTemplate");
        code.Should().Contain(": ITemplate");
        code.Should().Contain("public sealed record ContractId(string Value)");
        code.Should().Contain("public sealed record Contract(ContractId Id, SimpleTemplate Data)");
    }
}
