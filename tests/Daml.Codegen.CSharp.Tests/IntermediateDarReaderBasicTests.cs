// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbField = Daml.Codegen.Intermediate.Field;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbType = Daml.Codegen.Intermediate.Type;
using PbVariant = Daml.Codegen.Intermediate.Variant;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    [Fact]
    public void intermediate_dar_proto_types_are_available()
    {
        var dar = new IntermediateDar();
        dar.Should().NotBeNull();
    }

    [Fact]
    public void upgraded_package_id_round_trips_from_proto()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p2",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                UpgradedPackageId = "p1-upgraded-id",
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.UpgradedPackageId.Should().Be("p1-upgraded-id");
    }

    [Fact]
    public void empty_upgraded_package_id_round_trips_as_null()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p1",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.UpgradedPackageId.Should().BeNull();
    }

    [Fact]
    public void empty_intermediate_dar_round_trips_to_empty_model()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "pkg-id-1",
                PackageName = "test-pkg",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);

        model.MainPackage.PackageId.Should().Be("pkg-id-1");
        model.MainPackage.Name.Should().Be("test-pkg");
        model.MainPackage.Version.Should().Be(new Version(1, 0, 0));
        model.MainPackage.LfVersion.Should().Be("2.1");
        model.MainPackage.Modules.Should().BeEmpty();
        model.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void null_proto_throws_argument_null_exception()
    {
        var act = () => IntermediateDarReader.Read(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void proto_without_main_throws_invalid_data_exception()
    {
        var proto = new IntermediateDar();
        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*main is required*");
    }

    [Fact]
    public void dependencies_round_trip_in_order()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "main-pkg",
                PackageName = "main",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
            },
            Dependencies =
            {
                new PbPackage
                {
                    PackageId = "dep-a", PackageName = "depA",
                    PackageVersion = "0.1.0", LanguageVersion = "2.1",
                },
                new PbPackage
                {
                    PackageId = "dep-b", PackageName = "depB",
                    PackageVersion = "0.2.0", LanguageVersion = "2.1",
                },
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.Dependencies.Should().HaveCount(2);
        model.Dependencies[0].PackageId.Should().Be("dep-a");
        model.Dependencies[1].PackageId.Should().Be("dep-b");
    }

    [Fact]
    public void module_name_segments_join_with_dot()
    {
        var proto = MakePackageWith(module =>
        {
            module.NameSegments.Clear();
            module.NameSegments.Add("DA");
            module.NameSegments.Add("Internal");
            module.NameSegments.Add("Template");
        });

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Modules[0].Name.Should().Be("DA.Internal.Template");
    }

    [Fact]
    public void invalid_version_falls_back_to_three_part_zero()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p",
                PackageName = "test",
                PackageVersion = "abc",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Version.Should().Be(new Version(0, 0, 0));
    }

    [Fact]
    public void unit_typed_variant_constructor_emits_noarg_record()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Outcome",
                IsSerializable = true,
                Variant = new PbVariant
                {
                    Constructors =
                    {
                        new PbField { Name = "Success", Type = new PbType { Builtin = PbBuiltinType.Unit } },
                    },
                },
            });
        });
        var model = IntermediateDarReader.Read(proto);

        var options = new CodeGen.CodeGenOptions();
        var generator = new CodeGen.CSharpCodeGenerator(options, new ConsoleLogger(0));

        var files = generator.Generate(model);

        var outcomeFile = files.Should().Contain(f => f.RelativePath.EndsWith("Outcome.cs", StringComparison.Ordinal)).Which;
        outcomeFile.Content.Should().Contain("public sealed record Success() :",
            "unit-typed variant constructors collapse to no-arg records in the emitter, not the reader");
        outcomeFile.Content.Should().NotContain("Success(Unit Value)");
    }

    [Fact]
    public void generator_accepts_dar_model_from_intermediate_reader()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Note",
                IsSerializable = true,
                Record = new PbRecord { Fields = { TextField("body") } },
            });
        });
        var model = IntermediateDarReader.Read(proto);

        var options = new CodeGen.CodeGenOptions();
        var logger = new ConsoleLogger(0);
        var generator = new CodeGen.CSharpCodeGenerator(options, logger);

        var files = generator.Generate(model);

        files.Should().NotBeNull();
        files.Should().Contain(f => f.RelativePath.EndsWith("Note.cs", StringComparison.Ordinal));
    }

    private static IntermediateDar MakePackageWith(Action<IntermediateModule> configure)
    {
        var module = new PbModule { NameSegments = { "Foo" } };
        configure(module);
        return new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "pkg-id-1",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules = { module },
            },
        };
    }

    private static Field TextField(string name) => new()
    {
        Name = name,
        Type = new PbType { Builtin = PbBuiltinType.Text },
    };

    private static Field PartyField(string name) => new()
    {
        Name = name,
        Type = new PbType { Builtin = PbBuiltinType.Party },
    };
}
