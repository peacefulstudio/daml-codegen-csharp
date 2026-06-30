// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class EnumEmitterTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlPackage Package(params DamlDataType[] dataTypes) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Main",
                    Templates = [],
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options(bool generateXmlDocs) =>
        new() { RootNamespace = "Test.Package", GenerateXmlDocs = generateXmlDocs };

    private static string EmitEnum(string name, string[] constructors, bool generateXmlDocs = true)
    {
        var enumDef = new DamlEnumDefinition(constructors);
        var dataType = new DamlDataType { Name = name, Definition = enumDef };
        var options = Options(generateXmlDocs);
        var context = PackageEmitContext.ForPackage(Package(dataType), options);
        var emitter = new EnumEmitter(context, options);
        var sb = new StringBuilder();
        emitter.WriteEnumType(new IndentWriter(sb), dataType, enumDef);
        return sb.ToString();
    }

    [Fact]
    public void emits_the_enum_declaration_with_every_constructor()
    {
        var output = EmitEnum("Status", ["Pending", "Active", "Completed", "Cancelled"]);

        output.Should().Contain("public enum Status");
        output.Should().Contain("Pending,");
        output.Should().Contain("Active,");
        output.Should().Contain("Completed,");
        output.Should().Contain("Cancelled,");
    }

    [Fact]
    public void names_extension_methods_after_their_DamlEnum_type()
    {
        var output = EmitEnum("Status", ["Pending", "Active"]);

        output.Should().Contain("public static DamlEnum ToDamlEnum(this Status value)");
        output.Should().Contain("public static Status FromDamlEnum(DamlEnum value)");
        output.Should().NotContain("ToRecord");
        output.Should().NotContain("FromRecord");
    }

    [Fact]
    public void round_trips_each_constructor_through_the_DamlEnum_serializers()
    {
        var output = EmitEnum("Status", ["Pending", "Active"]);

        output.Should().Contain("Status.Pending => DamlEnum.Create(\"Pending\"),");
        output.Should().Contain("Status.Active => DamlEnum.Create(\"Active\"),");
        output.Should().Contain("\"Pending\" => Status.Pending,");
        output.Should().Contain("\"Active\" => Status.Active,");
    }

    [Fact]
    public void emits_xml_docs_when_enabled()
    {
        var output = EmitEnum("Color", ["Red", "Green", "Blue"], generateXmlDocs: true);

        output.Should().Contain("/// <summary>");
        output.Should().Contain("/// Generated from Daml enum Color");
        output.Should().Contain("/// </summary>");
        output.Should().Contain("/// Extension methods for Color serialization.");
    }

    [Fact]
    public void omits_the_type_xml_docs_when_disabled()
    {
        var output = EmitEnum("Color", ["Red", "Green", "Blue"], generateXmlDocs: false);

        output.Should().NotContain("/// Generated from Daml enum Color");
        output.Should().NotContain("/// Extension methods for Color serialization.");
        output.Should().NotContain("Converts to a DamlEnum value");
        output.Should().NotContain("Creates an instance from a DamlEnum value");
        output.Should().Contain("public enum Color");
    }
}
