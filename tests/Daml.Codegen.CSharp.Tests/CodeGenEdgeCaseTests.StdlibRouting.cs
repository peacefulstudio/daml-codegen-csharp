// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public partial class CodeGenEdgeCaseTests
{
    #region Stdlib Type Routing Tests

    [Fact]
    public void Generate_should_route_stdlib_RelTime_through_Daml_Runtime_Stdlib_namespace()
    {
        // Arrange — daml-stdlib-DA-Time-Types is a stdlib package whose `RelTime`
        // type the codegen maps to the hand-coded Daml.Runtime.Stdlib.RelTime.
        // Stdlib refs must NOT produce a <PackageReference> on the generated csproj.
        const string StdlibTimePackageId = "stdlib-time-id";

        var stdlibModule = new DamlModule
        {
            Name = "DA.Time.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "RelTime",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("microseconds", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                }
            ],
            Interfaces = []
        };
        var stdlibPkg = CreateTestPackage(StdlibTimePackageId, "daml-stdlib-DA-Time-Types", stdlibModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Timer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("d", new DamlTypeRef(StdlibTimePackageId, "DA.Time.Types", "RelTime"))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, stdlibPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var timer = files.FirstOrDefault(f => f.RelativePath.EndsWith("Timer.cs", StringComparison.Ordinal));
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        // Assert
        timer.Should().NotBeNull();
        timer!.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        timer.Content.Should().Contain("RelTime D");
        timer.Content.Should().Contain("RelTime.FromRecord");
        // No <PackageReference> for stdlib packages — they're served by the runtime stub.
        csproj.Should().NotBeNull();
        csproj!.Content.Should().NotContain("Daml.Stdlib");
        csproj.Content.Should().NotContain("daml-stdlib");
    }

    [Fact]
    public void Generate_should_route_stdlib_Tuple2_through_Daml_Runtime_Stdlib_namespace()
    {
        // Arrange — Tuple2 lives in daml-prim's DA.Types module. The codegen must
        // route it to Daml.Runtime.Stdlib.Tuple2 with the parameterised arguments
        // preserved, and emit delegate-based ToRecord / FromRecord.
        const string DamlPrimPackageId = "daml-prim-id";

        var stdlibModule = new DamlModule
        {
            Name = "DA.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Tuple2",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("_1", new DamlTypeVar("a")),
                        new DamlFieldDefinition("_2", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };
        var stdlibPkg = CreateTestPackage(DamlPrimPackageId, "daml-prim-DA-Types", stdlibModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Pair",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("p", new DamlTypeApp(
                            new DamlTypeRef(DamlPrimPackageId, "DA.Types", "Tuple2"),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Int64),
                                new DamlPrimitiveType(DamlPrimitive.Text)
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, stdlibPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var pair = files.FirstOrDefault(f => f.RelativePath.EndsWith("Pair.cs", StringComparison.Ordinal));

        // Assert — type uses the stdlib name, FromRecord uses delegate-based decoder.
        pair.Should().NotBeNull();
        pair!.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        pair.Content.Should().Contain("Tuple2<long, string>");
        pair.Content.Should().Contain("Tuple2<long, string>.FromRecord(");
    }

    [Fact]
    public void Generate_should_route_stdlib_Set_through_Daml_Runtime_Stdlib_namespace()
    {
        // Arrange — Set lives in daml-stdlib's DA.Set.Types module. Wire shape is
        // a record wrapping `Map k ()`, exposed as Daml.Runtime.Stdlib.Set<k>.
        const string SetPackageId = "stdlib-set-types";

        var stdlibModule = new DamlModule
        {
            Name = "DA.Set.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Set",
                    TypeParams = ["k"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("map", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [new DamlTypeVar("k"), new DamlPrimitiveType(DamlPrimitive.Unit)]))
                    ])
                }
            ],
            Interfaces = []
        };
        var stdlibPkg = CreateTestPackage(SetPackageId, "daml-stdlib-DA-Set-Types", stdlibModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Roster",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("members", new DamlTypeApp(
                            new DamlTypeRef(SetPackageId, "DA.Set.Types", "Set"),
                            [new DamlPrimitiveType(DamlPrimitive.Party)]))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, stdlibPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var roster = files.FirstOrDefault(f => f.RelativePath.EndsWith("Roster.cs", StringComparison.Ordinal));

        // Assert
        roster.Should().NotBeNull();
        roster!.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        roster.Content.Should().Contain("Set<Party>");
        roster.Content.Should().Contain("Set<Party>.FromRecord(");
    }

    [Fact]
    public void Generate_should_not_route_user_defined_DA_Types_Tuple2_through_Daml_Runtime_Stdlib()
    {
        const string UserPackageId = "user-pkg-id";

        var userTuplesModule = new DamlModule
        {
            Name = "DA.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Tuple2",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("_1", new DamlTypeVar("a")),
                        new DamlFieldDefinition("_2", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };
        var userTuplesPkg = CreateTestPackage(UserPackageId, "my-cheeky-package", userTuplesModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Pair",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("p", new DamlTypeApp(
                            new DamlTypeRef(UserPackageId, "DA.Types", "Tuple2"),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Int64),
                                new DamlPrimitiveType(DamlPrimitive.Text)
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, userTuplesPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);

        var files = generator.Generate(dar).ToList();
        var pair = files.FirstOrDefault(f => f.RelativePath.EndsWith("Pair.cs", StringComparison.Ordinal));

        pair.Should().NotBeNull();
        pair!.Content.Should().NotContain("Daml.Runtime.Stdlib.Tuple2");
        pair.Content.Should().NotContain("using Daml.Runtime.Stdlib;");
    }

    [Fact]
    public void Generate_should_route_stdlib_Tuple2_to_runtime_when_package_is_placeholder_named()
    {
        const string PlaceholderPackageId = "lf1x-prim-types-id";

        var stdlibModule = new DamlModule
        {
            Name = "DA.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Tuple2",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("_1", new DamlTypeVar("a")),
                        new DamlFieldDefinition("_2", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };
        var stdlibPkg = CreateTestPackage(PlaceholderPackageId, "-no-package-metadata", stdlibModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Pair",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("p", new DamlTypeApp(
                            new DamlTypeRef(PlaceholderPackageId, "DA.Types", "Tuple2"),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Int64),
                                new DamlPrimitiveType(DamlPrimitive.Text)
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, stdlibPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        var files = generator.Generate(dar).ToList();
        var pair = files.FirstOrDefault(f => f.RelativePath.EndsWith("Pair.cs", StringComparison.Ordinal));
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        pair.Should().NotBeNull();
        pair!.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        pair.Content.Should().Contain("Tuple2<long, string>");
        pair.Content.Should().Contain("Tuple2<long, string>.FromRecord(");
        pair.Content.Should().NotContain("No.Package.Metadata");

        csproj.Should().NotBeNull();
        csproj!.Content.Should().NotContain("No.Package.Metadata");
        csproj.Content.Should().NotContain("PackageReference Include=\".");
    }

    #endregion
}
