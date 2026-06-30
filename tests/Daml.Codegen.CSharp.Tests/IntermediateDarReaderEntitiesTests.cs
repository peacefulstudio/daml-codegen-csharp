// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbInterface = Daml.Codegen.Intermediate.Interface;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    [Fact]
    public void template_round_trips_with_choices_and_fields_from_record()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Iou",
                Record = new PbRecord
                {
                    Fields = { TextField("issuer"), TextField("currency") },
                },
            });
            module.Templates.Add(new PbTemplate
            {
                Name = "Iou",
                Choices =
                {
                    new PbChoice
                    {
                        Name = "Transfer",
                        Consuming = true,
                        ArgumentType = new PbType { Builtin = PbBuiltinType.Unit },
                        ReturnType = new PbType { Builtin = PbBuiltinType.Unit },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var template = model.MainPackage.Modules[0].Templates.Single();
        template.Name.Should().Be("Iou");
        template.Fields.Should().HaveCount(2);
        template.Fields[0].Name.Should().Be("issuer");
        template.Choices.Should().HaveCount(1);
        template.Choices[0].Name.Should().Be("Transfer");
        template.Choices[0].Consuming.Should().BeTrue();
    }

    [Fact]
    public void interface_round_trips_with_view_type_and_choices()
    {
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface
            {
                Name = "Holding",
                ViewType = new PbType
                {
                    TypeCon = new PbTypeConName
                    {
                        PackageId = "self",
                        ModuleNameSegments = { "Foo" },
                        NameSegments = { "HoldingView" },
                    },
                },
                Choices =
                {
                    new PbChoice
                    {
                        Name = "Split",
                        Consuming = false,
                        ArgumentType = new PbType { Builtin = PbBuiltinType.Int64 },
                        ReturnType = new PbType { Builtin = PbBuiltinType.Unit },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var iface = model.MainPackage.Modules[0].Interfaces.Single();
        iface.Name.Should().Be("Holding");
        iface.ViewType.Should().BeOfType<DamlTypeRef>()
            .Which.Name.Should().Be("HoldingView");
        iface.Choices.Should().HaveCount(1);
        iface.Choices[0].Name.Should().Be("Split");
    }

    [Fact]
    public void template_with_no_key_type_yields_null_key()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "T",
                Record = new PbRecord { Fields = { TextField("v") } },
            });
            module.Templates.Add(new PbTemplate { Name = "T" });
        });

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Modules[0].Templates.Single().Key.Should().BeNull();
    }

    [Fact]
    public void template_with_key_type_round_trips()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "T",
                Record = new PbRecord { Fields = { TextField("v") } },
            });
            module.Templates.Add(new PbTemplate
            {
                Name = "T",
                KeyType = new PbType { Builtin = PbBuiltinType.Party },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var key = model.MainPackage.Modules[0].Templates.Single().Key;
        key.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.Party);
    }

    [Fact]
    public void interface_with_no_view_type_yields_null_view()
    {
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface { Name = "I" });
        });

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Modules[0].Interfaces.Single().ViewType.Should().BeNull();
    }

    [Fact]
    public void duplicate_template_and_data_type_name_collision_does_not_throw()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Iou",
                Record = new PbRecord { Fields = { TextField("issuer") } },
            });
            module.Templates.Add(new PbTemplate { Name = "Iou" });
        });

        var model = IntermediateDarReader.Read(proto);
        var template = model.MainPackage.Modules[0].Templates.Single();
        template.Fields.Should().HaveCount(1);
        template.Fields[0].Name.Should().Be("issuer");
    }

    [Fact]
    public void choice_with_null_argument_type_throws()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules =
                {
                    new PbModule
                    {
                        NameSegments = { "M" },
                        DataTypes =
                        {
                            new PbDataType
                            {
                                Name = "T",
                                IsSerializable = true,
                                Record = new PbRecord(),
                            },
                        },
                        Templates =
                        {
                            new PbTemplate
                            {
                                Name = "T",
                                Choices =
                                {
                                    new PbChoice
                                    {
                                        Name = "Exercise",
                                        Consuming = true,
                                        ReturnType = new PbType { Builtin = PbBuiltinType.Unit },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Exercise*argument_type*");
    }

    [Fact]
    public void choice_with_null_return_type_throws()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules =
                {
                    new PbModule
                    {
                        NameSegments = { "M" },
                        DataTypes =
                        {
                            new PbDataType
                            {
                                Name = "T",
                                IsSerializable = true,
                                Record = new PbRecord(),
                            },
                        },
                        Templates =
                        {
                            new PbTemplate
                            {
                                Name = "T",
                                Choices =
                                {
                                    new PbChoice
                                    {
                                        Name = "Exercise",
                                        Consuming = true,
                                        ArgumentType = new PbType { Builtin = PbBuiltinType.Unit },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Exercise*return_type*");
    }
}
