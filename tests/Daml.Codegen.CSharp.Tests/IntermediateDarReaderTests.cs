// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using Daml.Codegen.Intermediate;
using FluentAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbDynamicParties = Daml.Codegen.Intermediate.DynamicParties;
using PbEnum = Daml.Codegen.Intermediate.Enum;
using PbField = Daml.Codegen.Intermediate.Field;
using PbInterface = Daml.Codegen.Intermediate.Interface;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbPartyAnalysis = Daml.Codegen.Intermediate.PartyAnalysis;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbStaticParties = Daml.Codegen.Intermediate.StaticParties;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;
using PbTypeApp = Daml.Codegen.Intermediate.TypeApp;
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;
using PbVariant = Daml.Codegen.Intermediate.Variant;

namespace Daml.Codegen.CSharp.Tests;

public class IntermediateDarReaderTests
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
    public void record_data_type_round_trips_with_field_types()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Greeting",
                IsSerializable = true,
                Record = new PbRecord
                {
                    Fields =
                    {
                        TextField("salutation"),
                        TextField("recipient"),
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var dataTypes = model.MainPackage.Modules[0].DataTypes;

        dataTypes.Should().HaveCount(1);
        dataTypes[0].Name.Should().Be("Greeting");
        dataTypes[0].Serializable.Should().BeTrue();
        var record = dataTypes[0].Definition.Should().BeOfType<DamlRecordDefinition>().Subject;
        record.Fields.Should().HaveCount(2);
        record.Fields[0].Name.Should().Be("salutation");
        record.Fields[0].Type.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.Text);
    }

    [Fact]
    public void variant_data_type_round_trips_constructors()
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
                        new PbField { Name = "Failure", Type = new PbType { Builtin = PbBuiltinType.Text } },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var variant = model.MainPackage.Modules[0].DataTypes[0].Definition
            .Should().BeOfType<DamlVariantDefinition>().Subject;
        variant.Constructors.Should().HaveCount(2);
        variant.Constructors[0].Name.Should().Be("Success");
        variant.Constructors[0].ArgumentType.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.Unit,
                "the proto path preserves wire-level fidelity — the Unit-vs-noarg collapse happens in the emitter, not the reader");
        variant.Constructors[1].Name.Should().Be("Failure");
        variant.Constructors[1].ArgumentType.Should().NotBeNull();
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

        var options = new CodeGen.CodeGenOptions { OutputDirectory = "/tmp/test-147" };
        var generator = new CodeGen.CSharpCodeGenerator(options, new ConsoleLogger(0));

        var files = generator.Generate(model);

        var outcomeFile = files.Should().Contain(f => f.RelativePath.EndsWith("Outcome.cs", StringComparison.Ordinal)).Which;
        outcomeFile.Content.Should().Contain("public sealed record Success() :",
            "unit-typed variant constructors collapse to no-arg records in the emitter, not the reader");
        outcomeFile.Content.Should().NotContain("Success(Unit Value)");
    }

    [Fact]
    public void enum_data_type_round_trips_constructors()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Colour",
                IsSerializable = true,
                EnumType = new PbEnum
                {
                    Constructors = { "Red", "Green", "Blue" },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var enumDef = model.MainPackage.Modules[0].DataTypes[0].Definition
            .Should().BeOfType<DamlEnumDefinition>().Subject;
        enumDef.Constructors.Should().Equal("Red", "Green", "Blue");
    }

    [Fact]
    public void type_app_round_trips_with_function_and_arguments()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField
                        {
                            Name = "contents",
                            Type = new PbType
                            {
                                TypeApp = new PbTypeApp
                                {
                                    Function = new PbType { Builtin = PbBuiltinType.List },
                                    Arguments = { new PbType { Builtin = PbBuiltinType.Text } },
                                },
                            },
                        },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        var typeApp = record.Fields[0].Type.Should().BeOfType<DamlTypeApp>().Subject;
        typeApp.Base.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.List);
        typeApp.Arguments.Should().HaveCount(1);
        typeApp.Arguments[0].Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.Text);
    }

    [Fact]
    public void type_con_round_trips_to_daml_type_ref()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Wrapper",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField
                        {
                            Name = "inner",
                            Type = new PbType
                            {
                                TypeCon = new PbTypeConName
                                {
                                    PackageId = "other-pkg",
                                    ModuleNameSegments = { "Foo", "Bar" },
                                    NameSegments = { "Inner" },
                                },
                            },
                        },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        var typeRef = record.Fields[0].Type.Should().BeOfType<DamlTypeRef>().Subject;
        typeRef.PackageId.Should().Be("other-pkg");
        typeRef.Module.Should().Be("Foo.Bar");
        typeRef.Name.Should().Be("Inner");
    }

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
    public void template_signatories_default_to_dynamic()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "T",
                Record = new PbRecord { Fields = { PartyField("party") } },
            });
            module.Templates.Add(new PbTemplate { Name = "T" });
        });

        var model = IntermediateDarReader.Read(proto);
        var template = model.MainPackage.Modules[0].Templates.Single();
        template.Signatories.Source.Should().Be(DamlPartySource.Dynamic);
        template.Observers.Source.Should().Be(DamlPartySource.Dynamic);
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

        var options = new CodeGen.CodeGenOptions { OutputDirectory = "/tmp/test-147" };
        var logger = new ConsoleLogger(0);
        var generator = new CodeGen.CSharpCodeGenerator(options, logger);

        var files = generator.Generate(model);

        files.Should().NotBeNull();
        files.Should().Contain(f => f.RelativePath.EndsWith("Note.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void unspecified_builtin_throws_invalid_data()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField { Name = "v", Type = new PbType { Builtin = PbBuiltinType.Unspecified } },
                    },
                },
            });
        });

        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*BUILTIN_TYPE_UNSPECIFIED*");
    }

    [Fact]
    public void unmapped_builtin_throws_not_supported()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField { Name = "v", Type = new PbType { Builtin = PbBuiltinType.Bignumeric } },
                    },
                },
            });
        });

        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<NotSupportedException>()
           .WithMessage("*BIGNUMERIC*");
    }

    [Fact]
    public void type_with_no_sort_set_throws_invalid_data()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields = { new PbField { Name = "v", Type = new PbType() } },
                },
            });
        });

        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*sort*");
    }

    [Fact]
    public void nat_type_round_trips_as_type_var()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Decimal10",
                Record = new PbRecord
                {
                    Fields = { new PbField { Name = "scale", Type = new PbType { Nat = 10 } } },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        record.Fields[0].Type.Should().BeOfType<DamlTypeVar>()
            .Which.Name.Should().Be("10");
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

    [Fact]
    public void field_with_null_type_throws()
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
                                Name = "R",
                                IsSerializable = true,
                                Record = new PbRecord
                                {
                                    Fields = { new PbField { Name = "x" } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void variant_constructor_with_null_type_throws()
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
                                Name = "V",
                                IsSerializable = true,
                                Variant = new PbVariant
                                {
                                    Constructors = { new PbField { Name = "Ctor" } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>();
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

    [Fact]
    public void data_type_with_no_shape_throws()
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
                        DataTypes = { new PbDataType { Name = "Bare", IsSerializable = true } },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Bare*");
    }

    [Fact]
    public void template_with_unset_signatories_defaults_to_dynamic()
    {
        var proto = MakeProtoWithSingleTemplate(new PbTemplate
        {
            Name = "Tpl",
        });

        var model = IntermediateDarReader.Read(proto);

        var template = model.MainPackage.Modules[0].Templates[0];
        template.Signatories.Source.Should().Be(DamlPartySource.Dynamic);
        template.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void template_with_static_signatories_reads_payload_field_references()
    {
        var proto = MakeProtoWithSingleTemplate(new PbTemplate
        {
            Name = "Tpl",
            Signatories = new PbPartyAnalysis
            {
                Static = new PbStaticParties { PayloadFields = { "platform", "initiator" } },
            },
            Observers = new PbPartyAnalysis
            {
                Dynamic = new PbDynamicParties(),
            },
        });

        var model = IntermediateDarReader.Read(proto);
        var template = model.MainPackage.Modules[0].Templates[0];

        template.Signatories.Source.Should().Be(DamlPartySource.Static);
        template.Signatories.Parties.Should().HaveCount(2);
        template.Signatories.Parties.Select(p => ((DamlPartyPayloadField)p).FieldName)
            .Should().Equal("platform", "initiator");
        template.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void choice_party_analysis_round_trips_static_and_dynamic()
    {
        var proto = MakeProtoWithSingleTemplate(new PbTemplate
        {
            Name = "Tpl",
            Choices =
            {
                new PbChoice
                {
                    Name = "Archive",
                    Consuming = true,
                    ArgumentType = BuiltinType(PbBuiltinType.Unit),
                    ReturnType = BuiltinType(PbBuiltinType.Unit),
                    Controllers = new PbPartyAnalysis
                    {
                        Static = new PbStaticParties { PayloadFields = { "owner" } },
                    },
                    Observers = new PbPartyAnalysis { Dynamic = new PbDynamicParties() },
                },
            },
        });

        var model = IntermediateDarReader.Read(proto);
        var choice = model.MainPackage.Modules[0].Templates[0].Choices[0];

        choice.Controllers.Source.Should().Be(DamlPartySource.Static);
        choice.Controllers.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
        choice.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    private static IntermediateDar MakeProtoWithSingleTemplate(PbTemplate template) =>
        new()
        {
            Main = new PbPackage
            {
                PackageId = "p1",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules =
                {
                    new PbModule { NameSegments = { "M" }, Templates = { template } },
                },
            },
        };

    private static PbType BuiltinType(PbBuiltinType b) => new() { Builtin = b };
}
