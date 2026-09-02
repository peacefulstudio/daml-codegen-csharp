// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
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
    public void MapType_maps_text_primitive_to_string()
    {
        Mapper().MapType(Prim(DamlPrimitive.Text)).Should().Be("string");
    }

    [Theory]
    [InlineData(DamlPrimitive.Bool, "bool")]
    [InlineData(DamlPrimitive.Int64, "long")]
    [InlineData(DamlPrimitive.Numeric, "decimal")]
    [InlineData(DamlPrimitive.Date, "DateOnly")]
    [InlineData(DamlPrimitive.Timestamp, "DateTimeOffset")]
    public void MapType_maps_primitives_to_their_clr_types(DamlPrimitive primitive, string expected)
    {
        Mapper().MapType(Prim(primitive)).Should().Be(expected);
    }

    [Fact]
    public void MapType_wraps_list_argument_in_ireadonlylist()
    {
        Mapper().MapType(App(DamlPrimitive.List, Prim(DamlPrimitive.Int64)))
            .Should().Be("IReadOnlyList<long>");
    }

    [Fact]
    public void MapType_renders_optional_argument_as_nullable()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))
            .Should().Be("string?");
    }

    [Fact]
    public void MapType_renders_genmap_as_ireadonlydictionary()
    {
        Mapper().MapType(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)))
            .Should().Be("IReadOnlyDictionary<string, long>");
    }

    [Fact]
    public void MapType_renders_contract_id_argument()
    {
        Mapper().MapType(App(DamlPrimitive.ContractId, Prim(DamlPrimitive.Party)))
            .Should().Be("ContractId<Party>");
    }

    [Fact]
    public void MapType_resolves_cross_package_type_ref_through_the_resolver()
    {
        var mapper = Mapper(new StubResolver(resolvedName: "Acme.Widget"));

        mapper.MapType(new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget"))
            .Should().Be("Acme.Widget");
    }

    [Fact]
    public void MapType_nests_optional_inside_list()
    {
        Mapper().MapType(App(DamlPrimitive.List, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))))
            .Should().Be("IReadOnlyList<string?>");
    }

    [Fact]
    public void MapType_nests_list_inside_optional()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, App(DamlPrimitive.List, Prim(DamlPrimitive.Int64))))
            .Should().Be("IReadOnlyList<long>?");
    }

    [Fact]
    public void MapType_rejects_excessively_deep_types_before_managed_stack_overflow()
    {
        var type = Enumerable.Range(0, 300)
            .Aggregate((DamlType)Prim(DamlPrimitive.Text), (inner, _) => App(DamlPrimitive.List, inner));

        Mapper().Invoking(m => m.MapType(type))
            .Should().Throw<InvalidDataException>()
            .WithMessage("*depth*");
    }

    [Fact]
    public void MapType_nests_optional_inside_a_genmap_value()
    {
        Mapper().MapType(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64))))
            .Should().Be("IReadOnlyDictionary<string, long?>");
    }

    [Fact]
    public void MapType_resolves_a_cross_package_argument_inside_a_contract_id()
    {
        var mapper = Mapper(new StubResolver(resolvedName: "Acme.Widget"));

        mapper.MapType(App(DamlPrimitive.ContractId, new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget")))
            .Should().Be("ContractId<Acme.Widget>");
    }

    [Fact]
    public void MapType_emits_the_wrapper_at_both_levels_of_a_nested_optional()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))))
            .Should().Be("Optional<Optional<string>>");
    }

    [Fact]
    public void MapType_emits_the_wrapper_at_both_levels_of_a_nested_optional_over_a_type_variable()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, new DamlTypeVar("a"))))
            .Should().Be("Optional<Optional<TA>>");
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_a_nested_optional_passed_to_an_emitted_generic()
    {
        Mapper(BoxResolver()).MapType(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))]))
            .Should().Be("Acme.Box<Optional<Optional<string>>>");
    }

    [Fact]
    public void ToValue_emits_the_chain_for_a_nested_optional_passed_to_an_emitted_generic()
    {
        Mapper(BoxResolver()).ToValue(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))]),
                "Field")
            .Should().Be(
                "Field.ToRecord(__t0 => (DamlValue)(__t0.ToChainValue(__optional1 => "
                + "__optional1.ToChainValue(__optional2 => new DamlText(__optional2)))))");
    }

    [Fact]
    public void MapType_refuses_a_nested_optional_passed_to_a_generic_that_wraps_the_parameter()
    {
        var act = () => Mapper(CrateResolver()).MapType(
            new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Crate"),
                [App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))]));

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*")
            .WithMessage("*one array level short*");
    }

    [Fact]
    public void ToValue_refuses_a_single_optional_passed_to_a_generic_that_wraps_the_parameter()
    {
        var act = () => Mapper(CrateResolver()).ToValue(
            new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Crate"),
                [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]),
            "Field");

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*");
    }

    [Fact]
    public void FromValue_refuses_a_single_optional_passed_to_a_generic_that_wraps_the_parameter()
    {
        var act = () => Mapper(CrateResolver()).FromValue(
            new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Crate"),
                [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]),
            "value");

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*");
    }

    [Fact]
    public void MapType_keeps_a_single_optional_passed_to_a_generic_that_does_not_wrap_the_parameter()
    {
        Mapper(BoxResolver()).MapType(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]))
            .Should().Be("Acme.Box<Optional<string>>");
    }

    [Fact]
    public void ToValue_serializes_every_level_of_a_nested_optional_through_the_chain_encoding()
    {
        Mapper().ToValue(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "MaybeMaybeNote")
            .Should().Be("MaybeMaybeNote.ToChainValue(__optional0 => __optional0.ToChainValue(__optional1 => new DamlText(__optional1)))");
    }

    [Fact]
    public void FromValue_deserializes_every_level_of_a_nested_optional_through_the_chain_encoding()
    {
        Mapper().FromValue(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "value")
            .Should().Contain("Optional<Optional<string>>.FromChainValue(")
            .And.Contain("Optional<string>.FromChainValue(");
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_an_optional_genmap_key()
    {
        Mapper().MapType(App(
                DamlPrimitive.GenMap,
                App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                Prim(DamlPrimitive.Int64)))
            .Should().Be("IReadOnlyDictionary<Optional<string>, long>");
    }

    [Fact]
    public void FromValue_deserializes_an_optional_genmap_key_through_the_wrapper()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                    Prim(DamlPrimitive.Int64)),
                "value")
            .Should().Be(
                "(IReadOnlyDictionary<Optional<string>, long>)value.As<DamlGenMap>().Entries.ToDictionary("
                + "kv => Optional<string>.FromValue(kv.Key, __optional1 => __optional1.As<DamlText>().Value), "
                + "kv => kv.Value.As<DamlInt64>().Value)");
    }

    [Fact]
    public void FromValue_never_emits_a_nullable_key_selector_for_an_optional_genmap_key()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                    Prim(DamlPrimitive.Int64)),
                "value")
            .Should().NotContain(": null");
    }

    [Fact]
    public void ToValue_serializes_an_optional_genmap_key_through_the_flat_wrapper()
    {
        Mapper().ToValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                    Prim(DamlPrimitive.Int64)),
                "Registry")
            .Should().Be(
                "new DamlGenMap(Registry.Select(kv => ("
                + "(DamlValue)kv.Key.ToValue(__optional1 => new DamlText(__optional1)), "
                + "(DamlValue)new DamlInt64(kv.Value))).ToList())");
    }

    [Fact]
    public void FromValue_deserializes_a_nested_optional_genmap_key_through_the_chain_encoding()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))),
                    Prim(DamlPrimitive.Int64)),
                "value")
            .Should().Be(
                "(IReadOnlyDictionary<Optional<Optional<string>>, long>)value.As<DamlGenMap>().Entries.ToDictionary("
                + "kv => Optional<Optional<string>>.FromChainValue(kv.Key, __optional1 => "
                + "Optional<string>.FromChainValue(__optional1, __optional2 => __optional2.As<DamlText>().Value)), "
                + "kv => kv.Value.As<DamlInt64>().Value)");
    }

    [Fact]
    public void FromValue_keeps_an_optional_genmap_value_nullable()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    Prim(DamlPrimitive.Text),
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64))),
                "value")
            .Should().Be(
                "(IReadOnlyDictionary<string, long?>)value.As<DamlGenMap>().Entries.ToDictionary("
                + "kv => kv.Key.As<DamlText>().Value, "
                + "kv => kv.Value.AsOptional().HasValue ? kv.Value.AsOptional().Value!.As<DamlInt64>().Value : null)");
    }

    [Fact]
    public void DamlTypeMapper_handles_the_nested_chain_encoding_in_all_three_methods()
    {
        var mapper = Mapper();
        var chained = new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain);

        mapper.MapType(chained).Should().Be("Optional<string>");
        mapper.ToValue(chained, "Note").Should().Contain("ToChainValue(");
        mapper.FromValue(chained, "value").Should().Contain("FromChainValue(");
    }

    [Fact]
    public void ToValue_serializes_int64_primitive()
    {
        Mapper().ToValue(Prim(DamlPrimitive.Int64), "Amount")
            .Should().Be("new DamlInt64(Amount)");
    }

    [Fact]
    public void ToValue_serializes_list_container()
    {
        Mapper().ToValue(App(DamlPrimitive.List, Prim(DamlPrimitive.Text)), "Items")
            .Should().Be("new DamlList(Items.Select(x => (DamlValue)new DamlText(x)).ToList())");
    }

    [Fact]
    public void ToValue_serializes_optional_container()
    {
        Mapper().ToValue(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64)), "Maybe")
            .Should().Be("Maybe is { } __Maybe ? new DamlOptional(new DamlInt64(__Maybe)) : DamlOptional.None");
    }

    [Fact]
    public void ToValue_rejects_excessively_deep_types_before_managed_stack_overflow()
    {
        var type = Enumerable.Range(0, 300)
            .Aggregate((DamlType)Prim(DamlPrimitive.Text), (inner, _) => App(DamlPrimitive.List, inner));

        Mapper().Invoking(m => m.ToValue(type, "Items"))
            .Should().Throw<InvalidDataException>()
            .WithMessage("*depth*");
    }

    [Fact]
    public void ToValue_serializes_parametric_stdlib_type_through_the_stub()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var either = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Either"),
            [Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).ToValue(either, "Choice")
            .Should().Be("Choice.ToValue(__t0 => (DamlValue)(new DamlText(__t0)), __t1 => (DamlValue)(new DamlInt64(__t1)))");
    }

    [Fact]
    public void FromValue_deserializes_int64_primitive()
    {
        Mapper().FromValue(Prim(DamlPrimitive.Int64), "value")
            .Should().Be("value.As<DamlInt64>().Value");
    }

    [Fact]
    public void FromValue_deserializes_optional_container()
    {
        Mapper().FromValue(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)), "value")
            .Should().Be("value.AsOptional().HasValue ? value.AsOptional().Value!.As<DamlText>().Value : null");
    }

    [Fact]
    public void FromValue_deserializes_a_list_of_optionals()
    {
        Mapper().FromValue(App(DamlPrimitive.List, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "value")
            .Should().Be("(IReadOnlyList<string?>)value.As<DamlList>().Values.Select(x => x.AsOptional().HasValue ? x.AsOptional().Value!.As<DamlText>().Value : null).ToList()");
    }

    [Fact]
    public void FromValue_casts_a_nested_genmap_of_genmap_to_the_declared_ireadonlydictionary()
    {
        Mapper().FromValue(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Party), App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64))), "value")
            .Should().Be("(IReadOnlyDictionary<Party, IReadOnlyDictionary<string, long>>)value.As<DamlGenMap>().Entries.ToDictionary(kv => Party.FromDamlValue(kv.Key.As<DamlParty>()), kv => (IReadOnlyDictionary<string, long>)kv.Value.As<DamlGenMap>().Entries.ToDictionary(kv => kv.Key.As<DamlText>().Value, kv => kv.Value.As<DamlInt64>().Value))");
    }

    [Fact]
    public void FromValue_casts_a_nested_textmap_of_textmap_to_the_declared_ireadonlydictionary()
    {
        Mapper().FromValue(App(DamlPrimitive.TextMap, App(DamlPrimitive.TextMap, Prim(DamlPrimitive.Int64))), "value")
            .Should().Be("(IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>>)value.As<DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, long>)kv.Value.As<DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => kv.Value.As<DamlInt64>().Value))");
    }

    [Fact]
    public void FromValue_rejects_excessively_deep_types_before_managed_stack_overflow()
    {
        var type = Enumerable.Range(0, 300)
            .Aggregate((DamlType)Prim(DamlPrimitive.Text), (inner, _) => App(DamlPrimitive.List, inner));

        Mapper().Invoking(m => m.FromValue(type, "value"))
            .Should().Throw<InvalidDataException>()
            .WithMessage("*depth*");
    }

    [Fact]
    public void FromValue_deserializes_parametric_stdlib_type_through_the_stub()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var either = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Either"),
            [Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).FromValue(either, "value")
            .Should().Be("Either<string, long>.FromValue(value, __v0 => __v0.As<DamlText>().Value, __v1 => __v1.As<DamlInt64>().Value)");
    }

    [Fact]
    public void ToValue_serializes_set_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var set = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Set.Types", "Set"),
            [Prim(DamlPrimitive.Text)]);

        Mapper(resolver).ToValue(set, "Members")
            .Should().Be("Members.ToRecord(__t0 => (DamlValue)(new DamlText(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_set_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var set = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Set.Types", "Set"),
            [Prim(DamlPrimitive.Text)]);

        Mapper(resolver).FromValue(set, "value")
            .Should().Be("Set<string>.FromRecord(value.As<DamlRecord>(), __v0 => __v0.As<DamlText>().Value)");
    }

    [Fact]
    public void ToValue_serializes_nonempty_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var nonEmpty = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.NonEmpty.Types", "NonEmpty"),
            [Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).ToValue(nonEmpty, "Items")
            .Should().Be("Items.ToRecord(__t0 => (DamlValue)(new DamlInt64(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_nonempty_through_the_conversion_table()
    {
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });
        var nonEmpty = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.NonEmpty.Types", "NonEmpty"),
            [Prim(DamlPrimitive.Int64)]);

        Mapper(resolver).FromValue(nonEmpty, "value")
            .Should().Be("NonEmpty<long>.FromRecord(value.As<DamlRecord>(), __v0 => __v0.As<DamlInt64>().Value)");
    }

    [Fact]
    public void FromValue_handles_type_var_with_a_runtime_stub()
    {
        Mapper().FromValue(new DamlTypeVar("a"), "value")
            .Should().Be("GenericStub.NotImplemented<TA>(\"a\")");
    }

    private static DamlPackage PackageWithGenericType(string module, string name, DamlDataTypeDefinition definition) =>
        new()
        {
            PackageId = CrossPackageId,
            Name = "acme",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = module,
                    Templates = [],
                    Interfaces = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = name,
                            TypeParams = ["a"],
                            Definition = definition,
                        }
                    ],
                }
            ],
            DependencyReferences = [],
        };

    private static StubResolver ResolverWith(string resolvedName, DamlPackage package) =>
        new(resolvedName, new Dictionary<string, DamlPackage> { [CrossPackageId] = package });

    private static StubResolver CrateResolver() =>
        ResolverWith(
            "Acme.Crate",
            PackageWithGenericType(
                "Acme.Shapes",
                "Crate",
                new DamlRecordDefinition(
                    [new DamlFieldDefinition("item", App(DamlPrimitive.Optional, new DamlTypeVar("a")))])));

    private static StubResolver BoxResolver() =>
        ResolverWith(
            "Acme.Box",
            PackageWithGenericType(
                "Acme.Shapes",
                "Box",
                new DamlRecordDefinition([new DamlFieldDefinition("item", new DamlTypeVar("a"))])));

    private static DamlTypeApp GenericAppOfText(string module, string name) =>
        new(new DamlTypeRef(CrossPackageId, module, name), [Prim(DamlPrimitive.Text)]);

    [Fact]
    public void ToValue_serializes_a_user_generic_record_through_converter_lambdas()
    {
        var record = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]);
        var resolver = ResolverWith("Acme.Box", PackageWithGenericType("Acme.Boxes", "Box", record));

        Mapper(resolver).ToValue(GenericAppOfText("Acme.Boxes", "Box"), "Payload")
            .Should().Be("Payload.ToRecord(__t0 => (DamlValue)(new DamlText(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_a_user_generic_record_through_converter_lambdas()
    {
        var record = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]);
        var resolver = ResolverWith("Acme.Box", PackageWithGenericType("Acme.Boxes", "Box", record));

        Mapper(resolver).FromValue(GenericAppOfText("Acme.Boxes", "Box"), "value")
            .Should().Be("Acme.Box<string>.FromRecord(value.As<DamlRecord>(), __v0 => __v0.As<DamlText>().Value)");
    }

    [Fact]
    public void ToValue_serializes_a_user_generic_variant_through_converter_lambdas()
    {
        var variant = new DamlVariantDefinition([new DamlVariantConstructor("Wrap", new DamlTypeVar("a"))]);
        var resolver = ResolverWith("Acme.Choice", PackageWithGenericType("Acme.Choices", "Choice", variant));

        Mapper(resolver).ToValue(GenericAppOfText("Acme.Choices", "Choice"), "Payload")
            .Should().Be("Payload.ToVariant(__t0 => (DamlValue)(new DamlText(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_a_user_generic_variant_through_converter_lambdas()
    {
        var variant = new DamlVariantDefinition([new DamlVariantConstructor("Wrap", new DamlTypeVar("a"))]);
        var resolver = ResolverWith("Acme.Choice", PackageWithGenericType("Acme.Choices", "Choice", variant));

        Mapper(resolver).FromValue(GenericAppOfText("Acme.Choices", "Choice"), "value")
            .Should().Be("Acme.Choice<string>.FromVariant(value.As<DamlVariant>(), __v0 => __v0.As<DamlText>().Value)");
    }

    [Fact]
    public void ToValue_maps_a_type_var_field_to_its_injected_converter_delegate()
    {
        var delegates = new Dictionary<string, string> { ["a"] = "convertTA" };

        Mapper().ToValue(new DamlTypeVar("a"), "Value", delegates)
            .Should().Be("convertTA(Value)");
    }

    [Fact]
    public void FromValue_maps_a_type_var_field_to_its_injected_converter_delegate()
    {
        var delegates = new Dictionary<string, string> { ["a"] = "convertTA" };

        Mapper().FromValue(new DamlTypeVar("a"), "value", delegates)
            .Should().Be("convertTA(value)");
    }

    [Fact]
    public void FromValue_falls_back_to_the_stub_for_a_type_var_absent_from_the_delegate_map()
    {
        var delegates = new Dictionary<string, string> { ["b"] = "convertTB" };

        Mapper().FromValue(new DamlTypeVar("a"), "value", delegates)
            .Should().Be("GenericStub.NotImplemented<TA>(\"a\")");
    }

    [Fact]
    public void FromValue_emits_the_throwing_stub_for_a_higher_kinded_object_mapped_type()
    {
        var higherKinded = new DamlTypeApp(new DamlTypeVar("f"), [new DamlTypeVar("a")]);

        Mapper().FromValue(higherKinded, "value")
            .Should().Be("GenericStub.NotImplemented<object>(\"value\")");
    }

    [Fact]
    public void FromValue_throws_codegen_exception_for_an_unclassifiable_type_ref_application()
    {
        var unresolvable = new DamlTypeApp(
            new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget"),
            [Prim(DamlPrimitive.Text)]);

        Mapper().Invoking(m => m.FromValue(unresolvable, "value"))
            .Should().Throw<CodegenException>()
            .WithMessage("*Widget*");
    }

    private static readonly IReadOnlyDictionary<Type, IReadOnlyList<DamlType>> SubtypeRepresentatives =
        new Dictionary<Type, IReadOnlyList<DamlType>>
        {
            [typeof(DamlPrimitiveType)] = [Prim(DamlPrimitive.Text)],
            [typeof(DamlTypeApp)] = [App(DamlPrimitive.List, Prim(DamlPrimitive.Int64))],
            [typeof(DamlTypeRef)] = [new DamlTypeRef(LocalPackageId, "Test.Module", "Widget")],
            [typeof(DamlTypeVar)] = [new DamlTypeVar("a")],
            [typeof(DamlWrappedOptional)] =
            [
                .. Enum.GetValues<OptionalEncoding>()
                    .Select(encoding => new DamlWrappedOptional(Prim(DamlPrimitive.Text), encoding)),
            ]
        };

    private static IEnumerable<Type> ConcreteDamlTypeSubtypes() =>
        typeof(DamlType).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(DamlType).IsAssignableFrom(t));

    public static IEnumerable<object[]> EveryDamlTypeSubtype() =>
        SubtypeRepresentatives.Values.SelectMany(types => types).Select(type => new object[] { type });

    [Fact]
    public void DamlTypeMapper_every_parametric_stdlib_type_has_a_conversion_table_entry()
    {
        Mapper().StdlibConversionKeys.Should().BeEquivalentTo(StdlibPackages.ParametricStdlibTypes);
    }

    [Fact]
    public void DamlTypeMapper_every_parametric_stdlib_type_has_a_stdlib_mapping()
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
    public void DamlTypeMapper_every_stdlib_mapping_return_theory_has_cases()
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
    public void DamlTypeMapper_every_stdlib_mapping_return_resolves_to_a_public_runtime_type(string? returnedTypeName)
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
    public void DamlTypeMapper_drift_guard_covers_every_concrete_subtype_discovered_by_reflection()
    {
        ConcreteDamlTypeSubtypes()
            .Should().BeEquivalentTo(SubtypeRepresentatives.Keys,
                "every concrete DamlType subtype needs a representative so the mapper drift-guard exercises it");
    }

    [Fact]
    public void DamlTypeMapper_drift_guard_theory_exercises_every_optional_encoding()
    {
        WrappedOptionalTheoryCases().Select(wrapped => wrapped.Encoding)
            .Should().BeEquivalentTo(
                Enum.GetValues<OptionalEncoding>(),
                "the encoding axis is invisible to the reflection drift guard, which walks DamlType "
                + "subtypes only, so an encoding the theory omits reaches no mapper method at all");
    }

    [Fact]
    public void DamlTypeMapper_emits_a_distinct_converter_pair_for_every_optional_encoding()
    {
        var wrappers = WrappedOptionalTheoryCases().ToList();
        wrappers.Should().HaveCountGreaterThan(1,
            "one representative would make the uniqueness assertions below hold for free");

        var mapper = Mapper();
        wrappers.Select(wrapped => mapper.ToValue(wrapped, "Field"))
            .Should().OnlyHaveUniqueItems(
                "two encodings sharing a serializer means one of them is being written in the other's "
                + "wire form, which compiles and is only rejected by the participant");
        wrappers.Select(wrapped => mapper.FromValue(wrapped, "value"))
            .Should().OnlyHaveUniqueItems(
                "two encodings sharing a deserializer means one of them is being read in the other's "
                + "wire form, which compiles and silently yields a different Optional");
    }

    private static IEnumerable<DamlWrappedOptional> WrappedOptionalTheoryCases() =>
        EveryDamlTypeSubtype().Select(row => row[0]).OfType<DamlWrappedOptional>();

    [Theory]
    [MemberData(nameof(EveryDamlTypeSubtype))]
    public void DamlTypeMapper_every_subtype_is_handled_by_all_three_methods_without_throwing(DamlType type)
    {
        var mapper = Mapper();

        mapper.Invoking(m => m.MapType(type)).Should().NotThrow<NotSupportedException>();
        mapper.Invoking(m => m.ToValue(type, "field")).Should().NotThrow<NotSupportedException>();
        mapper.Invoking(m => m.FromValue(type, "value")).Should().NotThrow<NotSupportedException>();
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_an_optional_over_a_type_variable()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, new DamlTypeVar("a")))
            .Should().Be("Optional<TA>");
    }

    [Fact]
    public void MapsToReferenceType_places_an_optional_the_wrapper_carries_as_a_reference_type()
    {
        var mapper = Mapper();
        var nested = App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)));

        mapper.MapType(nested).Should().Be("Optional<Optional<string>>");
        mapper.MapsToReferenceType(nested).Should().BeTrue();
    }

    [Fact]
    public void MapsToReferenceType_places_an_optional_nullable_syntax_carries_as_a_non_reference_type()
    {
        var mapper = Mapper();
        var flat = App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text));

        mapper.MapType(flat).Should().Be("string?");
        mapper.MapsToReferenceType(flat).Should().BeFalse();
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_an_optional_argument_to_an_emitted_generic()
    {
        var mapper = Mapper(new StubResolver(resolvedName: "Acme.Box"));

        mapper.MapType(new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]))
            .Should().Be("Acme.Box<Optional<string>>");
    }

    [Fact]
    public void MapType_keeps_a_flat_optional_on_nullable_syntax()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))
            .Should().Be("string?");
    }

    [Fact]
    public void ToValue_serializes_a_wrapped_optional_through_the_runtime_wrapper()
    {
        Mapper().ToValue(App(DamlPrimitive.Optional, new DamlTypeVar("a")), "Note")
            .Should().Be("Note.ToValue(__optional0 => GenericStub.NotImplemented<DamlValue>(\"__optional0\"))");
    }

    [Fact]
    public void FromValue_deserializes_a_wrapped_optional_through_the_runtime_wrapper()
    {
        Mapper(BoxResolver()).FromValue(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]),
                "value")
            .Should().Contain("Optional<string>.FromValue(")
            .And.Contain("__optional");
    }

    [Fact]
    public void MapType_ToValue_and_FromValue_agree_on_which_optionals_are_wrapped()
    {
        var damlType = new DamlTypeApp(
            new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
            [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]);
        var mapper = Mapper(BoxResolver());

        mapper.MapType(damlType).Should().Contain("Optional<string>");
        mapper.ToValue(damlType, "Boxed").Should().Contain("ToValue(");
        mapper.FromValue(damlType, "value").Should().Contain("Optional<string>.FromValue(");
    }

    [Fact]
    public void DamlTypeMapper_emits_the_wrapper_rather_than_the_object_fallback_for_a_wrapped_optional_node()
    {
        var wrapped = new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat);
        var mapper = Mapper();

        mapper.MapType(wrapped).Should().Be("Optional<string>");
        mapper.ToValue(wrapped, "Note").Should().Be("Note.ToValue(__optional0 => new DamlText(__optional0))");
        mapper.FromValue(wrapped, "value")
            .Should().Be("Optional<string>.FromValue(value, __optional0 => __optional0.As<DamlText>().Value)");
    }
}
