// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ContractTypesTests
{
    // Shared test package metadata
    private const string TestPackageId = "test-package-id";
    private const string TestPackageName = "test-package-name";
    private const string TestModuleName = "Test.Module";
    private static readonly Version TestPackageV1 = new(1, 0, 0);

    // Test template for Contract testing
    private sealed record TestTemplate(Party Owner, long Amount) : ITemplate
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(TestTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlInt64(Amount)));

        public static TestTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlInt64>().Value);
    }

    // Hand-rolled stand-in for a codegen-emitted Daml interface placeholder. Daml-LF
    // emits one of these for every `interface I where ...` declaration; the codegen
    // surfaces them as `: ITemplate` with throwing static metadata so that
    // `ContractId<I>` satisfies the runtime's `where T : ITemplate` constraint while
    // refusing to pretend an interface has template identity. The shape mirrors what
    // CSharpCodeGenerator.WriteInterfacePlaceholderRecord produces.
    private const string PlaceholderThrowMessage =
        "'TestInterfacePlaceholder' is the C# placeholder for the Daml interface "
        + "'Test.Module:TestInterfacePlaceholder' and carries no template metadata. "
        + "Coerce ContractId<TestInterfacePlaceholder> to a typed ContractId<TConcrete> "
        + "before reading template metadata or exercising commands.";

    private sealed record TestInterfacePlaceholder : ITemplate
    {
        public static Identifier TemplateId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static string PackageId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static string PackageName =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static Version PackageVersion =>
            throw new InvalidOperationException(PlaceholderThrowMessage);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static TestInterfacePlaceholder FromRecord(DamlRecord record) => new();
    }

    #region ContractId<T> Tests

    [Fact]
    public void ContractId_should_store_value()
    {
        // Arrange & Act
        var contractId = new ContractId<TestTemplate>("contract-123");

        // Assert
        contractId.Value.Should().Be("contract-123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ContractId_construction_rejects_null_empty_or_whitespace(string? value)
    {
        var act = () => new ContractId<TestTemplate>(value!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ContractId_should_convert_to_string_explicitly()
    {
        // Arrange
        var contractId = new ContractId<TestTemplate>("contract-123");

        // Act
        var stringValue = (string)contractId;

        // Assert
        stringValue.Should().Be("contract-123");
    }

    [Fact]
    public void ContractId_should_convert_from_string_explicitly()
    {
        // Arrange & Act
        var contractId = (ContractId<TestTemplate>)"contract-456";

        // Assert
        contractId.Value.Should().Be("contract-456");
    }

    [Fact]
    public void ContractId_ToString_should_return_value()
    {
        // Arrange
        var contractId = new ContractId<TestTemplate>("contract-789");

        // Act
        var result = contractId.ToString();

        // Assert
        result.Should().Be("contract-789");
    }

    [Fact]
    public void ContractId_ToDamlValue_should_create_DamlContractId()
    {
        // Arrange
        var contractId = new ContractId<TestTemplate>("contract-abc");

        // Act
        var damlValue = contractId.ToDamlValue();

        // Assert
        damlValue.Value.Should().Be("contract-abc");
        damlValue.TemplateId.Should().Be(TestTemplate.TemplateId);
    }

    [Fact]
    public void ContractId_should_support_equality()
    {
        // Arrange
        var id1 = new ContractId<TestTemplate>("contract-123");
        var id2 = new ContractId<TestTemplate>("contract-123");
        var id3 = new ContractId<TestTemplate>("contract-456");

        // Assert
        id1.Should().Be(id2);
        id1.Should().NotBe(id3);
    }

    [Fact]
    public void ContractId_of_different_template_types_are_not_equal()
    {
        var sameTemplate1 = new ContractId<TestTemplate>("contract-x");
        var sameTemplate2 = new ContractId<TestTemplate>("contract-x");
        var differentTemplate = new ContractId<MyHolding>("contract-x");

        sameTemplate1.Should().Be(sameTemplate2);
        ((ContractId)sameTemplate1).Should().NotBe((ContractId)differentTemplate);
        sameTemplate1.Value.Should().Be(differentTemplate.Value);
    }

    #endregion

    #region Interface Placeholder Tests

    // These tests pin down the contract for codegen-emitted Daml interface placeholders.
    // The shape of TestInterfacePlaceholder above mirrors what CSharpCodeGenerator emits
    // for a record whose name matches an interface in the same module — see
    // WriteInterfacePlaceholderRecord. The expectation is: it satisfies ITemplate well
    // enough to flow through ContractId<T>, but every metadata accessor throws.

    [Fact]
    public void InterfacePlaceholder_can_satisfy_ContractId_T_constraint()
    {
        // Compile-time check: the line below would not compile if `where T : ITemplate`
        // were not satisfied by the placeholder. Construction itself must not throw —
        // a placeholder ContractId is exactly what splice-api-token-allocation-v1's
        // `Reference.Cid` field carries, and Sample will receive it pre-coercion.
        var cid = new ContractId<TestInterfacePlaceholder>("placeholder-cid-123");

        cid.Value.Should().Be("placeholder-cid-123");
        cid.ToString().Should().Be("placeholder-cid-123");
    }

    [Fact]
    public void InterfacePlaceholder_TemplateId_access_throws_with_explanatory_message()
    {
        // Calling T.TemplateId on a placeholder is always a logic error: the right path
        // is to coerce the contract id to the underlying template type first. Throw at
        // the access site, not at silent-null fallback.
        var act = () => TestInterfacePlaceholder.TemplateId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*placeholder for the Daml interface*Coerce ContractId*");
    }

    [Fact]
    public void InterfacePlaceholder_PackageId_access_throws()
    {
        var act = () => TestInterfacePlaceholder.PackageId;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InterfacePlaceholder_PackageName_access_throws()
    {
        var act = () => TestInterfacePlaceholder.PackageName;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InterfacePlaceholder_PackageVersion_access_throws()
    {
        var act = () => TestInterfacePlaceholder.PackageVersion;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InterfacePlaceholder_ContractId_ToDamlValue_propagates_throw_through_T_TemplateId()
    {
        // ContractId<T>.ToDamlValue() reads T.TemplateId statically — for a placeholder
        // it throws, which is the *correct* failure mode: nobody should serialize a
        // placeholder-typed contract id; coerce first. The runtime exception with a
        // pointer to the right path is more useful than a silent null TemplateId.
        var cid = new ContractId<TestInterfacePlaceholder>("c-1");
        var act = () => cid.ToDamlValue();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Coerce ContractId*");
    }

    [Fact]
    public void InterfacePlaceholder_round_trips_empty_record_without_throwing()
    {
        var placeholder = new TestInterfacePlaceholder();
        var record = placeholder.ToRecord();

        record.Should().NotBeNull();
        record.Fields.Should().BeEmpty();

        var recovered = TestInterfacePlaceholder.FromRecord(record);
        recovered.Should().Be(placeholder);
    }

    [Fact]
    public void InterfacePlaceholder_DamlContractId_ToTyped_compiles_and_constructs()
    {
        // DamlContractId.ToTyped<T>() also has `where T : ITemplate`. The placeholder
        // makes that callable for interface-typed contract ids — again, the typical
        // path is `damlContractId.ToTyped<Reference>()` for some real template; the
        // placeholder case exists for parity and round-trip correctness.
        var damlCid = new DamlContractId("placeholder-cid-456");

        var typed = damlCid.ToTyped<TestInterfacePlaceholder>();

        typed.Value.Should().Be("placeholder-cid-456");
    }

    #endregion

    #region IDamlType / IDamlInterface marker tests

    // Hand-rolled stand-in for a codegen-emitted Daml interface marker. A `interface I`
    // declaration in Daml is now reified as a C# interface with `: IDamlInterface` and
    // a static InterfaceId — exactly the shape CSharpCodeGenerator.GenerateInterface
    // produces. ContractId<IHolding> et al. ride through ContractId<T>'s relaxed
    // `where T : IDamlType` constraint. Concrete templates implement this marker so
    // dispatch from a typed transaction projection can find them.
    private interface ITestInterfaceMarker : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "ITestInterfaceMarker");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => TestPackageV1;
    }

    [Fact]
    public void IDamlType_marker_is_implemented_by_ITemplate()
    {
        // ITemplate : IDamlType means every existing template, by virtue of
        // implementing ITemplate, also satisfies the new shared `where T : IDamlType`
        // constraint with no codegen change.
        typeof(IDamlType).IsAssignableFrom(typeof(ITemplate)).Should().BeTrue();
        typeof(IDamlType).IsAssignableFrom(typeof(TestTemplate)).Should().BeTrue();
    }

    [Fact]
    public void IDamlType_marker_is_implemented_by_IDamlInterface()
    {
        // IDamlInterface : IDamlType means codegen-emitted interface markers slot
        // directly into ContractId<T> and the typed transaction helpers without a
        // separate code path.
        typeof(IDamlType).IsAssignableFrom(typeof(IDamlInterface)).Should().BeTrue();
        typeof(IDamlType).IsAssignableFrom(typeof(ITestInterfaceMarker)).Should().BeTrue();
    }

    [Fact]
    public void ContractId_T_accepts_an_interface_marker_as_T()
    {
        // The constraint relaxation from `where T : ITemplate` to `where T : IDamlType`
        // is the entire point of this change — a Daml interface marker (no template id,
        // no payload) must compile inside ContractId<>.
        var cid = new ContractId<ITestInterfaceMarker>("interface-cid-1");

        cid.Value.Should().Be("interface-cid-1");
    }

    [Fact]
    public void ContractId_ToDamlValue_uses_InterfaceId_for_interface_markers()
    {
        // For interface-typed contract ids the embedded identifier is the interface
        // id, not a template id — that is what flows back when this contract id is
        // serialised into a choice argument expecting `ContractId I` (e.g.
        // `cid.exercise (toInterfaceContractId @IHolding) Transfer`).
        var cid = new ContractId<ITestInterfaceMarker>("interface-cid-2");

        var damlCid = cid.ToDamlValue();

        damlCid.Value.Should().Be("interface-cid-2");
        damlCid.TemplateId.Should().Be(new Identifier(TestPackageId, TestModuleName, "ITestInterfaceMarker"));
    }

    [Fact]
    public void ContractId_ToDamlValue_uses_TemplateId_for_concrete_templates()
    {
        // Pin the template branch — same instance method, dispatched on the closed
        // generic. Regression coverage for the reflection-based id resolver.
        var cid = new ContractId<TestTemplate>("template-cid-3");

        var damlCid = cid.ToDamlValue();

        damlCid.Value.Should().Be("template-cid-3");
        damlCid.TemplateId.Should().Be(TestTemplate.TemplateId);
    }

    [Fact]
    public void DamlContractId_ToTyped_compiles_for_interface_marker()
    {
        // The relaxed `where T : IDamlType` on DamlContractId.ToTyped<T> means
        // round-tripping a wire-level contract id into a typed interface contract id
        // is now possible without a placeholder hop.
        var damlCid = new DamlContractId("interface-cid-4");

        var typed = damlCid.ToTyped<ITestInterfaceMarker>();

        typed.Value.Should().Be("interface-cid-4");
    }

    // Concrete template that explicitly implements ITestInterfaceMarker via the
    // codegen-emitted IImplements<I> witness. Mirrors what the codegen produces
    // for `template MyHolding implements IHolding`.
    private sealed record MyHolding(Party Owner) : ITemplate, IImplements<ITestInterfaceMarker>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(MyHolding));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));
    }

    [Fact]
    public void ToInterfaceContractId_preserves_value_and_re_types_to_interface_marker()
    {
        // Daml's `toInterfaceContractId @I cid` is a no-op at the wire level — the
        // contract id string does not change. The C# helper mirrors that: same
        // value, different static type witness so subsequent calls see
        // `ContractId<IHolding>` for downcast/dispatch purposes.
        var concreteCid = new ContractId<MyHolding>("template-cid-coerce");

        var interfaceCid = concreteCid.ToInterfaceContractId<MyHolding, ITestInterfaceMarker>();

        interfaceCid.Value.Should().Be("template-cid-coerce");
        interfaceCid.Should().BeOfType<ContractId<ITestInterfaceMarker>>();
    }

    [Fact]
    public void ExerciseCommand_ForInterface_uses_InterfaceId_in_template_id_slot()
    {
        // Per Canton commands.proto: "To exercise a choice on an interface, specify
        // the interface identifier in the template_id field." The runtime helper
        // honours that — `ExerciseCommand.ForInterface<I>(cid, choice, arg)` should
        // place the InterfaceId in TemplateId, not any concrete template id.
        var cid = new ContractId<ITestInterfaceMarker>("interface-cid-exec");
        var arg = new TestTemplate(new Party("alice"), 42L);

        var cmd = Daml.Runtime.Commands.ExerciseCommand.ForInterface<ITestInterfaceMarker>(
            cid, new Daml.Runtime.Commands.ChoiceName("Transfer"), arg.ToRecord());

        cmd.TemplateId.Should().Be(new Identifier(TestPackageId, TestModuleName, "ITestInterfaceMarker"));
        cmd.ContractId.Value.Should().Be("interface-cid-exec");
        cmd.Choice.Should().Be(new Daml.Runtime.Commands.ChoiceName("Transfer"));
    }

    #endregion

    #region DamlContractId Tests

    [Fact]
    public void DamlContractId_should_store_value_and_template_id()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");

        // Act
        var contractId = new DamlContractId("contract-id", templateId);

        // Assert
        contractId.Value.Should().Be("contract-id");
        contractId.TemplateId.Should().Be(templateId);
    }

    [Fact]
    public void DamlContractId_should_allow_null_template_id()
    {
        // Arrange & Act
        var contractId = new DamlContractId("contract-id");

        // Assert
        contractId.Value.Should().Be("contract-id");
        contractId.TemplateId.Should().BeNull();
    }

    [Fact]
    public void DamlContractId_ToTyped_should_create_typed_contract_id()
    {
        // Arrange
        var damlContractId = new DamlContractId("contract-xyz");

        // Act
        var typedId = damlContractId.ToTyped<TestTemplate>();

        // Assert
        typedId.Value.Should().Be("contract-xyz");
    }

    [Fact]
    public void DamlContractId_ToString_should_return_value()
    {
        // Arrange
        var contractId = new DamlContractId("contract-abc", new Identifier("pkg", "M", "T"));

        // Act
        var result = contractId.ToString();

        // Assert
        result.Should().Be("contract-abc");
    }

    #endregion

    #region Contract<T> Tests

    [Fact]
    public void Contract_should_store_id_and_data()
    {
        // Arrange
        var id = new ContractId<TestTemplate>("contract-1");
        var data = new TestTemplate(new Party("Alice"), 100);

        // Act
        var contract = new Contract<TestTemplate>(id, data);

        // Assert
        contract.Id.Should().Be(id);
        contract.Data.Should().Be(data);
    }

    [Fact]
    public void Contract_FromCreatedEvent_should_decode_contract()
    {
        // Arrange
        var templateId = TestTemplate.TemplateId;
        var createArgs = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("Bob")),
            DamlField.Create("amount", new DamlInt64(200)));

        var createdEvent = new CreatedEvent(
            EventId: "event-1",
            ContractId: "contract-from-event",
            TemplateId: templateId,
            CreateArguments: createArgs,
            WitnessParties: [new Party("Bob")],
            Signatories: [new Party("Bob")],
            Observers: []);

        // Act
        var contract = Contract<TestTemplate>.FromCreatedEvent(createdEvent, TestTemplate.FromRecord);

        // Assert
        contract.Id.Value.Should().Be("contract-from-event");
        contract.Data.Owner.Should().Be(new Party("Bob"));
        contract.Data.Amount.Should().Be(200);
    }

    [Fact]
    public void Contract_should_support_equality()
    {
        // Arrange
        var id = new ContractId<TestTemplate>("contract-1");
        var data = new TestTemplate(new Party("Alice"), 100);
        var contract1 = new Contract<TestTemplate>(id, data);
        var contract2 = new Contract<TestTemplate>(id, data);

        // Assert
        contract1.Should().Be(contract2);
    }

    #endregion

    #region CreatedEvent Tests

    [Fact]
    public void CreatedEvent_should_store_all_properties()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var createArgs = DamlRecord.Create();
        var witnesses = new List<Party> { new("Alice"), new("Bob") };
        var signatories = new List<Party> { new("Alice") };
        var observers = new List<Party> { new("Charlie") };
        var contractKey = new ContractKey(new DamlText("key-value"), templateId);
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var @event = new CreatedEvent(
            EventId: "event-123",
            ContractId: "contract-456",
            TemplateId: templateId,
            CreateArguments: createArgs,
            WitnessParties: witnesses,
            Signatories: signatories,
            Observers: observers,
            ContractKey: contractKey,
            CreatedAt: createdAt);

        // Assert
        @event.EventId.Should().Be("event-123");
        @event.ContractId.Should().Be("contract-456");
        @event.TemplateId.Should().Be(templateId);
        @event.CreateArguments.Should().Be(createArgs);
        @event.WitnessParties.Should().BeEquivalentTo(witnesses);
        @event.Signatories.Should().BeEquivalentTo(signatories);
        @event.Observers.Should().BeEquivalentTo(observers);
        @event.ContractKey.Should().Be(contractKey);
        @event.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void CreatedEvent_should_allow_optional_properties_null()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var createArgs = DamlRecord.Create();

        // Act
        var @event = new CreatedEvent(
            EventId: "event-1",
            ContractId: "contract-1",
            TemplateId: templateId,
            CreateArguments: createArgs,
            WitnessParties: [],
            Signatories: [],
            Observers: []);

        // Assert
        @event.ContractKey.Should().BeNull();
        @event.CreatedAt.Should().BeNull();
    }

    #endregion

    #region ArchivedEvent Tests

    [Fact]
    public void ArchivedEvent_should_store_all_properties()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var witnesses = new List<Party> { new("Alice"), new("Bob") };

        // Act
        var @event = new ArchivedEvent(
            EventId: "archive-event-1",
            ContractId: "contract-to-archive",
            TemplateId: templateId,
            WitnessParties: witnesses);

        // Assert
        @event.EventId.Should().Be("archive-event-1");
        @event.ContractId.Should().Be("contract-to-archive");
        @event.TemplateId.Should().Be(templateId);
        @event.WitnessParties.Should().BeEquivalentTo(witnesses);
    }

    [Fact]
    public void ArchivedEvent_should_support_equality()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var witnesses = new List<Party> { new("Alice") };
        var event1 = new ArchivedEvent("e1", "c1", templateId, witnesses);
        var event2 = new ArchivedEvent("e1", "c1", templateId, witnesses);

        // Assert - records with same reference for collections are equal
        event1.Should().Be(event2);
    }

    #endregion

    #region ContractKey Tests

    [Fact]
    public void ContractKey_should_store_value_and_template_id()
    {
        // Arrange
        var keyValue = new DamlText("my-key");
        var templateId = new Identifier("pkg", "Module", "Template");

        // Act
        var contractKey = new ContractKey(keyValue, templateId);

        // Assert
        contractKey.Value.Should().Be(keyValue);
        contractKey.TemplateId.Should().Be(templateId);
    }

    [Fact]
    public void ContractKey_should_allow_null_template_id()
    {
        // Arrange
        var keyValue = new DamlInt64(42);

        // Act
        var contractKey = new ContractKey(keyValue);

        // Assert
        contractKey.Value.Should().Be(keyValue);
        contractKey.TemplateId.Should().BeNull();
    }

    [Fact]
    public void ContractKey_should_support_complex_key_values()
    {
        // Arrange
        var complexKey = DamlRecord.Create(
            DamlField.Create("party", new DamlParty("Alice")),
            DamlField.Create("id", new DamlInt64(123)));
        var templateId = new Identifier("pkg", "Module", "KeyedTemplate");

        // Act
        var contractKey = new ContractKey(complexKey, templateId);

        // Assert
        contractKey.Value.Should().BeOfType<DamlRecord>();
        var record = contractKey.Value.As<DamlRecord>();
        record.GetField("party")!.As<DamlParty>().Value.Should().Be("Alice");
        record.GetField("id")!.As<DamlInt64>().Value.Should().Be(123);
    }

    #endregion

    #region ITemplate Interface Tests

    [Fact]
    public void ITemplate_static_properties_should_be_accessible()
    {
        // Assert - verify static abstract members work
        TestTemplate.TemplateId.PackageId.Should().Be(TestPackageId);
        TestTemplate.TemplateId.ModuleName.Should().Be(TestModuleName);
        TestTemplate.TemplateId.EntityName.Should().Be(nameof(TestTemplate));
        TestTemplate.PackageId.Should().Be(TestPackageId);
        TestTemplate.PackageName.Should().Be(TestPackageName);
        TestTemplate.PackageVersion.Should().Be(TestPackageV1);
    }

    [Fact]
    public void ITemplate_ToRecord_should_serialize_correctly()
    {
        // Arrange
        var template = new TestTemplate(new Party("Charlie"), 500);

        // Act
        var record = template.ToRecord();

        // Assert
        record.GetField("owner")!.As<DamlParty>().Value.Should().Be("Charlie");
        record.GetField("amount")!.As<DamlInt64>().Value.Should().Be(500);
    }

    [Fact]
    public void ITemplate_FromRecord_should_deserialize_correctly()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("Diana")),
            DamlField.Create("amount", new DamlInt64(750)));

        // Act
        var template = TestTemplate.FromRecord(record);

        // Assert
        template.Owner.Should().Be(new Party("Diana"));
        template.Amount.Should().Be(750);
    }

    #endregion

    #region IHasKey Interface Tests

    private sealed record KeyedTemplate(Party Owner, string AssetId) : ITemplate, IHasKey<string>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(KeyedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public string Key => AssetId;

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("assetId", new DamlText(AssetId)));

        public static KeyedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("assetId").As<DamlText>().Value);
    }

    [Fact]
    public void IHasKey_should_return_contract_key()
    {
        // Arrange
        var template = new KeyedTemplate(new Party("Alice"), "asset-123");

        // Act
        var key = template.Key;

        // Assert
        key.Should().Be("asset-123");
    }

    [Fact]
    public void IHasKey_should_be_covariant_with_key_type()
    {
        // Arrange
        IHasKey<string> keyedContract = new KeyedTemplate(new Party("Bob"), "asset-456");

        // Act
        var key = keyedContract.Key;

        // Assert
        key.Should().Be("asset-456");
    }

    #endregion

    #region IDamlInterface Tests

    private interface ITestInterface : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "TestInterface");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => new(2, 0, 0);
    }

    [Fact]
    public void IDamlInterface_should_provide_interface_metadata()
    {
        // Assert - verify static abstract members are accessible via generic constraint
        var interfaceId = GetInterfaceId<ITestInterface>();
        interfaceId.PackageId.Should().Be(TestPackageId);
        interfaceId.ModuleName.Should().Be(TestModuleName);
        interfaceId.EntityName.Should().Be("TestInterface");
    }

    private static Identifier GetInterfaceId<T>() where T : IDamlInterface
    {
        return T.InterfaceId;
    }

    #endregion

    #region IHasView Interface Tests

    private sealed record AssetView(Party Owner, decimal Amount);

    private sealed record ViewedTemplate(Party Owner, decimal Amount) : ITemplate, IHasView<AssetView>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(ViewedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public AssetView View => new(Owner, Amount);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlNumeric(Amount)));

        public static ViewedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }

    [Fact]
    public void IHasView_should_return_interface_view()
    {
        // Arrange
        var template = new ViewedTemplate(new Party("Charlie"), 1000.50m);

        // Act
        var view = template.View;

        // Assert
        view.Owner.Should().Be(new Party("Charlie"));
        view.Amount.Should().Be(1000.50m);
    }

    [Fact]
    public void IHasView_should_be_covariant_with_view_type()
    {
        // Arrange
        IHasView<AssetView> viewable = new ViewedTemplate(new Party("Diana"), 2500.00m);

        // Act
        var view = viewable.View;

        // Assert
        view.Owner.Should().Be(new Party("Diana"));
        view.Amount.Should().Be(2500.00m);
    }

    #endregion

    #region IUpgradeable Interface Tests

    private const string UpgradedPkgId = "upgraded-package";
    private const string UpgradedPackageName = "upgraded-package-name";

    private sealed record UpgradeableTemplate(string Value) : ITemplate, IUpgradeable
    {
        public static Identifier TemplateId => new(UpgradedPkgId, TestModuleName, nameof(UpgradeableTemplate));
        public static string PackageId => UpgradedPkgId;
        public static string PackageName => UpgradedPackageName;
        public static Version PackageVersion => new(2, 0, 0);
        public static string? UpgradedPackageId => "previous-package-id-12345";

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("value", new DamlText(Value)));

        public static UpgradeableTemplate FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("value").As<DamlText>().Value);
    }

    private sealed record NonUpgradeableTemplate(string Value) : ITemplate
    {
        public static Identifier TemplateId => new("new-package", TestModuleName, nameof(NonUpgradeableTemplate));
        public static string PackageId => "new-package";
        public static string PackageName => "new-package-name";
        public static Version PackageVersion => TestPackageV1;

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("value", new DamlText(Value)));

        public static NonUpgradeableTemplate FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("value").As<DamlText>().Value);
    }

    [Fact]
    public void IUpgradeable_should_provide_upgraded_package_id()
    {
        // Assert - verify static abstract member works
        UpgradeableTemplate.UpgradedPackageId.Should().Be("previous-package-id-12345");
    }

    [Fact]
    public void IUpgradeable_should_be_accessible_via_generic_constraint()
    {
        // Act
        var upgradedId = GetUpgradedPackageId<UpgradeableTemplate>();

        // Assert
        upgradedId.Should().Be("previous-package-id-12345");
    }

    private static string? GetUpgradedPackageId<T>() where T : IUpgradeable
    {
        return T.UpgradedPackageId;
    }

    [Fact]
    public void NonUpgradeable_template_should_not_implement_IUpgradeable()
    {
        // Assert
        typeof(NonUpgradeableTemplate).GetInterfaces()
            .Should().NotContain(typeof(IUpgradeable));
    }

    #endregion

    #region IImplements Interface Tests

    private interface ITransferable : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "Transferable");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => TestPackageV1;
    }

    private sealed record TransferableAsset(Party Owner, decimal Amount) : ITemplate, IImplements<ITransferable>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(TransferableAsset));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlNumeric(Amount)));

        public static TransferableAsset FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }

    [Fact]
    public void IImplements_should_mark_template_as_implementing_interface()
    {
        // Assert
        typeof(TransferableAsset).GetInterfaces()
            .Should().Contain(typeof(IImplements<ITransferable>));
    }

    [Fact]
    public void IImplements_template_should_be_assignable_to_IImplements()
    {
        // Arrange
        var asset = new TransferableAsset(new Party("Eve"), 5000m);

        // Assert
        asset.Should().BeAssignableTo<IImplements<ITransferable>>();
    }

    #endregion

    #region TemplateExtensions Tests

    [Fact]
    public void GetTemplateId_should_return_package_name_format_by_default()
    {
        // Act
        var templateId = TemplateExtensions.GetTemplateId<TestTemplate>();

        // Assert - default uses PackageName, not PackageId (hash)
        templateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_with_usePackageHash_should_return_hash_format()
    {
        // Act
        var templateId = TemplateExtensions.GetTemplateId<TestTemplate>(usePackageHash: true);

        // Assert - uses PackageId (hash) from Identifier
        templateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_method_should_return_package_name_format_by_default()
    {
        // Arrange
        var template = new TestTemplate(new Party("Alice"), 100);

        // Act
        var templateId = template.GetTemplateId();

        // Assert
        templateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_method_with_usePackageHash_should_return_hash_format()
    {
        // Arrange
        var template = new TestTemplate(new Party("Alice"), 100);

        // Act
        var templateId = template.GetTemplateId(usePackageHash: true);

        // Assert
        templateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_should_work_with_different_templates()
    {
        // Act
        var keyedTemplateId = TemplateExtensions.GetTemplateId<KeyedTemplate>();
        var viewedTemplateId = TemplateExtensions.GetTemplateId<ViewedTemplate>();
        var upgradeableTemplateId = TemplateExtensions.GetTemplateId<UpgradeableTemplate>();

        // Assert - all use PackageName format by default
        keyedTemplateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(KeyedTemplate)}");
        viewedTemplateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(ViewedTemplate)}");
        upgradeableTemplateId.Should().Be($"{UpgradedPackageName}:{TestModuleName}:{nameof(UpgradeableTemplate)}");
    }

    [Fact]
    public void GetTemplateId_with_usePackageHash_should_work_with_different_templates()
    {
        // Act
        var keyedTemplateId = TemplateExtensions.GetTemplateId<KeyedTemplate>(usePackageHash: true);
        var viewedTemplateId = TemplateExtensions.GetTemplateId<ViewedTemplate>(usePackageHash: true);
        var upgradeableTemplateId = TemplateExtensions.GetTemplateId<UpgradeableTemplate>(usePackageHash: true);

        // Assert - all use PackageId (hash) format
        keyedTemplateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(KeyedTemplate)}");
        viewedTemplateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(ViewedTemplate)}");
        upgradeableTemplateId.Should().Be($"{UpgradedPkgId}:{TestModuleName}:{nameof(UpgradeableTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_should_return_same_result_as_static_method()
    {
        // Arrange
        var template = new KeyedTemplate(new Party("Bob"), "asset-123");

        // Act
        var staticResult = TemplateExtensions.GetTemplateId<KeyedTemplate>();
        var extensionResult = template.GetTemplateId();

        // Assert
        staticResult.Should().Be(extensionResult);
    }

    [Fact]
    public void GetTemplateId_extension_with_usePackageHash_should_return_same_result_as_static_method()
    {
        // Arrange
        var template = new KeyedTemplate(new Party("Bob"), "asset-123");

        // Act
        var staticResult = TemplateExtensions.GetTemplateId<KeyedTemplate>(usePackageHash: true);
        var extensionResult = template.GetTemplateId(usePackageHash: true);

        // Assert
        staticResult.Should().Be(extensionResult);
    }

    #endregion
}
