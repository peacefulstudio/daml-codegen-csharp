using Daml.Codegen.CSharp.CodeGen;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class CodeGenOptionsTests
{
    [Fact]
    public void CodeGenOptions_should_have_correct_defaults()
    {
        // Arrange & Act
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/output"
        };

        // Assert
        options.GenerateJsonSupport.Should().BeTrue();
        options.EnableNullableReferenceTypes.Should().BeTrue();
        options.GenerateXmlDocs.Should().BeTrue();
        options.UseFileScopedNamespaces.Should().BeTrue();
        options.UseRecordTypes.Should().BeTrue();
        options.UsePrimaryConstructors.Should().BeTrue();
        options.Verbosity.Should().Be(1);
    }

    [Fact]
    public void CodeGenOptions_should_allow_customization()
    {
        // Arrange & Act
        var options = new CodeGenOptions
        {
            OutputDirectory = "/output",
            RootNamespace = "MyCompany.Contracts",
            RootFilter = ".*Iou.*",
            GenerateJsonSupport = false,
            EnableNullableReferenceTypes = false,
            Verbosity = 3
        };

        // Assert
        options.RootNamespace.Should().Be("MyCompany.Contracts");
        options.RootFilter.Should().Be(".*Iou.*");
        options.GenerateJsonSupport.Should().BeFalse();
        options.EnableNullableReferenceTypes.Should().BeFalse();
        options.Verbosity.Should().Be(3);
    }
}
