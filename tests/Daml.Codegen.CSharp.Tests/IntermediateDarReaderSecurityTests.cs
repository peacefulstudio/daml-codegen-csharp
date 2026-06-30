// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbEnum = Daml.Codegen.Intermediate.Enum;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbTemplate = Daml.Codegen.Intermediate.Template;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    public static TheoryData<string> NamesOutsideTheDamlLfIdentifierGrammar() =>
        [
            "Evil\" + new object() + \"",
            "Back\\slashName",
            "Brace{Name}",
            "New\nLine",
            "Angle<Name>",
            "Semi;colon",
        ];

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_template_names_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.Templates.Add(new PbTemplate { Name = maliciousName }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>(
                "a hand-crafted --intermediate proto must not be able to inject code into emitted C# string literals")
            .WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_field_names_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Rec",
            Record = new PbRecord { Fields = { TextField(maliciousName) } },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_enum_constructors_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Color",
            EnumType = new PbEnum { Constructors = { maliciousName } },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Fact]
    public void Read_accepts_dotted_type_names_and_tuple_style_field_names()
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Outcome.Win",
            Record = new PbRecord { Fields = { TextField("_1") } },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().NotThrow("dotted Daml-LF type names and tuple field labels are inside the grammar");
    }

    [Fact]
    public void Read_rejects_package_names_outside_the_package_name_grammar()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageName = "evil\"name";

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package name*");
    }

    [Fact]
    public void Read_rejects_an_empty_package_id()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageId = "";

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package id*",
            "an empty package id would flow into generated csproj metadata and namespaces");
    }

    [Fact]
    public void Read_rejects_an_empty_package_name()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageName = "";

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package name*",
            "an empty package name would flow into generated csproj metadata and namespaces");
    }
}
