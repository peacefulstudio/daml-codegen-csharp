// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public partial class CodeGenEdgeCaseTests
{
    #region Serialization Edge Cases

    [Fact]
    public void Generate_should_serialize_date_fields_correctly()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DateRecord",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("startDate", new DamlPrimitiveType(DamlPrimitive.Date)),
                        new DamlFieldDefinition("endDate", new DamlPrimitiveType(DamlPrimitive.Date))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var dateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("DateRecord.cs", StringComparison.Ordinal));

        // Assert
        dateFile.Should().NotBeNull();
        var code = dateFile!.Content;

        code.Should().Contain("new DamlDate(StartDate)");
        code.Should().Contain("As<DamlDate>().Value");
    }

    [Fact]
    public void Generate_should_serialize_timestamp_fields_correctly()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "TimestampRecord",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("createdAt", new DamlPrimitiveType(DamlPrimitive.Timestamp))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var timestampFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("TimestampRecord.cs", StringComparison.Ordinal));

        // Assert
        timestampFile.Should().NotBeNull();
        var code = timestampFile!.Content;

        code.Should().Contain("new DamlTimestamp(CreatedAt)");
        code.Should().Contain("As<DamlTimestamp>().Value");
    }

    [Fact]
    public void Generate_should_serialize_contract_id_fields_correctly()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Reference",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("target", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Module", "Asset")]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var refFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Reference.cs", StringComparison.Ordinal));

        // Assert
        refFile.Should().NotBeNull();
        var code = refFile!.Content;

        code.Should().Contain("Target.ToDamlValue()");
        code.Should().Contain("new ContractId<Asset>(");
    }

    [Fact]
    public void Generate_should_serialize_type_ref_fields_correctly()
    {
        // Arrange - A record referencing another record type
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Inner",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                },
                new DamlDataType
                {
                    Name = "Outer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("inner", new DamlTypeRef("", "Test.Module", "Inner"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var outerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Outer.cs", StringComparison.Ordinal));

        // Assert
        outerFile.Should().NotBeNull();
        var code = outerFile!.Content;

        code.Should().Contain("Inner.ToRecord()");
        code.Should().Contain("Inner.FromRecord(");
    }

    #endregion

    #region Empty Field Lists

    [Fact]
    public void Generate_should_handle_empty_record()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "EmptyRecord",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var emptyFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("EmptyRecord.cs", StringComparison.Ordinal));

        // Assert
        emptyFile.Should().NotBeNull();
        var code = emptyFile!.Content;

        // Record with no primary constructor parameters
        code.Should().Contain("public sealed record EmptyRecord : IDamlRecord");
        code.Should().Contain("DamlRecord.Create(");
    }

    #endregion

    #region Multiple Modules

    [Fact]
    public void Generate_should_handle_multiple_modules()
    {
        // Arrange
        var module1 = new DamlModule
        {
            Name = "Module.One",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Type1",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("v", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var module2 = new DamlModule
        {
            Name = "Module.Two",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Type2",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("v", new DamlPrimitiveType(DamlPrimitive.Int64))])
                }
            ],
            Interfaces = []
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module1, module2],
            DependencyReferences = []
        };

        var dar = new DarModel
        {
            MainPackage = package,
            Dependencies = []
        };

        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);

        // Assert
        files.Count.Should().BeGreaterThanOrEqualTo(2);

        var type1File = files.FirstOrDefault(f => f.RelativePath.EndsWith("Type1.cs", StringComparison.Ordinal));
        var type2File = files.FirstOrDefault(f => f.RelativePath.EndsWith("Type2.cs", StringComparison.Ordinal));

        type1File.Should().NotBeNull();
        type2File.Should().NotBeNull();

        // All types from all modules go into the root namespace (derived from package name)
        type1File!.Content.Should().Contain("namespace Test.Package;");
        type2File!.Content.Should().Contain("namespace Test.Package;");
    }

    #endregion
}
