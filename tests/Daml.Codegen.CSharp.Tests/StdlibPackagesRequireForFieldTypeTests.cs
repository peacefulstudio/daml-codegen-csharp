// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class StdlibPackagesRequireForFieldTypeTests
{
    private const string StdlibPackageId = "stdlib-pkg";
    private const string UserPackageId = "user-pkg";

    private sealed class StubResolver(IReadOnlyDictionary<string, DamlPackage> packages) : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => "Resolved";

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) =>
            packages.TryGetValue(packageId, out var package) ? package : null;
    }

    private static DamlPackage NamedPackage(string packageId, string name) =>
        new()
        {
            PackageId = packageId,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

    private static StubResolver Resolver(params DamlPackage[] packages) =>
        new(packages.ToDictionary(p => p.PackageId));

    private static StubResolver StdlibResolver() => Resolver(NamedPackage(StdlibPackageId, "daml-stdlib"));

    private static DamlPrimitiveType Prim(DamlPrimitive primitive) => new(primitive);

    private static DamlTypeApp App(DamlPrimitive basePrimitive, params DamlType[] arguments) =>
        new(Prim(basePrimitive), arguments);

    private static DamlTypeRef Ref(string packageId, string module, string name) =>
        new(packageId, module, name);

    private static IReadOnlyCollection<string> RequiredFor(ICrossPackageResolver resolver, DamlType type)
    {
        var indent = new IndentWriter(new StringBuilder());
        StdlibPackages.RequireForFieldType(resolver, indent, type);
        return indent.RequiredUsings;
    }

    public static TheoryData<DamlType, string[]> FieldTypeMatrix() => new()
    {
        { App(DamlPrimitive.List, Prim(DamlPrimitive.Text)), ["System.Collections.Generic", "System.Linq"] },
        { App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)), [] },
        { App(DamlPrimitive.TextMap, Prim(DamlPrimitive.Text)), ["System.Collections.Generic", "System.Linq"] },
        { App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Text)), ["System.Collections.Generic", "System.Linq"] },
        { App(DamlPrimitive.ContractId, Prim(DamlPrimitive.Text)), ["Daml.Runtime.Contracts"] },
        { Prim(DamlPrimitive.Date), ["System"] },
        { Prim(DamlPrimitive.Timestamp), ["System"] },
        { Prim(DamlPrimitive.Unit), ["Daml.Runtime.Stdlib"] },
        { Prim(DamlPrimitive.Text), [] },
        { new DamlTypeVar("a"), ["Daml.Runtime.Stdlib"] },
        { App(DamlPrimitive.Optional, App(DamlPrimitive.ContractId, Prim(DamlPrimitive.Text))), ["Daml.Runtime.Contracts"] },
    };

    [Theory]
    [MemberData(nameof(FieldTypeMatrix))]
    public void RequireForFieldType_matches_expected_namespace_set(DamlType type, string[] expected)
    {
        RequiredFor(StdlibResolver(), type).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void RequireForFieldType_requires_stdlib_for_parametric_stdlib_ref()
    {
        var resolver = StdlibResolver();
        var type = new DamlTypeApp(Ref(StdlibPackageId, "DA.Types", "Tuple2"), [Prim(DamlPrimitive.Date), Prim(DamlPrimitive.Text)]);

        RequiredFor(resolver, type).Should().BeEquivalentTo("Daml.Runtime.Stdlib", "System");
    }

    [Fact]
    public void RequireForFieldType_requires_stdlib_for_nonparametric_stdlib_ref()
    {
        var resolver = StdlibResolver();
        var type = Ref(StdlibPackageId, "DA.Time.Types", "RelTime");

        RequiredFor(resolver, type).Should().BeEquivalentTo("Daml.Runtime.Stdlib");
    }

    [Fact]
    public void RequireForFieldType_recurses_into_user_type_app_arguments_skipping_type_vars()
    {
        var resolver = Resolver(NamedPackage(UserPackageId, "my-app"));
        var type = new DamlTypeApp(Ref(UserPackageId, "MyModule", "MyRecord"), [new DamlTypeVar("a"), Prim(DamlPrimitive.Date)]);

        RequiredFor(resolver, type).Should().BeEquivalentTo("System");
    }
}
