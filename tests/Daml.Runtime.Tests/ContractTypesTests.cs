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

    #region ContractId<T> Tests

    [Fact]
    public void ContractId_should_store_value()
    {
        // Arrange & Act
        var contractId = new ContractId<TestTemplate>("contract-123");

        // Assert
        contractId.Value.Should().Be("contract-123");
    }

    [Fact]
    public void ContractId_should_convert_to_string_implicitly()
    {
        // Arrange
        var contractId = new ContractId<TestTemplate>("contract-123");

        // Act
        string stringValue = contractId;

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
