// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class ContractTypesTests
{
    [Fact]
    public void ITemplate_static_properties_should_be_accessible()
    {
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
        var template = new TestTemplate(new Party("Charlie"), 500);

        var record = template.ToRecord();

        record.GetField("owner")!.As<DamlParty>().Value.Should().Be("Charlie");
        record.GetField("amount")!.As<DamlInt64>().Value.Should().Be(500);
    }

    [Fact]
    public void ITemplate_FromRecord_should_deserialize_correctly()
    {
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("Diana")),
            DamlField.Create("amount", new DamlInt64(750)));

        var template = TestTemplate.FromRecord(record);

        template.Owner.Should().Be(new Party("Diana"));
        template.Amount.Should().Be(750);
    }

    private sealed record KeyedTemplate(Party Owner, string AssetId) : ITemplate, IHasKey<KeyedTemplate, string>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(KeyedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<KeyedTemplate, string> Key { get; } = new()
        {
            KeyEncoder = static key => new DamlText(key),
            KeyDecoder = static value => value.As<DamlText>().Value,
        };

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("assetId", new DamlText(AssetId)));

        public static KeyedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("assetId").As<DamlText>().Value);
    }

    private sealed record PartyKeyedTemplate(Party Steward) : ITemplate, IHasKey<PartyKeyedTemplate, Party>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(PartyKeyedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<PartyKeyedTemplate, Party> Key { get; } = new()
        {
            KeyEncoder = static key => key.ToDamlValue(),
            KeyDecoder = static value => Party.FromDamlValue(value.As<DamlParty>()),
        };

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("steward", Steward.ToDamlValue()));

        public static PartyKeyedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("steward").As<DamlParty>()));
    }

    private sealed record KeyFieldTemplate(Party Owner, string Key)
        : ITemplate, IHasKey<KeyFieldTemplate, string>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(KeyFieldTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        static KeyDescriptor<KeyFieldTemplate, string> IHasKey<KeyFieldTemplate, string>.Key { get; } = new()
        {
            KeyEncoder = static key => new DamlText(key),
            KeyDecoder = static value => value.As<DamlText>().Value,
        };

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("key", new DamlText(Key)));

        public static KeyFieldTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("key").As<DamlText>().Value);
    }

    private sealed record AccountKey(Party Owner, string Number);

    private sealed record RecordKeyedTemplate(Party Owner, string Number, long Balance)
        : ITemplate, IHasKey<RecordKeyedTemplate, AccountKey>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(RecordKeyedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<RecordKeyedTemplate, AccountKey> Key { get; } = new()
        {
            KeyEncoder = static key => DamlRecord.Create(
                DamlField.Create("owner", key.Owner.ToDamlValue()),
                DamlField.Create("number", new DamlText(key.Number))),
            KeyDecoder = static value => new AccountKey(
                Party.FromDamlValue(value.As<DamlRecord>().GetRequiredField("owner").As<DamlParty>()),
                value.As<DamlRecord>().GetRequiredField("number").As<DamlText>().Value),
        };

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("number", new DamlText(Number)),
            DamlField.Create("balance", new DamlInt64(Balance)));

        public static RecordKeyedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("number").As<DamlText>().Value,
                record.GetRequiredField("balance").As<DamlInt64>().Value);
    }

    private static TKey DecodeKey<TTemplate, TKey>(DamlValue value)
        where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
    {
        return TTemplate.Key.KeyDecoder(value);
    }

    private static TKey RoundTripKey<TTemplate, TKey>(TKey key)
        where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
    {
        return TTemplate.Key.KeyDecoder(TTemplate.Key.KeyEncoder(key));
    }

    private static ExerciseByKeyCommand ExerciseByKey<TTemplate, TKey>(TKey key, ChoiceName choice)
        where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
    {
        return new(TTemplate.TemplateId, TTemplate.Key.KeyEncoder(key), choice, DamlRecord.Create());
    }

    [Fact]
    public void IHasKey_should_reach_the_key_descriptor_through_a_generic_constraint()
    {
        var key = DecodeKey<KeyedTemplate, string>(new DamlText("asset-123"));

        key.Should().Be("asset-123");
    }

    [Fact]
    public void IHasKey_should_admit_a_key_type_that_is_not_a_record()
    {
        var key = DecodeKey<PartyKeyedTemplate, Party>(new DamlParty("Alice"));

        key.Should().Be(new Party("Alice"));
    }

    [Fact]
    public void IHasKey_should_reach_an_explicitly_implemented_descriptor_when_the_payload_names_a_field_Key()
    {
        var template = new KeyFieldTemplate(new Party("Bob"), "asset-456");

        template.Key.Should().Be("asset-456");
        DecodeKey<KeyFieldTemplate, string>(new DamlText("asset-789")).Should().Be("asset-789");
    }

    [Fact]
    public void KeyDescriptor_should_round_trip_a_key_through_the_encoder_and_the_decoder()
    {
        KeyedTemplate.Key.KeyEncoder("asset-123").Should().BeOfType<DamlText>()
            .Which.Value.Should().Be("asset-123");
        RoundTripKey<KeyedTemplate, string>("asset-123").Should().Be("asset-123");
    }

    [Fact]
    public void KeyDescriptor_should_round_trip_a_record_key_through_the_encoder_and_the_decoder()
    {
        var key = new AccountKey(new Party("Alice"), "acc-001");

        RecordKeyedTemplate.Key.KeyEncoder(key).As<DamlRecord>()
            .GetRequiredField("number").As<DamlText>().Value.Should().Be("acc-001");
        RoundTripKey<RecordKeyedTemplate, AccountKey>(key).Should().Be(key);
    }

    [Fact]
    public void KeyDescriptor_should_round_trip_a_bare_Party_key_through_the_encoder_and_the_decoder()
    {
        var key = new Party("Steward");

        PartyKeyedTemplate.Key.KeyEncoder(key).Should().BeOfType<DamlParty>()
            .Which.Value.Should().Be("Steward");
        RoundTripKey<PartyKeyedTemplate, Party>(key).Should().Be(key);
    }

    [Fact]
    public void KeyDescriptor_should_round_trip_an_explicitly_implemented_key_through_the_encoder_and_the_decoder()
    {
        RoundTripKey<KeyFieldTemplate, string>("asset-456").Should().Be("asset-456");
    }

    [Fact]
    public void KeyEncoder_should_give_generic_code_holding_only_a_key_a_route_to_ExerciseByKeyCommand()
    {
        var command = ExerciseByKey<RecordKeyedTemplate, AccountKey>(
            new AccountKey(new Party("Alice"), "acc-001"),
            new ChoiceName("Transfer"));

        command.TemplateId.Should().Be(RecordKeyedTemplate.TemplateId);
        command.ContractKey.As<DamlRecord>()
            .GetRequiredField("owner").As<DamlParty>().Value.Should().Be("Alice");
        RecordKeyedTemplate.Key.KeyDecoder(command.ContractKey).Number.Should().Be("acc-001");
    }

    private interface ITestInterface : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "TestInterface");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => new(2, 0, 0);
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier(TestPackageId, TestModuleName, "TestInterface"), DamlTypeKind.Interface, TestPackageName);
    }

    [Fact]
    public void IDamlInterface_should_provide_interface_metadata()
    {
        var interfaceId = GetInterfaceId<ITestInterface>();
        interfaceId.PackageId.Should().Be(TestPackageId);
        interfaceId.ModuleName.Should().Be(TestModuleName);
        interfaceId.EntityName.Should().Be("TestInterface");
    }

    private static Identifier GetInterfaceId<T>() where T : IDamlInterface
    {
        return T.InterfaceId;
    }

    private sealed record AssetView(Party Owner, decimal Amount);

    private sealed record ViewedTemplate(Party Owner, decimal Amount) : ITemplate, IHasView<AssetView>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(ViewedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlNumeric(Amount)));

        public static ViewedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }

    [Fact]
    public void IHasView_carries_no_members()
    {
        typeof(IHasView<>).GetMembers().Should().BeEmpty();
    }

    [Fact]
    public void IHasView_links_the_implementing_type_to_its_view_type()
    {
        var template = new ViewedTemplate(new Party("Diana"), 2500.00m);

        template.Should().BeAssignableTo<IHasView<AssetView>>();
    }

    private const string UpgradedPkgId = "upgraded-package";
    private const string UpgradedPackageName = "upgraded-package-name";

    private sealed record UpgradeableTemplate(string Value) : ITemplate, IUpgradeable
    {
        public static Identifier TemplateId => new(UpgradedPkgId, TestModuleName, nameof(UpgradeableTemplate));
        public static string PackageId => UpgradedPkgId;
        public static string PackageName => UpgradedPackageName;
        public static Version PackageVersion => new(2, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
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
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("value", new DamlText(Value)));

        public static NonUpgradeableTemplate FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("value").As<DamlText>().Value);
    }

    [Fact]
    public void IUpgradeable_should_provide_upgraded_package_id()
    {
        UpgradeableTemplate.UpgradedPackageId.Should().Be("previous-package-id-12345");
    }

    [Fact]
    public void IUpgradeable_should_be_accessible_via_generic_constraint()
    {
        var upgradedId = GetUpgradedPackageId<UpgradeableTemplate>();

        upgradedId.Should().Be("previous-package-id-12345");
    }

    private static string? GetUpgradedPackageId<T>() where T : IUpgradeable
    {
        return T.UpgradedPackageId;
    }

    [Fact]
    public void NonUpgradeable_template_should_not_implement_IUpgradeable()
    {
        typeof(NonUpgradeableTemplate).GetInterfaces()
            .Should().NotContain(typeof(IUpgradeable));
    }

    private interface ITransferable : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "Transferable");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => TestPackageV1;
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier(TestPackageId, TestModuleName, "Transferable"), DamlTypeKind.Interface, TestPackageName);
    }

    private sealed record TransferableAsset(Party Owner, decimal Amount) : ITemplate, IImplements<ITransferable>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(TransferableAsset));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

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
        typeof(TransferableAsset).GetInterfaces()
            .Should().Contain(typeof(IImplements<ITransferable>));
    }

    [Fact]
    public void IImplements_template_should_be_assignable_to_IImplements()
    {
        var asset = new TransferableAsset(new Party("Eve"), 5000m);

        asset.Should().BeAssignableTo<IImplements<ITransferable>>();
    }

    [Fact]
    public void GetTemplateId_should_return_package_name_format_by_default()
    {
        var templateId = TemplateExtensions.GetTemplateId<TestTemplate>();

        templateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_with_PackageHash_format_should_return_hash_format()
    {
        var templateId = TemplateExtensions.GetTemplateId<TestTemplate>(TemplateIdFormat.PackageHash);

        templateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_method_should_return_package_name_format_by_default()
    {
        var template = new TestTemplate(new Party("Alice"), 100);

        var templateId = template.GetTemplateId();

        templateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_method_with_PackageHash_format_should_return_hash_format()
    {
        var template = new TestTemplate(new Party("Alice"), 100);

        var templateId = template.GetTemplateId(TemplateIdFormat.PackageHash);

        templateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_should_work_with_different_templates()
    {
        var keyedTemplateId = TemplateExtensions.GetTemplateId<KeyedTemplate>();
        var viewedTemplateId = TemplateExtensions.GetTemplateId<ViewedTemplate>();
        var upgradeableTemplateId = TemplateExtensions.GetTemplateId<UpgradeableTemplate>();

        keyedTemplateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(KeyedTemplate)}");
        viewedTemplateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(ViewedTemplate)}");
        upgradeableTemplateId.Should().Be($"{UpgradedPackageName}:{TestModuleName}:{nameof(UpgradeableTemplate)}");
    }

    [Fact]
    public void GetTemplateId_with_PackageHash_format_should_work_with_different_templates()
    {
        var keyedTemplateId = TemplateExtensions.GetTemplateId<KeyedTemplate>(TemplateIdFormat.PackageHash);
        var viewedTemplateId = TemplateExtensions.GetTemplateId<ViewedTemplate>(TemplateIdFormat.PackageHash);
        var upgradeableTemplateId = TemplateExtensions.GetTemplateId<UpgradeableTemplate>(TemplateIdFormat.PackageHash);

        keyedTemplateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(KeyedTemplate)}");
        viewedTemplateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(ViewedTemplate)}");
        upgradeableTemplateId.Should().Be($"{UpgradedPkgId}:{TestModuleName}:{nameof(UpgradeableTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_should_return_same_result_as_static_method()
    {
        var template = new KeyedTemplate(new Party("Bob"), "asset-123");

        var staticResult = TemplateExtensions.GetTemplateId<KeyedTemplate>();
        var extensionResult = template.GetTemplateId();

        staticResult.Should().Be(extensionResult);
    }

    [Fact]
    public void GetTemplateId_extension_with_PackageHash_format_should_return_same_result_as_static_method()
    {
        var template = new KeyedTemplate(new Party("Bob"), "asset-123");

        var staticResult = TemplateExtensions.GetTemplateId<KeyedTemplate>(TemplateIdFormat.PackageHash);
        var extensionResult = template.GetTemplateId(TemplateIdFormat.PackageHash);

        staticResult.Should().Be(extensionResult);
    }

    private sealed record EmptyPackageNameTemplate : ITemplate
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(EmptyPackageNameTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => string.Empty;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static EmptyPackageNameTemplate FromRecord(DamlRecord record) => new();
    }

    [Fact]
    public void GetTemplateId_should_throw_for_empty_PackageName_instead_of_silently_falling_back_to_hash_format()
    {
        var act = () => TemplateExtensions.GetTemplateId<EmptyPackageNameTemplate>();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(ITemplate.PackageName)}*");
    }
}
