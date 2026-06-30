// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DamlTypeMapperTests
{
    private const string LocalPackageId = "pkg-id";
    private const string CrossPackageId = "other-pkg";
    private const string StdlibPackageId = "stdlib-pkg";

    private sealed class StubResolver(
        string resolvedName = "Resolved",
        IReadOnlyDictionary<string, DamlPackage>? packages = null) : ICrossPackageResolver
    {
        private readonly IReadOnlyDictionary<string, DamlPackage> _packages = packages ?? new Dictionary<string, DamlPackage>();

        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => resolvedName;

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) =>
            _packages.TryGetValue(packageId, out var package) ? package : null;
    }

    private static DamlPackage Package(string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static PackageEmitContext Context() =>
        PackageEmitContext.ForPackage(Package("test-package"), new CodeGenOptions { RootNamespace = "Test.Package" });

    private static DamlTypeMapper Mapper(StubResolver? resolver = null) =>
        new(Context(), resolver ?? new StubResolver());

    private static DamlPrimitiveType Prim(DamlPrimitive primitive) => new(primitive);

    private static DamlTypeApp App(DamlPrimitive constructor, params DamlType[] arguments) =>
        new(Prim(constructor), arguments);

    private static DamlPackage StdlibPackage() =>
        new()
        {
            PackageId = StdlibPackageId,
            Name = "daml-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

    [Fact]
    public void map_type_maps_text_primitive_to_string()
    {
        Mapper().MapType(Prim(DamlPrimitive.Text)).Should().Be("string");
    }

    [Theory]
    [InlineData(DamlPrimitive.Bool, "bool")]
    [InlineData(DamlPrimitive.Int64, "long")]
    [InlineData(DamlPrimitive.Numeric, "decimal")]
    [InlineData(DamlPrimitive.Date, "DateOnly")]
    [InlineData(DamlPrimitive.Timestamp, "DateTimeOffset")]
    public void map_type_maps_primitives_to_their_clr_types(DamlPrimitive primitive, string expected)
    {
        Mapper().MapType(Prim(primitive)).Should().Be(expected);
    }

    [Fact]
    public void map_type_wraps_list_argument_in_ireadonlylist()
    {
        Mapper().MapType(App(DamlPrimitive.List, Prim(DamlPrimitive.Int64)))
            .Should().Be("IReadOnlyList<long>");
    }

    [Fact]
    public void map_type_renders_optional_argument_as_nullable()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))
            .Should().Be("string?");
    }

    [Fact]
    public void map_type_renders_genmap_as_ireadonlydictionary()
    {
        Mapper().MapType(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)))
            .Should().Be("IReadOnlyDictionary<string, long>");
    }

    [Fact]
    public void map_type_renders_contract_id_argument()
    {
        Mapper().MapType(App(DamlPrimitive.ContractId, Prim(DamlPrimitive.Party)))
            .Should().Be("ContractId<Party>");
    }

    [Fact]
    public void map_type_resolves_cross_package_type_ref_through_the_resolver()
    {
        var mapper = Mapper(new StubResolver(resolvedName: "Acme.Widget"));

        mapper.MapType(new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget"))
            .Should().Be("Acme.Widget");
    }

    [Fact]
    public void map_type_nests_optional_inside_list()
    {
        Mapper().MapType(App(DamlPrimitive.List, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))))
            .Should().Be("IReadOnlyList<string?>");
    }

    [Fact]
    public void map_type_nests_list_inside_optional()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, App(DamlPrimitive.List, Prim(DamlPrimitive.Int64))))
            .Should().Be("IReadOnlyList<long>?");
    }

    [Fact]
    public void map_type_nests_optional_inside_a_genmap_value()
    {
        Mapper().MapType(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64))))
            .Should().Be("IReadOnlyDictionary<string, long?>");
    }

    [Fact]
    public void map_type_resolves_a_cross_package_argument_inside_a_contract_id()
    {
        var mapper = Mapper(new StubResolver(resolvedName: "Acme.Widget"));

        mapper.MapType(App(DamlPrimitive.ContractId, new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget")))
            .Should().Be("ContractId<Acme.Widget>");
    }

    [Fact]
    public void map_type_rejects_nested_optional_with_an_actionable_message()
    {
        Mapper().Invoking(m => m.MapType(
                App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))))
            .Should().Throw<NotSupportedException>()
            .WithMessage("*nested Optional*");
    }

    [Fact]
    public void to_value_serializes_int64_primitive()
    {
        Mapper().ToValue(Prim(DamlPrimitive.Int64), "Amount")
            .Should().Be("new DamlInt64(Amount)");
    }

    [Fact]
    public void to_value_serializes_list_container()
    {
        Mapper().ToValue(App(DamlPrimitive.List, Prim(DamlPrimitive.Text)), "Items")
            .Should().Be("new DamlList(Items.Select(x => (DamlValue)new DamlText(x)).ToList())");
    }

    [Fact]
    public void to_value_serializes_optional_container()
    {
        Mapper().ToValue(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64)), "Maybe")
            .Should().Be("Maybe is { } __Maybe ? new DamlOptional(new DamlInt64(__Maybe)) : DamlOptional.None");
    }

    [Fact]
    public void to_value_serializes_parametric_stdlib_type_through_the_stub()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var either = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Either"),
            [Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).ToValue(either, "Choice")
            .Should().Be("Choice.ToValue(__t0 => (DamlValue)(new DamlText(__t0)), __t1 => (DamlValue)(new DamlInt64(__t1)))");
    }

    [Fact]
    public void from_value_deserializes_int64_primitive()
    {
        Mapper().FromValue(Prim(DamlPrimitive.Int64), "value")
            .Should().Be("value.As<DamlInt64>().Value");
    }

    [Fact]
    public void from_value_deserializes_optional_container()
    {
        Mapper().FromValue(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)), "value")
            .Should().Be("value.AsOptional().HasValue ? value.AsOptional().Value!.As<DamlText>().Value : null");
    }

    [Fact]
    public void from_value_deserializes_a_list_of_optionals()
    {
        Mapper().FromValue(App(DamlPrimitive.List, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "value")
            .Should().Be("(IReadOnlyList<string?>)value.As<DamlList>().Values.Select(x => x.AsOptional().HasValue ? x.AsOptional().Value!.As<DamlText>().Value : null).ToList()");
    }

    [Fact]
    public void from_value_deserializes_parametric_stdlib_type_through_the_stub()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var either = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Either"),
            [Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).FromValue(either, "value")
            .Should().Be("Either<string, long>.FromValue(value, __v0 => __v0.As<DamlText>().Value, __v1 => __v1.As<DamlInt64>().Value)");
    }

    [Fact]
    public void to_value_serializes_set_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var set = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Set.Types", "Set"),
            [Prim(DamlPrimitive.Text)]);

        Mapper(resolver).ToValue(set, "Members")
            .Should().Be("Members.ToRecord(__t0 => (DamlValue)(new DamlText(__t0)))");
    }

    [Fact]
    public void from_value_deserializes_set_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var set = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Set.Types", "Set"),
            [Prim(DamlPrimitive.Text)]);

        Mapper(resolver).FromValue(set, "value")
            .Should().Be("Set<string>.FromRecord(value.As<DamlRecord>(), __v0 => __v0.As<DamlText>().Value)");
    }

    [Fact]
    public void to_value_serializes_nonempty_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var nonEmpty = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.NonEmpty.Types", "NonEmpty"),
            [Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).ToValue(nonEmpty, "Items")
            .Should().Be("Items.ToRecord(__t0 => (DamlValue)(new DamlInt64(__t0)))");
    }

    [Fact]
    public void from_value_deserializes_nonempty_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var nonEmpty = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.NonEmpty.Types", "NonEmpty"),
            [Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).FromValue(nonEmpty, "value")
            .Should().Be("NonEmpty<long>.FromRecord(value.As<DamlRecord>(), __v0 => __v0.As<DamlInt64>().Value)");
    }

    [Fact]
    public void from_value_handles_type_var_with_a_runtime_stub()
    {
        Mapper().FromValue(new DamlTypeVar("a"), "value")
            .Should().Be("GenericStub.NotImplemented<TA>(\"a\")");
    }

    private static readonly IReadOnlyDictionary<Type, DamlType> SubtypeRepresentatives =
        new Dictionary<Type, DamlType>
        {
            [typeof(DamlPrimitiveType)] = Prim(DamlPrimitive.Text),
            [typeof(DamlTypeApp)] = App(DamlPrimitive.List, Prim(DamlPrimitive.Int64)),
            [typeof(DamlTypeRef)] = new DamlTypeRef(LocalPackageId, "Test.Module", "Widget"),
            [typeof(DamlTypeVar)] = new DamlTypeVar("a")
        };

    private static IEnumerable<Type> ConcreteDamlTypeSubtypes() =>
        typeof(DamlType).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(DamlType).IsAssignableFrom(t));

    public static IEnumerable<object[]> EveryDamlTypeSubtype() =>
        SubtypeRepresentatives.Values.Select(type => new object[] { type });

    [Fact]
    public void every_parametric_stdlib_type_has_a_conversion_table_entry()
    {
        Mapper().StdlibConversionKeys.Should().BeEquivalentTo(StdlibPackages.ParametricStdlibTypes);
    }

    [Fact]
    public void every_parametric_stdlib_type_has_a_stdlib_mapping()
    {
        foreach (var (module, name) in StdlibPackages.ParametricStdlibTypes)
        {
            StdlibPackages.MapStdlibType(module, name)
                .Should().NotBeNullOrEmpty(
                    "ParametricStdlibTypes entry ({0}, {1}) must have a MapStdlibType result or codegen throws at emit time",
                    module, name);
        }
    }

    private static readonly IReadOnlyList<(string Module, string Type)> StdlibMappingKeys =
        StdlibPackages.ParametricStdlibTypes
            .Select(p => (Module: p.Module, Type: p.Name))
            .Concat(new[]
            {
                (Module: "DA.Date.Types", Type: "DayOfWeek"),
                (Module: "DA.Time.Types", Type: "RelTime"),
            })
            .ToList();

    public static IEnumerable<object[]> EveryStdlibMappingReturn() =>
        StdlibMappingKeys
            .Select(key => StdlibPackages.MapStdlibType(key.Module, key.Type))
            .Distinct()
            .Select(returned => new object[] { returned! });

    private static string StripGenericArity(string typeName)
    {
        var backtick = typeName.IndexOf('`');
        return backtick < 0 ? typeName : typeName[..backtick];
    }

    [Fact]
    public void every_stdlib_mapping_return_theory_has_cases()
    {
        EveryStdlibMappingReturn().Should().NotBeEmpty(
            "a wholesale null regression in MapStdlibType would otherwise empty the theory and pass silently");

        StdlibPackages.MapStdlibType("DA.Date.Types", "DayOfWeek").Should().NotBeNull(
            "StdlibMappingKeys entry (DA.Date.Types, DayOfWeek) must have a MapStdlibType result");
        StdlibPackages.MapStdlibType("DA.Time.Types", "RelTime").Should().NotBeNull(
            "StdlibMappingKeys entry (DA.Time.Types, RelTime) must have a MapStdlibType result");
    }

    [Theory]
    [MemberData(nameof(EveryStdlibMappingReturn))]
    public void every_stdlib_mapping_return_resolves_to_a_public_runtime_type(string? returnedTypeName)
    {
        returnedTypeName.Should().NotBeNull(
            "a key in StdlibMappingKeys returned null from MapStdlibType, so the switch and the guarded key set have drifted apart");

        var publicStdlibTypeNames = typeof(Daml.Runtime.Stdlib.Unit).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == Daml.Runtime.RuntimeNamespaces.Stdlib)
            .Select(t => StripGenericArity(t.Name))
            .ToHashSet();

        publicStdlibTypeNames.Should().Contain(returnedTypeName,
            "MapStdlibType returns {0} as a C# reference into {1}; a renamed runtime record must fail loudly here instead of drifting into broken generated code",
            returnedTypeName, Daml.Runtime.RuntimeNamespaces.Stdlib);
    }

    [Fact]
    public void drift_guard_covers_every_concrete_subtype_discovered_by_reflection()
    {
        ConcreteDamlTypeSubtypes()
            .Should().BeEquivalentTo(SubtypeRepresentatives.Keys,
                "every concrete DamlType subtype needs a representative so the mapper drift-guard exercises it");
    }

    [Theory]
    [MemberData(nameof(EveryDamlTypeSubtype))]
    public void every_subtype_is_handled_by_all_three_methods_without_throwing(DamlType type)
    {
        var mapper = Mapper();

        mapper.Invoking(m => m.MapType(type)).Should().NotThrow<NotSupportedException>();
        mapper.Invoking(m => m.ToValue(type, "field")).Should().NotThrow<NotSupportedException>();
        mapper.Invoking(m => m.FromValue(type, "value")).Should().NotThrow<NotSupportedException>();
    }
}
